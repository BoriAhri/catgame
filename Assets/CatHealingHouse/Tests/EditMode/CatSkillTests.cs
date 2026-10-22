using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Save;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>고양이 패시브 스킬: 스킬 포인트로 올리기, 효과 (트레이·하트·털뭉치·수염), 저장.</summary>
    public class CatSkillTests
    {
        private static Cat NewCat(CatSimulation sim) => sim.AddCat("냥", new Cell(BoardId.Indoor, HexCoord.Zero));

        private static CatSimulation NewSim(out HouseWorld world)
        {
            world = new HouseWorld(3, 3);
            return new CatSimulation(world, new DistanceMapCache(world), seed: 11);
        }

        [Test]
        public void UpgradeSpendsOnePoint_AndCapsAtMax()
        {
            var sim = NewSim(out _);
            var c = NewCat(sim);
            Assert.IsFalse(CatSkills.Upgrade(c, CatSkills.Coin), "포인트가 없으면 못 올린다");
            c.SkillPoints = 30;
            for (int i = 0; i < 30; i++) CatSkills.Upgrade(c, CatSkills.Coin);
            Assert.AreEqual(25, CatSkills.Level(c, CatSkills.Coin));
            Assert.AreEqual(5, c.SkillPoints, "최대 레벨을 넘으면 포인트를 안 쓴다");
        }

        [Test]
        public void MaxTotalIsMoreThanAllPoints()
        {
            int total = 0;
            foreach (var d in CatSkills.Defs) total += d.Max;
            Assert.Greater(total, 98, "Lv.99 까지 받는 포인트로 전부 최대는 못 찍는다");
            Assert.AreEqual(4, CatSkills.Defs.Length);
        }

        [Test]
        public void CoinSkillAddsExtraTrayItems()
        {
            int Count(int lv)
            {
                var sim = NewSim(out var world);
                var eco = new HouseEconomy(sim);
                eco.Tuning.TraySlotCap = 100000;
                Assert.AreEqual(PlaceResult.Ok, world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl));
                var c = NewCat(sim);
                CatSkills.SetLevel(c, CatSkills.Coin, lv);
                var m = typeof(HouseEconomy).GetMethod("OnInteractionCompleted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (int i = 0; i < 2000; i++) m.Invoke(eco, new object[] { c, bowl });
                return eco.Tray[0].Count;
            }
            int none = Count(0), full = Count(25);
            Assert.AreEqual(2000, none);
            Assert.Greater(full, 2000 * 1.4, "25레벨이면 약 50% 더");
            Assert.Less(full, 2000 * 1.6);
        }

        [Test]
        public void HeartSkillLetsHeartReappearSooner()
        {
            var sim = NewSim(out var world);
            var eco = new HouseEconomy(sim);
            Assert.AreEqual(PlaceResult.Ok, world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl));
            sim.Tuning.HungerDecay = sim.Tuning.BladderDecay = sim.Tuning.EnergyDecay = sim.Tuning.FunDecay = 0; // 저절로 가구를 쓰지 않게
            var plain = NewCat(sim);
            var skilled = NewCat(sim);
            CatSkills.SetLevel(skilled, CatSkills.Heart, 25);
            var m = typeof(HouseEconomy).GetMethod("OnInteractionCompleted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            m.Invoke(eco, new object[] { plain, bowl });
            Assert.IsTrue(eco.CollectHeart(bowl.Id));
            // 대기시간의 70%만 지났을 때: 보통 고양이는 하트가 안 뜨고, 하트 뿜뿜 25레벨(-37.5%)이면 뜬다
            for (double t = 0; t < eco.Tuning.HeartCooldown * 0.7; t += 1) sim.Step(1);
            m.Invoke(eco, new object[] { plain, bowl });
            Assert.IsNull(eco.HeartOn(bowl.Id));
            m.Invoke(eco, new object[] { skilled, bowl });
            Assert.IsNotNull(eco.HeartOn(bowl.Id));
        }

        [Test]
        public void DropSkillsScaleChances()
        {
            var sim = NewSim(out _);
            var c = NewCat(sim);
            Assert.AreEqual(1.0, CatSkills.FurballScale(c));
            CatSkills.SetLevel(c, CatSkills.Furball, 10);
            CatSkills.SetLevel(c, CatSkills.Whisker, 20);
            Assert.AreEqual(1.3, CatSkills.FurballScale(c), 1e-9);
            Assert.AreEqual(1.6, CatSkills.WhiskerScale(c), 1e-9);
        }

        [Test]
        public void SkillsSurviveSaveAndLoad()
        {
            GameState G()
            {
                var w = new HouseWorld(3, 3);
                var s = new CatSimulation(w, new DistanceMapCache(w));
                var e = new HouseEconomy(s);
                return new GameState { World = w, Sim = s, Economy = e, Shop = new Shop(w, e, grantStart: false) };
            }
            var g = G();
            var c = g.Sim.AddCat("저장냥", new Cell(BoardId.Indoor, HexCoord.Zero));
            CatSkills.SetLevel(c, CatSkills.Coin, 4);
            CatSkills.SetLevel(c, CatSkills.Whisker, 2);
            var json = UnityEngine.JsonUtility.ToJson(SaveGame.Capture(g, System.DateTime.UtcNow));
            var cats = SaveGame.Restore(G(), UnityEngine.JsonUtility.FromJson<SaveData>(json));
            Assert.AreEqual(4, CatSkills.Level(cats[0], CatSkills.Coin));
            Assert.AreEqual(2, CatSkills.Level(cats[0], CatSkills.Whisker));
            Assert.AreEqual(0, CatSkills.Level(cats[0], CatSkills.Heart));
        }
    }
}
