using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class WallFurnitureTests
    {
        private HouseWorld _w;

        [SetUp]
        public void SetUp() => _w = new HouseWorld(5, 4);

        private HexCoord FreeBackSlot()
        {
            int n = _w.Indoor.Radius + 1;
            return _w.WallSlots(BoardId.Indoor).First(c => c.R == -n && !_w.IsWindow(BoardId.Indoor, c)
                && _w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, c, false) == PlaceResult.Ok);
        }

        [Test]
        public void WallSlotsCoverBackAndBothDiagonals()
        {
            var slots = _w.WallSlots(BoardId.Indoor);
            Assert.IsTrue(slots.Any(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Back));
            Assert.IsTrue(slots.Any(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Left));
            Assert.IsTrue(slots.Any(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Right));
            Assert.IsTrue(slots.All(c => c.R < 0), "아래쪽 벽은 쓰지 않는다");
            // 반지름 5: 링 6 의 r<0 벽 칸 = 뒷벽 7 + 왼쪽 5 + 오른쪽 5
            Assert.AreEqual(17, slots.Count);
        }

        [Test]
        public void StepsPlaceOnBackWallWithFloorEntry()
        {
            var slot = FreeBackSlot();
            Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false, out var f));
            Assert.AreEqual(1, f.WallCells().Count());
            var entry = f.UsableCells().Single();
            Assert.AreEqual(TileKind.Floor, _w.Indoor.Get(entry).Kind);
            Assert.AreEqual(1, HexCoord.Distance(entry, slot));
            Assert.AreEqual(3, f.Def.Capacity);
        }

        [Test]
        public void CannotHangOnWindowFloorOrTakenSlot()
        {
            var win = WindowLight.WindowCells(_w.Indoor)[0];
            Assert.AreEqual(PlaceResult.Window, _w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, win, false));
            Assert.AreEqual(PlaceResult.NotWall, _w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, HexCoord.Zero, false));
            var slot = FreeBackSlot();
            _w.TryPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false, out _);
            Assert.AreEqual(PlaceResult.Occupied, _w.CanPlace(FurnitureCatalog.WallBox, BoardId.Indoor, slot, false));
            Assert.AreEqual(PlaceResult.WrongZone, _w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Outdoor, slot, false));
        }

        [Test]
        public void EntryCellBlockedByFloorFurnitureRefuses()
        {
            var slot = FreeBackSlot();
            var entry = _w.ResolveCells(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false).Single(c => c.Role == CellRole.Entry).Offset;
            Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(FurnitureCatalog.Cushion, BoardId.Indoor, entry, false, out _));
            Assert.AreEqual(PlaceResult.Occupied, _w.CanPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false));
        }

        [Test]
        public void DiagonalWallsWork()
        {
            var left = _w.WallSlots(BoardId.Indoor).First(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Left);
            var right = _w.WallSlots(BoardId.Indoor).First(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Right);
            Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(FurnitureCatalog.WallHammock, BoardId.Indoor, left, false, out _));
            Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(FurnitureCatalog.WallCatwalk, BoardId.Indoor, right, false, out var walk));
            Assert.AreEqual(2, walk.WallCells().Count());
            Assert.IsTrue(walk.WallCells().All(c => _w.SideOf(BoardId.Indoor, c) == WallSide.Right));
        }

        [Test]
        public void ExpandMovesWallFurnitureOutward()
        {
            var sim0 = new CatSimulation(_w, new DistanceMapCache(_w));
            var eco = new HouseEconomy(sim0);
            var shop = new Shop(_w, eco);
            var slot = FreeBackSlot();
            _w.TryPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false, out _);
            eco.Wallet.Add(ItemKind.Money, 100000);
            Assert.AreEqual(ShopResult.Ok, shop.BuyExpansion(BoardId.Indoor));
            var moved = _w.Furniture.Single(f => f.Def == FurnitureCatalog.WallSteps);
            Assert.AreEqual(WallSide.Back, _w.SideOf(BoardId.Indoor, moved.Origin));
            Assert.AreEqual(-(_w.Indoor.Radius + 1), moved.Origin.R);
            Assert.AreEqual(0, shop.Stock(FurnitureCatalog.WallSteps));
        }

        [Test]
        public void CatsClimbStepsOnePerLevel()
        {
            var slot = FreeBackSlot();
            _w.TryPlace(FurnitureCatalog.WallSteps, BoardId.Indoor, slot, false, out var steps);
            var sim = new CatSimulation(_w, new DistanceMapCache(_w), seed: 3);
            sim.Tuning.HungerDecay = sim.Tuning.BladderDecay = sim.Tuning.EnergyDecay = sim.Tuning.FunDecay = 0;
            sim.Tuning.LoungeChance = 0;
            for (int i = 0; i < 4; i++)
            {
                var cat = sim.AddCat("c" + i, new Cell(BoardId.Indoor, HexCoord.Zero));
                foreach (var nd in CatNeeds.All) cat.Needs.Set(nd, 100);
                cat.Needs.Set(NeedType.Fun, 5);
            }
            for (int i = 0; i < 4000 && steps.OccupiedSlots < 3; i++) sim.Step(CatSimulation.FixedStep);
            Assert.AreEqual(3, steps.OccupiedSlots, "세 층에 한 마리씩");
            var levels = sim.Cats.Where(c => c.TargetFurnitureId == steps.Id).Select(c => c.SlotIndex).ToList();
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, levels);
        }
    }
}
