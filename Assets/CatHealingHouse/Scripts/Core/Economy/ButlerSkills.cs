using System;
using System.Collections.Generic;
using CatHealingHouse.Stray;

namespace CatHealingHouse.Economy
{
    public sealed class SkillDef
    {
        public string Id;
        public string Name;
        public string Desc;
        public long[] Costs;                 // 레벨 n → n+1 비용 (젤리 하트)
        public bool Locked;                  // 아직 열리지 않은 스킬 (특제 간식 단계에서)
        public Func<int, string> EffectAt;   // 레벨별 효과 설명
        public int MaxLevel => Costs.Length;
    }

    public enum SkillResult { Ok, NotEnoughHearts, MaxLevel, Locked, Unknown }

    /// <summary>
    /// 집사 스킬. 젤리 하트로 레벨업.
    ///  - 넉넉한 트레이: 트레이 칸마다 쌓이는 상한 +5/레벨
    ///  - 능숙한 손길: 길냥이 쓰다듬기 호감도 +1/레벨
    ///  - 고급 간식 레시피 해금: 특제 간식 단계에서 연다 (지금은 잠김)
    /// 비용·효과량은 초안.
    /// </summary>
    public sealed class ButlerSkills
    {
        public const string TrayPlus = "tray_plus";
        public const string GentleHands = "gentle_hands";
        public const string RecipeUnlock = "recipe_unlock";

        public readonly Wallet Wallet;
        private readonly HouseEconomy _eco;
        private readonly StraySystem _strays;
        private readonly int _baseTrayCap;
        private readonly double _basePet;
        private readonly Dictionary<string, int> _levels = new Dictionary<string, int>();

        public readonly List<SkillDef> Defs = new List<SkillDef>();
        public event Action Changed;

        public int TrayPerLevel = 5;
        public double PetPerLevel = 1;

        public ButlerSkills(HouseEconomy eco, StraySystem strays)
        {
            _eco = eco;
            _strays = strays;
            Wallet = eco.Wallet;
            _baseTrayCap = eco.Tuning.TraySlotCap;
            _basePet = strays != null ? strays.Tuning.PetAffection : 4;
            Defs.Add(new SkillDef
            {
                Id = TrayPlus, Name = "넉넉한 트레이", Desc = "트레이 칸마다 쌓이는 상한이 늘어요",
                Costs = new long[] { 5, 10, 20, 35 },
                EffectAt = lv => $"칸마다 {_baseTrayCap + lv * TrayPerLevel}개",
            });
            Defs.Add(new SkillDef
            {
                Id = GentleHands, Name = "능숙한 손길", Desc = "길냥이를 쓰다듬을 때 호감도가 더 올라요",
                Costs = new long[] { 5, 12, 25 },
                EffectAt = lv => $"쓰다듬기 +{_basePet + lv * PetPerLevel:0}",
            });
            Defs.Add(new SkillDef
            {
                Id = RecipeUnlock, Name = "고급 간식 레시피 해금", Desc = "특제 간식 레시피를 단계별로 열어요 (준비 중)",
                Costs = new long[] { 10, 20, 40 }, Locked = true,
                EffectAt = lv => lv == 0 ? "레시피 없음" : $"레시피 {lv}단계",
            });
        }

        public SkillDef Get(string id) => Defs.Find(d => d.Id == id);
        public int Level(string id) => _levels.TryGetValue(id, out var lv) ? lv : 0;

        /// <summary>다음 레벨 비용. 최대면 -1.</summary>
        public long NextCost(string id)
        {
            var d = Get(id);
            int lv = Level(id);
            return d == null || lv >= d.MaxLevel ? -1 : d.Costs[lv];
        }

        public SkillResult LevelUp(string id)
        {
            var d = Get(id);
            if (d == null) return SkillResult.Unknown;
            if (d.Locked) return SkillResult.Locked;
            long cost = NextCost(id);
            if (cost < 0) return SkillResult.MaxLevel;
            if (!Wallet.TrySpend(ItemKind.Heart, cost)) return SkillResult.NotEnoughHearts;
            _levels[id] = Level(id) + 1;
            Apply();
            Changed?.Invoke();
            return SkillResult.Ok;
        }

        /// <summary>레벨을 실제 수치에 반영한다 (불러오기 후에도 부른다).</summary>
        public void Apply()
        {
            _eco.Tuning.TraySlotCap = _baseTrayCap + Level(TrayPlus) * TrayPerLevel;
            if (_strays != null) _strays.Tuning.PetAffection = _basePet + Level(GentleHands) * PetPerLevel;
        }
    }
}
