using System;
using System.Collections.Generic;

namespace CatHealingHouse.Hex
{
    /// <summary>
    /// 육각형 axial 좌표 (q, r). Pointy-top 기준. 숨은 세 번째 축 s = -q - r.
    /// </summary>
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int Q;
        public readonly int R;
        public int S => -Q - R;

        public HexCoord(int q, int r) { Q = q; R = r; }

        public static readonly HexCoord Zero = new HexCoord(0, 0);

        /// <summary>
        /// 6방향. 인덱스 순서가 곧 회전 순서이므로 (i+1)%6 은 60도 회전.
        /// 0:동, 1:북동, 2:북서, 3:서, 4:남서, 5:남동 (화면 기준, r 증가 = 아래쪽)
        /// </summary>
        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1),
        };

        public static HexCoord Direction(int dir) => Directions[((dir % 6) + 6) % 6];

        public HexCoord Neighbor(int dir) => this + Direction(dir);

        public IEnumerable<HexCoord> Neighbors()
        {
            for (int i = 0; i < 6; i++) yield return this + Directions[i];
        }

        /// <summary>중심(0,0)으로부터의 거리 = 링 번호.</summary>
        public int Length => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

        public static int Distance(HexCoord a, HexCoord b) => (a - b).Length;

        /// <summary>
        /// 화면 좌우 반전. 픽셀 x = sqrt3*(q + r/2) 의 부호만 뒤집으면 (q,r) → (-q-r, r).
        /// 두 번 적용하면 원래대로 돌아온다.
        /// </summary>
        public HexCoord MirrorX() => new HexCoord(-Q - R, R);

        /// <summary>반지름 radius 링 위의 칸들. radius 0 이면 중심 한 칸.</summary>
        public static IEnumerable<HexCoord> Ring(HexCoord center, int radius)
        {
            if (radius == 0) { yield return center; yield break; }
            var cur = center + Direction(4) * radius;
            for (int side = 0; side < 6; side++)
            {
                for (int step = 0; step < radius; step++)
                {
                    yield return cur;
                    cur = cur.Neighbor(side);
                }
            }
        }

        /// <summary>반지름 radius 이하의 모든 칸 (3R(R+1)+1 개).</summary>
        public static IEnumerable<HexCoord> Range(HexCoord center, int radius)
        {
            for (int q = -radius; q <= radius; q++)
            {
                int r1 = Math.Max(-radius, -q - radius);
                int r2 = Math.Min(radius, -q + radius);
                for (int r = r1; r <= r2; r++) yield return center + new HexCoord(q, r);
            }
        }

        public static int CountInRange(int radius) => 3 * radius * (radius + 1) + 1;

        /// <summary>실수 좌표를 가장 가까운 육각형 칸으로 반올림 (cube rounding).</summary>
        public static HexCoord Round(double q, double r)
        {
            double s = -q - r;
            double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord((int)rq, (int)rr);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.Q - b.Q, a.R - b.R);
        public static HexCoord operator *(HexCoord a, int k) => new HexCoord(a.Q * k, a.R * k);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord h && Equals(h);
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public override string ToString() => $"({Q},{R})";
    }

    /// <summary>
    /// 칸 좌표 ↔ 화면 좌표 변환. 엔진을 모르는 순수 계산이라 double 로 돌려준다.
    /// yScale 0.75 로 세로를 눌러 쿼터뷰 느낌을 낸다. 화면 y 는 위쪽이 + 이므로 r 이 커지면 아래로 내려간다.
    /// </summary>
    public readonly struct HexLayout
    {
        public static readonly double Sqrt3 = Math.Sqrt(3.0);

        public readonly double Size;   // 육각형 중심~꼭짓점 거리
        public readonly double YScale; // 쿼터뷰 압축

        public HexLayout(double size, double yScale = 0.75) { Size = size; YScale = yScale; }

        /// <summary>한 칸의 가로 폭 (pointy-top).</summary>
        public double CellWidth => Sqrt3 * Size;

        public void ToWorld(HexCoord h, out double x, out double y)
        {
            x = Size * Sqrt3 * (h.Q + h.R / 2.0);
            y = -Size * 1.5 * h.R * YScale;
        }

        public HexCoord FromWorld(double x, double y)
        {
            double py = -y / YScale;
            double q = (Sqrt3 / 3.0 * x - 1.0 / 3.0 * py) / Size;
            double r = (2.0 / 3.0 * py) / Size;
            return HexCoord.Round(q, r);
        }
    }
}
