using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>5단계: 상점, 보관함, 확장 구매, 사료.</summary>
    public class Stage5Tests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private HouseEconomy _eco;
        private Shop _shop;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world));
            _eco = new HouseEconomy(_sim);
            _shop = new Shop(_world, _eco);
        }

        [Test]
        public void StartsWithMoneyAndKibble()
        {
            Assert.AreEqual(_shop.Tuning.StartingMoney, _eco.Wallet.Money);
            Assert.AreEqual(_shop.Tuning.StartingKibble, _eco.Wallet.Kibble);
        }

        [Test]
        public void EveryPlaceableFurnitureHasAPrice()
        {
            foreach (var def in FurnitureCatalog.All) Assert.IsTrue(_shop.ForSale(def), def.Id);
        }

        [Test]
        public void BuyingPutsFurnitureInStorageAndCostsMoney()
        {
            long before = _eco.Wallet.Money;
            Assert.AreEqual(ShopResult.Ok, _shop.BuyFurniture(FurnitureCatalog.BoxSmall));
            Assert.AreEqual(1, _shop.Stock(FurnitureCatalog.BoxSmall));
            Assert.AreEqual(before - _shop.PriceOf(FurnitureCatalog.BoxSmall), _eco.Wallet.Money);
        }

        [Test]
        public void CannotBuyWithoutEnoughMoney()
        {
            long before = _eco.Wallet.Money;
            Assert.Less(before, _shop.PriceOf(FurnitureCatalog.CatTower5));
            Assert.AreEqual(ShopResult.NotEnoughMoney, _shop.BuyFurniture(FurnitureCatalog.CatTower5));
            Assert.AreEqual(before, _eco.Wallet.Money, "돈이 빠지면 안 된다");
            Assert.AreEqual(0, _shop.Stock(FurnitureCatalog.CatTower5));
        }

        [Test]
        public void PlacingUsesStorage_RemovingReturnsToStorage()
        {
            Assert.AreNotEqual(PlaceResult.Ok, _shop.PlaceFromStorage(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(1, 0), false, out _, out var s0));
            Assert.AreEqual(ShopResult.NotEnoughStock, s0);

            _shop.BuyFurniture(FurnitureCatalog.BoxSmall);
            Assert.AreEqual(PlaceResult.Ok, _shop.PlaceFromStorage(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(1, 0), false, out var placed, out _));
            Assert.AreEqual(0, _shop.Stock(FurnitureCatalog.BoxSmall));

            long money = _eco.Wallet.Money;
            Assert.IsTrue(_shop.Store(placed.Id));
            Assert.IsNull(_world.GetFurniture(placed.Id));
            Assert.AreEqual(1, _shop.Stock(FurnitureCatalog.BoxSmall), "철거하면 보관함으로");
            Assert.AreEqual(money, _eco.Wallet.Money, "환불은 없다");
        }

        [Test]
        public void BuyOrPlace_UsesStorageFirstThenBuys()
        {
            _shop.AddToStorage(FurnitureCatalog.BoxSmall);
            long money = _eco.Wallet.Money;
            Assert.AreEqual(PlaceResult.Ok, _shop.BuyOrPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(1, 0), false, out _, out _, out long paid1));
            Assert.AreEqual(0, paid1, "보관함에 있으면 공짜");
            Assert.AreEqual(money, _eco.Wallet.Money);
            Assert.AreEqual(PlaceResult.Ok, _shop.BuyOrPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(-1, 0), false, out _, out _, out long paid2));
            Assert.AreEqual(_shop.PriceOf(FurnitureCatalog.BoxSmall), paid2);
            Assert.AreEqual(money - paid2, _eco.Wallet.Money);
        }

        [Test]
        public void BuyOrPlace_BadCellCostsNothing()
        {
            long money = _eco.Wallet.Money;
            var r = _shop.BuyOrPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(4, 0), false, out _, out var s, out long paid);
            Assert.AreNotEqual(PlaceResult.Ok, r);
            Assert.AreEqual(0, paid);
            Assert.AreEqual(money, _eco.Wallet.Money);
            Assert.AreEqual(ShopResult.NotEnoughMoney, _shop.BuyOrPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(1, 0), false, out _, out _, out _) == PlaceResult.Ok ? ShopResult.Ok : ShopResult.NotEnoughMoney);
            Assert.AreEqual(money, _eco.Wallet.Money);
        }

        [Test]
        public void FailedPlacementKeepsStock()
        {
            _shop.BuyFurniture(FurnitureCatalog.BoxSmall);
            // 벽 위에는 못 놓는다
            var r = _shop.PlaceFromStorage(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(4, 0), false, out _, out var s);
            Assert.AreNotEqual(PlaceResult.Ok, r);
            Assert.AreEqual(1, _shop.Stock(FurnitureCatalog.BoxSmall));
        }

        [Test]
        public void ExpansionCostsRiseAndStopAtMax()
        {
            _eco.Wallet.Add(ItemKind.Money, 100000);
            var costs = _shop.Tuning.IndoorExpandCost;
            for (int i = 0; i < costs.Length; i++)
            {
                Assert.AreEqual(costs[i], _shop.ExpandCost(BoardId.Indoor));
                long before = _eco.Wallet.Money;
                Assert.AreEqual(ShopResult.Ok, _shop.BuyExpansion(BoardId.Indoor));
                Assert.AreEqual(before - costs[i], _eco.Wallet.Money);
                Assert.AreEqual(4 + i, _world.Indoor.Radius);
                if (i > 0) Assert.Greater(costs[i], costs[i - 1]);
            }
            Assert.AreEqual(-1, _shop.ExpandCost(BoardId.Indoor));
            Assert.AreEqual(ShopResult.MaxSize, _shop.BuyExpansion(BoardId.Indoor));
            Assert.AreEqual(6, _world.Indoor.Radius);
            Assert.AreEqual(3, _world.Outdoor.Radius, "마당은 따로");
        }

        [Test]
        public void ExpansionNeedsMoney()
        {
            Assert.AreEqual(ShopResult.NotEnoughMoney, _shop.BuyExpansion(BoardId.Outdoor));
            Assert.AreEqual(3, _world.Outdoor.Radius);
        }

        [Test]
        public void KibbleFillsTheYardBowl()
        {
            var strays = new StraySystem(_sim, _eco, defaultRoster: false);
            _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(0, 1), false, out var bowl);
            long k = _eco.Wallet.Kibble;
            Assert.AreEqual(ShopResult.Ok, _shop.FillBowl(strays, bowl.Id));
            Assert.AreEqual(k - 1, _eco.Wallet.Kibble);
            Assert.AreEqual(ShopResult.Failed, _shop.FillBowl(strays, bowl.Id), "가득 차 있으면 사료를 안 쓴다");
            Assert.AreEqual(k - 1, _eco.Wallet.Kibble);

            _eco.Wallet.Add(ItemKind.Kibble, -_eco.Wallet.Kibble);
            Assert.AreEqual(ShopResult.Ok, _shop.BuyKibble());
            Assert.AreEqual(_shop.Tuning.KibblePackSize, _eco.Wallet.Kibble);
        }

        [Test]
        public void NoKibbleNoFill()
        {
            var strays = new StraySystem(_sim, _eco, defaultRoster: false);
            _world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(0, 1), false, out var bowl);
            _eco.Wallet.Add(ItemKind.Kibble, -_eco.Wallet.Kibble);
            Assert.AreEqual(ShopResult.NotEnoughStock, _shop.FillBowl(strays, bowl.Id));
            Assert.AreEqual(0, strays.FoodIn(bowl.Id));
        }
    }
}
