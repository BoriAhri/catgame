using System;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Stray;

namespace CatHealingHouse.Cats
{
    /// <summary>친밀도 수치. 나중에 플레이해 보며 조정한다.</summary>
    [Serializable]
    public sealed class IntimacyTuning
    {
        public int MaxLevel = 99;

        // 간식: 코인으로 사서 준다. 고양이마다 하루 첫 몇 번만 경험치를 다 받고 그다음부터는 점점 준다 (연타 방지).
        public long SnackCost = 50;
        public double SnackExp = 10;
        public int SnackFullPerDay = 3;
        public double SnackMinExp = 1;
        public double SnackHunger = 15, SnackMood = 5;

        // 쓰다듬기를 만족으로 끝내면 (조금만)
        public double PetExp = 3;
    }

    public enum SnackResult { Ok, NoMoney, NotResident }

    /// <summary>
    /// 집 고양이 친밀도 레벨 (Lv.1 ~ 99). 간식 주기·놀아주기·쓰다듬기로 경험치가 쌓이고,
    /// 레벨이 오를 때마다 스킬 포인트 1개 (패시브 스킬은 다음 단계).
    /// 길냥이를 들이면 성격(개냥이·보통·싸가지)과 호감도에 따라 시작 레벨을 얹어 준다.
    /// </summary>
    public sealed class Intimacy
    {
        public readonly Wallet Wallet;
        public readonly IntimacyTuning Tuning;

        /// <summary>레벨이 올랐을 때 (고양이, 새 레벨).</summary>
        public event Action<Cat, int> LevelUp;

        public Intimacy(Wallet wallet, IntimacyTuning tuning = null)
        {
            Wallet = wallet;
            Tuning = tuning ?? new IntimacyTuning();
        }

        /// <summary>level 에서 다음 레벨까지 필요한 경험치. 처음엔 빨리, 뒤로 갈수록 느리게.</summary>
        public static int Need(int level) => (int)Math.Round(10 + 3.0 * level + 0.15 * level * level);

        /// <summary>Lv.1 에서 level 까지 모아야 하는 경험치 합.</summary>
        public static long TotalTo(int level)
        {
            long sum = 0;
            for (int l = 1; l < level; l++) sum += Need(l);
            return sum;
        }

        public bool IsMax(Cat c) => c.IntimacyLevel >= Tuning.MaxLevel;

        /// <summary>지금 레벨 안에서 채운 비율 (0~1). 최대 레벨이면 1.</summary>
        public double Progress(Cat c) => IsMax(c) ? 1 : Math.Min(1, c.IntimacyExp / Need(c.IntimacyLevel));

        /// <summary>경험치를 더한다. 오른 레벨 수를 돌려준다 (레벨당 스킬 포인트 1).</summary>
        public int Gain(Cat c, double exp)
        {
            if (c == null || exp <= 0 || c.Affiliation != Affiliation.Resident) return 0;
            if (c.IntimacyLevel < 1) c.IntimacyLevel = 1;
            if (IsMax(c)) { c.IntimacyExp = 0; return 0; }
            c.IntimacyExp += exp;
            int ups = 0;
            while (!IsMax(c) && c.IntimacyExp >= Need(c.IntimacyLevel))
            {
                c.IntimacyExp -= Need(c.IntimacyLevel);
                c.IntimacyLevel++;
                c.SkillPoints++;
                ups++;
                LevelUp?.Invoke(c, c.IntimacyLevel);
            }
            if (IsMax(c)) c.IntimacyExp = 0;
            return ups;
        }

        // ───────────────────────── 간식 ─────────────────────────

        private void RollDay(Cat c, int day)
        {
            if (c.SnackDay != day) { c.SnackDay = day; c.SnacksToday = 0; }
        }

        /// <summary>오늘 이 고양이에게 다음 간식을 주면 받을 경험치.</summary>
        public double NextSnackExp(Cat c, int day)
        {
            int n = c.SnackDay == day ? c.SnacksToday : 0;
            if (n < Tuning.SnackFullPerDay) return Tuning.SnackExp;
            return Math.Max(Tuning.SnackMinExp, Tuning.SnackExp * Math.Pow(0.5, n - Tuning.SnackFullPerDay + 1));
        }

        /// <summary>간식 주기 (day 는 오늘 날짜 번호, 바뀌면 하루 횟수가 새로 시작). 경험치는 exp 로.</summary>
        public SnackResult FeedSnack(Cat c, int day, out double exp)
        {
            exp = 0;
            if (c == null || c.Affiliation != Affiliation.Resident) return SnackResult.NotResident;
            if (!Wallet.TrySpend(ItemKind.Money, Tuning.SnackCost)) return SnackResult.NoMoney;
            exp = NextSnackExp(c, day);
            RollDay(c, day);
            c.SnacksToday++;
            c.Needs.Add(NeedType.Hunger, Tuning.SnackHunger);
            c.AddMood(Tuning.SnackMood);
            Gain(c, exp);
            return SnackResult.Ok;
        }

        // ───────────────────────── 길냥이 들이기 ─────────────────────────

        /// <summary>
        /// 들일 때 시작 레벨. 개냥이: 호감 80 이상 Lv.3, 100 이면 Lv.5.
        /// 보통 성격: 호감 100 일 때만 Lv.3. 싸가지: 100 이어도 Lv.1.
        /// </summary>
        public static int StartLevel(Temperament t, double affection)
        {
            bool full = affection >= 100 - 1e-9;
            switch (t)
            {
                case Temperament.Cuddly: return full ? 5 : affection >= 80 ? 3 : 1;
                case Temperament.Sassy: return 1;
                default: return full ? 3 : 1;
            }
        }

        /// <summary>막 들인 고양이에게 시작 레벨을 준다 (그만큼 스킬 포인트도).</summary>
        public void OnAdopted(Cat c, Temperament t, double affection)
        {
            int lv = Math.Min(Tuning.MaxLevel, StartLevel(t, affection));
            c.IntimacyLevel = lv;
            c.IntimacyExp = 0;
            c.SkillPoints = lv - 1;
        }
    }
}
