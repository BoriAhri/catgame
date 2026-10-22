using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Save;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>놀아주기: 레이저·낚싯대, 한 마리/다 같이(반경), 쫓기·덮치기, 친밀도·즐거움·기력, 지치면 드러눕기, 하루 감소, 낚싯대 구매·저장.</summary>
    public class PlayTests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private Wallet _wallet;
        private Intimacy _int;
        private PlaySystem _play;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(4, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world), seed: 11);
            _sim.Tuning.HungerDecay = _sim.Tuning.BladderDecay = _sim.Tuning.EnergyDecay = _sim.Tuning.FunDecay = 0;
            _wallet = new Wallet { Money = 1000 };
            _int = new Intimacy(_wallet);
            _play = new PlaySystem(_sim, _int);
        }

        private static Cell C(int q, int r) => new Cell(BoardId.Indoor, new HexCoord(q, r));

        private Cat FullCat(Cell at)
        {
            var cat = _sim.AddCat("냥", at);
            foreach (var n in CatNeeds.All) cat.Needs.Set(n, 100);
            return cat;
        }

        private void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += CatSimulation.FixedStep) _sim.Step(CatSimulation.FixedStep);
        }

        [Test]
        public void SoloCatChasesToyAcrossRoom()
        {
            var cat = FullCat(C(-2, 0));
            _play.Start(ToyKind.Laser, cat);
            _play.SetToy(C(2, 0), 3);   // 빠르게 움직이는 중
            Run(0.2);
            Assert.AreEqual(CatState.Playing, cat.State);
            Run(6);
            Assert.AreEqual(C(2, 0), cat.Cell, "장난감 칸까지 쫓아간다");
        }

        [Test]
        public void SlowToyGetsCaught_GivesExpFunAndCostsEnergy()
        {
            var cat = FullCat(C(0, 0));
            cat.Needs.Set(NeedType.Fun, 50);
            double caught = 0;
            _play.Caught += (c, exp) => caught += exp;
            _play.Start(ToyKind.Wand, cat);
            _play.SetToy(C(0, 0), 0.5);
            Run(0.5);
            Assert.Greater(caught, 0, "천천히 멈춘 장난감은 덮친다");
            Assert.AreEqual(_play.Tuning.WandExp, cat.IntimacyExp, 1e-9);
            Assert.Greater(cat.Needs.Fun, 50);
            Assert.Less(cat.Needs.Energy, 100);
        }

        [Test]
        public void ToyOnFurniture_CaughtFromNextCell()
        {
            Assert.AreEqual(PlaceResult.Ok, _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl));
            var on = C(2, 0);
            Assume.That(!_world.IsTransit(on), "밥그릇 칸은 못 들어가는 칸");
            var cat = FullCat(C(0, 0));
            int n = 0;
            _play.Caught += (c, e) => n++;
            _play.Start(ToyKind.Laser, cat);
            _play.SetToy(on, 0.5);
            Run(2);
            Assert.Greater(n, 0, "가구 위 장난감은 옆 칸에서 덮친다");
            Assert.AreEqual(1, HexCoord.Distance(cat.Cell.Coord, on.Coord));
        }

        [Test]
        public void FastToyIsNotCaught()
        {
            var cat = FullCat(C(0, 0));
            int n = 0;
            _play.Caught += (c, e) => n++;
            _play.Start(ToyKind.Laser, cat);
            for (int i = 0; i < 30; i++) { _play.SetToy(C(0, 0), 5); _sim.Step(CatSimulation.FixedStep); }
            Assert.AreEqual(0, n, "빨리 움직이면 노려보기만");
        }

        [Test]
        public void ToyLeftStillTooLongIsBoring()
        {
            var cat = FullCat(C(0, 0));
            int n = 0;
            _play.Caught += (c, e) => n++;
            _play.Start(ToyKind.Wand, cat);
            _play.SetToy(C(0, 0), 0);
            Run(10);
            int afterTen = n;
            Run(10);
            Assert.Greater(afterTen, 0);
            Assert.AreEqual(afterTen, n, "가만히 둔 낚싯대는 금방 시시해한다");
        }

        [Test]
        public void GroupPlay_OnlyNearbyCatsJoin()
        {
            var near = FullCat(C(1, 0));
            var far = FullCat(C(-3, 0));
            _play.Start(ToyKind.Laser);
            _play.SetToy(C(2, 0), 3);
            Run(0.3);
            Assert.IsTrue(_play.IsPlaying(near));
            Assert.IsFalse(_play.IsPlaying(far), "장난감에서 먼 고양이는 반응하지 않는다");
        }

        [Test]
        public void GroupPlay_CatLosesInterestWhenToyGone()
        {
            var cat = FullCat(C(-1, 0));
            _play.Start(ToyKind.Laser);
            _play.SetToy(C(0, 0), 3);
            Run(0.3);
            Assert.IsTrue(_play.IsPlaying(cat));
            _play.SetToy(null, 0);   // 레이저를 뗌
            Run(_play.Tuning.GoneSeconds + 1.5);
            Assert.IsFalse(_play.IsPlaying(cat));
            Assert.AreNotEqual(CatState.Playing, cat.State);
        }

        [Test]
        public void SoloCatWaitsWhenToyGone()
        {
            var cat = FullCat(C(0, 0));
            _play.Start(ToyKind.Laser, cat);
            _play.SetToy(C(1, 0), 3);
            Run(0.3);
            _play.SetToy(null, 0);
            Run(_play.Tuning.GoneSeconds + 2);
            Assert.IsTrue(_play.IsPlaying(cat), "한 마리와 놀기는 장난감을 다시 꺼낼 때까지 기다린다");
        }

        [Test]
        public void TiredCatLiesDownAndRests()
        {
            var cat = FullCat(C(0, 0));
            cat.Needs.Set(NeedType.Energy, _play.Tuning.TiredEnergy + 1);
            bool tired = false;
            _play.Tired += c => tired = true;
            _play.Start(ToyKind.Laser, cat);
            _play.SetToy(C(0, 0), 0.5);
            Run(1.5);
            Assert.IsTrue(tired);
            Assert.AreEqual(CatState.Lounging, cat.State);
            Assert.AreEqual(LoungeKind.Sprawl, cat.Lounge);
            Assert.IsFalse(_play.IsPlaying(cat));
            Run(1);
            Assert.IsFalse(_play.IsPlaying(cat), "쉬는 동안은 다시 안 논다");
        }

        [Test]
        public void ExpShrinksAfterDailyAmount()
        {
            var cat = FullCat(C(0, 0));
            _play.Today = () => 5;
            cat.PlayDay = 5;
            cat.PlayExpToday = _play.Tuning.DailyFullExp;
            _play.Start(ToyKind.Wand, cat);
            Assert.AreEqual(_play.Tuning.WandExp * _play.Tuning.LateShare, _play.NextExp(cat), 1e-9);
            _play.Today = () => 6;
            Assert.AreEqual(_play.Tuning.WandExp, _play.NextExp(cat), 1e-9, "다음 날은 다시 다 받는다");
        }

        [Test]
        public void StopReleasesCats()
        {
            var cat = FullCat(C(0, 0));
            _play.Start(ToyKind.Laser, cat);
            _play.SetToy(C(1, 0), 3);
            Run(0.3);
            _play.Stop();
            Run(0.2);
            Assert.AreNotEqual(CatState.Playing, cat.State);
            Assert.IsFalse(_play.Active);
        }

        [Test]
        public void WandIsBoughtOnce_AndSaved()
        {
            var economy = new HouseEconomy(_sim);
            var shop = new Shop(_world, economy, grantStart: false);
            economy.Wallet.Money = shop.Tuning.WandPrice + 10;
            Assert.IsFalse(shop.HasWand);
            Assert.AreEqual(ShopResult.Ok, shop.BuyWand());
            Assert.IsTrue(shop.HasWand);
            Assert.AreEqual(10, economy.Wallet.Money);
            Assert.AreEqual(ShopResult.Failed, shop.BuyWand(), "이미 있다");

            var data = SaveGame.Capture(new GameState { World = _world, Sim = _sim, Economy = economy, Shop = shop }, System.DateTime.UtcNow);
            Assert.IsTrue(data.Wand);
        }
    }
}
