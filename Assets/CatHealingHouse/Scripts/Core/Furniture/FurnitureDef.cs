using System;
using System.Collections.Generic;
using CatHealingHouse.Hex;
using CatHealingHouse.World;

namespace CatHealingHouse.Furniture
{
    /// <summary>풋프린트의 각 칸이 고양이에게 어떤 의미인지.</summary>
    public enum CellRole
    {
        Blocked,  // 지나갈 수도, 들어갈 수도 없음 (캣타워 기둥, 침대 프레임)
        Passable, // 바닥처럼 지나갈 수 있음 (러그)
        Entry,    // 고양이가 가구를 쓰러 들어가는 칸. 도착은 되지만 경유는 안 됨
    }

    /// <summary>가구가 채워주는 욕구. 심즈식: 100 = 충족.</summary>
    public enum NeedType { None, Hunger, Bladder, Energy, Fun }

    /// <summary>애니메이션 묶음 (기획서 '상호작용 유형').</summary>
    public enum InteractionType { None, InPlace, Climb, Enter, Run }

    [Flags]
    public enum ZoneMask { None = 0, Indoor = 1, Outdoor = 2, Both = Indoor | Outdoor }

    [Serializable]
    public readonly struct FootprintCell
    {
        public readonly HexCoord Offset;
        public readonly CellRole Role;
        public FootprintCell(HexCoord offset, CellRole role) { Offset = offset; Role = role; }
        public FootprintCell(int q, int r, CellRole role) : this(new HexCoord(q, r), role) { }
    }

    /// <summary>
    /// 가구 정의 (디자이너가 표로 관리하는 값). 런타임 상태는 FurnitureInstance 에 둔다.
    /// 나중에 ScriptableObject 가 이 값을 채워 넘겨주는 방식으로 연결한다.
    /// </summary>
    public sealed class FurnitureDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int Capacity;          // 정원 (동시에 쓸 수 있는 마릿수)
        public readonly NeedType Satisfies;
        public readonly InteractionType Interaction;
        public readonly ZoneMask Zones;
        public readonly IReadOnlyList<FootprintCell> Footprint;

        public FurnitureDef(string id, string displayName, int capacity, NeedType satisfies,
            InteractionType interaction, ZoneMask zones, params FootprintCell[] footprint)
        {
            if (footprint == null || footprint.Length == 0) throw new ArgumentException("풋프린트가 비었습니다.");
            Id = id;
            DisplayName = displayName;
            Capacity = Math.Max(0, capacity);
            Satisfies = satisfies;
            Interaction = interaction;
            Zones = zones;
            Footprint = footprint;
        }

        /// <summary>욕구가 차는 속도 배율 (공방 장난감은 1보다 크다).</summary>
        public double Power { get; private set; } = 1;

        /// <summary>정의를 만들 때 회복 배율을 정한다.</summary>
        public FurnitureDef WithPower(double power) { Power = power; return this; }

        /// <summary>벽에 거는 가구면 차지하는 벽 자리 수 (1 이상). 바닥 가구는 0.</summary>
        public readonly int WallWidth;
        public bool WallMounted => WallWidth > 0;

        /// <summary>
        /// 벽 가구. 풋프린트는 놓을 자리(벽 쪽)에 따라 달라지므로 HouseWorld.ResolveCells 가 정한다:
        /// 벽 자리 WallWidth 칸(막힘) + 그 아래 바닥 한 칸(올라가는 입구).
        /// </summary>
        public FurnitureDef(string id, string displayName, int capacity, NeedType satisfies,
            InteractionType interaction, int wallWidth)
            : this(id, displayName, capacity, satisfies, interaction, ZoneMask.Indoor, new FootprintCell(0, 0, CellRole.Blocked))
        {
            WallWidth = Math.Max(1, wallWidth);
        }

        public int Size => WallMounted ? WallWidth + 1 : Footprint.Count;

        /// <summary>기준 칸 origin 에 놓았을 때 실제로 차지하는 칸들. mirrored 면 좌우 반전.</summary>
        public IEnumerable<FootprintCell> Resolve(HexCoord origin, bool mirrored)
        {
            foreach (var f in Footprint)
            {
                var off = mirrored ? f.Offset.MirrorX() : f.Offset;
                yield return new FootprintCell(origin + off, f.Role);
            }
        }

        public bool AllowedOn(BoardId board) =>
            (Zones & (board == BoardId.Indoor ? ZoneMask.Indoor : ZoneMask.Outdoor)) != 0;
    }

    /// <summary>보드 위에 놓인 가구 하나.</summary>
    public sealed class FurnitureInstance
    {
        public readonly int Id;
        public readonly FurnitureDef Def;
        public readonly BoardId Board;
        public readonly HexCoord Origin;
        public readonly bool Mirrored;
        public readonly IReadOnlyList<FootprintCell> Cells;

        // 슬롯마다 예약한 고양이 ID (없으면 0). 정원 5면 칸 5개.
        private readonly int[] _slots;

        /// <summary>예약 + 사용 중인 슬롯 수.</summary>
        public int OccupiedSlots
        {
            get { int n = 0; foreach (var s in _slots) if (s != 0) n++; return n; }
        }

        /// <summary>빈 슬롯을 예약하고 그 번호를 돌려준다. 꽉 찼으면 -1.</summary>
        public int ReserveSlot(int catId)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] == 0) { _slots[i] = catId; return i; }
            return -1;
        }

        public void ReleaseSlot(int slot, int catId)
        {
            if (slot >= 0 && slot < _slots.Length && _slots[slot] == catId) _slots[slot] = 0;
        }

        public int SlotOwner(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot] : 0;

        public FurnitureInstance(int id, FurnitureDef def, BoardId board, HexCoord origin, bool mirrored)
            : this(id, def, board, origin, mirrored, new List<FootprintCell>(def.Resolve(origin, mirrored))) { }

        /// <summary>칸을 직접 정해서 만든다 (벽 가구처럼 자리에 따라 풋프린트가 달라지는 경우).</summary>
        public FurnitureInstance(int id, FurnitureDef def, BoardId board, HexCoord origin, bool mirrored, IReadOnlyList<FootprintCell> cells)
        {
            Id = id;
            Def = def;
            Board = board;
            Origin = origin;
            Mirrored = mirrored;
            Cells = cells;
            _slots = new int[def.Capacity];
        }

        /// <summary>벽 가구가 걸린 벽 칸들 (바닥 가구면 비어 있음).</summary>
        public IEnumerable<HexCoord> WallCells()
        {
            if (!Def.WallMounted) yield break;
            foreach (var c in Cells) if (c.Role == CellRole.Blocked) yield return c.Offset;
        }

        public bool HasFreeSlot => OccupiedSlots < Def.Capacity;

        /// <summary>고양이가 이 가구를 쓰려고 설 수 있는 칸. 진입점이 있으면 진입점, 없으면 통과 칸.</summary>
        public IEnumerable<HexCoord> UsableCells()
        {
            bool anyEntry = false;
            foreach (var c in Cells) if (c.Role == CellRole.Entry) { anyEntry = true; yield return c.Offset; }
            if (anyEntry) yield break;
            foreach (var c in Cells) if (c.Role == CellRole.Passable) yield return c.Offset;
        }

        public override string ToString() => $"{Def.DisplayName}#{Id}@{Board}{Origin}{(Mirrored ? "(반전)" : "")}";
    }
}
