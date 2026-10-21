using System;
using UnityEngine;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 거리장(SDF)으로 도형을 그려 텍스처를 만드는 작은 캔버스.
    /// 가장자리가 부드럽게 번지고 테두리가 자연스러워서, 임시 그래픽을 "몽글몽글"하게 만든다.
    /// 좌표는 0~1 (왼쪽 아래가 0,0). 도형 함수는 점까지의 부호 있는 거리를 돌려준다 (안쪽이 음수).
    /// </summary>
    public sealed class SdfCanvas
    {
        public delegate float Shape(float x, float y);

        public readonly int Size;
        private readonly Color[] _px;
        private readonly float _aa;

        public SdfCanvas(int size)
        {
            Size = size;
            _px = new Color[size * size];
            _aa = 1.25f / size;
        }

        /// <summary>도형을 채운다. outlineWidth 만큼 바깥에 테두리를 두른다.</summary>
        public void Fill(Shape shape, Color fill, Color outline = default, float outlineWidth = 0f)
        {
            for (int j = 0; j < Size; j++)
            {
                float y = (j + 0.5f) / Size;
                for (int i = 0; i < Size; i++)
                {
                    float x = (i + 0.5f) / Size;
                    float d = shape(x, y);
                    if (d > outlineWidth + _aa) continue;
                    float total = 1f - Smooth(-_aa, _aa, d - outlineWidth);
                    if (total <= 0f) continue;
                    float inner = outlineWidth > 0f ? 1f - Smooth(-_aa, _aa, d) : 1f;
                    var c = outlineWidth > 0f ? Color.Lerp(outline, fill, inner) : fill;
                    c.a *= total;
                    Blend(i, j, c);
                }
            }
        }

        private void Blend(int i, int j, Color src)
        {
            int k = j * Size + i;
            var dst = _px[k];
            float a = src.a + dst.a * (1f - src.a);
            if (a <= 0f) return;
            var rgb = (new Color(src.r, src.g, src.b) * src.a + new Color(dst.r, dst.g, dst.b) * dst.a * (1f - src.a)) / a;
            _px[k] = new Color(rgb.r, rgb.g, rgb.b, a);
        }

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public Texture2D ToTexture(string name)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(_px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>pivot 은 0~1. worldWidth 는 스프라이트 한 장이 월드에서 차지할 폭.</summary>
        public Sprite ToSprite(string name, float worldWidth, Vector2 pivot)
        {
            var tex = ToTexture(name);
            var s = Sprite.Create(tex, new Rect(0, 0, Size, Size), pivot, Size / Mathf.Max(0.01f, worldWidth));
            s.name = name;
            return s;
        }

        // ───────────────────────── 도형 ─────────────────────────

        public static Shape Circle(float cx, float cy, float r) =>
            (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        /// <summary>근사 타원 (충분히 둥근 모양이면 눈에 띄는 오차 없음).</summary>
        public static Shape Ellipse(float cx, float cy, float rx, float ry) =>
            (x, y) =>
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
            };

        /// <summary>둥근 사각형. hw, hh 는 반폭·반높이, r 은 모서리 반지름.</summary>
        public static Shape RoundBox(float cx, float cy, float hw, float hh, float r) =>
            (x, y) =>
            {
                float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
                float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            };

        public static Shape Capsule(float ax, float ay, float bx, float by, float r) =>
            (x, y) =>
            {
                float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
                float h = Mathf.Clamp01((pax * bax + pay * bay) / Mathf.Max(1e-6f, bax * bax + bay * bay));
                float dx = pax - bax * h, dy = pay - bay * h;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            };

        /// <summary>삼각형 (round 만큼 모서리를 둥글린다).</summary>
        public static Shape Triangle(Vector2 a, Vector2 b, Vector2 c, float round = 0f) =>
            (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, c), SegDist(p, c, a)));
                float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
                bool inside = (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
                return (inside ? -d : d) - round;
            };

        /// <summary>별. n 개 뾰족한 끝, inner 는 안쪽 반지름 비율.</summary>
        public static Shape Star(float cx, float cy, float r, int n, float inner, float round = 0f)
        {
            var pts = new Vector2[n * 2];
            for (int i = 0; i < n * 2; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / n;
                float rr = i % 2 == 0 ? r : r * inner;
                pts[i] = new Vector2(cx + Mathf.Cos(ang) * rr, cy + Mathf.Sin(ang) * rr);
            }
            return Polygon(pts, round);
        }

        public static Shape Polygon(Vector2[] pts, float round = 0f) =>
            (x, y) =>
            {
                var p = new Vector2(x, y);
                float d = float.MaxValue;
                bool inside = false;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    d = Mathf.Min(d, SegDist(p, pts[j], pts[i]));
                    if ((pts[i].y > y) != (pts[j].y > y) &&
                        x < (pts[j].x - pts[i].x) * (y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                        inside = !inside;
                }
                return (inside ? -d : d) - round;
            };

        /// <summary>하트. 위쪽 두 원 + 아래 뾰족한 삼각형.</summary>
        public static Shape Heart(float cx, float cy, float s) =>
            Union(
                Circle(cx - s * 0.5f, cy + s * 0.25f, s * 0.55f),
                Circle(cx + s * 0.5f, cy + s * 0.25f, s * 0.55f),
                Triangle(new Vector2(cx - s * 1.0f, cy + s * 0.05f), new Vector2(cx + s * 1.0f, cy + s * 0.05f), new Vector2(cx, cy - s * 1.0f), s * 0.08f));

        public static Shape Union(params Shape[] shapes) =>
            (x, y) =>
            {
                float d = float.MaxValue;
                foreach (var s in shapes) d = Mathf.Min(d, s(x, y));
                return d;
            };

        public static Shape Subtract(Shape a, Shape b) => (x, y) => Mathf.Max(a(x, y), -b(x, y));

        public static Shape Intersect(Shape a, Shape b) => (x, y) => Mathf.Max(a(x, y), b(x, y));

        public static Shape Ring(float cx, float cy, float r, float thickness) =>
            (x, y) => Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) - thickness;

        public static Shape Offset(Shape s, float dx, float dy) => (x, y) => s(x - dx, y - dy);

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a; var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return (pa - ba * h).magnitude;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        public static Color Col(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.magenta;
    }
}
