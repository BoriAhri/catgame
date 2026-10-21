using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    /// <summary>6단계: 공방 제작(장난감·코스튬), 집사 스킬.</summary>
    public class Stage6Tests
    {
        private HouseWorld _world;
        private CatSimulation _sim;
        private HouseEconomy _eco;
        private Shop _shop;
        private StraySystem _strays;
        private Workshop _ws;
        private ButlerSkills _skills;

        [SetUp]
        public void SetUp()
        {
            _world = new HouseWorld(3, 3);
            _sim = new CatSimulation(_world, new DistanceMapCache(_world));
            _eco = new HouseEconomy(_sim);
            _shop = new Shop(_world, _eco);
            _strays = new StraySystem(_sim, _eco);
            _ws = new Workshop(_eco.Wallet, _shop);
            _skills = new ButlerSkills(_eco, _strays);
        }

        [Test]
        public void CraftingToyNeedsMaterialsAndGoesToStorage()
        {
            var ball = _ws.Get("felt_ball");
            Assert.AreEqual(CraftResult.NotEnoughFurballs, _ws.Craft(ball));
            _eco.Wallet.Add(ItemKind.Furball, 7);
            Assert.AreEqual(CraftResult.Ok, _ws.Craft(ball));
            Assert.AreEqual(2, _eco.Wallet.Furballs);
            Assert.AreEqual(1, _shop.Stock(FurnitureCatalog.FeltBall));
            // 보관함에서 꺼내 놓으면 돈이 들지 않는다
            long money = _eco.Wallet.Money;
            Assert.AreEqual(PlaceResult.Ok, _shop.BuyOrPlace(FurnitureCatalog.FeltBall, BoardId.Indoor, HexCoord.Zero, false, out _, out _, out long paid));
            Assert.AreEqual(0, paid);
            Assert.AreEqual(money, _eco.Wallet.Money);
        }

        [Test]
        public void WhiskersAreRequiredForRareRecipes()
        {
            _eco.Wallet.Add(ItemKind.Furball, 50);
            Assert.AreEqual(CraftResult.NotEnoughWhiskers, _ws.Craft(_ws.Get("mouse_toy")));
            _eco.Wallet.Add(ItemKind.Whisker, 1);
            Assert.AreEqual(CraftResult.Ok, _ws.Craft(_ws.Get("mouse_toy")));
            Assert.AreEqual(0, _eco.Wallet.Whiskers);
        }

        [Test]
        public void CraftedToysAreNotSoldInShop()
        {
            foreach (var d in FurnitureCatalog.Crafted) Assert.IsFalse(_shop.ForSale(d), d.Id);
        }

        [Test]
        public void CostumeWearAndSwapReturnsToCloset()
        {
            var cat = _sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero));
            Assert.IsFalse(_ws.Wear(cat, Costumes.Ribbon), "옷장에 없으면 못 입힘");
            _eco.Wallet.Add(ItemKind.Furball, 100);
            _eco.Wallet.Add(ItemKind.Whisker, 10);
            _ws.Craft(_ws.Get("ribbon"));
            _ws.Craft(_ws.Get("beanie"));
            Assert.IsTrue(_ws.Wear(cat, Costumes.Ribbon));
            Assert.AreEqual(0, _ws.Closet(Costumes.Ribbon));
            Assert.IsTrue(_ws.Wear(cat, Costumes.Beanie));
            Assert.AreEqual(Costumes.Beanie, cat.Costume);
            Assert.AreEqual(1, _ws.Closet(Costumes.Ribbon), "갈아입으면 원래 옷은 옷장으로");
            _ws.TakeOff(cat);
            Assert.IsNull(cat.Costume);
            Assert.AreEqual(1, _ws.Closet(Costumes.Beanie));
        }

        [Test]
        public void TraySkillRaisesCapAndCostsHearts()
        {
            int cap = _eco.Tuning.TraySlotCap;
            Assert.AreEqual(SkillResult.NotEnoughHearts, _skills.LevelUp(ButlerSkills.TrayPlus));
            _eco.Wallet.Add(ItemKind.Heart, 100);
            Assert.AreEqual(SkillResult.Ok, _skills.LevelUp(ButlerSkills.TrayPlus));
            Assert.AreEqual(cap + 5, _eco.Tuning.TraySlotCap);
            Assert.AreEqual(95, _eco.Wallet.Hearts);
            while (_skills.LevelUp(ButlerSkills.TrayPlus) == SkillResult.Ok) { }
            Assert.AreEqual(4, _skills.Level(ButlerSkills.TrayPlus));
            Assert.AreEqual(SkillResult.MaxLevel, _skills.LevelUp(ButlerSkills.TrayPlus));
            Assert.AreEqual(cap + 20, _eco.Tuning.TraySlotCap);
        }

        [Test]
        public void GentleHandsRaisesPetAffection()
        {
            double pet = _strays.Tuning.PetAffection;
            _eco.Wallet.Add(ItemKind.Heart, 100);
            _skills.LevelUp(ButlerSkills.GentleHands);
            _skills.LevelUp(ButlerSkills.GentleHands);
            Assert.AreEqual(pet + 2, _strays.Tuning.PetAffection, 1e-9);
        }

        [Test]
        public void RecipeUnlockIsLockedForNow()
        {
            _eco.Wallet.Add(ItemKind.Heart, 100);
            Assert.AreEqual(SkillResult.Locked, _skills.LevelUp(ButlerSkills.RecipeUnlock));
            Assert.AreEqual(100, _eco.Wallet.Hearts);
        }

        [Test]
        public void ToysRefillFunFasterThanScratcher()
        {
            Assert.Greater(FurnitureCatalog.FeltBall.Power, FurnitureCatalog.Scratcher.Power);
            Assert.Greater(FurnitureCatalog.MouseToy.Power, FurnitureCatalog.FeltBall.Power);
        }
    }
}
