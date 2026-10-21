using System;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Cats
{
    /// <summary>
    /// 고양이 한마디 (말풍선). 확대해서 들여다볼 때의 반응.
    /// </summary>
    public static class CatThoughts
    {
        public const double MoodHigh = 80;

        private static string Any(Random r, params string[] a) => a[r.Next(a.Length)];

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
