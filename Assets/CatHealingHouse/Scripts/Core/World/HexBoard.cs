using System;
using System.Collections.Generic;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;

namespace CatHealingHouse.World
{
    public enum BoardId { Indoor = 0, Outdoor = 1 }

    public enum TileKind
    {
        Floor, // 바닥. 가구를 놓을 수 있다.
        Wall,  // 테두리 벽. 걸을 수 없다. (실내)
        Edge,  // 테두리 수풀·담벼락. 걸을 수 있지만 가구는 못 놓는다. 길냥이 스폰/퇴장 지점. (야외)
        Door,  // 두 보드를 잇는 문.
    }

    /// <summary>보드 위의 한 칸.</summary>
    public sealed class Tile
    {
        public readonly HexCoord Coord;
        public TileKind Kind;
        public FurnitureInstance Furniture; // 이 칸을 차지한 가구 (없으면 null)
        public CellRole FurnitureRole;      // 그 가구에서 이 칸의 역할

        public Tile(HexCoord coord, TileKind kind) { Coord = coord; Kind = kind; }

        public bool HasFurniture => Furniture != null;
    }

    /// <summary>
    /// 육각형 보드 하나 (실내 또는 야외). 반지름 R 까지는 바닥, R+1 링은 테두리.
    /// 문은 테두리 링 위 정해진 방향의 꼭짓점 칸에 있고, 확장하면 바깥으로 함께 밀려난다.
    /// </summary>
    public sealed class HexBoard
    {
        public readonly BoardId Id;
        public readonly TileKind BorderKind;
        /// <summary>문을 꼭짓점 대신 맨 아래 변의 가운데(6시 방향)에 둔다. doorDirection 자리에 넣는다.</summary>
        public const int DoorBottomCenter = -1;

        public readonly int DoorDirection;
        public readonly int MaxRadius;

        public int Radius { get; private set; }
        public HexCoord DoorCoord { get; private set; }

        private readonly Dictionary<HexCoord, Tile> _tiles = new Dictionary<HexCoord, Tile>();
        public IReadOnlyDictionary<HexCoord, Tile> Tiles => _tiles;

        public HexBoard(BoardId id, int radius, TileKind borderKind, int doorDirection, int maxRadius = 8)
        {
            if (borderKind != TileKind.Wall && borderKind != TileKind.Edge)
                throw new ArgumentException("테두리는 Wall 또는 Edge 여야 합니다.", nameof(borderKind));
            if (radius < 1) throw new ArgumentOutOfRangeException(nameof(radius));

            Id = id;
            BorderKind = borderKind;
            DoorDirection = doorDirection;
            MaxRadius = maxRadius;
            Radius = radius;

            foreach (var c in HexCoord.Range(HexCoord.Zero, radius)) _tiles[c] = new Tile(c, TileKind.Floor);
            BuildBorder();
        }

        public bool TryGet(HexCoord c, out Tile tile) => _tiles.TryGetValue(c, out tile);
        public Tile Get(HexCoord c) => _tiles.TryGetValue(c, out var t) ? t : null;
        public bool Contains(HexCoord c) => _tiles.ContainsKey(c);

        public int FloorCount
        {
            get { int n = 0; foreach (var t in _tiles.Values) if (t.Kind == TileKind.Floor) n++; return n; }
        }

        public bool CanExpand => Radius < MaxRadius;

        /// <summary>반지름 +1. 기존 테두리(문 포함)는 바닥이 되고, 바깥에 새 테두리와 문이 생긴다.</summary>
        public bool Expand()
        {
            if (!CanExpand) return false;
            foreach (var c in HexCoord.Ring(HexCoord.Zero, Radius + 1)) _tiles[c].Kind = TileKind.Floor;
            Radius++;
            BuildBorder();
            return true;
        }

        private void BuildBorder()
        {
            foreach (var c in HexCoord.Ring(HexCoord.Zero, Radius + 1))
            {
                if (_tiles.TryGetValue(c, out var t)) t.Kind = BorderKind;
                else _tiles[c] = new Tile(c, BorderKind);
            }
            int n = Radius + 1;
            // 맨 아래 줄은 r = n (화면 y = -1.5r). 그 줄의 가운데 칸. n 이 홀수면 반 칸 오른쪽.
            DoorCoord = DoorDirection == DoorBottomCenter ? new HexCoord(-(n / 2), n) : HexCoord.Direction(DoorDirection) * n;
            _tiles[DoorCoord].Kind = TileKind.Door;
        }
    }
}
