using System;
using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class Stage4Tests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private HouseEconomy _eco;
        private StraySystem _strays;
        private FurnitureInstance _bowl;
        private FurnitureInstance _box;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world));
            _eco = new HouseEconomy(_sim);
            _strays = new StraySystem(_sim, _eco, defaultRoster: false);
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(0, 1), false, out _bowl));
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.BoxSmall, BoardId.Outdoor, new HexCoord(2, -1), false, out _box));
        }

        private StrayProfile Profile(string name, double affection, Temperament t = Temperament.Normal) =>
            _strays.AddProfile(name, Breed.Cheese, affection, CatPersonalities.Foodie, temperament: t);

        private bool RunUntil(Func<bool> done, double seconds = 120)
        {
            for (double t = 0; t < seconds; t += CatSimulation.FixedStep)
            {
                if (done()) return true;
                _sim.Step(CatSimulation.FixedStep);
            }
            return done();
        }

        private Cat SpawnNow()
        {
            _strays.FillBowl(_bowl.Id);
            var cat = _strays.TrySpawn();
            Assert.IsNotNull(cat);
            return cat;
        }

        // ───────────── 등장·식사·구간 행동 ─────────────

        [Test]
        public void NoSpawnWithoutFood()
        {
            Profile("a", 0);
            Assert.IsNull(_strays.TrySpawn());
            RunUntil(() => false, 60);
            Assert.AreEqual(0, _strays.StrayCount);
        }

        [Test]
        public void SpawnsAtTopEdgeAndAtMostTwo()
        {
            Profile("a", 0); Profile("b", 0); Profile("c", 0);
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(-2, 2), false, out var bowl2));
            _strays.FillBowl(_bowl.Id);
            _strays.FillBowl(bowl2.Id);
            var c1 = _strays.TrySpawn();
            var c2 = _strays.TrySpawn();
            Assert.IsNotNull(c1); Assert.IsNotNull(c2); Assert.IsNull(_strays.TrySpawn());
            Assert.AreEqual(TileKind.Edge, _world.GetTile(c1.Cell).Kind);
            Assert.Less(c1.Cell.Coord.R, 0, "집 반대편 위쪽에서만 나온다");
            Assert.Less(c2.Cell.Coord.R, 0);
            Assert.AreNotEqual(c1.StrayId, c2.StrayId);
        }

        [Test]
        public void TierBoundaries()
        {
            Assert.AreEqual(AffectionTier.Wary, StraySystem.TierOf(20));
            Assert.AreEqual(AffectionTier.Shy, StraySystem.TierOf(21));
            Assert.AreEqual(AffectionTier.Shy, StraySystem.TierOf(40));
            Assert.AreEqual(AffectionTier.Friendly, StraySystem.TierOf(41));
            Assert.AreEqual(AffectionTier.Friendly, StraySystem.TierOf(60));
            Assert.AreEqual(AffectionTier.Close, StraySystem.TierOf(61));
            Assert.AreEqual(AffectionTier.Close, StraySystem.TierOf(80));
            Assert.AreEqual(AffectionTier.Bonded, StraySystem.TierOf(81));
            Assert.AreEqual(AffectionTier.Bonded, StraySystem.TierOf(100));
        }

        [Test]
        public void LowAffectionEatsThenLeaves_AffectionPersists()
        {
            var p = Profile("a", 0);
            var cat = SpawnNow();
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Eating));
            Assert.IsTrue(RunUntil(() => !_sim.Cats.Contains(cat)), "먹고 떠나야 한다");
            Assert.Greater(p.Affection, 0);
            Assert.AreEqual(_strays.Tuning.BowlPortions - 1, _strays.FoodIn(_bowl.Id));
            Assert.AreEqual(1, p.Meals);
        }

        [Test]
        public void FriendlyLoafsInOutdoorBox()
        {
            Profile("a", 45);
            var cat = SpawnNow();
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Loafing));
            Assert.AreEqual(_box.Id, cat.TargetFurnitureId);
        }

        [Test]
        public void CloseComesToTheDoor()
        {
            Profile("a", 62);
            var cat = SpawnNow();
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Peeking));
            Assert.AreEqual(1, HexCoord.Distance(cat.Cell.Coord, _world.Outdoor.DoorCoord));
        }

        [Test]
        public void BondedWaitsLongAtTheDoor()
        {
            Profile("a", 85);
            var cat = SpawnNow();
            bool ready = false;
            _strays.BecameReady += (c, _) => ready = true;
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Peeking));
            Assert.IsTrue(ready);
            Assert.GreaterOrEqual(cat.StrayTimer, _strays.Tuning.BondedWaitMin - 1);
        }

        // ───────────── 문 위치·마당 진입 ─────────────

        [Test]
        public void YardDoorIsAtBottomCenterAndStraysComeFromTop()
        {
            var door = _world.Outdoor.DoorCoord;
            Assert.AreEqual(4, door.R, "맨 아래 줄");
            new HexLayout(1).ToWorld(door, out double dx, out _);
            Assert.AreEqual(0.0, dx, 1e-6, "가운데");
            Assert.IsTrue(_strays.FarEdgeCells().All(c => c.Coord.R < 0));
            Assert.IsTrue(_strays.FarEdgeCells(true).All(c => c.Coord.R == -4));
            _world.Expand(BoardId.Outdoor);
            Assert.AreEqual(5, _world.Outdoor.DoorCoord.R);
        }

        [Test]
        public void EnteringYardScaresOnly0to20_ToTopRow()
        {
            var low = Profile("low", 20);
            var cat = SpawnNow();
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Eating));
            double before = low.Affection;
            Assert.AreEqual(1, _strays.NotifyBoardEntered(BoardId.Outdoor));
            Assert.AreEqual(StrayState.Fleeing, cat.StrayState);
            Assert.AreEqual(StrayReaction.Alert, cat.Reaction);
            Assert.AreEqual(before, low.Affection, "문 여는 건 플레이어 잘못이 아니니 호감도는 그대로");
            Cell last = cat.Cell;
            Assert.IsTrue(RunUntil(() => { if (_sim.Cats.Contains(cat)) last = cat.Cell; return !_sim.Cats.Contains(cat); }, 30));
            Assert.AreEqual(-4, last.Coord.R, "맨 윗줄(12시)로 도망간다");
        }

        [Test]
        public void EnteringYardDoesNotScare21Plus()
        {
            Profile("shy", 21);
            var cat = SpawnNow();
            Assert.AreEqual(0, _strays.NotifyBoardEntered(BoardId.Outdoor));
            Assert.AreNotEqual(StrayState.Fleeing, cat.StrayState);
        }

        // ───────────── 터치 ─────────────

        [Test]
        public void Wary_FleesFromTouchWithin3()
        {
            var p = Profile("a", 10);
            var cat = SpawnNow();
            double before = p.Affection;
            var near = new Cell(BoardId.Outdoor, cat.Cell.Coord + HexCoord.Direction(4) * 3);
            Assert.AreEqual(1, _strays.Presence(near));
            Assert.AreEqual(StrayState.Fleeing, cat.StrayState);
            Assert.Less(p.Affection, before);
            Assert.IsFalse(cat.HasTarget, "밥그릇 자리를 놓아야 한다");
            Assert.IsTrue(RunUntil(() => !_sim.Cats.Contains(cat), 30));
        }

        [Test]
        public void Shy_FleesOnlyWhenTouchedOrNextTo()
        {
            Profile("a", 30);
            var cat = SpawnNow();
            var two = new Cell(BoardId.Outdoor, cat.Cell.Coord + HexCoord.Direction(4) * 2);
            Assert.AreEqual(0, _strays.Presence(two), "2칸 떨어진 터치는 괜찮다");
            Assert.AreNotEqual(StrayState.Fleeing, cat.StrayState);
            var one = new Cell(BoardId.Outdoor, cat.Cell.Coord + HexCoord.Direction(4));
            Assert.AreEqual(1, _strays.Presence(one));
            Assert.AreEqual(StrayState.Fleeing, cat.StrayState);
        }

        [Test]
        public void Friendly_DoesNotFleeFromTouch_AndOpensMenu()
        {
            Profile("a", 41);
            var cat = SpawnNow();
            Assert.AreEqual(0, _strays.Presence(cat.Cell, cat));
            Assert.AreNotEqual(StrayState.Fleeing, cat.StrayState);
            Assert.IsTrue(_strays.MenuAvailable(cat));
        }

        [Test]
        public void MenuNotAvailableBelow41()
        {
            Profile("a", 40);
            var cat = SpawnNow();
            Assert.IsFalse(_strays.MenuAvailable(cat));
            Assert.AreEqual(InteractOutcome.Unavailable, _strays.Interact(cat, StrayAction.Snack).Outcome);
        }

        [Test]
        public void GraceAfterEnteringIgnoresTouches()
        {
            Profile("a", 30);
            var cat = SpawnNow();
            _strays.NotifyBoardEntered(BoardId.Outdoor);
            Assert.AreEqual(0, _strays.Presence(cat.Cell, cat));
            Assert.AreNotEqual(StrayState.Fleeing, cat.StrayState);
        }

        // ───────────── 메뉴 ─────────────

        [Test]
        public void SnackAndPlay_AlwaysAccepted_OncePerVisit()
        {
            var p = Profile("a", 50, Temperament.Sassy);
            var cat = SpawnNow();
            var r = _strays.Interact(cat, StrayAction.Snack, roll: 0.999);
            Assert.AreEqual(InteractOutcome.Accepted, r.Outcome);
            Assert.AreEqual(_strays.Tuning.SnackAffection, r.AffectionDelta, 1e-9);
            Assert.AreEqual(InteractOutcome.AlreadyDone, _strays.Interact(cat, StrayAction.Snack).Outcome);
            Assert.AreEqual(InteractOutcome.Accepted, _strays.Interact(cat, StrayAction.Play, roll: 0.999).Outcome);
            Assert.AreEqual(InteractOutcome.AlreadyDone, _strays.Interact(cat, StrayAction.Play).Outcome);
            Assert.AreEqual(50 + _strays.Tuning.SnackAffection + _strays.Tuning.PlayAffection, p.Affection, 1e-9);
        }

        [Test]
        public void ActivityHoldsTheCatInPlace()
        {
            Profile("a", 50);
            var cat = SpawnNow();
            var at = cat.Cell;
            _strays.Interact(cat, StrayAction.Play, roll: 0);
            for (int i = 0; i < 20; i++) _sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(at, cat.Cell);
            Assert.AreEqual((int)StrayAction.Play + 1, cat.Activity);
            _strays.Hold(cat, true);
            RunUntil(() => false, 10);
            Assert.AreEqual(at, cat.Cell, "메뉴가 열려 있으면 기다린다");
            _strays.Hold(cat, false);
            Assert.IsTrue(RunUntil(() => cat.Cell != at, 10));
        }

        [Test]
        public void PetAccepted_GivesAffectionOnlyOncePerVisit()
        {
            var p = Profile("a", 50, Temperament.Cuddly);
            var cat = SpawnNow();
            var r1 = _strays.Interact(cat, StrayAction.Pet, roll: 0);
            Assert.AreEqual(InteractOutcome.Accepted, r1.Outcome);
            Assert.AreEqual(StrayReaction.Love, cat.Reaction);
            Assert.AreEqual(_strays.Tuning.PetAffection, r1.AffectionDelta, 1e-9);
            RunUntil(() => cat.Activity == 0, 10);
            var r2 = _strays.Interact(cat, StrayAction.Pet, roll: 0);
            Assert.AreEqual(InteractOutcome.Accepted, r2.Outcome);
            Assert.AreEqual(0, r2.AffectionDelta, 1e-9);
        }

        [Test]
        public void Refusal_FleesLosesAffectionAndSkipsTheDay()
        {
            var p = Profile("a", 50, Temperament.Sassy);
            var cat = SpawnNow();
            var r = _strays.Interact(cat, StrayAction.Pet, roll: 0.99);
            Assert.AreEqual(InteractOutcome.Refused, r.Outcome);
            Assert.AreEqual(-_strays.Tuning.RefusePenalty, r.AffectionDelta, 1e-9);
            Assert.AreEqual(StrayState.Fleeing, cat.StrayState);
            Assert.IsTrue(RunUntil(() => !_sim.Cats.Contains(cat), 30));
            // 쿨타임(60초)이 지나도 그날(10분)은 안 온다
            RunUntil(() => false, 120);
            _strays.FillBowl(_bowl.Id);
            Assert.IsNull(_strays.TrySpawn());
        }

        [Test]
        public void AdoptAccepted_BecomesResidentInside()
        {
            var p = Profile("a", 85, Temperament.Normal);
            var cat = SpawnNow();
            var r = _strays.Interact(cat, StrayAction.Adopt);
            Assert.AreEqual(InteractOutcome.Adopting, r.Outcome, "보통 성격 81 이상은 무조건");
            Assert.IsTrue(RunUntil(() => cat.Affiliation == Affiliation.Resident, 40));
            Assert.AreEqual(BoardId.Indoor, cat.Cell.Board);
            Assert.IsTrue(p.Adopted);
            Assert.AreEqual("foodie", cat.Personality.Id);
            _strays.FillBowl(_bowl.Id);
            Assert.IsNull(_strays.TrySpawn(), "영입된 고양이는 다시 길냥이로 오지 않는다");
        }

        [Test]
        public void AcceptChanceTable()
        {
            // 41~60
            Assert.Less(StraySystem.AcceptChance(StrayAction.Pet, 50, Temperament.Sassy), StraySystem.AcceptChance(StrayAction.Pet, 50, Temperament.Normal));
            Assert.Less(StraySystem.AcceptChance(StrayAction.Pet, 50, Temperament.Normal), StraySystem.AcceptChance(StrayAction.Pet, 50, Temperament.Cuddly));
            Assert.Less(StraySystem.AcceptChance(StrayAction.Adopt, 50, Temperament.Cuddly), 0.5);
            // 61~80 은 더 높다
            foreach (Temperament t in Enum.GetValues(typeof(Temperament)))
            {
                Assert.Greater(StraySystem.AcceptChance(StrayAction.Pet, 70, t), StraySystem.AcceptChance(StrayAction.Pet, 50, t));
                Assert.Greater(StraySystem.AcceptChance(StrayAction.Adopt, 70, t), StraySystem.AcceptChance(StrayAction.Adopt, 50, t));
            }
            // 81~100: 보통·개냥이는 무조건, 싸가지없음은 81~90 가끔 튕기고 91~ 무조건
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Adopt, 81, Temperament.Normal));
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Adopt, 81, Temperament.Cuddly));
            Assert.Less(StraySystem.AcceptChance(StrayAction.Adopt, 85, Temperament.Sassy), 1);
            Assert.Greater(StraySystem.AcceptChance(StrayAction.Adopt, 85, Temperament.Sassy), 0.5);
            Assert.Less(StraySystem.AcceptChance(StrayAction.Adopt, 90, Temperament.Sassy), 1);
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Adopt, 91, Temperament.Sassy));
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Pet, 91, Temperament.Sassy));
            // 간식·놀이는 항상
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Snack, 41, Temperament.Sassy));
            Assert.AreEqual(1, StraySystem.AcceptChance(StrayAction.Play, 41, Temperament.Sassy));
        }

        [Test]
        public void SassyBonded81to90_SometimesRefusesAdoption()
        {
            int refused = 0;
            var rng = new Random(7);
            for (int i = 0; i < 40; i++)
            {
                SetUp();
                Profile("s", 85, Temperament.Sassy);
                var cat = SpawnNow();
                if (_strays.Interact(cat, StrayAction.Adopt, rng.NextDouble()).Outcome == InteractOutcome.Refused) refused++;
            }
            Assert.Greater(refused, 0, "가끔은 튕긴다");
            Assert.Less(refused, 20);
        }

        // ───────────── 흔적 ─────────────

        [Test]
        public void GiftGivesTwoHearts()
        {
            Assert.IsTrue(_eco.AddGift(new Cell(BoardId.Outdoor, new HexCoord(1, 1))));
            var gift = _eco.Drops.Single(d => d.Kind == ItemKind.Gift);
            Assert.IsTrue(_eco.PickDrop(gift.Id));
            Assert.AreEqual(2, _eco.Wallet.Hearts);
        }

        [Test]
        public void StraysLeavePawPrints()
        {
            Profile("a", 0);
            var cat = SpawnNow();
            Assert.IsTrue(RunUntil(() => cat.StrayState == StrayState.Eating));
            Assert.Greater(_strays.PawPrints.Count, 0);
        }
    }
}
