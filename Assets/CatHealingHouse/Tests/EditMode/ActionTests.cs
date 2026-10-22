using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>몸짓: 기지개 → 하품, 뒷발로 귀 긁기, 우다다, 햇빛 속 옆으로 길게 눕기.</summary>
    public class ActionTests
    {
        private HouseWorld _world;
        private CatSimulation _sim;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world), seed: 7);
            // 가구 없는 판이라 욕구는 멈춰 둔다
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
        }

        private Cat FullCat(CatPersonality p = null)
        {
            var cat = _sim.AddCat("냥", new Cell(BoardId.Indoor, HexCoord.Zero), p);
            foreach (var n in CatNeeds.All) cat.Needs.Set(n, 100);
            return cat;
        }

        private void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += CatSimulation.FixedStep) _sim.Step(CatSimulation.FixedStep);
        }

        [Test]
        public void GettingUpFromLyingStretchesThenYawns()
        {
            _sim.Tuning.LieStretchChance = 1;
            var cat = FullCat();
            cat.State = CatState.Lounging;
            cat.Lounge = LoungeKind.Lie;
            cat.LoungeTimer = 0.05;
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.Acting, cat.State);
            Assert.AreEqual(CatAct.Stretch, cat.Act);
            Run(_sim.Tuning.StretchSeconds + 0.1);
            Assert.AreEqual(CatAct.Yawn, cat.Act, "기지개 다음엔 하품");
            Run(_sim.Tuning.YawnSeconds + 0.1);
            Assert.AreEqual(CatState.Idle, cat.State);
            Assert.AreEqual(CatAct.None, cat.Act);
        }

        [Test]
        public void SittingDoesNotStretch()
        {
            _sim.Tuning.LieStretchChance = 1;
            var cat = FullCat();
            cat.State = CatState.Lounging;
            cat.Lounge = LoungeKind.Sit;
            cat.LoungeTimer = 0.05;
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.Idle, cat.State);
        }

        [Test]
        public void UrgentNeedInterruptsStretch()
        {
            var cat = FullCat();
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(Furniture.FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl));
            _sim.StartAct(cat, CatAct.Stretch);
            cat.Needs.Set(NeedType.Hunger, 10);
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(CatState.MovingToTarget, cat.State);
            Assert.AreEqual(CatAct.None, cat.Act);
        }

        [Test]
        public void CatsSometimesScratchEarInsteadOfGrooming()
        {
            _sim.Tuning.GroomChance = 1;
            var cat = FullCat();
            int ear = 0, groom = 0;
            CatState last = cat.State;
            for (int i = 0; i < 6000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (cat.State != last)
                {
                    if (cat.State == CatState.Acting && cat.Act == CatAct.EarScratch) ear++;
                    if (cat.State == CatState.Grooming) groom++;
                    last = cat.State;
                }
            }
            Assert.Greater(ear, 0);
            Assert.Greater(groom, ear, "귀 긁기는 그루밍보다 드물다");
        }

        [Test]
        public void ZoomiesAtNight_AreFastAndLong()
        {
            _sim.IsNight = () => true;
            _sim.Tuning.ZoomiesNightChance = 1;
            _sim.Tuning.LoungeChance = 0;
            _sim.Tuning.GroomChance = 0;
            var cat = FullCat();
            bool seen = false;
            double minHop = double.MaxValue;
            for (int i = 0; i < 3000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (cat.Zoomies && cat.IsHopping) { seen = true; minHop = System.Math.Min(minHop, cat.HopDuration); }
            }
            Assert.IsTrue(seen);
            Assert.Less(minHop, _sim.Tuning.HopSeconds * 0.6, "우다다는 빠르다");
        }

        [Test]
        public void NoZoomiesForNormalCatInDaytime()
        {
            _sim.IsNight = () => false;
            var cat = FullCat();
            for (int i = 0; i < 6000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                Assert.IsFalse(cat.Zoomies);
            }
        }

        [Test]
        public void EnergizerGetsZoomiesInDaytime()
        {
            _sim.IsNight = () => false;
            var cat = FullCat(CatPersonalities.Energizer());
            bool seen = false;
            for (int i = 0; i < 12000 && !seen; i++) { _sim.Step(CatSimulation.FixedStep); seen = cat.Zoomies; }
            Assert.IsTrue(seen);
        }

        [Test]
        public void TiredCatHasNoZoomies()
        {
            _sim.IsNight = () => true;
            _sim.Tuning.ZoomiesNightChance = 1;
            _sim.Tuning.LoungeChance = 0; // 누워 쉬면 기운이 다시 차므로
            var cat = FullCat();
            cat.Needs.Set(NeedType.Energy, 50);
            for (int i = 0; i < 3000; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                Assert.IsFalse(cat.Zoomies);
            }
        }

        [Test]
        public void SunbathingCatSprawlsAndRests()
        {
            _sim.IsSunny = c => true;
            _sim.Tuning.SunbathChance = 1;
            _sim.Tuning.SprawlChance = 1;
            _sim.Tuning.LoungeChance = 1;
            _sim.Tuning.GroomChance = 0;
            var cat = FullCat(CatPersonalities.Sleepyhead());
            for (int i = 0; i < 6000 && !(cat.State == CatState.Lounging && cat.Sunbathing); i++) _sim.Step(CatSimulation.FixedStep);
            Assert.IsTrue(cat.Sunbathing);
            Assert.AreEqual(LoungeKind.Sprawl, cat.Lounge);
            cat.Needs.Set(NeedType.Energy, 50);
            Run(5);
            Assert.Greater(cat.Needs.Energy, 50, "길게 누워도 쉬는 것");
        }
    }
}
