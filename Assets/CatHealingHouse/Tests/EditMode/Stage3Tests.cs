using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class EconomyTests
    {
        private HouseWorld _w;
        private CatSimulation _sim;
        private HouseEconomy _eco;

        [SetUp]
        public void SetUp()
        {
            _w = new HouseWorld(3, 3);
            _sim = new CatSimulation(_w, new DistanceMapCache(_w), seed: 5);
            _eco = new HouseEconomy(_sim, seed: 5);
        }

        private Cat HungryCat(HexCoord at)
        {
            var c = _sim.AddCat("치즈", new Cell(BoardId.Indoor, at));
            foreach (var n in CatNeeds.All) c.Needs.Set(n, 100);
            c.Needs.Set(NeedType.Hunger, 10);
            return c;
        }

        private void RunUntil(System.Func<bool> done, int maxSteps = 3000)
        {
            for (int i = 0; i < maxSteps && !done(); i++) _sim.Step(CatSimulation.FixedStep);
        }

        [Test]
        public void EatingSendsFishBoneToTrayAndSpawnsHeart()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl);
            HungryCat(new HexCoord(-1, 0));
            int sent = 0;
            _eco.ItemSentToTray += (f, k, n) => { if (f == bowl && k == ItemKind.FishBone) sent += n; };
            RunUntil(() => _eco.Tray.Count > 0);

            Assert.AreEqual(1, _eco.Tray.Count);
            Assert.AreEqual(ItemKind.FishBone, _eco.Tray[0].Kind);
            Assert.AreEqual(1, _eco.Tray[0].Count);
            Assert.AreEqual(1, sent);
            Assert.IsNotNull(_eco.HeartOn(bowl.Id), "가구 위에 하트");
        }

        [Test]
        public void CollectingOneSlotOnlyConvertsThatSlot()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            _w.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(-2, 0), false, out _);
            var a = HungryCat(new HexCoord(1, 0));
            var b = _sim.AddCat("까망이", new Cell(BoardId.Indoor, new HexCoord(-1, 0)));
            foreach (var n in CatNeeds.All) b.Needs.Set(n, 100);
            b.Needs.Set(NeedType.Fun, 10);
            RunUntil(() => _eco.Tray.Count >= 2);
            Assert.AreEqual(2, _eco.Tray.Count);

            var first = _eco.Tray[0];
            long gained = _eco.CollectTraySlot(0);
            Assert.AreEqual(first.Value, gained);
            Assert.AreEqual(gained, _eco.Wallet.Money);
            Assert.AreEqual(1, _eco.Tray.Count, "다른 칸은 그대로");
        }

        [Test]
        public void TraySlotStopsAtCap()
        {
            _eco.Tuning.TraySlotCap = 3;
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            var c = HungryCat(new HexCoord(1, 0));
            for (int round = 0; round < 6; round++)
            {
                c.Needs.Set(NeedType.Hunger, 10);
                int before = _sim.Cats.Count;
                RunUntil(() => c.State == CatState.Using);
                RunUntil(() => c.State == CatState.Idle || c.State == CatState.Wandering);
            }
            Assert.AreEqual(3, _eco.Tray[0].Count, "칸 상한에서 멈춤");
        }

        [Test]
        public void OnlyOneHeartPerFurnitureAndItExpires()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl);
            var c = HungryCat(new HexCoord(1, 0));
            RunUntil(() => _eco.HeartOn(bowl.Id) != null);
            var h = _eco.HeartOn(bowl.Id);
            c.Needs.Set(NeedType.Hunger, 10);
            RunUntil(() => _eco.Tray[0].Count == 2);
            Assert.AreSame(h, _eco.HeartOn(bowl.Id), "두 번 먹어도 하트는 하나");
            Assert.AreEqual(1, _eco.Hearts.Count);

            RunUntil(() => _eco.HeartOn(bowl.Id) == null, (int)(_eco.Tuning.HeartLifetime / CatSimulation.FixedStep) + 50);
            Assert.IsNull(_eco.HeartOn(bowl.Id), "시간이 지나면 사라짐");
        }

        [Test]
        public void CollectHeartAddsJellyHeart()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl);
            HungryCat(new HexCoord(1, 0));
            RunUntil(() => _eco.HeartOn(bowl.Id) != null);
            Assert.IsTrue(_eco.CollectHeart(bowl.Id));
            Assert.AreEqual(1, _eco.Wallet.Hearts);
            Assert.IsFalse(_eco.CollectHeart(bowl.Id), "이미 주움");
        }

        [Test]
        public void LitterGetsDirtierAndCleaningGivesPotatoesAndMattdongsan()
        {
            _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(2, 0), false, out var litter);
            var c = _sim.AddCat("고등어", new Cell(BoardId.Indoor, new HexCoord(1, 0)));
            foreach (var n in CatNeeds.All) c.Needs.Set(n, 100);
            for (int i = 0; i < 4; i++)
            {
                c.Needs.Set(NeedType.Bladder, 5);
                int uses = _eco.UsesOfLitter(litter.Id);
                RunUntil(() => _eco.UsesOfLitter(litter.Id) == uses + 1);
            }
            Assert.AreEqual(4, _eco.UsesOfLitter(litter.Id));
            Assert.AreEqual(LitterStage.VeryDirty, _eco.StageOfLitter(litter.Id));
            Assert.AreEqual(0, _eco.Tray.Count, "사용만으로는 트레이에 안 감");

            Assert.IsTrue(_eco.CleanLitter(litter.Id));
            Assert.AreEqual(LitterStage.Clean, _eco.StageOfLitter(litter.Id));
            int total = _eco.Tray.Where(s => s.Kind == ItemKind.Potato || s.Kind == ItemKind.Mattdongsan).Sum(s => s.Count);
            Assert.AreEqual(4, total, "사용 횟수만큼 감자+맛동산");
            Assert.IsFalse(_eco.CleanLitter(litter.Id), "깨끗하면 청소할 것이 없음");
        }

        [Test]
        public void DirtyLitterHurtsMoodAndCleaningHelps()
        {
            _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(2, 0), false, out var litter);
            var c = _sim.AddCat("고등어", new Cell(BoardId.Indoor, new HexCoord(1, 0)));
            foreach (var n in CatNeeds.All) c.Needs.Set(n, 100);
            for (int i = 0; i < 4; i++)
            {
                c.Needs.Set(NeedType.Bladder, 5);
                int uses = _eco.UsesOfLitter(litter.Id);
                RunUntil(() => _eco.UsesOfLitter(litter.Id) == uses + 1);
            }
            // 깨끗(+6) 보통(0) 더러움(-6) 매우 더러움(-15) 을 차례로 겪음 → 평소(70)보다 낮아야 함
            Assert.Less(c.Mood, 70);
            double before = c.Mood;
            _eco.CleanLitter(litter.Id);
            Assert.Greater(c.Mood, before);
        }

        [Test]
        public void CatsAvoidVeryDirtyLitterWhenAnotherExists()
        {
            _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(1, 0), false, out var near);
            _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-3, 2), false, out var far);
            var c = _sim.AddCat("고등어", new Cell(BoardId.Indoor, HexCoord.Zero));
            foreach (var n in CatNeeds.All) c.Needs.Set(n, 100);
            // 가까운 화장실을 3번 써서 매우 더럽게
            int guard = 0;
            while (_eco.UsesOfLitter(near.Id) < 3 && guard++ < 20)
            {
                c.Needs.Set(NeedType.Bladder, 5);
                int total = _eco.UsesOfLitter(near.Id) + _eco.UsesOfLitter(far.Id);
                RunUntil(() => _eco.UsesOfLitter(near.Id) + _eco.UsesOfLitter(far.Id) == total + 1);
            }
            Assert.AreEqual(LitterStage.VeryDirty, _eco.StageOfLitter(near.Id));
            RunUntil(() => (c.State == CatState.Idle || c.State == CatState.Wandering) && !c.IsHopping);
            c.Needs.Set(NeedType.Bladder, 5);
            RunUntil(() => c.HasTarget, 50);
            Assert.AreEqual(far.Id, c.TargetFurnitureId, "매우 더러우면 먼 화장실로");
        }

        [Test]
        public void DropsStopAtFivePerBoardAndCanBePicked()
        {
            _eco.Tuning.FurballPerHop = 1.0; // 매 걸음 떨어뜨림
            _eco.Tuning.WhiskerPerHop = 0;
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            for (int i = 0; i < 2000; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(5, _eco.DropCount(BoardId.Indoor), "최대 5개");
            Assert.IsTrue(_eco.Drops.All(d => _w.IsTransit(d.Cell) && _w.GetTile(d.Cell).Kind == TileKind.Floor));
            Assert.AreEqual(5, _eco.Drops.Select(d => d.Cell).Distinct().Count(), "한 칸에 하나씩");

            int id = _eco.Drops[0].Id;
            Assert.IsTrue(_eco.PickDrop(id));
            Assert.AreEqual(1, _eco.Wallet.Furballs);
            Assert.AreEqual(4, _eco.DropCount(BoardId.Indoor));
        }

        [Test]
        public void EveryUsableFurnitureHasATrayItemExceptLitter()
        {
            foreach (var d in FurnitureCatalog.All.Where(d => d.Capacity > 0))
            {
                var k = RewardTable.TrayItemFor(d);
                if (RewardTable.IsLitterBox(d)) Assert.AreEqual(ItemKind.None, k);
                else Assert.AreNotEqual(ItemKind.None, k, d.DisplayName);
            }
        }
    }
}
