using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class CatNeedsTests
    {
        [Test]
        public void NeedsClampBetweenZeroAndHundred()
        {
            var n = new CatNeeds();
            n.Add(NeedType.Hunger, 50);
            Assert.AreEqual(100, n.Hunger);
            n.Add(NeedType.Hunger, -250);
            Assert.AreEqual(0, n.Hunger);
        }

        [Test]
        public void NeedsDecayOverTime()
        {
            var w = new HouseWorld(3, 3);
            var sim = new CatSimulation(w, new DistanceMapCache(w));
            var cat = sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            sim.Advance(10);
            Assert.Less(cat.Needs.Hunger, 100);
            Assert.Less(cat.Needs.Fun, 100);
        }
    }

    public class CatBehaviourTests
    {
        private HouseWorld _w;
        private CatSimulation _sim;

        [SetUp]
        public void SetUp()
        {
            _w = new HouseWorld(3, 3);
            _sim = new CatSimulation(_w, new DistanceMapCache(_w), seed: 7);
        }

        private static void OnlyNeed(Cat c, NeedType t, double value)
        {
            foreach (var n in CatNeeds.All) c.Needs.Set(n, 100);
            c.Needs.Set(t, value);
        }

        [Test]
        public void HungryCatWalksToBowlEatsAndReleasesSlot()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, -1), false, out var bowl);
            var cat = _sim.AddCat("치즈", new Cell(BoardId.Indoor, new HexCoord(-2, 1)));
            OnlyNeed(cat, NeedType.Hunger, 10);

            int completed = 0;
            _sim.InteractionCompleted += (c, f) => { if (f == bowl) completed++; };

            bool sawReserved = false, sawUsing = false;
            for (int i = 0; i < 600 && completed == 0; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (bowl.OccupiedSlots == 1) sawReserved = true;
                if (cat.State == CatState.Using) sawUsing = true;
            }

            Assert.IsTrue(sawReserved, "슬롯을 예약해야 함");
            Assert.IsTrue(sawUsing, "밥그릇을 사용해야 함");
            Assert.AreEqual(1, completed, "사용 완료 이벤트");
            Assert.GreaterOrEqual(cat.Needs.Hunger, _sim.Tuning.SatisfiedLevel - 1);
            Assert.AreEqual(0, bowl.OccupiedSlots, "끝나면 슬롯 반납");
        }

        [Test]
        public void CapacityIsNeverExceeded()
        {
            _w.TryPlace(FurnitureCatalog.CatTower3, BoardId.Indoor, HexCoord.Zero, false, out var tower);
            var starts = new[] { new HexCoord(-3, 0), new HexCoord(-2, 2), new HexCoord(2, -3), new HexCoord(0, -3), new HexCoord(-1, 3), new HexCoord(3, -1) };
            for (int i = 0; i < starts.Length; i++)
                OnlyNeed(_sim.AddCat("고양이" + i, new Cell(BoardId.Indoor, starts[i])), NeedType.Fun, 5);

            int maxSeen = 0;
            for (int i = 0; i < 1500; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                maxSeen = System.Math.Max(maxSeen, tower.OccupiedSlots);
                int using_ = _sim.Cats.Count(c => c.TargetFurnitureId == tower.Id);
                Assert.LessOrEqual(using_, tower.Def.Capacity, "정원 3을 넘으면 안 됨");
            }
            Assert.AreEqual(3, maxSeen, "정원만큼은 채워져야 함");
        }

        [Test]
        public void TwoBowlsServeTwoHungryCatsAtOnce()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var a);
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(-2, 0), false, out var b);
            OnlyNeed(_sim.AddCat("치즈", new Cell(BoardId.Indoor, new HexCoord(0, -1))), NeedType.Hunger, 10);
            OnlyNeed(_sim.AddCat("까망이", new Cell(BoardId.Indoor, new HexCoord(0, 1))), NeedType.Hunger, 12);

            _sim.Step(CatSimulation.FixedStep);
            for (int i = 0; i < 30; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(1, a.OccupiedSlots);
            Assert.AreEqual(1, b.OccupiedSlots);
        }

        [Test]
        public void WanderingStaysOnWalkableIndoorCells()
        {
            _w.TryPlace(FurnitureCatalog.Bookshelf, BoardId.Indoor, new HexCoord(0, -1), false, out _);
            _w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, 1), false, out _);
            for (int i = 0; i < 3; i++) _sim.AddCat("c" + i, new Cell(BoardId.Indoor, new HexCoord(i, 1)));
            // 욕구가 떨어지지 않게 해서 순수 배회만 본다
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;

            for (int i = 0; i < 3000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                foreach (var c in _sim.Cats)
                {
                    Assert.AreEqual(BoardId.Indoor, c.Cell.Board, "실내 고양이는 마당으로 나가지 않음");
                    var t = _w.GetTile(c.Cell);
                    Assert.AreNotEqual(TileKind.Wall, t.Kind, "벽 위");
                    Assert.AreNotEqual(TileKind.Door, t.Kind, "문 위");
                    // 멍때리러 가구에 들어간 건 괜찮다 (그 가구의 진입 칸)
                    bool inOwnFurniture = c.HasTarget && t.Furniture != null && t.Furniture.Id == c.TargetFurnitureId;
                    Assert.IsTrue(_w.IsTransit(c.Cell) || inOwnFurniture, $"막힌 칸 위: {c}");
                }
            }
        }

        [Test]
        public void WanderingPrefersGoingStraight()
        {
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            var cat = _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            int straight = 0, moves = 0, lastHeading = cat.Heading;
            var lastCell = cat.Cell;
            for (int i = 0; i < 20000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (cat.Cell != lastCell && cat.State == CatState.Wandering)
                {
                    if (cat.Heading == lastHeading) straight++;
                    moves++;
                }
                lastCell = cat.Cell;
                lastHeading = cat.Heading;
            }
            Assert.Greater(moves, 100); // 멍때리는 시간이 생겨서 걸음 수는 전보다 적다
            Assert.Greater(straight / (double)moves, 0.35, "직진 비율이 다른 방향보다 높아야 함");
        }

        [Test]
        public void RemovingTargetFurnitureMidTripRecovers()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(3, 0), false, out var bowl);
            var cat = _sim.AddCat("치즈", new Cell(BoardId.Indoor, new HexCoord(-3, 0)));
            OnlyNeed(cat, NeedType.Hunger, 10);
            for (int i = 0; i < 20 && cat.State != CatState.MovingToTarget; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.MovingToTarget, cat.State);

            _w.Remove(bowl.Id);
            for (int i = 0; i < 50; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreNotEqual(CatState.MovingToTarget, cat.State);
            Assert.AreEqual(-1, cat.TargetFurnitureId);
        }

        [Test]
        public void NoFurnitureMeansWanderingNotStuck()
        {
            var cat = _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            OnlyNeed(cat, NeedType.Bladder, 5);
            var visited = new System.Collections.Generic.HashSet<Cell>();
            for (int i = 0; i < 600; i++) { _sim.Step(CatSimulation.FixedStep); visited.Add(cat.Cell); }
            Assert.Greater(visited.Count, 3, "화장실이 없어도 계속 돌아다녀야 함");
        }

        [Test]
        public void ThreeCatsLiveOnTheirOwnForTenMinutes()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-3, 2), false, out _);
            _w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, -1), false, out _);
            _w.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(1, -2), false, out _);
            _w.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(0, 2), false, out _);
            _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            _sim.AddCat("까망이", new Cell(BoardId.Indoor, new HexCoord(1, 1)));
            _sim.AddCat("고등어", new Cell(BoardId.Indoor, new HexCoord(-1, 0)));

            int completed = 0;
            _sim.InteractionCompleted += (c, f) => completed++;
            _sim.Advance(600);

            Assert.Greater(completed, 5, "10분 동안 여러 번 가구를 써야 함");
            foreach (var c in _sim.Cats)
                foreach (var n in CatNeeds.All)
                    Assert.Greater(c.Needs.Get(n), 0, $"{c.Name} 의 {n} 이 바닥나면 안 됨");
            foreach (var f in _w.Furniture)
                Assert.LessOrEqual(f.OccupiedSlots, f.Def.Capacity);
        }
    }
}
