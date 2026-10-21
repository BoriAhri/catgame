using System;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Cats
{
    /// <summary>
    /// 고양이 혼잣말 (말풍선). 지금 상태·욕구·기분에 맞는 한마디를 고른다. 할 말이 없으면 null.
    /// 화장실 쓰는 중에는 말하지 않는다 (사생활).
    /// </summary>
    public static class CatThoughts
    {
        public const double NeedLow = 30;
        public const double MoodLow = 35;
        public const double MoodHigh = 80;

        private static string Any(Random r, params string[] a) => a[r.Next(a.Length)];

        public static string Pick(Cat c, FurnitureInstance f, Random rng)
        {
            if (c == null) return null;
            if (c.Affiliation == Affiliation.Stray)
            {
                if (c.Held || c.Activity > 0) return null;
                switch (c.StrayState)
                {
                    case StrayState.Approaching: return Any(rng, "누가 있나...", "킁킁, 맛있는 냄새");
                    case StrayState.Eating: return Any(rng, "냠냠 맛있다!", "배부르다~");
                    case StrayState.Loafing: return Any(rng, "여기 따뜻하네", "조금만 쉬다 갈래");
                    case StrayState.Peeking:
                    case StrayState.ReadyToAdopt: return Any(rng, "저 안은 어떨까?", "들어가 보고 싶다...");
                    case StrayState.Fleeing: return "무서워!";
                    default: return null;
                }
            }

            if (c.State == CatState.Using && f != null && !c.LoungeVisit)
            {
                switch (c.TargetNeed)
                {
                    case NeedType.Bladder: return null;
                    case NeedType.Hunger: return Any(rng, "맛있다!", "냠냠냠");
                    case NeedType.Energy: return Any(rng, "Zzz...", "쿨... 쿨...");
                    case NeedType.Fun:
                        switch (f.Def.Interaction)
                        {
                            case InteractionType.Run: return Any(rng, "달려라~!", "더 빨리!");
                            case InteractionType.Climb: return Any(rng, "높은 곳 최고!", "다 내려다보여");
                            default: return Any(rng, "박박 긁기!", "시원하다~");
                        }
                }
            }
            if (c.Sunbathing && c.State != CatState.ToSpot) return Any(rng, "따뜻해~", "햇빛 좋다");

            // 급한 욕구 (가장 낮은 것)
            var low = NeedType.None;
            double lowV = NeedLow;
            foreach (var t in CatNeeds.All)
            {
                double v = c.Needs.Get(t);
                if (v < lowV) { lowV = v; low = t; }
            }
            switch (low)
            {
                case NeedType.Hunger: return Any(rng, "배고파...", "밥 줘냥");
                case NeedType.Bladder: return Any(rng, "화장실 가고 싶어", "급해...");
                case NeedType.Energy: return Any(rng, "졸려...", "하암~");
                case NeedType.Fun: return Any(rng, "심심해~", "놀아줘!");
            }

            if (c.Mood < MoodLow) return Any(rng, "기분이 별로야", "우울해...");
            if (c.Costume != null && rng.Next(4) == 0) return "이 옷 어때?";
            if (c.State == CatState.Lounging) return Any(rng, "멍...", "아무 생각 없음", "...");
            if (c.State == CatState.Grooming) return "단장 중~";
            if (c.Mood > MoodHigh) return Any(rng, "여기 좋아~", "행복해냥", "집사 최고!");
            return Any(rng, "냥~", "오늘도 평화롭다", "뭐 재밌는 거 없나");
        }

        /// <summary>확대해서 가까이 들여다볼 때의 반응.</summary>
        public static string Noticed(Cat c, Random rng)
        {
            if (c.State == CatState.Using && c.TargetNeed == NeedType.Bladder) return "보지 마!";
            if (c.State == CatState.Using && c.TargetNeed == NeedType.Energy) return "음냐...";
            if (c.Mood > MoodHigh) return Any(rng, "집사다!", "쓰다듬어 줘~");
            return Any(rng, "뭐냥?", "왜 쳐다봐?", "부끄러워...");
        }
    }
}
