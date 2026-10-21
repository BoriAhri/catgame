using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.World;

namespace CatHealingHouse.Economy
{
    /// <summary>트레이의 한 칸. 가구 하나 + 결과물 종류 하나 (화장실은 감자 칸과 맛동산 칸이 따로).</summary>
    public sealed class TraySlot
    {
        public readonly int FurnitureId;
        public readonly ItemKind Kind;
        public readonly string Label;
        public int Count;

        public TraySlot(int furnitureId, ItemKind kind, string label)
        {
            FurnitureId = furnitureId;
            Kind = kind;
            Label = label;
        }

        public int Value => Count * RewardTable.UnitValue(Kind);
    }

    /// <summary>가구 위에 떠 있는 하트. 가구당 최대 1개, 시간이 지나면 사라진다.</summary>
    public sealed class HeartMarker
    {
        public int FurnitureId;
        public BoardId Board;
        public double CreatedAt;
        public double ExpiresAt;
    }

    /// <summary>화장실 하나의 상태. 마지막 청소 후 사용 횟수가 곧 오염 단계.</summary>
    public sealed class LitterState
    {
        public int Pee;
        public int Poop;
        public int Uses => Pee + Poop;
    }

    /// <summary>바닥에 떨어진 털뭉치·수염.</summary>
    public sealed class DroppedItem
    {
        public int Id;
        public Cell Cell;
        public ItemKind Kind;
        public double CreatedAt;
    }

    public enum LitterStage { Clean = 0, Normal = 1, Dirty = 2, VeryDirty = 3 }

    [Serializable]
    public sealed class EconomyTuning
    {
        public int TraySlotCap = 10;              // 칸마다 쌓이는 상한 (나중에 '넉넉한 트레이' 스킬로 증가)
        public double HeartLifetime = 180;         // 하트가 떠 있는 시간(초)
        public int MaxDropsPerBoard = 5;

        public double FurballPerHop = 0.025;       // 걸을 때 한 칸마다
        public double WhiskerPerHop = 0.003;
        public double FurballGroomPerSecond = 0.05;
        public double FurballScratchPerSecond = 0.04;
        public double WhiskerScratchPerSecond = 0.008;

        public double PeeChance = 0.65;            // 화장실 한 번 쓸 때 소변일 확률 (나머지는 대변)

        // 화장실 단계별 사용 후 기분 변화 (깨끗 / 보통 / 더러움 / 매우 더러움)
        public double[] LitterMood = { 6, 0, -6, -15 };
        public double CleanMoodBonus = 5;          // 청소하면 같은 보드 고양이 모두
        public double VeryDirtyAvoidCost = 8;      // 매우 더러운 화장실은 이만큼 멀게 느낀다
    }

    /// <summary>
    /// 3단계 경제: 트레이(돈), 가구 위 하트, 화장실 오염과 청소, 바닥 드롭.
    /// 고양이 시뮬레이션의 이벤트에 붙어서 같은 시계로 돈다.
    /// </summary>
    public sealed class HouseEconomy
    {
        public readonly CatSimulation Sim;
        public readonly Wallet Wallet = new Wallet();
        public readonly EconomyTuning Tuning;

        private readonly List<TraySlot> _tray = new List<TraySlot>();
        private readonly Dictionary<int, HeartMarker> _hearts = new Dictionary<int, HeartMarker>();
        private readonly Dictionary<int, LitterState> _litter = new Dictionary<int, LitterState>();
        private readonly List<DroppedItem> _drops = new List<DroppedItem>();
        private readonly Random _rng;
        private int _nextDropId = 1;

        public IReadOnlyList<TraySlot> Tray => _tray;
        public IReadOnlyCollection<HeartMarker> Hearts => _hearts.Values;
        public IReadOnlyList<DroppedItem> Drops => _drops;

        /// <summary>결과물이 가구에서 트레이로 날아갈 때 (화면 연출용). (가구, 종류, 개수)</summary>
        public event Action<FurnitureInstance, ItemKind, int> ItemSentToTray;
        public event Action<DroppedItem> DropSpawned;
        public event Action Changed;

        public HouseEconomy(CatSimulation sim, EconomyTuning tuning = null, int seed = 99)
        {
            Sim = sim;
            Tuning = tuning ?? new EconomyTuning();
            _rng = new Random(seed);
            sim.InteractionCompleted += OnInteractionCompleted;
            sim.CatHopped += OnCatHopped;
            sim.Stepped += OnStepped;
            sim.ExtraCost = LitterAvoidCost;
        }

        private double Now => Sim.SimTime;

        // ───────────────────────── 가구 사용 완료 ─────────────────────────

        private void OnInteractionCompleted(Cat cat, FurnitureInstance f)
        {
            if (RewardTable.IsLitterBox(f.Def))
            {
                var st = LitterOf(f.Id);
                var stageBefore = StageOf(st.Uses);
                cat.AddMood(Tuning.LitterMood[(int)stageBefore]);
                if (_rng.NextDouble() < Tuning.PeeChance) st.Pee++; else st.Poop++;
            }
            else
            {
                var kind = RewardTable.TrayItemFor(f.Def);
                if (kind != ItemKind.None) AddToTray(f, kind, 1);
            }

            if (!_hearts.ContainsKey(f.Id))
                _hearts[f.Id] = new HeartMarker { FurnitureId = f.Id, Board = f.Board, CreatedAt = Now, ExpiresAt = Now + Tuning.HeartLifetime };
            Changed?.Invoke();
        }

        private int AddToTray(FurnitureInstance f, ItemKind kind, int count)
        {
            var slot = _tray.Find(s => s.FurnitureId == f.Id && s.Kind == kind);
            if (slot == null)
            {
                slot = new TraySlot(f.Id, kind, f.Def.DisplayName);
                _tray.Add(slot);
            }
            int add = Math.Max(0, Math.Min(count, Tuning.TraySlotCap - slot.Count));
            slot.Count += add;
            if (add > 0) ItemSentToTray?.Invoke(f, kind, add);
            return add;
        }

        /// <summary>트레이의 한 칸을 탭: 그 칸만 돈으로 바뀐다.</summary>
        public long CollectTraySlot(int index)
        {
            if (index < 0 || index >= _tray.Count) return 0;
            var slot = _tray[index];
            long value = slot.Value;
            _tray.RemoveAt(index);
            if (value > 0) Wallet.Add(ItemKind.Money, value);
            Changed?.Invoke();
            return value;
        }

        // ───────────────────────── 하트 ─────────────────────────

        public HeartMarker HeartOn(int furnitureId) => _hearts.TryGetValue(furnitureId, out var h) ? h : null;

        public bool CollectHeart(int furnitureId)
        {
            if (!_hearts.Remove(furnitureId)) return false;
            Wallet.Add(ItemKind.Heart, 1);
            Changed?.Invoke();
            return true;
        }

        // ───────────────────────── 화장실 ─────────────────────────

        private LitterState LitterOf(int furnitureId)
        {
            if (!_litter.TryGetValue(furnitureId, out var st)) _litter[furnitureId] = st = new LitterState();
            return st;
        }

        public static LitterStage StageOf(int uses) => uses <= 0 ? LitterStage.Clean : uses == 1 ? LitterStage.Normal : uses == 2 ? LitterStage.Dirty : LitterStage.VeryDirty;

        public LitterStage StageOfLitter(int furnitureId) => StageOf(_litter.TryGetValue(furnitureId, out var st) ? st.Uses : 0);

        public int UsesOfLitter(int furnitureId) => _litter.TryGetValue(furnitureId, out var st) ? st.Uses : 0;

        /// <summary>화장실을 탭해서 청소. 마지막 청소 후 사용 횟수만큼 감자(소변)·맛동산(대변)이 트레이로 간다.</summary>
        public bool CleanLitter(int furnitureId)
        {
            var f = Sim.World.GetFurniture(furnitureId);
            if (f == null || !RewardTable.IsLitterBox(f.Def)) return false;
            var st = LitterOf(furnitureId);
            if (st.Uses == 0) return false;

            if (st.Pee > 0) AddToTray(f, ItemKind.Potato, st.Pee);
            if (st.Poop > 0) AddToTray(f, ItemKind.Mattdongsan, st.Poop);
            st.Pee = st.Poop = 0;

            foreach (var c in Sim.Cats)
                if (c.Cell.Board == f.Board) c.AddMood(Tuning.CleanMoodBonus);
            Changed?.Invoke();
            return true;
        }

        private double LitterAvoidCost(Cat cat, FurnitureInstance f)
        {
            if (!RewardTable.IsLitterBox(f.Def)) return 0;
            return StageOfLitter(f.Id) == LitterStage.VeryDirty ? Tuning.VeryDirtyAvoidCost : 0;
        }

        // ───────────────────────── 바닥 드롭 ─────────────────────────

        private void OnCatHopped(Cat cat)
        {
            if (cat.Affiliation != Affiliation.Resident) return;
            if (cat.State != CatState.Wandering && cat.State != CatState.MovingToTarget) return;
            // 방금 떠난 칸에 떨어뜨린다 (고양이가 지나간 자리)
            double roll = _rng.NextDouble();
            if (roll < Tuning.WhiskerPerHop) TryDrop(cat.PrevCell, ItemKind.Whisker);
            else if (roll < Tuning.WhiskerPerHop + Tuning.FurballPerHop) TryDrop(cat.PrevCell, ItemKind.Furball);
        }

        private void OnStepped(double dt)
        {
            // 만료된 하트 정리
            List<int> expired = null;
            foreach (var h in _hearts.Values)
                if (Now >= h.ExpiresAt) (expired ?? (expired = new List<int>())).Add(h.FurnitureId);
            if (expired != null)
            {
                foreach (var id in expired) _hearts.Remove(id);
                Changed?.Invoke();
            }

            // 그루밍·스크래칭 중 털 날림
            foreach (var cat in Sim.Cats)
            {
                if (cat.Affiliation != Affiliation.Resident) continue;
                if (cat.State == CatState.Grooming)
                {
                    if (_rng.NextDouble() < Tuning.FurballGroomPerSecond * dt) TryDropNear(cat, ItemKind.Furball);
                }
                else if (cat.State == CatState.Using)
                {
                    var f = Sim.World.GetFurniture(cat.TargetFurnitureId);
                    if (f == null || f.Def.Id != FurnitureCatalog.Scratcher.Id) continue;
                    double roll = _rng.NextDouble();
                    if (roll < Tuning.WhiskerScratchPerSecond * dt) TryDropNear(cat, ItemKind.Whisker);
                    else if (roll < (Tuning.WhiskerScratchPerSecond + Tuning.FurballScratchPerSecond) * dt) TryDropNear(cat, ItemKind.Furball);
                }
            }
        }

        private void TryDropNear(Cat cat, ItemKind kind)
        {
            if (CanDropAt(cat.Cell)) { TryDrop(cat.Cell, kind); return; }
            foreach (var n in Sim.World.Neighbors(cat.Cell, throughDoor: false))
                if (CanDropAt(n)) { TryDrop(n, kind); return; }
        }

        private bool CanDropAt(Cell c)
        {
            var t = Sim.World.GetTile(c);
            if (t == null || t.Kind != TileKind.Floor || !Sim.World.IsTransit(c)) return false;
            foreach (var d in _drops) if (d.Cell == c) return false;
            return true;
        }

        public int DropCount(BoardId board)
        {
            int n = 0;
            foreach (var d in _drops) if (d.Cell.Board == board) n++;
            return n;
        }

        /// <summary>보드 위 드롭이 5개면 더 이상 떨어지지 않는다 (줍으러 올 이유).</summary>
        private bool TryDrop(Cell c, ItemKind kind)
        {
            if (DropCount(c.Board) >= Tuning.MaxDropsPerBoard || !CanDropAt(c)) return false;
            var d = new DroppedItem { Id = _nextDropId++, Cell = c, Kind = kind, CreatedAt = Now };
            _drops.Add(d);
            DropSpawned?.Invoke(d);
            Changed?.Invoke();
            return true;
        }

        public bool PickDrop(int dropId)
        {
            int i = _drops.FindIndex(d => d.Id == dropId);
            if (i < 0) return false;
            var d = _drops[i];
            _drops.RemoveAt(i);
            if (d.Kind == ItemKind.Gift) Wallet.Add(ItemKind.Heart, 2);
            else Wallet.Add(d.Kind, 1);
            Changed?.Invoke();
            return true;
        }

        /// <summary>길냥이가 두고 간 선물. 드롭 상한(5개)과 상관없이 놓인다. 그 칸이 안 되면 옆 칸.</summary>
        public bool AddGift(Cell near)
        {
            var candidates = new List<Cell> { near };
            foreach (var n in Sim.World.Neighbors(near, throughDoor: false)) candidates.Add(n);
            foreach (var c in candidates)
            {
                if (!CanDropAt(c)) continue;
                var d = new DroppedItem { Id = _nextDropId++, Cell = c, Kind = ItemKind.Gift, CreatedAt = Now };
                _drops.Add(d);
                DropSpawned?.Invoke(d);
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>가구가 철거되면 그 가구의 하트·화장실 상태를 정리 (트레이 칸은 남겨서 수확할 수 있게).</summary>
        public void ForgetFurniture(int furnitureId)
        {
            _hearts.Remove(furnitureId);
            _litter.Remove(furnitureId);
            Changed?.Invoke();
        }
    }
}
