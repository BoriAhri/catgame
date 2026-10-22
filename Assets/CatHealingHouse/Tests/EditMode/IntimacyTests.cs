using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Save;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>친밀도 레벨 (Lv.1~99): 간식·쓰다듬기 경험치, 스킬 포인트, 길냥이 들일 때 시작 레벨, 저장.</summary>
    public class IntimacyTests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private Wallet _wallet;
        private Intimacy _int;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world), seed: 3);
            _wallet = new Wallet { Money = 10000 };
            _int = new Intimacy(_wallet);
        }

        private Cat NewCat() => _sim.AddCat("냥", new Cell(BoardId.Indoor, HexCoord.Zero));

        [Test]
        public void NewCatStartsAtLevelOne()
        {
            var c = NewCat();
            Assert.AreEqual(1, c.IntimacyLevel);
            Assert.AreEqual(0, c.SkillPoints);
        }

        [Test]
        public void NeedGrowsWithLevel_AndMaxTakesLong()
        {
            Assert.AreEqual(13, Intimacy.Need(1));
            Assert.Greater(Intimacy.Need(50), Intimacy.Need(10));
            Assert.Greater(Intimacy.TotalTo(99), 50000);
        }

        [Test]
        public void LevelUpGivesSkillPoint_AndCarriesOverflow()
        {
            var c = NewCat();
            int ups = 0;
            _int.LevelUp += (cat, lv) => ups++;
            _int.Gain(c, Intimacy.Need(1) + Intimacy.Need(2) + 1);
            Assert.AreEqual(3, c.IntimacyLevel);
            Assert.AreEqual(2, c.SkillPoints);
            Assert.AreEqual(2, ups);
            Assert.AreEqual(1, c.IntimacyExp, 1e-9);
        }

        [Test]
        public void LevelCapsAt99()
        {
            var c = NewCat();
            _int.Gain(c, Intimacy.TotalTo(99) + 5000);
            Assert.AreEqual(99, c.IntimacyLevel);
            Assert.AreEqual(98, c.SkillPoints);
            Assert.AreEqual(0, c.IntimacyExp);
            Assert.AreEqual(1, _int.Progress(c));
        }

        [Test]
        public void SnackCostsCoins_AndGivesExp()
        {
            var c = NewCat();
            c.Needs.Set(NeedType.Hunger, 50);
            Assert.AreEqual(SnackResult.Ok, _int.FeedSnack(c, 100, out var exp));
            Assert.AreEqual(10, exp);
            Assert.AreEqual(10000 - 50, _wallet.Money);
            Assert.AreEqual(10, c.IntimacyExp, 1e-9);
            Assert.Greater(c.Needs.Hunger, 50);
        }

        [Test]
        public void SnackNeedsMoney()
        {
            var c = NewCat();
            _wallet.Money = 49;
            Assert.AreEqual(SnackResult.NoMoney, _int.FeedSnack(c, 100, out _));
            Assert.AreEqual(49, _wallet.Money);
            Assert.AreEqual(0, c.IntimacyExp);
        }

        [Test]
        public void SnackExpDropsAfterThreeADay_AndResetsNextDay()
        {
            var c = NewCat();
            double[] got = new double[6];
            for (int i = 0; i < 6; i++) _int.FeedSnack(c, 7, out got[i]);
            Assert.AreEqual(10, got[0]); Assert.AreEqual(10, got[1]); Assert.AreEqual(10, got[2]);
            Assert.AreEqual(5, got[3], 1e-9);
            Assert.Less(got[4], got[3]);
            Assert.GreaterOrEqual(got[5], 1);
            Assert.AreEqual(10, _int.NextSnackExp(c, 8), "다음 날은 다시 10");
        }

        [Test]
        public void StraysCannotEatProfileSnack()
        {
            var s = _sim.AddStray("길", new Cell(BoardId.Outdoor, HexCoord.Zero), 1);
            Assert.AreEqual(SnackResult.NotResident, _int.FeedSnack(s, 1, out _));
            Assert.AreEqual(10000, _wallet.Money);
        }

        [Test]
        public void SatisfiedPettingGivesALittleExp()
        {
            var c = NewCat();
            var pet = new Petting(_sim, _wallet, null) { Intimacy = _int };
            var s = pet.Begin(c);
            for (int i = 0; i < 200 && !s.Done; i++) pet.Stroke(s, PetZone.Head, 0.1);
            Assert.AreEqual(PetEnd.Satisfied, s.End);
            Assert.AreEqual(_int.Tuning.PetExp, s.ExpGiven);
            Assert.Less(s.ExpGiven, _int.Tuning.SnackExp, "쓰다듬기는 간식보다 조금");
            Assert.AreEqual(_int.Tuning.PetExp, c.IntimacyExp, 1e-9);
        }

        [Test]
        public void AnnoyedPettingGivesNoExp()
        {
            var c = NewCat();
            var pet = new Petting(_sim, _wallet, null) { Intimacy = _int };
            var s = pet.Begin(c);
            for (int i = 0; i < 200 && !s.Done; i++) pet.Stroke(s, PetZone.Tail, 0.1);
            Assert.AreEqual(PetEnd.Annoyed, s.End);
            Assert.AreEqual(0, c.IntimacyExp);
        }

        [Test]
        public void AdoptionStartLevel_ByTemperamentAndAffection()
        {
            Assert.AreEqual(1, Intimacy.StartLevel(Temperament.Cuddly, 79));
            Assert.AreEqual(3, Intimacy.StartLevel(Temperament.Cuddly, 80));
            Assert.AreEqual(5, Intimacy.StartLevel(Temperament.Cuddly, 100));
            Assert.AreEqual(1, Intimacy.StartLevel(Temperament.Normal, 99));
            Assert.AreEqual(3, Intimacy.StartLevel(Temperament.Normal, 100));
            Assert.AreEqual(1, Intimacy.StartLevel(Temperament.Sassy, 100));

            var c = NewCat();
            _int.OnAdopted(c, Temperament.Cuddly, 100);
            Assert.AreEqual(5, c.IntimacyLevel);
            Assert.AreEqual(4, c.SkillPoints, "얹어 준 레벨만큼 스킬 포인트도");
        }

        [Test]
        public void IntimacySurvivesSaveAndLoad()
        {
            var g = Stage6Like();
            var c = g.Sim.AddCat("저장냥", new Cell(BoardId.Indoor, HexCoord.Zero));
            c.IntimacyLevel = 7; c.IntimacyExp = 12.5; c.SkillPoints = 6; c.SnackDay = 42; c.SnacksToday = 2;
            var data = SaveGame.Capture(g, System.DateTime.UtcNow);
            var json = UnityEngine.JsonUtility.ToJson(data);

            var g2 = Stage6Like();
            var cats = SaveGame.Restore(g2, UnityEngine.JsonUtility.FromJson<SaveData>(json));
            var r = cats[0];
            Assert.AreEqual(7, r.IntimacyLevel);
            Assert.AreEqual(12.5, r.IntimacyExp, 1e-9);
            Assert.AreEqual(6, r.SkillPoints);
            Assert.AreEqual(42, r.SnackDay);
            Assert.AreEqual(2, r.SnacksToday);
        }

        [Test]
        public void OldSaveWithoutIntimacyLoadsAsLevelOne()
        {
            var g = Stage6Like();
            var data = new SaveData();
            data.Cats.Add(new CatSave { Name = "옛냥", Hunger = 80, Bladder = 80, Energy = 80, Fun = 80, Mood = 70 });
            var cats = SaveGame.Restore(g, data);
            Assert.AreEqual(1, cats[0].IntimacyLevel);
        }

        private static GameState Stage6Like()
        {
            var world = new HouseWorld(3, 3);
            var sim = new CatSimulation(world, new DistanceMapCache(world));
            var eco = new HouseEconomy(sim);
            return new GameState { World = world, Sim = sim, Economy = eco, Shop = new Shop(world, eco, grantStart: false) };
        }
    }
}
