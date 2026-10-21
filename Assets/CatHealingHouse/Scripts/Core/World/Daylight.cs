using System;
using System.Collections.Generic;
using CatHealingHouse.Hex;

namespace CatHealingHouse.World
{
    public enum LightKind { None, Sun, Moon }

    /// <summary>
    /// 게임 안 시계. 현실 24분 = 게임 하루 (게임 1시간 = 현실 1분).
    /// 시뮬레이션 시간으로 돌아서 배속을 올리면 시계도 빨라지고, 나중에 오프라인 진행에도 그대로 쓴다.
    /// </summary>
    public sealed class DayClock
    {
        public double SecondsPerDay = 24 * 60;
        public double StartHour;
        public double Elapsed { get; private set; }

        public DayClock(double startHour = 7) { StartHour = startHour; }

        public void Advance(double seconds) { Elapsed += seconds; }

        /// <summary>불러오기용.</summary>
        public void SetElapsed(double seconds) { Elapsed = Math.Max(0, seconds); }

        /// <summary>0~24 시.</summary>
        public double Hour
        {
            get
            {
                double h = StartHour + Elapsed / SecondsPerDay * 24.0;
                h %= 24.0;
                return h < 0 ? h + 24 : h;
            }
        }

        public int Day => (int)Math.Floor((StartHour + Elapsed / SecondsPerDay * 24.0) / 24.0) + 1;

        public void SetHour(double hour)
        {
            Elapsed += ((hour - Hour) % 24 + 24) % 24 / 24.0 * SecondsPerDay;
        }

        public string Label
        {
            get
            {
                double h = Hour;
                int hh = (int)h, mm = (int)((h - hh) * 60);
                string ampm = hh < 12 ? "오전" : "오후";
                int h12 = hh % 12 == 0 ? 12 : hh % 12;
                return $"{ampm} {h12}:{mm:00}";
            }
        }
    }

    /// <summary>
    /// 해와 달. 해는 6시에 떠서 18시에 지고, 달은 19시에 떠서 5시에 진다.
    /// Progress 는 뜰 때 0 → 질 때 1 (빛이 들어오는 방향을 정한다).
    /// </summary>
    public static class Daylight
    {
        public const double SunRise = 6, SunSet = 18, MoonRise = 19, MoonSet = 5;

        public static bool SunUp(double h) => h >= SunRise && h < SunSet;
        public static bool MoonUp(double h) => h >= MoonRise || h < MoonSet;

        public static LightKind Kind(double h) => SunUp(h) ? LightKind.Sun : MoonUp(h) ? LightKind.Moon : LightKind.None;

        public static double Progress(double h)
        {
            if (SunUp(h)) return (h - SunRise) / (SunSet - SunRise);
            if (MoonUp(h))
            {
                double t = h >= MoonRise ? h - MoonRise : h + 24 - MoonRise;
                return t / (MoonSet + 24 - MoonRise);
            }
            return 0;
        }

        /// <summary>빛 세기 0~1. 뜨고 질 때 약하고 한가운데에서 가장 세다. 달빛은 해의 40%.</summary>
        public static double Strength(double h)
        {
            var k = Kind(h);
            if (k == LightKind.None) return 0;
            double s = Math.Sin(Math.PI * Progress(h));
            s = Math.Min(1, s * 1.4); // 아침·저녁에도 꽤 들어오게
            return k == LightKind.Sun ? s : s * 0.4;
        }
    }

    /// <summary>
    /// 실내 창문으로 들어오는 빛 조각. 창문은 위쪽 벽 가운데 두 칸.
    /// 아침에는 해가 오른쪽(동쪽)에 있어서 빛이 왼쪽 아래로 길게, 낮에는 창 바로 아래 짧게,
    /// 저녁에는 오른쪽 아래로 길게 늘어진다. 달빛도 같은 식으로 움직인다.
    /// </summary>
    public static class WindowLight
    {
        /// <summary>창문이 달린 위쪽 벽 칸 (보드 크기에 따라 다시 계산).</summary>
        public static List<HexCoord> WindowCells(HexBoard board)
        {
            int n = board.Radius + 1;
            var list = new List<HexCoord>();
            // 맨 윗줄 r = -n, 화면 x ∝ q - n/2. 가운데에 가까운 두 칸.
            var cands = new List<HexCoord>();
            for (int q = 0; q <= n; q++) cands.Add(new HexCoord(q, -n));
            cands.Sort((a, b) => Math.Abs(a.Q - n / 2.0).CompareTo(Math.Abs(b.Q - n / 2.0)));
            for (int i = 0; i < Math.Min(2, cands.Count); i++)
            {
                var t = board.Get(cands[i]);
                if (t != null && t.Kind == TileKind.Wall) list.Add(cands[i]);
            }
            return list;
        }

        /// <summary>그 시각에 빛이 닿는 바닥 칸들. 밤·새벽 사이(달도 해도 없을 때)는 비어 있다.</summary>
        public static HashSet<HexCoord> LitCells(HexBoard board, double hour)
        {
            var lit = new HashSet<HexCoord>();
            var kind = Daylight.Kind(hour);
            if (kind == LightKind.None || Daylight.Strength(hour) < 0.08) return lit;
            double p = Daylight.Progress(hour);
            double side = 2 * p - 1;                       // -1 (빛이 왼쪽으로) … +1 (오른쪽으로)
            double rows = 1.6 + 3.2 * Math.Abs(side);       // 낮엔 짧게, 아침·저녁엔 길게
            if (kind == LightKind.Moon) rows *= 0.85;
            var layout = new HexLayout(1, 1);
            foreach (var w in WindowCells(board))
            {
                layout.ToWorld(w, out double wx, out double wy);
                // 창 아래 첫 줄부터 아래로, 옆으로 비스듬히
                for (double i = 0.9; i <= rows + 0.6; i += 0.5)
                {
                    double x = wx + side * 1.15 * i;
                    double y = wy - 1.5 * i;
                    var c = layout.FromWorld(x, y);
                    var t = board.Get(c);
                    if (t == null) break;
                    if (t.Kind == TileKind.Wall || t.Kind == TileKind.Door) { if (i > 1.5) break; continue; }
                    lit.Add(c);
                }
            }
            return lit;
        }
    }
}
