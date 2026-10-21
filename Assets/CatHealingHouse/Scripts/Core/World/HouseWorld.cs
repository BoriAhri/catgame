using System;
using System.Collections.Generic;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;

namespace CatHealingHouse.World
{
    /// <summary>두 보드를 통틀어 한 칸을 가리키는 주소.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly BoardId Board;
        public readonly HexCoord Coord;
        public Cell(BoardId board, HexCoord coord) { Board = board; Coord = coord; }
        public bool Equals(Cell o) => Board == o.Board && Coord == o.Coord;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => unchecked(Coord.GetHashCode() * 31 + (int)Board);
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"{Board}{Coord}";
    }

    public enum PlaceResult
    {
        Ok,
        WrongZone,     // 이 보드에 놓을 수 없는 가구 (예: 화장실은 실내 전용)
        OutOfBoard,    // 풋프린트가 보드 밖으로 나감
        NotFloor,      // 벽, 수풀, 문 위에는 못 놓음
        Occupied,      // 이미 다른 가구가 있음
        BlocksPath,    // 놓으면 어떤 칸이 고립되거나 문/가구로 가는 길이 막힘
        NotWall,       // 벽 가구는 위쪽 벽(뒷벽·사선 벽)에만
        Window,        // 창문 자리에는 벽 가구를 못 건다
    }

    /// <summary>벽 가구를 걸 수 있는 위쪽 벽의 어느 면인지.</summary>
    public enum WallSide { None, Back, Left, Right }

    /// <summary>
    /// 실내 보드 + 야외 보드 + 둘을 잇는 문. 가구 배치와 길 연결 규칙을 책임진다.
    /// 엔진을 모르는 순수 로직이라 화면이 꺼져 있어도, 오프라인 계산 중에도 그대로 돈다.
    /// </summary>
    public sealed class HouseWorld
    {
        public readonly HexBoard Indoor;
        public readonly HexBoard Outdoor;

        private readonly Dictionary<int, FurnitureInstance> _furniture = new Dictionary<int, FurnitureInstance>();
        private int _nextFurnitureId = 1;

        /// <summary>구조(타일, 가구)가 바뀔 때마다 올라간다. 거리맵 캐시가 이걸 보고 다시 굽는다.</summary>
        public int Version { get; private set; }
        public event Action Changed;

        public HouseWorld(int indoorRadius = 3, int outdoorRadius = 3)
        {
            // 실내 문은 아래쪽(남동), 야외 문은 위쪽(북서). 마당에서 위로 올라가면 집으로 들어가는 느낌.
            Indoor = new HexBoard(BoardId.Indoor, indoorRadius, TileKind.Wall, doorDirection: 5);
            // 마당 문은 맨 아래 가운데(집 쪽). 길냥이는 반대편 위쪽 수풀에서 나오고 그쪽으로 도망간다.
            Outdoor = new HexBoard(BoardId.Outdoor, outdoorRadius, TileKind.Edge, doorDirection: HexBoard.DoorBottomCenter);
        }

        public HexBoard GetBoard(BoardId id) => id == BoardId.Indoor ? Indoor : Outdoor;
        public IEnumerable<FurnitureInstance> Furniture => _furniture.Values;
        public FurnitureInstance GetFurniture(int id) => _furniture.TryGetValue(id, out var f) ? f : null;

        public Tile GetTile(Cell c) => GetBoard(c.Board).Get(c.Coord);

        /// <summary>문 칸이면 반대편 보드의 문 칸을 돌려준다.</summary>
        public bool TryGetPortal(Cell c, out Cell other)
        {
            var board = GetBoard(c.Board);
            if (c.Coord == board.DoorCoord)
            {
                var ob = c.Board == BoardId.Indoor ? Outdoor : Indoor;
                other = new Cell(ob.Id, ob.DoorCoord);
                return true;
            }
            other = default;
            return false;
        }

        /// <summary>인접 칸 (보드 안 6방향 + 문으로 이어진 반대편). 존재하는 칸만.</summary>
        public IEnumerable<Cell> Neighbors(Cell c, bool throughDoor = true)
        {
            var board = GetBoard(c.Board);
            foreach (var n in c.Coord.Neighbors())
                if (board.Contains(n)) yield return new Cell(c.Board, n);
            if (throughDoor && TryGetPortal(c, out var other)) yield return other;
        }

        /// <summary>지나가는 길로 쓸 수 있는 칸인지 (빈 바닥, 수풀, 문, 통과 가능한 가구 칸).</summary>
        public bool IsTransit(Cell c)
        {
            var t = GetTile(c);
            return t != null && IsTransit(t.Kind, t.Furniture != null ? t.FurnitureRole : (CellRole?)null);
        }

        private static bool IsTransit(TileKind kind, CellRole? role)
        {
            if (kind == TileKind.Wall) return false;
            return role == null || role == CellRole.Passable;
        }

        // ───────────────────────── 배치 ─────────────────────────

        public PlaceResult CanPlace(FurnitureDef def, BoardId boardId, HexCoord origin, bool mirrored)
        {
            if (!def.AllowedOn(boardId)) return PlaceResult.WrongZone;
            if (def.WallMounted) return CanPlaceWall(def, boardId, origin);
            var board = GetBoard(boardId);
            var overlay = new Dictionary<HexCoord, CellRole>();
            foreach (var cell in def.Resolve(origin, mirrored))
            {
                var t = board.Get(cell.Offset);
                if (t == null) return PlaceResult.OutOfBoard;
                if (t.Kind != TileKind.Floor) return PlaceResult.NotFloor;
                if (t.Furniture != null) return PlaceResult.Occupied;
                overlay[cell.Offset] = cell.Role;
            }
            var probe = new FurnitureInstance(0, def, boardId, origin, mirrored);
            return IsLayoutConnected(board, overlay, removedId: -1, extra: probe) ? PlaceResult.Ok : PlaceResult.BlocksPath;
        }

        public PlaceResult TryPlace(FurnitureDef def, BoardId boardId, HexCoord origin, bool mirrored, out FurnitureInstance placed)
        {
            placed = null;
            var result = CanPlace(def, boardId, origin, mirrored);
            if (result != PlaceResult.Ok) return result;

            placed = def.WallMounted
                ? new FurnitureInstance(_nextFurnitureId++, def, boardId, origin, false, ResolveCells(def, boardId, origin, false))
                : new FurnitureInstance(_nextFurnitureId++, def, boardId, origin, mirrored);
            var board = GetBoard(boardId);
            foreach (var cell in placed.Cells)
            {
                var t = board.Get(cell.Offset);
                t.Furniture = placed;
                t.FurnitureRole = cell.Role;
            }
            _furniture[placed.Id] = placed;
            MarkChanged();
            return PlaceResult.Ok;
        }

        /// <summary>가구를 치워서 길이 막히는 일은 없으므로 존재 여부만 확인한다.</summary>
        public bool CanRemove(int furnitureId) => _furniture.ContainsKey(furnitureId);

        public bool Remove(int furnitureId)
        {
            if (!_furniture.TryGetValue(furnitureId, out var f)) return false;
            var board = GetBoard(f.Board);
            foreach (var cell in f.Cells)
            {
                var t = board.Get(cell.Offset);
                if (t != null && t.Furniture == f) { t.Furniture = null; t.FurnitureRole = default; }
            }
            _furniture.Remove(furnitureId);
            MarkChanged();
            return true;
        }

        /// <summary>마지막 Expand 에서 새 벽에 다시 걸 자리가 없어 빠진 벽 가구 (상점이 보관함에 넣는다).</summary>
        public readonly List<FurnitureDef> LastDisplaced = new List<FurnitureDef>();

        public bool Expand(BoardId boardId)
        {
            LastDisplaced.Clear();
            var board = GetBoard(boardId);
            if (!board.CanExpand) return false;

            // 벽 가구는 벽과 함께 바깥으로 옮긴다: 떼어 두었다가, 넓힌 뒤 같은 방향의 가장 가까운 벽 자리에 다시 건다.
            var wall = new List<KeyValuePair<FurnitureDef, double>>();
            foreach (var f in new List<FurnitureInstance>(_furniture.Values))
            {
                if (f.Board != boardId || !f.Def.WallMounted) continue;
                wall.Add(new KeyValuePair<FurnitureDef, double>(f.Def, AngleOf(f.Origin)));
                Remove(f.Id);
            }
            if (!board.Expand()) return false;
            foreach (var kv in wall)
            {
                var slots = WallSlots(boardId);
                slots.Sort((a, b) => AngleDiff(AngleOf(a), kv.Value).CompareTo(AngleDiff(AngleOf(b), kv.Value)));
                bool ok = false;
                foreach (var s in slots)
                    if (TryPlace(kv.Key, boardId, s, false, out _) == PlaceResult.Ok) { ok = true; break; }
                if (!ok) LastDisplaced.Add(kv.Key);
            }
            MarkChanged();
            return true;
        }

        private static double AngleOf(HexCoord c)
        {
            new HexLayout(1, 1).ToWorld(c, out double x, out double y);
            return Math.Atan2(y, x);
        }

        private static double AngleDiff(double a, double b)
        {
            double d = Math.Abs(a - b) % (2 * Math.PI);
            return d > Math.PI ? 2 * Math.PI - d : d;
        }

        // ───────────────────────── 벽 가구 ─────────────────────────

        /// <summary>
        /// 위쪽 벽의 어느 면인지. 테두리 링 n = 반지름+1 에서 r &lt; 0 인 벽 칸만 (화면 위쪽 절반).
        /// 맨 윗줄(r = -n)은 뒷벽, q = n 쪽은 오른쪽 사선 벽, s = n 쪽은 왼쪽 사선 벽.
        /// </summary>
        public WallSide SideOf(BoardId boardId, HexCoord c)
        {
            var board = GetBoard(boardId);
            var t = board.Get(c);
            if (t == null || t.Kind != TileKind.Wall || c.R >= 0) return WallSide.None;
            int n = board.Radius + 1;
            if (c.R == -n) return WallSide.Back;
            if (c.Q == n) return WallSide.Right;
            if (c.S == n) return WallSide.Left;
            return WallSide.None;
        }

        /// <summary>창문 칸은 벽 가구를 걸 수 없다.</summary>
        public bool IsWindow(BoardId boardId, HexCoord c) =>
            boardId == BoardId.Indoor && WindowLight.WindowCells(Indoor).Contains(c);

        /// <summary>벽 가구를 걸 수 있는 벽 자리 전부 (창문 포함 여부와 상관없이 위쪽 벽 칸).</summary>
        public List<HexCoord> WallSlots(BoardId boardId)
        {
            var list = new List<HexCoord>();
            foreach (var t in GetBoard(boardId).Tiles.Values)
                if (SideOf(boardId, t.Coord) != WallSide.None) list.Add(t.Coord);
            return list;
        }

        private static HexCoord AlongWall(WallSide side)
        {
            switch (side)
            {
                case WallSide.Back: return new HexCoord(1, 0);   // 오른쪽으로
                case WallSide.Left: return new HexCoord(-1, 1);  // 사선 따라 아래로
                default: return new HexCoord(0, 1);              // 오른쪽 사선 따라 아래로
            }
        }

        private static readonly HexCoord[] BackIn = { new HexCoord(0, 1), new HexCoord(-1, 1) };
        private static readonly HexCoord[] LeftIn = { new HexCoord(0, 1), new HexCoord(1, 0) };
        private static readonly HexCoord[] RightIn = { new HexCoord(-1, 1), new HexCoord(-1, 0) };

        /// <summary>
        /// 가구가 놓였을 때 차지할 칸. 벽 가구는 벽 자리 WallWidth 칸 + 아래 바닥 입구 한 칸.
        /// 벽 가구인데 자리가 맞지 않으면 null.
        /// </summary>
        public List<FootprintCell> ResolveCells(FurnitureDef def, BoardId boardId, HexCoord origin, bool mirrored)
        {
            if (!def.WallMounted) return new List<FootprintCell>(def.Resolve(origin, mirrored));
            var side = SideOf(boardId, origin);
            if (side == WallSide.None) return null;
            var board = GetBoard(boardId);
            var cells = new List<FootprintCell>();
            var step = AlongWall(side);
            // 여러 칸짜리는 벽을 따라 한쪽으로 이어 붙이고, 그쪽이 막혔으면(벽 끝) 반대쪽으로
            bool Fits(HexCoord d)
            {
                for (int i = 1; i < def.WallWidth; i++) if (SideOf(boardId, origin + d * i) == WallSide.None) return false;
                return true;
            }
            if (!Fits(step))
            {
                var back = new HexCoord(-step.Q, -step.R);
                if (!Fits(back)) return null;
                step = back;
            }
            for (int i = 0; i < def.WallWidth; i++) cells.Add(new FootprintCell(origin + step * i, CellRole.Blocked));
            // 입구: 첫 벽 칸 바로 안쪽 바닥. 바닥이 아니면 다음 후보.
            var ins = side == WallSide.Back ? BackIn : side == WallSide.Left ? LeftIn : RightIn;
            HexCoord? entry = null;
            foreach (var d in ins)
            {
                var e = origin + d;
                var et = board.Get(e);
                if (et != null && et.Kind == TileKind.Floor) { entry = e; break; }
            }
            if (!entry.HasValue) return null;
            cells.Add(new FootprintCell(entry.Value, CellRole.Entry));
            return cells;
        }

        private PlaceResult CanPlaceWall(FurnitureDef def, BoardId boardId, HexCoord origin)
        {
            var board = GetBoard(boardId);
            var t0 = board.Get(origin);
            if (t0 == null) return PlaceResult.OutOfBoard;
            var cells = ResolveCells(def, boardId, origin, false);
            if (cells == null) return PlaceResult.NotWall;
            var overlay = new Dictionary<HexCoord, CellRole>();
            foreach (var cell in cells)
            {
                var t = board.Get(cell.Offset);
                if (t.Furniture != null) return PlaceResult.Occupied;
                if (cell.Role == CellRole.Blocked && IsWindow(boardId, cell.Offset)) return PlaceResult.Window;
                overlay[cell.Offset] = cell.Role;
            }
            var probe = new FurnitureInstance(0, def, boardId, origin, false, cells);
            return IsLayoutConnected(board, overlay, removedId: -1, extra: probe) ? PlaceResult.Ok : PlaceResult.BlocksPath;
        }

        private void MarkChanged()
        {
            Version++;
            Changed?.Invoke();
        }

        /// <summary>
        /// 가상의 배치(overlay)를 반영했을 때 보드가 여전히 하나로 이어져 있는지 확인.
        /// 1) 문에서 출발해 지나갈 수 있는 칸을 모두 방문할 수 있어야 하고
        /// 2) 쓸 수 있는 가구(정원 1 이상)는 모두 진입 가능한 칸이 하나 이상 있어야 한다.
        /// </summary>
        private bool IsLayoutConnected(HexBoard board, Dictionary<HexCoord, CellRole> overlay, int removedId, FurnitureInstance extra)
        {
            bool Transit(HexCoord c)
            {
                var t = board.Get(c);
                if (t == null) return false;
                if (overlay.TryGetValue(c, out var role)) return IsTransit(t.Kind, role);
                bool hasF = t.Furniture != null && t.Furniture.Id != removedId;
                return IsTransit(t.Kind, hasF ? t.FurnitureRole : (CellRole?)null);
            }

            var visited = new HashSet<HexCoord>();
            var queue = new Queue<HexCoord>();
            visited.Add(board.DoorCoord);
            queue.Enqueue(board.DoorCoord);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in cur.Neighbors())
                    if (!visited.Contains(n) && Transit(n)) { visited.Add(n); queue.Enqueue(n); }
            }

            foreach (var t in board.Tiles.Values)
                if (Transit(t.Coord) && !visited.Contains(t.Coord)) return false;

            bool Reachable(FurnitureInstance f)
            {
                if (f.Def.Capacity <= 0) return true;
                foreach (var u in f.UsableCells())
                {
                    if (visited.Contains(u)) return true;
                    foreach (var n in u.Neighbors()) if (visited.Contains(n)) return true;
                }
                return false;
            }

            foreach (var f in _furniture.Values)
                if (f.Board == board.Id && f.Id != removedId && !Reachable(f)) return false;
            if (extra != null && !Reachable(extra)) return false;
            return true;
        }
    }
}
