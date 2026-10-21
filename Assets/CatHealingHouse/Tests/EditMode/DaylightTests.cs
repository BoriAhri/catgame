using System.Collections.Generic;
using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>하루 시계, 창문 빛, 햇빛 따라가는 고양이.</summary>
    public class DaylightTests
    {
        [Test]
        public void ClockRunsOneDayIn24RealMinutes()
        {
            var clock = new DayClock(7);
            Assert.AreEqual(7, clock.Hour, 1e-9);
            clock.Advance(60); // 현실 1분 = 게임 1시간
            Assert.AreEqual(8, clock.Hour, 1e-9);
            clock.Advance(23 * 60);
            Assert.AreEqual(7, clock.Hour, 1e-6);
            Assert.AreEqual(2, clock.Day);
            clock.SetHour(18.5);
            Assert.AreEqual(18.5, clock.Hour, 1e-6);
            Assert.AreEqual("오후 6:30", clock.Label);
        }

        [Test]
        public void SunByDayMoonByNight()
        {
            Assert.AreEqual(LightKind.Sun, Daylight.Kind(12));
            Assert.AreEqual(LightKind.Moon, Daylight.Kind(23));
            Assert.AreEqual(LightKind.Moon, Daylight.Kind(2));
            Assert.AreEqual(LightKind.None, Daylight.Kind(5.5));
            Assert.Greater(Daylight.Strength(12), Daylight.Strength(23), "달빛은 햇빛보다 약하다");
        }

        private static double CenterX(IEnumerable<HexCoord> cells)
        {
            var layout = new HexLayout(1, 1);
            double sum = 0; int n = 0;
            foreach (var c in cells) { layout.ToWorld(c, out double x, out _); sum += x; n++; }
            return n == 0 ? 0 : sum / n;
        }

        [Test]
        public void WindowLightMovesAcrossTheFloorDuringTheDay()
        {
            var world = new HouseWorld(4, 3);
            var morning = WindowLight.LitCells(world.Indoor, 8);
            var noon = WindowLight.LitCells(world.Indoor, 12);
            var evening = WindowLight.LitCells(world.Indoor, 16);
            Assert.Greater(morning.Count, 0);
            Assert.Greater(noon.Count, 0);
            Assert.Less(CenterX(morning), CenterX(noon), "아침 빛은 왼쪽으로");
            Assert.Greater(CenterX(evening), CenterX(noon), "저녁 빛은 오른쪽으로");
            Assert.Less(noon.Count, morning.Count, "낮에는 창 아래 짧게");
            foreach (var c in morning.Concat(noon).Concat(evening))
                Assert.AreEqual(TileKind.Floor, world.Indoor.Get(c).Kind, "빛은 바닥에만");
            Assert.AreEqual(0, WindowLight.LitCells(world.Indoor, 5.5).Count, "새벽 해 뜨기 전엔 없음");
            Assert.Greater(WindowLight.LitCells(world.Indoor, 0.5).Count, 0, "한밤에는 달빛");
        }

        [Test]
        public void WindowsFollowTheWallWhenExpanded()
        {
            var world = new HouseWorld(4, 3);
            world.Expand(BoardId.Indoor);
            var w = WindowLight.WindowCells(world.Indoor);
            Assert.AreEqual(2, w.Count);
            foreach (var c in w) Assert.AreEqual(TileKind.Wall, world.Indoor.Get(c).Kind);
        }

        // ───────────── 햇빛 쬐기 ─────────────

        private CatSimulation SimWithSun(out HouseWorld world, out double[] hour)
        {
            world = new HouseWorld(4, 3);
            var sim = new CatSimulation(world, new DistanceMapCache(world), seed: 3);
            sim.Tuning.HungerDecay = sim.Tuning.BladderDecay = sim.Tuning.EnergyDecay = sim.Tuning.FunDecay = 0;
            var h = new[] { 9.0 };
            hour = h;
            var w = world;
            sim.IsSunny = c => c.Board == BoardId.Indoor && Daylight.Kind(h[0]) == LightKind.Sun && WindowLight.LitCells(w.Indoor, h[0]).Contains(c.Coord);
            return sim;
        }

        [Test]
        public void SunLoversSpendTimeInTheWindowLight()
        {
            var sim = SimWithSun(out var world, out var hour);
            var cat = sim.AddCat("잠", new Cell(BoardId.Indoor, new HexCoord(0, 2)), CatPersonalities.Sleepyhead());
            double inSun = 0;
            for (int i = 0; i < 6000; i++)
            {
                sim.Step(CatSimulation.FixedStep);
                if (cat.Sunbathing && cat.State == CatState.Lounging) inSun += CatSimulation.FixedStep;
            }
            Assert.Greater(inSun, 60, "10분 중 1분 넘게 햇빛에서 뒹군다");
        }

        [Test]
        public void SunbathingCatFollowsTheMovingLight()
        {
            var sim = SimWithSun(out var world, out var hour);
            var cat = sim.AddCat("잠", new Cell(BoardId.Indoor, new HexCoord(0, 0)), CatPersonalities.Sleepyhead());
            sim.Tuning.SunLoungeMinSeconds = sim.Tuning.SunLoungeMaxSeconds = 5000; // 오래 누워 있게
            for (int i = 0; i < 6000 && !(cat.Sunbathing && cat.State == CatState.Lounging); i++) sim.Step(CatSimulation.FixedStep);
            Assert.IsTrue(cat.Sunbathing && cat.State == CatState.Lounging, "햇빛 자리에 누워야 한다");
            var first = cat.Cell;

            // 해가 움직여서 지금 칸에 빛이 안 들 때까지 시간을 돌린다
            int moves = 0;
            for (double h = 9.2; h <= 15.0; h += 0.2)
            {
                hour[0] = h;
                var before = cat.Cell;
                for (int i = 0; i < 300; i++) sim.Step(CatSimulation.FixedStep);
                if (cat.Cell != before) moves++;
                if (cat.Sunbathing) Assert.IsTrue(sim.IsSunny(cat.Cell) || cat.State == CatState.ToSpot, $"{h}시: 햇빛 밖에 누워 있으면 안 된다");
            }
            Assert.Greater(moves, 0, "빛을 따라 자리를 옮긴다");
            Assert.AreNotEqual(first, cat.Cell);
        }

        [Test]
        public void NoSunbathingAtNight()
        {
            var sim = SimWithSun(out var world, out var hour);
            hour[0] = 23;
            var cat = sim.AddCat("잠", new Cell(BoardId.Indoor, new HexCoord(0, 0)), CatPersonalities.Sleepyhead());
            for (int i = 0; i < 6000; i++)
            {
                sim.Step(CatSimulation.FixedStep);
                Assert.IsFalse(cat.Sunbathing);
            }
        }

        [Test]
        public void TwoCatsDoNotStackOnTheSameSunCell()
        {
            var sim = SimWithSun(out var world, out var hour);
            sim.Tuning.SunbathChance = 1;
            var a = sim.AddCat("a", new Cell(BoardId.Indoor, new HexCoord(0, 0)), CatPersonalities.Sleepyhead());
            var b = sim.AddCat("b", new Cell(BoardId.Indoor, new HexCoord(1, 0)), CatPersonalities.Sleepyhead());
            for (int i = 0; i < 6000; i++)
            {
                sim.Step(CatSimulation.FixedStep);
                if (a.Sunbathing && b.Sunbathing && a.State == CatState.Lounging && b.State == CatState.Lounging)
                    Assert.AreNotEqual(a.Cell, b.Cell);
            }
        }
    }
}
