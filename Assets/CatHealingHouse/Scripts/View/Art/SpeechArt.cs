using UnityEngine;

namespace CatHealingHouse.View.Art
{
    /// <summary>말풍선 그림: 늘어나는 둥근 상자(9-slice)와 아래 꼬리.</summary>
    public static class SpeechArt
    {
        public const float Ppu = 100f;
        private static Sprite _box, _tail;
        private static readonly Color Fill = new Color(1f, 0.976f, 0.93f, 1f);
        private static readonly Color Line = new Color(0.54f, 0.42f, 0.33f, 1f);

        private static float SS(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>9-slice 상자. 테두리 24px.</summary>
        public static Sprite Box()
        {
            if (_box != null) return _box;
            const int n = 64; const float rad = 22f, line = 3.2f;
            var tex = NewTex(n, n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, rad, n - rad), cy = Mathf.Clamp(y + 0.5f, rad, n - rad);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - (rad - 1.5f); // <0 안쪽
                float a = 1f - SS(-0.8f, 0.8f, d);
                float edge = SS(-line - 0.8f, -line + 0.8f, d);
                var c = Color.Lerp(Fill, Line, edge);
                c.a = a;
                px[y * n + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            _box = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), Ppu, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            return _box;
        }

        /// <summary>꼬리 (위가 상자에 붙는 쪽, 피벗은 위 가운데).</summary>
        public static Sprite Tail()
        {
            if (_tail != null) return _tail;
            const int w = 32, h = 24; const float line = 3.2f;
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fy = (y + 0.5f) / h; // 0 아래 끝, 1 위
                float half = Mathf.Lerp(1.5f, 11f, fy);
                float cxl = w * 0.5f - (1f - fy) * 4f; // 살짝 왼쪽으로 휜 끝
                float dx = Mathf.Abs(x + 0.5f - cxl) - half; // <0 안쪽
                float a = 1f - SS(-0.8f, 0.8f, dx);
                float edge = SS(-line - 0.8f, -line + 0.8f, dx);
                if (y >= h - 4) edge = 0f; // 상자와 붙는 위쪽은 테두리 없음
                var c = Color.Lerp(Fill, Line, edge);
                c.a = a;
                px[y * w + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            _tail = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), Ppu, 0, SpriteMeshType.FullRect);
            return _tail;
        }
    }
}
