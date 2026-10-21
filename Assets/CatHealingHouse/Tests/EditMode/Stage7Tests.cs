using System;
using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Save;
using CatHealingHouse.Stray;
using CatHealingHouse.World;
using NUnit.Framework;
using UnityEngine;

namespace CatHealingHouse.Tests
{
    /// <summary>7단계: 저장·불러오기, 자리를 비운 사이 계산.</summary>
    public class Stage7Tests
    {
        private static GameState NewGame(bool grantStart = true)
        {
            var world = new HouseWorld(5, 4);
            var sim = new CatSimulation(world, new DistanceMapCache(world), seed: 3);
            var eco = new HouseEconomy(sim, seed: 5);
            var strays = new StraySystem(sim, eco, seed: 7);
            var shop = new Shop(world, eco, grantStart: grantStart);
            return new GameState
            {
                World = world, Sim = sim, Economy = eco, Strays = strays, Shop = shop,
                Workshop = new Workshop(eco.Wallet, shop), Skills = new ButlerSkills(eco, strays), Clock = new DayClock(7),
            };
        }

        private static GameState Populated()
        {
            var g = NewGame();
            var w = g.World;
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _));
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-3, 2), false, out var litter));
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, -1), true, out _));
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(1, -2), false, out _));
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(0, 1), false, out var bowl));
            var slot = w.WallSlots(BoardId.Indoor).First(c => w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, c, false) == PlaceResult.Ok);
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false, out _));
            g.Strays.FillBowl(bowl.Id);
            g.Economy.RestoreLitter(litter.Id, 2, 1);
            var c1 = g.Sim.AddCat("치즈", new Cell(BoardId.Indoor, HexCoord.Zero), CatPersonalities.Foodie());
            c1.LookKey = "cheese";
            c1.Mood = 81;
            c1.Needs.Set(NeedType.Fun, 33);
            g.Economy.Wallet.Add(ItemKind.Heart, 40);
            g.Economy.Wallet.Add(ItemKind.Furball, 30);
            g.Economy.Wallet.Add(ItemKind.Whisker, 5);
            g.Workshop.Craft(g.Workshop.Get("beanie"));
            g.Workshop.Craft(g.Workshop.Get("ribbon"));
            g.Workshop.Wear(c1, Costumes.Beanie);
            g.Skills.LevelUp(ButlerSkills.TrayPlus);
            g.Shop.AddToStorage(FurnitureCatalog.Cushion, 2);
            g.Strays.Profiles[1].Affection = 57;
            g.Strays.Profiles[1].Discovered = true;
            g.Clock.Advance(5000);
            return g;
        }

        [Test]
        public void SaveRoundTripThroughJsonKeepsEverything()
        {
            var a = Populated();
            var data = SaveGame.Capture(a, new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));
            string json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<SaveData>(json);

            var b = NewGame(grantStart: false);
            var cats = SaveGame.Restore(b, back);

            Assert.AreEqual(a.World.Furniture.Count(), b.World.Furniture.Count(), "가구 수");
            foreach (var f in a.World.Furniture)
                Assert.IsTrue(b.World.Furniture.Any(x => x.Def == f.Def && x.Board == f.Board && x.Origin == f.Origin && x.Mirrored == f.Mirrored), f.ToString());
            Assert.AreEqual(a.Economy.Wallet.Money, b.Economy.Wallet.Money);
            Assert.AreEqual(a.Economy.Wallet.Hearts, b.Economy.Wallet.Hearts);
            Assert.AreEqual(a.Economy.Wallet.Furballs, b.Economy.Wallet.Furballs);
            Assert.AreEqual(a.Economy.Wallet.Kibble, b.Economy.Wallet.Kibble);
            var litterB = b.World.Furniture.First(f => RewardTable.IsLitterBox(f.Def));
            Assert.AreEqual(3, b.Economy.UsesOfLitter(litterB.Id));
            var bowlB = b.World.Furniture.First(f => b.Strays.IsOutdoorBowl(f));
            Assert.AreEqual(b.Strays.Tuning.BowlPortions, b.Strays.FoodIn(bowlB.Id));

            Assert.AreEqual(1, cats.Count);
            var cat = cats[0];
            Assert.AreEqual("치즈", cat.Name);
            Assert.AreEqual("cheese", cat.LookKey);
            Assert.AreEqual(CatPersonalities.Foodie().Id, cat.Personality.Id);
            Assert.AreEqual(Costumes.Beanie, cat.Costume);
            Assert.AreEqual(81, cat.Mood, 1e-9);
            Assert.AreEqual(33, cat.Needs.Fun, 1e-9);

            Assert.AreEqual(1, b.Workshop.Closet(Costumes.Ribbon));
            Assert.AreEqual(1, b.Skills.Level(ButlerSkills.TrayPlus));
            Assert.AreEqual(a.Economy.Tuning.TraySlotCap, b.Economy.Tuning.TraySlotCap, "스킬 효과도 다시 반영");
            Assert.AreEqual(2, b.Shop.Stock(FurnitureCatalog.Cushion), "보관함");
            Assert.AreEqual(57, b.Strays.Profiles[1].Affection, 1e-9);
            Assert.IsTrue(b.Strays.Profiles[1].Discovered);
            Assert.AreEqual(a.Clock.Elapsed, b.Clock.Elapsed, 1e-6);
        }

        [Test]
        public void ExpandedBoardsAndRelocatedWallFurnitureSurviveReload()
        {
            var a = Populated();
            a.Economy.Wallet.Add(ItemKind.Money, 100000);
            Assert.AreEqual(ShopResult.Ok, a.Shop.BuyExpansion(BoardId.Indoor));
            var data = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(SaveGame.Capture(a, DateTime.UtcNow)));
            var b = NewGame(grantStart: false);
            SaveGame.Restore(b, data);
            Assert.AreEqual(a.World.Indoor.Radius, b.World.Indoor.Radius);
            Assert.AreEqual(a.World.Furniture.Count(), b.World.Furniture.Count());
            Assert.AreEqual(1, b.World.Furniture.Count(f => f.Def == FurnitureCatalog.WallSteps));
            Assert.AreEqual(a.Shop.ExpandCost(BoardId.Indoor), b.Shop.ExpandCost(BoardId.Indoor), "다음 넓히기 가격도 이어짐");
        }

        [Test]
        public void ElapsedIsZeroWhenClockGoesBackwards()
        {
            var saved = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(0, OfflineProgress.ElapsedSeconds(saved.Ticks, saved.AddHours(-3)));
            Assert.AreEqual(3600, OfflineProgress.ElapsedSeconds(saved.Ticks, saved.AddHours(1)), 1e-6);
        }

        [Test]
        public void OfflineFillsTrayDirtiesLitterAndFeedsStrays()
        {
            var g = Populated();
            long hearts = g.Economy.Wallet.Hearts;
            var s = OfflineProgress.Run(g, 3 * 3600, seed: 11);
            Assert.Greater(s.TrayItems, 0, "트레이가 쌓임");
            Assert.LessOrEqual(g.Economy.Tray.Max(t => t.Count), g.Economy.Tuning.TraySlotCap, "칸 상한");
            Assert.Greater(s.LitterUses, 0);
            Assert.AreEqual(3, s.Meals, "밥그릇 3끼를 다 먹음");
            var bowl = g.World.Furniture.First(f => g.Strays.IsOutdoorBowl(f));
            Assert.AreEqual(0, g.Strays.FoodIn(bowl.Id));
            Assert.IsTrue(s.Visits.Count > 0 && s.Visits.All(v => v.AffectionAfter > v.AffectionBefore));
            Assert.AreEqual(hearts, g.Economy.Wallet.Hearts, "젤리 하트는 쌓이지 않음");
            Assert.LessOrEqual(g.Economy.DropCount(BoardId.Indoor), g.Economy.Tuning.MaxDropsPerBoard);
        }

        [Test]
        public void OfflineIsCappedAtTwelveHoursAndHasNoPenalty()
        {
            var g = Populated();
            foreach (var c in g.Sim.Cats) foreach (var n in CatNeeds.All) c.Needs.Set(n, 5);
            double before = g.Clock.Elapsed;
            var s = OfflineProgress.Run(g, 30 * 3600, seed: 3);
            Assert.AreEqual(12 * 3600, s.Seconds, 1e-6);
            Assert.IsTrue(s.Capped);
            Assert.AreEqual(before + 12 * 3600, g.Clock.Elapsed, 1e-6);
            foreach (var c in g.Sim.Cats) foreach (var n in CatNeeds.All) Assert.GreaterOrEqual(c.Needs.Get(n), 55, n.ToString());
        }

        [Test]
        public void NoTimeMeansNothingHappens()
        {
            var g = Populated();
            var s = OfflineProgress.Run(g, 0, seed: 1);
            Assert.IsTrue(s.IsEmpty);
        }
    }
}
