using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>앉아서·누워서 멍때리기 (빈칸, 캣타워·숨숨집 위).</summary>
    public class LoungeTests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private HouseEconomy _eco;
        private FurnitureInstance _tower, _hiding;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world), seed: 5);
            _eco = new HouseEconomy(_sim);
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, -1), false, out _tower));
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.HidingHouse, BoardId.Indoor, new HexCoord(2, -3), false, out _hiding));
        }

        private Cat FullCat(string name, CatPersonality p = null)
        {
            var cat = _sim.AddCat(name, new Cell(BoardId.Indoor, HexCoord.Zero), p);
            foreach (var n in CatNeeds.All) cat.Needs.Set(n, 100);
            return cat;
        }

        /// <summary>10분 동안 상태별 시간 비율.</summary>
        private Dictionary<string, double> Observe(Cat cat, double seconds)
        {
            var share = new Dictionary<string, double>();
            for (double t = 0; t < seconds; t += CatSimulation.FixedStep)
            {
                _sim.Step(CatSimulation.FixedStep);
                string key = cat.State == CatState.Lounging ? "floor"
                    : cat.LoungeVisit && cat.State == CatState.Using ? "furniture"
                    : cat.IsHopping ? "walk" : "other";
                share[key] = (share.TryGetValue(key, out var v) ? v : 0) + CatSimulation.FixedStep / seconds;
            }
            return share;
        }

        [Test]
        public void CatsSpendRealTimeLounging_OnFloorAndOnFurniture()
        {
            // 밥그릇·화장실이 없는 판이라 욕구는 멈춰 둔다 (급한 고양이는 멍때리지 않으므로)
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            var cat = FullCat("멍");
            var s = Observe(cat, 600);
            double floor = s.TryGetValue("floor", out var a) ? a : 0;
            double furn = s.TryGetValue("furniture", out var b) ? b : 0;
            Assert.Greater(floor, 0.1, "빈칸에서 멍때리는 시간이 있어야 한다");
            Assert.Greater(furn, 0.03, "캣타워·숨숨집에서 멍때리는 시간도 있어야 한다");
            Assert.Less(s.TryGetValue("walk", out var w) ? w : 0, 0.4, "쉴 새 없이 걷지 않는다");
        }

        [Test]
        public void SleepyheadLoungesMoreThanEnergizer()
        {
            // 욕구는 빼고 성격(멍때리는 습관)만 비교한다
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            var sleepy = FullCat("잠", CatPersonalities.Sleepyhead());
            var s1 = Observe(sleepy, 600);
            SetUp();
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            var energy = FullCat("에", CatPersonalities.Energizer());
            var s2 = Observe(energy, 600);
            double L(Dictionary<string, double> d) => (d.TryGetValue("floor", out var a) ? a : 0) + (d.TryGetValue("furniture", out var b) ? b : 0);
            Assert.Greater(L(s1), L(s2));
        }

        [Test]
        public void UrgentNeedInterruptsLounging()
        {
            var cat = FullCat("배고파");
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl));
            for (int i = 0; i < 6000 && cat.State != CatState.Lounging; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.Lounging, cat.State);
            cat.Needs.Set(NeedType.Hunger, 10);
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.MovingToTarget, cat.State);
            Assert.AreEqual(bowl.Id, cat.TargetFurnitureId);
            Assert.AreEqual(LoungeKind.None, cat.Lounge);
        }

        [Test]
        public void LoungingOnFurnitureGivesNoTrayReward()
        {
            var cat = FullCat("멍");
            int lounges = 0;
            bool wasOn = false;
            for (int i = 0; i < 12000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                foreach (var n in CatNeeds.All) cat.Needs.Set(n, 100); // 욕구로는 가구를 안 쓰게
                bool on = cat.LoungeVisit && cat.State == CatState.Using;
                if (on && !wasOn) lounges++;
                wasOn = on;
            }
            Assert.Greater(lounges, 0);
            Assert.AreEqual(0, _eco.Tray.Count, "멍때리기는 가구 사용이 아니다");
        }
    }
}
