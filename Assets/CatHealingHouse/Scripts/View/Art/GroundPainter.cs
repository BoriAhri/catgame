using System;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using UnityEngine;

namespace CatHealingHouse.View.Art
{
    /// <summary>바닥 그림 한 장과, 실내 벽이 시작하는 높이(열마다).</summary>
    public sealed class GroundResult
    {
        public Sprite Sprite;
        public float MinX, MinY, MaxX, MaxY, PixelsPerUnit;
        public float[] WallBaseY;   // 열(x)마다 바닥 위쪽 경계의 월드 y. 바닥이 없는 열은 NaN.
        public float WallHeight;
        public Vector3 DoorPos;

        public float WallBaseAt(float worldX)
        {
            if (WallBaseY == null) return float.NaN;
            int i = Mathf.Clamp(Mathf.RoundToInt((worldX - MinX) * PixelsPerUnit), 0, WallBaseY.Length - 1);
            return WallBaseY[i];
        }
    }

    /// <summary>
    /// 고양이스프처럼 칸이 보이지 않는 바닥 한 장을 그린다.
    ///  1) 바닥 칸(+문)을 마스크로 찍고  2) 흐리게 펴서 육각형 톱니를 없애고  3) 잡음으로 가장자리를 자연스럽게 일렁이게 한다.
    /// 마당: 풀밭 공터(얼룩·풀잎·작은 꽃) + 바깥은 어두운 숲 바닥 + 문에서 아래로 흙길.
    /// 실내: 마루(널빤지 줄·결) + 위쪽 경계를 따라 서는 벽지 벽(걸레받이) + 앞쪽 바닥 두께 턱. 바깥은 어두운 실내색.
    /// 판이 바뀔 때(넓히기) 한 번만 그린다.
    /// </summary>
    public static class GroundPainter
    {
        public static readonly Color OutdoorOutside = Parse("#4C6544");
        public static readonly Color IndoorOutside = Parse("#3B302A");

        private static readonly System.Collections.Generic.Dictionary<string, Color> _cols = new System.Collections.Generic.Dictionary<string, Color>();
        private static Color Parse(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        private static Color Col(string hex)
        {
            if (_cols.TryGetValue(hex, out var c)) return c;
            ColorUtility.TryParseHtmlString(hex, out c);
            _cols[hex] = c;
            return c;
        }

        public static GroundResult Paint(HexBoard board, HexLayout layout, bool indoor, int seed)
        {
            // ── 범위
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var t in board.Tiles.Values)
            {
                layout.ToWorld(t.Coord, out double x, out double y);
                minX = Mathf.Min(minX, (float)x); maxX = Mathf.Max(maxX, (float)x);
                minY = Mathf.Min(minY, (float)y); maxY = Mathf.Max(maxY, (float)y);
            }
            float size = (float)layout.Size;
            float wallH = size * 4.5f; // 통창 + 벽 가구가 들어가게 높게
            float margin = size * (indoor ? 4f : 7f);
            minX -= margin; maxX += margin; minY -= margin; maxY += margin + (indoor ? wallH : 0);

            float ppu = 46f / size; // 칸 크기 기준 해상도 (폰에서 그리는 시간 때문에 너무 높이지 않는다)
            int w = Mathf.Min(2048, Mathf.CeilToInt((maxX - minX) * ppu));
            int h = Mathf.Min(2048, Mathf.CeilToInt((maxY - minY) * ppu));
            ppu = Mathf.Min(w / (maxX - minX), h / (maxY - minY));

            // ── 1) 마스크: 바닥(+문) 칸이면 1. 어차피 흐리게 펼 거라 1/4 해상도로 계산해 빠르게.
            const int K = 4;
            int cw = w / K + 2, ch = h / K + 2;
            var mask = new float[cw * ch];
            var cn1 = new float[cw * ch];
            var cn2 = new float[cw * ch];
            float seedOff = seed * 0.137f;
            for (int py = 0; py < ch; py++)
            for (int px = 0; px < cw; px++)
            {
                double wx = minX + (px * K + 0.5) / ppu, wy = minY + (py * K + 0.5) / ppu;
                var t = board.Get(layout.FromWorld(wx, wy));
                int i = py * cw + px;
                mask[i] = t != null && (t.Kind == TileKind.Floor || t.Kind == TileKind.Door) ? 1f : 0f;
                cn1[i] = Fbm((float)wx * 1.3f + seedOff, (float)wy * 1.3f);
                cn2[i] = Fbm((float)wx * 4.5f + 7.1f, (float)wy * 4.5f - seedOff);
            }

            // ── 2) 흐리게 (상자 흐림 두 번 ≈ 가우스)
            int r = Mathf.Max(1, Mathf.RoundToInt(size * 0.42f * ppu / K));
            var csoft = BoxBlur(mask, cw, ch, r);
            csoft = BoxBlur(csoft, cw, ch, r);
            var soft = Upsample(csoft, cw, ch, w, h, K);
            var n1s = Upsample(cn1, cw, ch, w, h, K);
            var n2s = Upsample(cn2, cw, ch, w, h, K);

            // ── 3) 칠하기
            var pixels = new Color32[w * h];
            var rnd = new System.Random(seed);
            layout.ToWorld(board.DoorCoord, out double ddx, out double ddy);
            var doorPos = new Vector3((float)ddx, (float)ddy, 0);
            var result = new GroundResult { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, PixelsPerUnit = ppu, WallHeight = wallH, DoorPos = doorPos };

            // 실내 벽: 열마다 바닥 위쪽 경계 (부드럽게)
            float[] topY = null;
            float edgeAmp = indoor ? 0.12f : 0.32f; // 실내 벽 밑은 덜 일렁이게
            if (indoor)
            {
                topY = new float[w];
                for (int px = 0; px < w; px++)
                {
                    topY[px] = float.NaN;
                    for (int py = h - 1; py >= 0; py--)
                        if (soft[py * w + px] > 0.5f + (n1s[py * w + px] - 0.5f) * edgeAmp) { topY[px] = minY + (py + 0.5f) / ppu; break; }
                }
                topY = SmoothColumns(topY, Mathf.RoundToInt(size * 0.3f * ppu));
                result.WallBaseY = topY;
            }

            Color outside = indoor ? IndoorOutside : OutdoorOutside;
            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                int i = py * w + px;
                float wx = minX + (px + 0.5f) / ppu, wy = minY + (py + 0.5f) / ppu;
                float n1 = n1s[i];
                float n2 = n2s[i];
                // 일렁이는 가장자리
                float edgeT = 0.5f + (n1 - 0.5f) * edgeAmp;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edgeT - 0.05f, edgeT + 0.05f, soft[i]));
                float rim = 1f - Mathf.InverseLerp(edgeT, Mathf.Min(1f, edgeT + 0.3f), soft[i]); // 가장자리 쪽일수록 1

                Color ground;
                if (indoor)
                {
                    ground = Wood(wx, wy, n2, size);
                    ground = Color.Lerp(ground, Col("#B4865E"), rim * 0.45f);
                }
                else
                {
                    ground = Color.Lerp(Col("#93AC76"), Col("#86A06A"), Mathf.Clamp01((n1 - 0.35f) * 2.2f));
                    ground = Color.Lerp(ground, Col("#9DB67F"), Mathf.Clamp01((n2 - 0.62f) * 4f) * 0.6f);
                    ground = Color.Lerp(ground, Col("#7C9763"), rim * 0.55f);
                }

                Color outCol = outside;
                if (!indoor)
                {
                    // 숲 바닥: 얼룩 + 공터 가까이는 살짝 밝게
                    outCol = Color.Lerp(Col("#4A6342"), Col("#55704B"), n2);
                    outCol = Color.Lerp(outCol, Col("#62804F"), Mathf.Clamp01(soft[i] * 2.2f) * 0.6f);
                }
                else
                {
                    // 바닥 앞쪽 두께(턱): 바로 위가 바닥인데 여기는 바닥 밖
                    int up = py + Mathf.RoundToInt(size * 0.2f * ppu);
                    if (up < h && soft[up * w + px] > 0.5f && soft[i] < 0.5f)
                        outCol = Color.Lerp(Col("#8E6A4B"), Col("#7A5A3F"), (float)(py % 3) / 3f);
                    // 벽: 열의 바닥 위쪽 경계에서 wallH 만큼
                    float baseY = topY != null ? topY[px] : float.NaN;
                    if (!float.IsNaN(baseY) && wy > baseY - size * 0.25f && wy < baseY + wallH)
                        outCol = WallPaper(wx, Mathf.Max(0f, wy - baseY), wallH, size);
                }
                var col = Color.Lerp(outCol, ground, a);

                // 마당: 문에서 아래로 흙길
                if (!indoor)
                {
                    float dx = Mathf.Abs(wx - doorPos.x + Mathf.Sin(wy * 1.7f) * size * 0.25f);
                    if (wy < doorPos.y + size * 0.4f && dx < size * (0.55f + 0.1f * n1))
                    {
                        float pathA = Mathf.Clamp01((size * 0.6f - dx) / (size * 0.15f));
                        pathA *= Mathf.Clamp01((wy - minY) / (size * 3f)); // 그림 끝에서 스르륵 사라지게
                        col = Color.Lerp(col, Color.Lerp(Col("#B49A73"), Col("#A58A63"), n2), pathA * 0.9f);
                    }
                }
                pixels[i] = col;
            }

            // ── 잔무늬: 풀잎·꽃 (마당) / 먼지 반짝임 없음 (실내)
            if (!indoor)
            {
                int tufts = (int)(w * h / 900f);
                for (int k = 0; k < tufts; k++)
                {
                    int px = rnd.Next(w), py = rnd.Next(h);
                    float m = soft[py * w + px];
                    if (m < 0.55f) continue;
                    bool flower = rnd.NextDouble() < 0.12;
                    if (flower) Dot(pixels, w, h, px, py, 2, rnd.NextDouble() < 0.5 ? Col("#F7F1DE") : Col("#F2D27A"));
                    else Tuft(pixels, w, h, px, py, Col("#6F8A58"));
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = indoor ? "ground_indoor" : "ground_outdoor" };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var pivot = new Vector2((0 - minX) / (maxX - minX), (0 - minY) / (maxY - minY));
            result.Sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
            result.Sprite.name = tex.name;
            return result;
        }

        // ───────────────────────── 재질 ─────────────────────────

        private static Color Wood(float x, float y, float n, float size)
        {
            float plankH = size * 0.42f;
            int row = Mathf.FloorToInt(y / plankH);
            float fy = y / plankH - row;
            // 줄마다 이음새 위치를 엇갈리게
            float plankW = size * 2.6f;
            float off = (row * 0.37f) % 1f * plankW;
            float fx = Mathf.Repeat(x + off, plankW) / plankW;
            var c = Color.Lerp(Col("#D9AB7D"), Col("#CF9F70"), Hash(row * 13.1f, Mathf.Floor((x + off) / plankW)));
            c = Color.Lerp(c, Col("#C89664"), n * 0.25f);                    // 결
            if (fy < 0.06f) c = Color.Lerp(c, Col("#B07E55"), 0.55f);        // 널빤지 사이 줄
            if (fx < 0.012f) c = Color.Lerp(c, Col("#B07E55"), 0.45f);       // 이음새
            return c;
        }

        private static Color WallPaper(float x, float dy, float wallH, float size)
        {
            if (dy < size * 0.16f) return Col("#B98A62");                     // 걸레받이
            if (dy < size * 0.2f) return Col("#9C7350");
            if (dy > wallH - size * 0.12f) return Col("#D9C2A3");             // 위 몰딩
            float stripe = Mathf.Repeat(x / (size * 0.36f), 1f);
            var c = stripe < 0.28f ? Col("#E8D7BF") : Col("#EFE2CF");
            // 아래쪽이 살짝 어둡게 (깊이감)
            return Color.Lerp(c, Col("#E0CDB2"), Mathf.Clamp01(1f - dy / wallH) * 0.25f);
        }

        // ───────────────────────── 도구 ─────────────────────────

        private static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[w * h];
            var dst = new float[w * h];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                float acc = 0;
                for (int x = -r; x <= r; x++) acc += src[y * w + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = acc * inv;
                    acc += src[y * w + Mathf.Min(x + r + 1, w - 1)] - src[y * w + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * inv;
                    acc += tmp[Mathf.Min(y + r + 1, h - 1) * w + x] - tmp[Mathf.Max(y - r, 0) * w + x];
                }
            }
            return dst;
        }

        private static float[] Upsample(float[] src, int sw, int sh, int w, int h, int k)
        {
            var dst = new float[w * h];
            float inv = 1f / k;
            for (int y = 0; y < h; y++)
            {
                float fy = (y - 0.5f * (k - 1)) * inv; if (fy < 0) fy = 0;
                int y0 = (int)fy; float ty = fy - y0; int y1 = Mathf.Min(y0 + 1, sh - 1); y0 = Mathf.Min(y0, sh - 1);
                for (int x = 0; x < w; x++)
                {
                    float fx = (x - 0.5f * (k - 1)) * inv; if (fx < 0) fx = 0;
                    int x0 = (int)fx; float tx = fx - x0; int x1 = Mathf.Min(x0 + 1, sw - 1); x0 = Mathf.Min(x0, sw - 1);
                    float a = src[y0 * sw + x0] + (src[y0 * sw + x1] - src[y0 * sw + x0]) * tx;
                    float b = src[y1 * sw + x0] + (src[y1 * sw + x1] - src[y1 * sw + x0]) * tx;
                    dst[y * w + x] = a + (b - a) * ty;
                }
            }
            return dst;
        }

        private static float[] SmoothColumns(float[] v, int r)
        {
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                if (float.IsNaN(v[i])) { o[i] = float.NaN; continue; }
                float s = 0; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= v.Length || float.IsNaN(v[j])) continue;
                    s += v[j]; n++;
                }
                o[i] = s / n;
            }
            return o;
        }

        private static void Dot(Color32[] p, int w, int h, int cx, int cy, int r, Color c)
        {
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                int px = cx + x, py = cy + y;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                p[py * w + px] = c;
            }
        }

        private static void Tuft(Color32[] p, int w, int h, int cx, int cy, Color c)
        {
            // 짧은 풀잎 세 가닥
            for (int k = -1; k <= 1; k++)
            for (int t = 0; t < 6; t++)
            {
                int px = cx + k * 2 + (k * t) / 3, py = cy + t;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                p[py * w + px] = Color.Lerp(p[py * w + px], c, 0.8f);
            }
        }

        private static float Hash(float x, float y)
        {
            unchecked
            {
                uint h = (uint)(int)x * 374761393u + (uint)(int)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static float Noise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float x, float y) => Noise(x, y) * 0.65f + Noise(x * 2.1f + 3.3f, y * 2.1f - 1.7f) * 0.35f;
    }
}
