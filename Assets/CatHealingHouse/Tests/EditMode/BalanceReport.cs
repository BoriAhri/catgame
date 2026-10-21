using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;
using UnityEngine;

namespace CatHealingHouse.Tests
{
    /// <summary>
    /// 밸런싱 측정: 시작 배치 + 고양이 4마리를 현실 1시간 돌리고, 부지런한 집사(2분마다 트레이, 1분마다 하트·드롭, 5분마다 화장실)가
    /// 얼마를 모으는지 잰다. 결과는 콘솔에 [BALANCE] 로 찍는다.
    /// </summary>
    public class BalanceReport
    {
        [Test]
        public void OneHourIncome()
        {
            var world = new HouseWorld(5, 4);
            var sim = new CatSimulation(world, new DistanceMapCache(world));
            var eco = new HouseEconomy(sim);
            world.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, -1), false, out _);
            world.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(1, -2), false, out _);
            world.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            world.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-3, 2), false, out _);
            world.TryPlace(FurnitureCatalog.CatWheel, BoardId.Indoor, new HexCoord(-2, 3), false, out _);
            world.TryPlace(FurnitureCatalog.HidingHouse, BoardId.Indoor, new HexCoord(2, -3), false, out _);
            world.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(-1, -2), false, out _);
            var pers = new System.Func<CatPersonality>[] { CatPersonalities.Foodie, CatPersonalities.TowerKing, CatPersonalities.Shy, CatPersonalities.Curious };
            for (int i = 0; i < 4; i++) sim.AddCat("c" + i, new Cell(BoardId.Indoor, new HexCoord(i - 1, 1)), pers[i]());

            long money = 0, hearts = 0, fur = 0, whisk = 0, litter = 0;
            long w0Hearts = eco.Wallet.Hearts, w0Fur = eco.Wallet.Furballs, w0Wh = eco.Wallet.Whiskers;
            for (int t = 1; t <= 3600; t++)
            {
                sim.Advance(1.0);
                if (t % 60 == 0)
                {
                    foreach (var h in eco.Hearts.ToList()) eco.CollectHeart(h.FurnitureId);
                    foreach (var d in eco.Drops.ToList()) eco.PickDrop(d.Id);
                }
                if (t % 120 == 0)
                    for (int i = eco.Tray.Count - 1; i >= 0; i--) money += eco.CollectTraySlot(i);
                if (t % 300 == 0)
                    foreach (var f in world.Furniture.ToList())
                        if (RewardTable.IsLitterBox(f.Def)) { litter += eco.UsesOfLitter(f.Id); eco.CleanLitter(f.Id); }
            }
            for (int i = eco.Tray.Count - 1; i >= 0; i--) money += eco.CollectTraySlot(i);
            hearts = eco.Wallet.Hearts - w0Hearts;
            fur = eco.Wallet.Furballs - w0Fur;
            whisk = eco.Wallet.Whiskers - w0Wh;
            double mood = sim.Cats.Average(c => c.Mood);
            Debug.Log($"[BALANCE] 1h: money {money}, hearts {hearts}, furballs {fur}, whiskers {whisk}, litterUses {litter}, avgMood {mood:0}");
            Assert.Greater(money, 0);
        }
    }
}
