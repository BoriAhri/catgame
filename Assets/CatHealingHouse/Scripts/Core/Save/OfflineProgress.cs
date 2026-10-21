using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Stray;
using CatHealingHouse.World;

namespace CatHealingHouse.Save
{
    [Serializable]
    public sealed class OfflineTuning
    {
        public double MaxHours = 12;                 // 계산 상한 (초안)
        public double ChunkSeconds = 60;             // 1분씩 끊어서 결과만 계산
        public double UsesPerCatPerMinute = 0.6;     // 집 고양이 한 마리가 1분에 가구를 다 쓰는 횟수 (실시간 관찰값 근처)
        public double FurballPerCatPerMinute = 0.05;
        public double WhiskerPerCatPerMinute = 0.006;
        public double StrayVisitEveryMinutes = 3;    // 밥이 있으면 이 정도마다 한 마리
        public double NeedFloor = 55;                // 벌칙 없음: 돌아오면 욕구가 이 아래로는 안 떨어져 있다
        public double MinSecondsForSummary = 60;
    }

    public sealed class StrayVisit { public string Name; public double AffectionBefore, AffectionAfter; public bool FirstTime; }

    /// <summary>"자리를 비운 사이" 팝업에 보여줄 내용.</summary>
    public sealed class AwaySummary
    {
        public double Seconds;                       // 실제로 계산한 시간 (상한 적용 후)
        public double RealSeconds;                   // 실제로 비운 시간
        public int TrayItems;
        public long TrayValue;
        public int Furballs, Whiskers, Gifts;
        public int LitterUses;
        public int Meals;
        public readonly List<StrayVisit> Visits = new List<StrayVisit>();
        public bool Capped => RealSeconds > Seconds + 1;
        public bool IsEmpty => TrayItems == 0 && Furballs == 0 && Whiskers == 0 && Gifts == 0 && LitterUses == 0 && Visits.Count == 0;
    }

    /// <summary>
    /// 앱을 껐다 켰을 때 지난 시간만큼 굵게 계산해서 결과만 반영한다 (FSM 을 몇 시간치 돌리지 않는다).
    /// 계산 항목: 가구 사용 → 트레이 결과물(칸 상한까지), 화장실 오염, 바닥 드롭(보드당 5개까지),
    /// 마당 밥그릇 사료 → 길냥이 방문·호감도·첫 발견·선물. 젤리 하트는 쌓이지 않는다.
    /// 기기 시간을 되돌렸으면 0초로 친다.
    /// </summary>
    public static class OfflineProgress
    {
        public static double ElapsedSeconds(long savedUtcTicks, DateTime utcNow)
        {
            double s = (utcNow.Ticks - savedUtcTicks) / (double)TimeSpan.TicksPerSecond;
            return s > 0 ? s : 0; // 시간을 되돌렸으면 0
        }

        public static AwaySummary Run(GameState g, double realSeconds, OfflineTuning tuning = null, int seed = 0)
        {
            var t = tuning ?? new OfflineTuning();
            var rng = new Random(seed != 0 ? seed : Environment.TickCount);
            var sum = new AwaySummary { RealSeconds = Math.Max(0, realSeconds) };
            double secs = Math.Min(sum.RealSeconds, t.MaxHours * 3600);
            sum.Seconds = secs;
            if (secs <= 0) return sum;

            var w = g.World;
            var eco = g.Economy;
            var residents = new List<Cat>();
            foreach (var c in g.Sim.Cats) if (c.Affiliation == Affiliation.Resident) residents.Add(c);
            var indoorFloor = new List<Cell>();
            foreach (var tile in w.Indoor.Tiles.Values)
            {
                var c = new Cell(BoardId.Indoor, tile.Coord);
                if (tile.Kind == TileKind.Floor && w.IsTransit(c)) indoorFloor.Add(c);
            }

            int chunks = (int)Math.Ceiling(secs / t.ChunkSeconds);
            double strayAccum = 0;
            for (int i = 0; i < chunks; i++)
            {
                double mins = Math.Min(t.ChunkSeconds, secs - i * t.ChunkSeconds) / 60.0;
                foreach (var cat in residents)
                {
                    if (rng.NextDouble() < t.UsesPerCatPerMinute * mins)
                    {
                        var f = PickUse(g, cat, rng);
                        if (f != null)
                        {
                            if (RewardTable.IsLitterBox(f.Def)) sum.LitterUses++;
                            int added = eco.GrantUseQuiet(f);
                            if (added > 0)
                            {
                                sum.TrayItems += added;
                                sum.TrayValue += added * RewardTable.UnitValue(RewardTable.TrayItemFor(f.Def));
                            }
                        }
                    }
                    if (indoorFloor.Count > 0)
                    {
                        double roll = rng.NextDouble();
                        if (roll < t.WhiskerPerCatPerMinute * mins) { if (eco.DropQuiet(indoorFloor[rng.Next(indoorFloor.Count)], ItemKind.Whisker)) sum.Whiskers++; }
                        else if (roll < (t.WhiskerPerCatPerMinute + t.FurballPerCatPerMinute) * mins) { if (eco.DropQuiet(indoorFloor[rng.Next(indoorFloor.Count)], ItemKind.Furball)) sum.Furballs++; }
                    }
                }

                // 마당: 밥이 남아 있는 동안 가끔 길냥이가 와서 먹고 간다
                if (g.Strays != null)
                {
                    strayAccum += mins;
                    while (strayAccum >= t.StrayVisitEveryMinutes)
                    {
                        strayAccum -= t.StrayVisitEveryMinutes;
                        StrayVisitOnce(g, rng, sum);
                    }
                }
            }

            // 벌칙 없음: 오랜만에 켜도 비참하지 않게
            foreach (var cat in residents)
                foreach (var n in CatNeeds.All)
                    if (cat.Needs.Get(n) < t.NeedFloor) cat.Needs.Set(n, t.NeedFloor + rng.NextDouble() * 20);

            g.Clock?.Advance(secs);
            eco.NotifyChanged();
            return sum;
        }

        private static FurnitureInstance PickUse(GameState g, Cat cat, Random rng)
        {
            // 어떤 욕구를 채우러 갔을지: 줄어드는 속도(성격 포함)에 비례
            var needs = CatNeeds.All;
            double total = 0;
            var weights = new double[needs.Length];
            var tuning = g.Sim.Tuning;
            for (int i = 0; i < needs.Length; i++) { weights[i] = tuning.Decay(needs[i]) * cat.Personality.Decay(needs[i]); total += weights[i]; }
            double r = rng.NextDouble() * total;
            NeedType need = needs[needs.Length - 1];
            for (int i = 0; i < needs.Length; i++) { if (r < weights[i]) { need = needs[i]; break; } r -= weights[i]; }

            FurnitureInstance pick = null;
            double sumLove = 0;
            foreach (var f in g.World.Furniture)
            {
                if (f.Board != BoardId.Indoor || f.Def.Capacity <= 0 || f.Def.Satisfies != need) continue;
                double love = Math.Max(0.1, cat.Personality.Love(f.Def));
                sumLove += love;
                if (rng.NextDouble() * sumLove < love) pick = f; // 가중 무작위 (저수지 표집)
            }
            return pick;
        }

        private static void StrayVisitOnce(GameState g, Random rng, AwaySummary sum)
        {
            FurnitureInstance bowl = null;
            foreach (var f in g.World.Furniture)
                if (g.Strays.IsOutdoorBowl(f) && g.Strays.FoodIn(f.Id) > 0) { bowl = f; break; }
            if (bowl == null) return;

            StrayProfile p = null;
            double sw = 0;
            foreach (var cand in g.Strays.Profiles)
            {
                if (cand.Adopted) continue;
                sw += cand.SpawnWeight;
                if (rng.NextDouble() * sw < cand.SpawnWeight) p = cand;
            }
            if (p == null || !g.Strays.EatQuiet(bowl.Id)) return;

            var tun = g.Strays.Tuning;
            var v = new StrayVisit { Name = p.Name, AffectionBefore = p.Affection, FirstTime = !p.Discovered };
            p.Discovered = true;
            p.Visits++;
            p.Meals++;
            p.AddAffection(tun.MealAffection + tun.CalmBonus); // 아무도 놀래키지 않았으니 차분한 방문
            v.AffectionAfter = p.Affection;
            sum.Meals++;
            var existing = sum.Visits.Find(x => x.Name == p.Name);
            if (existing != null) { existing.AffectionAfter = p.Affection; }
            else sum.Visits.Add(v);

            if (p.Tier >= AffectionTier.Friendly && rng.NextDouble() < tun.GiftChance)
            {
                var near = bowl.Cells.Count > 0 ? new Cell(BoardId.Outdoor, bowl.Cells[0].Offset) : new Cell(BoardId.Outdoor, bowl.Origin);
                if (g.Economy.AddGift(near)) sum.Gifts++;
            }
        }
    }
}
