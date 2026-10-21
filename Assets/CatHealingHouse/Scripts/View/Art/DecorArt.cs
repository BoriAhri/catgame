using System.Collections.Generic;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 배경 장식 그림: 덤불, 소나무, 부드러운 얼룩(가구 발판·그림자·빛), 비네트.
    /// 고양이스프처럼 외곽선 없이 면과 명암으로만 그린다. 모두 코드로 그린 임시 그림.
    /// </summary>
    public static class DecorArt
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>GLSL smoothstep (Mathf.SmoothStep 은 보간 함수라 다르다).</summary>
        private static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            _cache[key] = s;
            return s;
        }

        /// <summary>덤불. 아래 가운데 피벗, 월드 폭 1. variant 0~2 모양이 조금씩 다르다.</summary>
        public static Sprite Bush(int variant) => Cached("bush" + variant, () =>
        {
            var c = new SdfCanvas(128);
            var rnd = new System.Random(11 + variant * 7);
            var dark = Col("#4F7046");
            var mid = Col("#668A55");
            var light = Col("#7FA366");
            var blobs = new List<Vector3>();
            int n = 5 + variant;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float x = 0.16f + t * 0.68f + (float)(rnd.NextDouble() - 0.5) * 0.06f;
                float arch = Mathf.Sin(t * Mathf.PI);
                float r = 0.13f + arch * 0.09f + (float)rnd.NextDouble() * 0.03f;
                float y = 0.1f + r * 0.85f + arch * 0.1f;
                blobs.Add(new Vector3(x, y, r));
            }
            // 그림자 쪽(아래·왼쪽)
            foreach (var b in blobs) c.Fill(Circle(b.x, b.y, b.z), dark);
            // 몸통
            foreach (var b in blobs) c.Fill(Circle(b.x + 0.015f, b.y + 0.03f, b.z * 0.88f), mid);
            // 빛 받는 위쪽
            foreach (var b in blobs) c.Fill(Circle(b.x + 0.03f, b.y + 0.07f, b.z * 0.55f), light);
            // 잎 결
            for (int i = 0; i < 6; i++)
            {
                var b = blobs[rnd.Next(blobs.Count)];
                c.Fill(Ellipse(b.x + (float)(rnd.NextDouble() - 0.5) * b.z, b.y + (float)(rnd.NextDouble() - 0.3) * b.z, 0.018f, 0.01f), dark);
            }
            // 바닥 그림자
            c.Fill(Ellipse(0.5f, 0.09f, 0.38f, 0.035f), new Color(0.16f, 0.22f, 0.14f, 0.35f));
            return c.ToSprite("bush" + variant, 1f, new Vector2(0.5f, 0.06f));
        });

        /// <summary>소나무. 아래 가운데 피벗, 월드 폭 1 (높이 약 1.6).</summary>
        public static Sprite Pine(int variant) => Cached("pine" + variant, () =>
        {
            var c = new SdfCanvas(160);
            var dark = variant == 1 ? Col("#3B5A40") : Col("#425F3F");
            var mid = variant == 1 ? Col("#4D7150") : Col("#56784C");
            var light = variant == 1 ? Col("#628A63") : Col("#6D915C");
            c.Fill(RoundBox(0.5f, 0.07f, 0.035f, 0.07f, 0.01f), Col("#6B4E3A"));
            c.Fill(Ellipse(0.5f, 0.03f, 0.2f, 0.025f), new Color(0.12f, 0.16f, 0.1f, 0.35f));
            float[] ys = { 0.12f, 0.33f, 0.52f, 0.7f };
            float[] ws = { 0.3f, 0.25f, 0.19f, 0.12f };
            float[] hs = { 0.3f, 0.27f, 0.24f, 0.24f };
            for (int i = 0; i < ys.Length; i++)
            {
                var a = new Vector2(0.5f - ws[i], ys[i]);
                var b = new Vector2(0.5f + ws[i], ys[i]);
                var top = new Vector2(0.5f, ys[i] + hs[i]);
                c.Fill(Triangle(a, b, top, 0.035f), dark);
                // 오른쪽 위가 밝게 (빛)
                c.Fill(Triangle(new Vector2(0.5f, ys[i] + 0.02f), new Vector2(0.5f + ws[i] * 0.85f, ys[i] + 0.02f), top, 0.03f), mid);
                c.Fill(Triangle(new Vector2(0.52f, ys[i] + hs[i] * 0.45f), new Vector2(0.5f + ws[i] * 0.45f, ys[i] + hs[i] * 0.4f), top, 0.02f), light);
            }
            return c.ToSprite("pine" + variant, 1f, new Vector2(0.5f, 0.02f));
        });

        /// <summary>가장자리가 부드럽게 사라지는 흰 원 (가운데 피벗, 월드 폭 1). 색을 입혀 발판·그림자·빛으로 쓴다.</summary>
        public static Sprite SoftBlob() => Cached("soft_blob", () =>
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "soft_blob" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 1f - SS(0.45f, 1f, d);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
        });

        /// <summary>흙 발판: 가장자리가 살짝 울퉁불퉁하고 안쪽이 밝은 타원 (가운데 피벗, 월드 폭 1).</summary>
        public static Sprite DirtPatch() => Cached("dirt_patch", () =>
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "dirt_patch" };
            var px = new Color32[n * n];
            var outer = Col("#A88E68");
            var inner = Col("#BCA37A");
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float ang = Mathf.Atan2(dy, dx);
                float wob = 0.9f + 0.05f * Mathf.Sin(ang * 5f + 1.3f) + 0.03f * Mathf.Sin(ang * 9f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) / wob;
                float a = 1f - SS(0.86f, 1f, d);
                var col = Color.Lerp(inner, outer, SS(0.3f, 0.95f, d));
                col.a = a;
                px[y * n + x] = col;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
        });

        /// <summary>화면 가장자리가 은은하게 어두운 비네트 (가운데 피벗, 월드 폭 1).</summary>
        public static Sprite Vignette() => Cached("vignette", () =>
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float d = Mathf.Sqrt(dx * dx * 0.9f + dy * dy);
                float a = SS(0.55f, 1.25f, d);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
        });

        /// <summary>타원 러그 (가운데 피벗, 월드 폭 1, 높이 0.5).</summary>
        public static Sprite Rug() => Cached("rug", () =>
        {
            var c = new SdfCanvas(192);
            c.Fill(Ellipse(0.5f, 0.5f, 0.48f, 0.24f), Col("#D98F86"));
            c.Fill(Ellipse(0.5f, 0.5f, 0.44f, 0.21f), Col("#F1C2B4"));
            c.Fill(Ellipse(0.5f, 0.5f, 0.36f, 0.165f), Col("#EDB3A6"));
            c.Fill(Ellipse(0.5f, 0.5f, 0.33f, 0.145f), Col("#F6D5C8"));
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2;
                c.Fill(Circle(0.5f + Mathf.Cos(a) * 0.4f, 0.5f + Mathf.Sin(a) * 0.19f, 0.012f), Col("#FFF3EA"));
            }
            c.Fill(Heart(0.5f, 0.5f, 0.06f), Col("#E9A196"));
            return c.ToSprite("rug", 1f, new Vector2(0.5f, 0.5f));
        });

        /// <summary>문 앞 발판 (가운데 피벗, 월드 폭 1). outdoor 면 징검돌, 아니면 줄무늬 매트.</summary>
        public static Sprite DoorMat(bool outdoor) => Cached("doormat" + outdoor, () =>
        {
            var c = new SdfCanvas(128);
            if (outdoor)
            {
                c.Fill(Ellipse(0.5f, 0.5f, 0.46f, 0.28f), Col("#9A8C7C"));
                c.Fill(Ellipse(0.49f, 0.53f, 0.42f, 0.24f), Col("#BDB1A2"));
                c.Fill(Ellipse(0.42f, 0.58f, 0.2f, 0.09f), Col("#CBC1B4"));
            }
            else
            {
                c.Fill(RoundBox(0.5f, 0.5f, 0.46f, 0.26f, 0.08f), Col("#A86F55"));
                c.Fill(RoundBox(0.5f, 0.5f, 0.41f, 0.21f, 0.06f), Col("#D99A78"));
                for (int i = 0; i < 5; i++) c.Fill(RoundBox(0.18f + i * 0.16f, 0.5f, 0.025f, 0.17f, 0.02f), Col("#C9886A"));
            }
            return c.ToSprite("doormat", 1f, new Vector2(0.5f, 0.5f));
        });

        private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        /// <summary>가장자리 네 변이 부드럽게 사라지는 흰 사각 텍스처 (바닥 빛 모양 메시에 입힌다).</summary>
        public static Texture2D SoftRectTex() => Tex("soft_rect", 64, 64, (u, v) => SS(0f, 0.28f, Mathf.Min(u, 1 - u)) * SS(0f, 0.22f, Mathf.Min(v, 1 - v)));

        /// <summary>좌우만 부드럽게 사라지는 흰 텍스처 (공기 중 빛줄기).</summary>
        public static Texture2D BeamTex() => Tex("beam", 64, 8, (u, v) => SS(0f, 0.35f, Mathf.Min(u, 1 - u)));

        private static Texture2D Tex(string key, int w, int h, System.Func<float, float, float> alpha)
        {
            if (_tex.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = key };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1, 1, 1, alpha((x + 0.5f) / w, (y + 0.5f) / h));
            t.SetPixels32(px);
            t.Apply(false, true);
            _tex[key] = t;
            return t;
        }

        /// <summary>작은 풀꽃 무더기 (아래 가운데 피벗, 월드 폭 1).</summary>
        public static Sprite Flowers(int variant) => Cached("flowers" + variant, () =>
        {
            var c = new SdfCanvas(64);
            var leaf = Col("#6E8F58");
            c.Fill(Ellipse(0.35f, 0.2f, 0.12f, 0.06f), leaf);
            c.Fill(Ellipse(0.62f, 0.18f, 0.13f, 0.06f), leaf);
            var petal = variant == 0 ? Col("#FFF6E4") : variant == 1 ? Col("#F7D57A") : Col("#F2B2B6");
            Vector2[] at = { new Vector2(0.32f, 0.36f), new Vector2(0.55f, 0.44f), new Vector2(0.7f, 0.3f) };
            foreach (var p in at)
            {
                c.Fill(Capsule(p.x, 0.15f, p.x, p.y, 0.012f), leaf);
                for (int k = 0; k < 5; k++)
                {
                    float a = k / 5f * Mathf.PI * 2;
                    c.Fill(Circle(p.x + Mathf.Cos(a) * 0.045f, p.y + Mathf.Sin(a) * 0.045f, 0.035f), petal);
                }
                c.Fill(Circle(p.x, p.y, 0.025f), Col("#E9B04C"));
            }
            return c.ToSprite("flowers" + variant, 1f, new Vector2(0.5f, 0.12f));
        });
    }
}
