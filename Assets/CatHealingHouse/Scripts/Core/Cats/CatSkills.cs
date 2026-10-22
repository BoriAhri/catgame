using System;
using System.Collections.Generic;

namespace CatHealingHouse.Cats
{
    /// <summary>고양이 패시브 스킬 하나 (친밀도 스킬 포인트로 올린다).</summary>
    public sealed class CatSkillDef
    {
        public string Id, Name, Desc;
        public int Max;
        /// <summary>레벨에 따른 효과 한 줄 (예: "트레이 +6%").</summary>
        public Func<int, string> Effect;
    }

    /// <summary>
    /// 고양이마다 따로 찍는 패시브 스킬. 그 고양이가 가구를 쓰거나 돌아다닐 때 나오는 재화를 늘려 준다.
    /// 집 전체에 적용되는 집사 스킬(ButlerSkills)과는 별개. 최대치 합(100)이 Lv.99 까지 받는 포인트(98)보다 많아서 다 찍을 수는 없다.
    /// </summary>
    public static class CatSkills
    {
        public const string Coin = "coin", Heart = "heart", Furball = "furball", Whisker = "whisker";

        public const double CoinPerLevel = 0.02;     // 가구를 쓰면 트레이 결과물 하나 더 나올 확률
        public const double HeartPerLevel = 0.015;   // 가구 하트 대기시간 단축
        public const double FurballPerLevel = 0.03;  // 털뭉치 떨어질 확률
        public const double WhiskerPerLevel = 0.03;  // 수염 떨어질 확률

        public static readonly CatSkillDef[] Defs =
        {
            new CatSkillDef { Id = Coin, Name = "동전 솜씨", Max = 25, Desc = "가구를 쓰면 가끔 트레이 결과물이 하나 더 나와요",
                Effect = lv => $"트레이 +{lv * CoinPerLevel * 100:0}%" },
            new CatSkillDef { Id = Heart, Name = "하트 뿜뿜", Max = 25, Desc = "가구 하트가 더 빨리 다시 떠요",
                Effect = lv => $"하트 대기 -{lv * HeartPerLevel * 100:0.#}%" },
            new CatSkillDef { Id = Furball, Name = "털뭉치 장인", Max = 25, Desc = "털뭉치를 더 자주 떨어뜨려요",
                Effect = lv => $"털뭉치 +{lv * FurballPerLevel * 100:0}%" },
            new CatSkillDef { Id = Whisker, Name = "수염 수집가", Max = 25, Desc = "수염을 더 자주 떨어뜨려요",
                Effect = lv => $"수염 +{lv * WhiskerPerLevel * 100:0}%" },
        };

        public static CatSkillDef Def(string id)
        {
            foreach (var d in Defs) if (d.Id == id) return d;
            return null;
        }

        public static int Level(Cat c, string id) => c != null && c.Skills.TryGetValue(id, out var lv) ? lv : 0;

        public static bool CanUpgrade(Cat c, string id)
        {
            var d = Def(id);
            return c != null && d != null && c.SkillPoints > 0 && Level(c, id) < d.Max;
        }

        /// <summary>스킬 포인트 1개로 한 레벨 올린다.</summary>
        public static bool Upgrade(Cat c, string id)
        {
            if (!CanUpgrade(c, id)) return false;
            c.Skills[id] = Level(c, id) + 1;
            c.SkillPoints--;
            return true;
        }

        public static void SetLevel(Cat c, string id, int lv)
        {
            var d = Def(id);
            if (c == null || d == null) return;
            c.Skills[id] = Math.Max(0, Math.Min(d.Max, lv));
        }

        public static int Spent(Cat c)
        {
            int n = 0;
            foreach (var kv in c.Skills) n += kv.Value;
            return n;
        }

        public static double CoinChance(Cat c) => Level(c, Coin) * CoinPerLevel;
        public static double HeartCut(Cat c) => Level(c, Heart) * HeartPerLevel;
        public static double FurballScale(Cat c) => 1 + Level(c, Furball) * FurballPerLevel;
        public static double WhiskerScale(Cat c) => 1 + Level(c, Whisker) * WhiskerPerLevel;

        public static IEnumerable<KeyValuePair<string, int>> All(Cat c) => c.Skills;
    }
}
