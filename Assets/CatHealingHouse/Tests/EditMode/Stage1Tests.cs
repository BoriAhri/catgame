using System.Linq;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class HexCoordTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 7)]
        [TestCase(2, 19)]
        [TestCase(3, 37)]
        [TestCase(4, 61)]
        public void RangeCountMatchesFormula(int radius, int expected)
        {
            Assert.AreEqual(expected, HexCoord.CountInRange(radius));
            Assert.AreEqual(expected, HexCoord.Range(HexCoord.Zero, radius).Distinct().Count());
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void RingHasSixRCellsAllAtDistanceR(int radius)
        {
            var ring = HexCoord.Ring(HexCoord.Zero, radius).ToList();
            Assert.AreEqual(6 * radius, ring.Distinct().Count());
            Assert.IsTrue(ring.All(c => c.Length == radius));
        }

        [Test]
        public void DistanceFormula()
        {
            Assert.AreEqual(0, HexCoord.Distance(new HexCoord(2, -1), new HexCoord(2, -1)));
            Assert.AreEqual(3, new HexCoord(3, -3).Length);
            Assert.AreEqual(4, HexCoord.Distance(new HexCoord(-2, 0), new HexCoord(2, 0)));
            Assert.AreEqual(3, HexCoord.Distance(new HexCoord(1, -2), new HexCoord(-1, 1)));
        }

        [Test]
        public void NeighborsAreAtDistanceOne()
        {
            var c = new HexCoord(2, -3);
            foreach (var n in c.Neighbors()) Assert.AreEqual(1, HexCoord.Distance(c, n));
        }

        [Test]
        public void MirrorIsInvolutionAndFlipsScreenX()
        {
            var layout = new HexLayout(1.0);
            foreach (var c in HexCoord.Range(HexCoord.Zero, 3))
            {
                Assert.AreEqual(c, c.MirrorX().MirrorX());
                layout.ToWorld(c, out var x, out var y);
                layout.ToWorld(c.MirrorX(), out var mx, out var my);
                Assert.AreEqual(-x, mx, 1e-9);
                Assert.AreEqual(y, my, 1e-9);
            }
        }

        [Test]
        public void PixelRoundTrip()
        {
            var layout = new HexLayout(0.5, 0.75);
            foreach (var c in HexCoord.Range(HexCoord.Zero, 5))
            {
                layout.ToWorld(c, out var x, out var y);
                Assert.AreEqual(c, layout.FromWorld(x, y));
                Assert.AreEqual(c, layout.FromWorld(x + 0.1, y - 0.05)); // 칸 안쪽 아무 점
            }
        }
    }

    public class BoardTests
    {
        [Test]
        public void NewBoardHasFloorBorderAndDoor()
        {
            var b = new HexBoard(BoardId.Indoor, 3, TileKind.Wall, doorDirection: 5);
            Assert.AreEqual(37, b.FloorCount);
            Assert.AreEqual(37 + 24, b.Tiles.Count);
            Assert.AreEqual(TileKind.Door, b.Get(b.DoorCoord).Kind);
            Assert.AreEqual(4, b.DoorCoord.Length);
            Assert.AreEqual(23, b.Tiles.Values.Count(t => t.Kind == TileKind.Wall));
        }

        [Test]
        public void ExpandTurnsOldBorderIntoFloorAndMovesDoorOut()
        {
            var b = new HexBoard(BoardId.Outdoor, 3, TileKind.Edge, doorDirection: 2);
            var oldDoor = b.DoorCoord;
            Assert.IsTrue(b.Expand());
            Assert.AreEqual(4, b.Radius);
            Assert.AreEqual(61, b.FloorCount);
            Assert.AreEqual(TileKind.Floor, b.Get(oldDoor).Kind);
            Assert.AreEqual(5, b.DoorCoord.Length);
            Assert.AreEqual(TileKind.Door, b.Get(b.DoorCoord).Kind);
        }

        [Test]
        public void ExpandStopsAtMaxRadius()
        {
            var b = new HexBoard(BoardId.Indoor, 2, TileKind.Wall, 5, maxRadius: 3);
            Assert.IsTrue(b.Expand());
            Assert.IsFalse(b.Expand());
            Assert.AreEqual(3, b.Radius);
        }
    }

    public class PlacementTests
    {
        private HouseWorld _w;

        [SetUp]
        public void SetUp() => _w = new HouseWorld(3, 3);

        [Test]
        public void PlaceMultiCellFurnitureOccupiesFootprint()
        {
            var r = _w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, HexCoord.Zero, false, out var f);
            Assert.AreEqual(PlaceResult.Ok, r);
            Assert.AreEqual(3, f.Cells.Count);
            Assert.AreSame(f, _w.Indoor.Get(new HexCoord(1, 0)).Furniture);
            Assert.AreEqual(CellRole.Entry, _w.Indoor.Get(new HexCoord(0, 1)).FurnitureRole);
        }

        [Test]
        public void MirroredFootprintGoesToTheOtherSide()
        {
            _w.TryPlace(FurnitureCatalog.CatTower3, BoardId.Indoor, HexCoord.Zero, true, out var f);
            // (1,0) 의 반전은 (-1,0)
            Assert.AreSame(f, _w.Indoor.Get(new HexCoord(-1, 0)).Furniture);
            Assert.IsNull(_w.Indoor.Get(new HexCoord(1, 0)).Furniture);
        }

        [Test]
        public void RejectOverlapWallDoorAndWrongZone()
        {
            _w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out _);
            Assert.AreEqual(PlaceResult.Occupied, _w.CanPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, HexCoord.Zero, false));
            Assert.AreEqual(PlaceResult.NotFloor, _w.CanPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(4, -4), false));
            Assert.AreEqual(PlaceResult.NotFloor, _w.CanPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, _w.Indoor.DoorCoord, false));
            Assert.AreEqual(PlaceResult.OutOfBoard, _w.CanPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(9, 0), false));
            Assert.AreEqual(PlaceResult.WrongZone, _w.CanPlace(FurnitureCatalog.LitterBox, BoardId.Outdoor, HexCoord.Zero, false));
        }

        [Test]
        public void RejectPlacementThatBlocksTheDoor()
        {
            // 실내 문 (0,4) 과 맞닿은 바닥은 (0,3) 하나뿐 (꼭짓점 문). 막히는 가구로 덮으면 거부.
            var door = _w.Indoor.DoorCoord;
            var inner = door.Neighbors().Where(n => _w.Indoor.Get(n)?.Kind == TileKind.Floor).ToList();
            Assert.AreEqual(1, inner.Count);
            var pillar = new FurnitureDef("pillar", "기둥", 0, NeedType.None, InteractionType.None, ZoneMask.Both,
                new FootprintCell(0, 0, CellRole.Blocked));
            var r = _w.CanPlace(pillar, BoardId.Indoor, inner[0], false);
            Assert.AreEqual(PlaceResult.BlocksPath, r);
        }

        [Test]
        public void RejectPlacementThatIsolatesACell()
        {
            // 중앙 (0,0) 을 둘러싼 6칸 중 5칸을 막는 가구로 채우고, 마지막 칸을 막으면 (0,0) 이 고립된다.
            var ring = HexCoord.Ring(HexCoord.Zero, 1).ToList();
            for (int i = 0; i < 5; i++)
            {
                var single = new FurnitureDef("blk" + i, "막힘", 0, NeedType.None, InteractionType.None, ZoneMask.Both,
                    new FootprintCell(0, 0, CellRole.Blocked));
                Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(single, BoardId.Indoor, ring[i], false, out _), "칸 " + i);
            }
            var last = new FurnitureDef("blk5", "막힘", 0, NeedType.None, InteractionType.None, ZoneMask.Both,
                new FootprintCell(0, 0, CellRole.Blocked));
            Assert.AreEqual(PlaceResult.BlocksPath, _w.CanPlace(last, BoardId.Indoor, ring[5], false));
        }

        [Test]
        public void RugDoesNotBlockAnything()
        {
            Assert.AreEqual(PlaceResult.Ok, _w.TryPlace(FurnitureCatalog.BigRug, BoardId.Indoor, HexCoord.Zero, false, out _));
            Assert.IsTrue(_w.IsTransit(new Cell(BoardId.Indoor, HexCoord.Zero)));
        }

        [Test]
        public void RemoveFreesCellsAndBumpsVersion()
        {
            _w.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, HexCoord.Zero, false, out var f);
            int v = _w.Version;
            Assert.IsTrue(_w.Remove(f.Id));
            Assert.Greater(_w.Version, v);
            Assert.IsNull(_w.Indoor.Get(HexCoord.Zero).Furniture);
        }

        [Test]
        public void FurnitureSurvivesExpansion()
        {
            _w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, HexCoord.Zero, false, out var f);
            Assert.IsTrue(_w.Expand(BoardId.Indoor));
            Assert.AreSame(f, _w.Indoor.Get(HexCoord.Zero).Furniture);
        }
    }

    public class DistanceMapTests
    {
        [Test]
        public void TargetIsZeroAndNeighborsAreOne()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out var bowl);
            var map = DistanceMap.Build(w, bowl);
            Assert.AreEqual(0, map.Get(new Cell(BoardId.Indoor, HexCoord.Zero)));
            foreach (var n in HexCoord.Zero.Neighbors()) Assert.AreEqual(1, map.Get(new Cell(BoardId.Indoor, n)));
            Assert.AreEqual(3, map.Get(new Cell(BoardId.Indoor, new HexCoord(3, 0))));
        }

        [Test]
        public void DistanceCrossesTheDoorIntoTheYard()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out var bowl);
            var map = DistanceMap.Build(w, bowl);

            var indoorDoor = new Cell(BoardId.Indoor, w.Indoor.DoorCoord);
            var yardDoor = new Cell(BoardId.Outdoor, w.Outdoor.DoorCoord);
            var yardCenter = new Cell(BoardId.Outdoor, HexCoord.Zero);

            Assert.AreEqual(4, map.Get(indoorDoor));           // 중앙에서 문까지 4칸
            Assert.AreEqual(5, map.Get(yardDoor));             // 문을 넘으면 +1
            Assert.AreEqual(5 + 4, map.Get(yardCenter));       // 마당 문에서 마당 중앙까지 4칸
        }

        [Test]
        public void WallsAreNeverMapped()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out var bowl);
            var map = DistanceMap.Build(w, bowl);
            foreach (var t in w.Indoor.Tiles.Values.Where(t => t.Kind == TileKind.Wall))
                Assert.AreEqual(DistanceMap.Unreachable, map.Get(new Cell(BoardId.Indoor, t.Coord)));
        }

        [Test]
        public void BlockedCellsAreRoutedAround()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out var bowl);
            // 그릇과 중앙 사이 (1,0) 을 1칸 기둥으로 막음
            var pillar = new FurnitureDef("pillar", "기둥", 0, NeedType.None, InteractionType.None, ZoneMask.Both,
                new FootprintCell(0, 0, CellRole.Blocked));
            Assert.AreEqual(PlaceResult.Ok, w.TryPlace(pillar, BoardId.Indoor, new HexCoord(1, 0), false, out _));
            var map = DistanceMap.Build(w, bowl);
            Assert.AreEqual(DistanceMap.Unreachable, map.Get(new Cell(BoardId.Indoor, new HexCoord(1, 0))));
            Assert.AreEqual(3, map.Get(new Cell(BoardId.Indoor, HexCoord.Zero))); // 직선이면 2칸, 돌아서 3칸
        }

        [Test]
        public void WalkingDownhillAlwaysArrives()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-1, -1), false, out var tower);
            w.TryPlace(FurnitureCatalog.Bookshelf, BoardId.Indoor, new HexCoord(1, 1), false, out _);
            var map = DistanceMap.Build(w, tower);

            var cur = new Cell(BoardId.Outdoor, new HexCoord(2, 1)); // 마당에서 출발
            int steps = 0;
            while (map.Get(cur) != 0 && steps < 100)
            {
                Assert.IsTrue(map.TryStep(w, cur, out var next, 0.5));
                Assert.Less(map.Get(next), map.Get(cur));
                cur = next;
                steps++;
            }
            Assert.AreEqual(0, map.Get(cur));
            Assert.AreEqual(BoardId.Indoor, cur.Board);
        }

        [Test]
        public void StrayCannotStepThroughTheDoor()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out var bowl);
            var map = DistanceMap.Build(w, bowl);
            var yardDoor = new Cell(BoardId.Outdoor, w.Outdoor.DoorCoord);
            Assert.IsFalse(map.TryStep(w, yardDoor, out _, 0, throughDoor: false));
            Assert.IsTrue(map.TryStep(w, yardDoor, out var inside, 0, throughDoor: true));
            Assert.AreEqual(BoardId.Indoor, inside.Board);
        }

        [Test]
        public void CacheRebuildsOnlyAfterWorldChanges()
        {
            var w = new HouseWorld(3, 3);
            w.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, HexCoord.Zero, false, out var bowl);
            var cache = new DistanceMapCache(w);
            var a = cache.Get(bowl.Id);
            Assert.AreSame(a, cache.Get(bowl.Id));
            w.TryPlace(FurnitureCatalog.BoxSmall, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            Assert.AreNotSame(a, cache.Get(bowl.Id));
        }
    }
}
