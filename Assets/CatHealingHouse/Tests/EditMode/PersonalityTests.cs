using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class PersonalityTests
    {
        private HouseWorld _w;
        private CatSimulation _sim;

        [SetUp]
        public void SetUp()
        {
            _w = new HouseWorld(3, 3);
            _sim = new CatSimulation(_w, new DistanceMapCache(_w), seed: 11);
            _sim.Tuning.LoungeChance = 0; // 여기서는 욕구로 가구 쓰는 행동만 본다 (멍때리기는 LoungeTests)
        }

        private static void Fill(Cat c, double v = 100)
        {
            foreach (var n in CatNeeds.All) c.Needs.Set(n, v);
        }

        /// <summary>한 고양이가 한 가구에서 보낸 총 사용 시간(초)과 방문 횟수.</summary>
        private (double seconds, int visits) Observe(Cat cat, FurnitureInstance f, double simSeconds)
        {
            double total = 0;
            int visits = 0;
            _sim.InteractionCompleted += (c, x) => { if (c == cat && x == f) visits++; };
            int steps = (int)(simSeconds / CatSimulation.FixedStep);
            for (int i = 0; i < steps; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (cat.State == CatState.Using && cat.TargetFurnitureId == f.Id) total += CatSimulation.FixedStep;
            }
            return (total, visits);
        }

        [Test]
        public void FastCatHopsFasterThanSleepyCat()
        {
            var fast = _sim.AddCat("빠름", new Cell(BoardId.Indoor, HexCoord.Zero), CatPersonalities.Energizer());
            var slow = _sim.AddCat("느림", new Cell(BoardId.Indoor, new HexCoord(1, 1)), CatPersonalities.Sleepyhead());
            double fastHop = 0, slowHop = 0;
            for (int i = 0; i < 600; i++)
            {
                _sim.Step(CatSimulation.FixedStep);
                if (fast.IsHopping) fastHop = fast.HopDuration;
                if (slow.IsHopping) slowHop = slow.HopDuration;
            }
            Assert.Greater(fastHop, 0);
            Assert.Greater(slowHop, 0);
            Assert.Less(fastHop, slowHop * 0.75, "에너자이저가 잠꾸러기보다 확실히 빨라야 함");
        }

        [Test]
        public void TowerLoverStaysOnTowerLongerThanNormalCat()
        {
            _w.TryPlace(FurnitureCatalog.CatTower3, BoardId.Indoor, HexCoord.Zero, false, out var tower);
            var lover = _sim.AddCat("대장", new Cell(BoardId.Indoor, new HexCoord(-2, 0)), CatPersonalities.TowerKing());
            Fill(lover);
            lover.Needs.Set(NeedType.Fun, 40);
            var (loverSec, _) = Observe(lover, tower, 60);

            SetUp();
            _w.TryPlace(FurnitureCatalog.CatTower3, BoardId.Indoor, HexCoord.Zero, false, out tower);
            var normal = _sim.AddCat("보통", new Cell(BoardId.Indoor, new HexCoord(-2, 0)));
            Fill(normal);
            normal.Needs.Set(NeedType.Fun, 40);
            var (normalSec, _) = Observe(normal, tower, 60);

            Assert.Greater(normalSec, 0);
            Assert.GreaterOrEqual(loverSec, _sim.Tuning.LingerPerLove * 2 - 0.5, "애정도 3이면 최소 16초는 머문다");
            Assert.Greater(loverSec, normalSec * 1.5);
        }

        [Test]
        public void WheelLoverGoesBeforeFunIsUrgent()
        {
            _w.TryPlace(FurnitureCatalog.CatWheel, BoardId.Indoor, HexCoord.Zero, false, out var wheel);
            var lover = _sim.AddCat("달리기", new Cell(BoardId.Indoor, new HexCoord(-2, 1)), CatPersonalities.Energizer());
            var normal = _sim.AddCat("보통", new Cell(BoardId.Indoor, new HexCoord(2, -2)));
            Fill(lover); Fill(normal);
            lover.Needs.Set(NeedType.Fun, 65);  // 기본 임계치(45)보다 높지만 애정도 3 임계치(75)보다 낮음
            normal.Needs.Set(NeedType.Fun, 65);

            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(wheel.Id, lover.TargetFurnitureId, "캣휠 러버는 덜 심심해도 캣휠로 간다");
            Assert.AreEqual(-1, normal.TargetFurnitureId, "보통 고양이는 아직 안 간다");
        }

        [Test]
        public void WheelLoverRidesMuchMoreOften()
        {
            _w.TryPlace(FurnitureCatalog.CatWheel, BoardId.Indoor, HexCoord.Zero, false, out var wheel);
            _w.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(-2, 2), false, out _);
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            var lover = _sim.AddCat("달리기", new Cell(BoardId.Indoor, new HexCoord(0, 2)), CatPersonalities.Energizer());
            var (loverSec, loverVisits) = Observe(lover, wheel, 600);

            SetUp();
            _w.TryPlace(FurnitureCatalog.CatWheel, BoardId.Indoor, HexCoord.Zero, false, out wheel);
            _w.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(-2, 2), false, out _);
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            var normal = _sim.AddCat("보통", new Cell(BoardId.Indoor, new HexCoord(0, 2)));
            var (normalSec, normalVisits) = Observe(normal, wheel, 600);

            Assert.Greater(loverVisits, normalVisits, "더 자주 탄다");
            Assert.Greater(loverSec, normalSec * 2, "훨씬 오래 탄다");
        }

        [Test]
        public void FavoriteFurnitureBeatsCloserOne()
        {
            // 가까운 방석 vs 먼 침대. 잠꾸러기는 침대(애정도 3)를 고른다.
            _w.TryPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(1, 0), false, out var near);
            _w.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(-2, -1), false, out var bed);
            var sleepy = _sim.AddCat("잠", new Cell(BoardId.Indoor, HexCoord.Zero), CatPersonalities.Sleepyhead());
            Fill(sleepy);
            sleepy.Needs.Set(NeedType.Energy, 10);
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(bed.Id, sleepy.TargetFurnitureId);

            var normal = _sim.AddCat("보통", new Cell(BoardId.Indoor, new HexCoord(0, 1)));
            Fill(normal);
            normal.Needs.Set(NeedType.Energy, 10);
            _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(near.Id, normal.TargetFurnitureId, "보통 고양이는 가까운 곳");
        }

        [Test]
        public void SleepyheadSleepsMoreThanEnergizer()
        {
            void Layout()
            {
                _w.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(-2, -1), false, out _);
                _w.TryPlace(FurnitureCatalog.CatWheel, BoardId.Indoor, new HexCoord(1, 1), false, out _);
                _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, -2), false, out _);
                _w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-2, 2), false, out _);
            }
            double SleepSeconds(CatPersonality p)
            {
                SetUp();
                Layout();
                var c = _sim.AddCat("c", new Cell(BoardId.Indoor, HexCoord.Zero), p);
                double s = 0;
                for (int i = 0; i < 6000; i++)
                {
                    _sim.Step(CatSimulation.FixedStep);
                    if (c.State == CatState.Using && c.TargetNeed == NeedType.Energy) s += CatSimulation.FixedStep;
                }
                return s;
            }
            double sleepy = SleepSeconds(CatPersonalities.Sleepyhead());
            double energetic = SleepSeconds(CatPersonalities.Energizer());
            Assert.Greater(sleepy, energetic * 2, $"잠꾸러기 {sleepy:0}초 vs 에너자이저 {energetic:0}초");
        }

        [Test]
        public void AllPresetsHaveDistinctIdsAndNames()
        {
            var ps = CatPersonalities.All.Select(f => f()).ToList();
            Assert.AreEqual(ps.Count, ps.Select(p => p.Id).Distinct().Count());
            Assert.AreEqual(ps.Count, ps.Select(p => p.DisplayName).Distinct().Count());
        }
    }
}
