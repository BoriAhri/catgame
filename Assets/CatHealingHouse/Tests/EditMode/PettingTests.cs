using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>8단계: 가까이 보기 쓰다듬기.</summary>
    public class PettingTests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private HouseEconomy _eco;
        private StraySystem _strays;
        private Petting _pet;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world));
            _eco = new HouseEconomy(_sim);
            _strays = new StraySystem(_sim, _eco);
            _pet = new Petting(_sim, _eco.Wallet, _strays);
        }

        private Cat Resident(System.Func<CatPersonality> p = null)
        {
            var c = _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero), p != null ? p() : null);
            c.Mood = 50;
            return c;
        }

        [Test]
        public void LovedSpotsFillSatisfactionAndGiveMoodAndHeart()
        {
            var cat = Resident();
            long hearts = _eco.Wallet.Hearts;
            var s = _pet.Begin(cat);
            Assert.IsTrue(cat.Held);
            for (int i = 0; i < 40 && !s.Done; i++) _pet.Stroke(s, PetZone.Head, 0.2);
            Assert.AreEqual(PetEnd.Satisfied, s.End);
            Assert.IsFalse(cat.Held, "끝나면 풀어준다");
            Assert.AreEqual(hearts + 1, _eco.Wallet.Hearts);
            Assert.That(cat.Mood, Is.EqualTo(50 + _pet.Tuning.MoodPerSession).Within(0.01));
        }

        [Test]
        public void DislikedSpotsAnnoyAndEndBadly()
        {
            var cat = Resident();
            var s = _pet.Begin(cat);
            Assert.AreEqual(PetFeel.Dislike, s.Feel(PetZone.Belly));
            for (int i = 0; i < 40 && !s.Done; i++) _pet.Stroke(s, PetZone.Belly, 0.2);
            Assert.AreEqual(PetEnd.Annoyed, s.End);
            Assert.Less(cat.Mood, 50);
            Assert.AreEqual(0, s.HeartsGiven);
        }

        [Test]
        public void HeartHasCooldownAndFatigueSlowsRepeats()
        {
            var cat = Resident();
            var s1 = _pet.Begin(cat);
            int strokes1 = 0;
            while (!s1.Done) { _pet.Stroke(s1, PetZone.Cheek, 0.1); strokes1++; }
            var s2 = _pet.Begin(cat);
            int strokes2 = 0;
            while (!s2.Done && strokes2 < 10000) { _pet.Stroke(s2, PetZone.Cheek, 0.1); strokes2++; }
            Assert.Greater(strokes2, strokes1, "바로 또 쓰다듬으면 더 오래 걸린다");
            Assert.AreEqual(0, s2.HeartsGiven);
            Assert.IsTrue(s2.HeartOnCooldown);

            // 시간이 지나면 피로가 풀리고 하트도 다시
            for (int i = 0; i < 2000; i++) _sim.Step(1.0);
            Assert.AreEqual(0, _pet.FatigueOf(cat), 1e-9);
            Assert.AreEqual(0, _pet.HeartWait(cat), 1e-9);
        }

        [Test]
        public void PersonalitiesChangePreferences()
        {
            Assert.AreEqual(PetFeel.Dislike, PetPrefs.ForPersonality("normal", PetZone.Belly));
            Assert.AreEqual(PetFeel.Like, PetPrefs.ForPersonality("sleepyhead", PetZone.Belly));
            Assert.AreEqual(PetFeel.Like, PetPrefs.ForStray(Temperament.Sassy, PetZone.Head));
            Assert.AreEqual(PetFeel.Like, PetPrefs.ForStray(Temperament.Cuddly, PetZone.Belly), "개냥이는 배도 내준다");
            Assert.AreEqual(PetFeel.Like, PetPrefs.ForPersonality("normal", PetZone.Rump));
            Assert.AreEqual(PetFeel.Dislike, PetPrefs.ForStray(Temperament.Cuddly, PetZone.Tail));
        }

        [Test]
        public void SkillMakesItFaster()
        {
            var a = Resident();
            var b = Resident();
            var sa = _pet.Begin(a);
            _pet.SkillLevel = () => 3;
            var sb = _pet.Begin(b);
            _pet.Stroke(sa, PetZone.Head, 0.5);
            _pet.Stroke(sb, PetZone.Head, 0.5);
            Assert.Greater(sb.Satisfaction, sa.Satisfaction);
        }

        [Test]
        public void StrayGetsAffectionBonusOncePerVisit()
        {
            var p = _strays.Profiles[0];
            p.Affection = 50;
            var cat = Resident();
            cat.Affiliation = Affiliation.Stray;
            cat.StrayId = p.Id;
            var s = _pet.Begin(cat);
            while (!s.Done) _pet.Stroke(s, PetZone.Head, 0.2);
            Assert.AreEqual(PetEnd.Satisfied, s.End);
            Assert.AreEqual(_pet.Tuning.StrayBonusAffection, s.AffectionGiven, 1e-9);
            Assert.AreEqual(50 + _pet.Tuning.StrayBonusAffection, p.Affection, 1e-9);
            var s2 = _pet.Begin(cat);
            while (!s2.Done && s2.Satisfaction < 1) _pet.Stroke(s2, PetZone.Head, 0.2);
            Assert.AreEqual(0, s2.AffectionGiven, 1e-9, "같은 방문에는 한 번만");
        }

        [Test]
        public void HeldResidentStaysPut()
        {
            var cat = Resident();
            var s = _pet.Begin(cat);
            var at = cat.Cell;
            for (int i = 0; i < 300; i++) _sim.Step(0.1);
            Assert.AreEqual(at, cat.Cell);
            _pet.Stop(s);
            Assert.AreEqual(PetEnd.Stopped, s.End);
            Assert.IsFalse(cat.Held);
        }
    

        [Test]
        public void RollsOverAtHalfSatisfaction()
        {
            var cat = Resident();
            var s = _pet.Begin(cat);
            Assert.IsFalse(s.BellyUp);
            while (s.Satisfaction < _pet.Tuning.RollAt) _pet.Stroke(s, PetZone.Head, 0.05);
            Assert.IsTrue(s.BellyUp, "반쯤 차면 스스로 발라당");
            Assert.IsTrue(s.Rolled);
        }

        [Test]
        public void BellyLoverEnjoysBellyWhenRolled()
        {
            var cat = Resident(CatPersonalities.Sleepyhead);
            var s = _pet.Begin(cat);
            Assert.IsTrue(s.BellyLover);
            while (!s.BellyUp) _pet.Stroke(s, PetZone.Head, 0.05);
            double before = s.Satisfaction;
            Assert.AreEqual(PetFeel.Love, _pet.Stroke(s, PetZone.Belly, 0.1));
            Assert.Greater(s.Satisfaction, before);
            Assert.IsTrue(s.BellyUp, "좋아하면 계속 누워 있다");
        }

        [Test]
        public void BellyTrapKicksAndGetsUp()
        {
            var cat = Resident();
            var s = _pet.Begin(cat);
            Assert.IsFalse(s.BellyLover);
            while (!s.BellyUp) _pet.Stroke(s, PetZone.Head, 0.05);
            double annoy = s.Annoyance;
            Assert.AreEqual(PetFeel.Dislike, _pet.Stroke(s, PetZone.Belly, 0.01));
            Assert.AreEqual(1, s.Kicks);
            Assert.IsFalse(s.BellyUp, "뒷발 팡팡 하고 일어난다");
            Assert.Greater(s.Annoyance, annoy + 0.3);
            // 한 세션에 발라당은 한 번만
            while (!s.Done && s.Satisfaction < 0.99) _pet.Stroke(s, PetZone.Head, 0.05);
            Assert.IsFalse(s.BellyUp);
        }
}
}
