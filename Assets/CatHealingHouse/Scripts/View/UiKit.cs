using System.Collections.Generic;
using CatHealingHouse.View.Art;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 게임 UI 스킨 (임시 IMGUI 를 게임처럼 보이게).
    ///  - 둥근 모서리 9-슬라이스 텍스처: 버튼(크림 + 갈색 테두리 + 아래 두께), 눌림, 켜짐(주황), 꺼짐(베이지)
    ///  - 패널(크림), 카드(밝은 크림), 알약(재화·시계), 동그란 버튼
    ///  - 귀여운 한글 글꼴 Jua (OFL, Resources/Fonts)
    /// 스킨은 기본 GUI.skin 을 복제해서 바꾸므로 에디터 스킨은 건드리지 않는다.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = Col("#5E4B3C");
        public static readonly Color InkSoft = Col("#8E7462");
        public static readonly Color Cream = Col("#FFF7EA");
        public static readonly Color Brown = Col("#8E6A4B");
        public static readonly Color Orange = Col("#F3A65A");

        public static Font Font { get; private set; }

        public static Font LoadFont()
        {
            if (Font == null) Font = Resources.Load<Font>("Fonts/Jua-Regular");
            return Font;
        }

        /// <summary>
        /// 둥근 사각형 텍스처. 64×64, 모서리 반지름 r, 테두리 bw, 아래쪽 두께(lip) 만큼 진한 색 띠.
        /// 9-슬라이스 테두리는 Border 값 (모서리 + 여유).
        /// </summary>
        public static Texture2D RoundTex(Color fill, Color border, float bw, Color lip, float lipH, float radius = 20, int size = 64, Color? shine = null)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = RoundBoxSdf(fx - half, fy - half, half - 1f, half - 1f, radius);
                float a = Mathf.Clamp01(0.5f - d);                 // 바깥 경계 안티앨리어싱
                if (a <= 0) { px[y * size + x] = new Color(0, 0, 0, 0); continue; }
                Color c;
                if (d > -bw) c = border;                             // 테두리
                else
                {
                    // 아래쪽 두께: 바닥 테두리 안쪽 lipH 픽셀은 진한 색 (텍스처 y=0 이 아래)
                    float fromBottom = fy - (bw + 1f);
                    c = fromBottom < lipH ? lip : fill;
                    if (shine.HasValue && fy > size - bw - 1 - radius * 0.55f && Mathf.Abs(fx - half) < half - radius * 0.6f)
                        c = Color.Lerp(c, shine.Value, 0.5f);
                    float inner = Mathf.Clamp01(-d - bw + 0.5f);   // 테두리와 안쪽 사이 부드럽게
                    c = Color.Lerp(border, c, inner);
                }
                c.a *= a;
                px[y * size + x] = c;
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static float RoundBoxSdf(float px, float py, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px) - hx + r, qy = Mathf.Abs(py) - hy + r;
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        public static GUIStyle Style(Texture2D bg, int border = 24, Color? text = null, int fontSize = 17, Texture2D active = null)
        {
            var s = new GUIStyle
            {
                border = new RectOffset(border, border, border, border),
                padding = new RectOffset(10, 10, 6, 8),
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                richText = true,
                wordWrap = true,
                font = Font,
            };
            s.normal.background = bg;
            s.hover.background = bg;
            s.focused.background = bg;
            s.active.background = active ?? bg;
            var tc = text ?? Ink;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = tc;
            return s;
        }

        // ── 미리 만든 텍스처
        private static Texture2D _btn, _btnDown, _btnOn, _btnOff, _panel, _card, _pill, _dark, _popup;

        public static Texture2D ButtonTex => _btn ? _btn : _btn = RoundTex(Col("#FFF7EA"), Col("#8E6A4B"), 3, Col("#E7CBA6"), 7, 20, 64, Color.white);
        public static Texture2D ButtonDownTex => _btnDown ? _btnDown : _btnDown = RoundTex(Col("#F4E6CF"), Col("#8E6A4B"), 3, Col("#F4E6CF"), 2, 20);
        public static Texture2D ButtonOnTex => _btnOn ? _btnOn : _btnOn = RoundTex(Col("#F6B268"), Col("#8E5A35"), 3, Col("#D9884A"), 7, 20, 64, Col("#FFD7A3"));
        public static Texture2D ButtonOffTex => _btnOff ? _btnOff : _btnOff = RoundTex(Col("#EDE3D6"), Col("#B8A28C"), 3, Col("#DCCDBB"), 6, 20);
        public static Texture2D PanelTex => _panel ? _panel : _panel = RoundTex(Col("#FBF0DE"), Col("#8E6A4B"), 3, Col("#EBD7BA"), 5, 22);
        public static Texture2D PopupTex => _popup ? _popup : _popup = RoundTex(Col("#F3DFC2"), Col("#7A5A3F"), 4, Col("#E2C8A4"), 6, 26);
        public static Texture2D CardTex => _card ? _card : _card = RoundTex(Col("#FFFBF3"), Col("#D9BE9B"), 2, Col("#F1E2CC"), 5, 18);
        public static Texture2D PillTex => _pill ? _pill : _pill = RoundTex(new Color(1f, 0.97f, 0.92f, 0.96f), Col("#8E6A4B"), 2.5f, Col("#EFDCC2"), 4, 17);
        public static Texture2D DarkPillTex => _dark ? _dark : _dark = RoundTex(new Color(0.37f, 0.29f, 0.24f, 0.88f), new Color(0.37f, 0.29f, 0.24f, 0.95f), 1, new Color(0.3f, 0.23f, 0.19f, 0.88f), 3, 30);

        /// <summary>복제한 스킨에 버튼·상자·글꼴을 입힌다.</summary>
        public static GUISkin MakeSkin(GUISkin from)
        {
            LoadFont();
            var skin = Object.Instantiate(from);
            skin.hideFlags = HideFlags.DontSave;
            skin.font = Font;
            skin.button = Style(ButtonTex, 24, Ink, 17, ButtonDownTex);
            skin.box = Style(PanelTex, 24, Ink, 16);
            skin.label = new GUIStyle(from.label) { font = Font, richText = true, wordWrap = true };
            skin.label.normal.textColor = Ink;
            skin.verticalScrollbar = new GUIStyle(from.verticalScrollbar) { fixedWidth = 8 };
            skin.verticalScrollbar.normal.background = RoundTex(new Color(0.55f, 0.42f, 0.32f, 0.18f), new Color(0, 0, 0, 0), 0, new Color(0.55f, 0.42f, 0.32f, 0.18f), 0, 4, 16);
            skin.verticalScrollbarThumb = new GUIStyle(from.verticalScrollbarThumb) { fixedWidth = 8 };
            skin.verticalScrollbarThumb.normal.background = RoundTex(new Color(0.55f, 0.42f, 0.32f, 0.6f), new Color(0, 0, 0, 0), 0, new Color(0.55f, 0.42f, 0.32f, 0.6f), 0, 4, 16);
            skin.verticalScrollbar.border = skin.verticalScrollbarThumb.border = new RectOffset(5, 5, 5, 5);
            return skin;
        }

        // ───────────────────────── 아이콘 ─────────────────────────

        private static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private static readonly Color Line = CuteArt.Line;

        /// <summary>UI 아이콘 (house, tree, broom, bag, cat, yarn, sparkle). 가운데 피벗.</summary>
        public static Sprite Icon(string id)
        {
            if (_icons.TryGetValue(id, out var s) && s != null) return s;
            var c = new SdfCanvas(128);
            float w = 0.035f;
            switch (id)
            {
                case "house":
                    c.Fill(RoundBox(0.5f, 0.38f, 0.28f, 0.22f, 0.04f), Col("#FBE3C4"), Line, w);
                    c.Fill(Triangle(new Vector2(0.14f, 0.56f), new Vector2(0.86f, 0.56f), new Vector2(0.5f, 0.88f), 0.04f), Col("#E98A6A"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.3f, 0.08f, 0.14f, 0.04f), Col("#B5835A"), Line, w * 0.8f);
                    c.Fill(Heart(0.5f, 0.66f, 0.06f), Col("#FFF3EA"));
                    break;
                case "tree":
                    c.Fill(RoundBox(0.5f, 0.2f, 0.06f, 0.12f, 0.02f), Col("#9B6E4C"), Line, w * 0.8f);
                    c.Fill(Union(Circle(0.5f, 0.6f, 0.24f), Circle(0.32f, 0.46f, 0.17f), Circle(0.68f, 0.46f, 0.17f)), Col("#8CBF72"), Line, w);
                    c.Fill(Circle(0.42f, 0.66f, 0.07f), Col("#A9D68F"));
                    break;
                case "broom":
                    c.Fill(Capsule(0.72f, 0.86f, 0.46f, 0.42f, 0.035f), Col("#B5835A"), Line, w * 0.8f);
                    c.Fill(Polygon(new[] { new Vector2(0.3f, 0.44f), new Vector2(0.56f, 0.5f), new Vector2(0.5f, 0.14f), new Vector2(0.18f, 0.14f) }, 0.04f), Col("#F3CF6B"), Line, w);
                    c.Fill(RoundBox(0.42f, 0.42f, 0.14f, 0.035f, 0.02f), Col("#E98A6A"), Line, w * 0.6f);
                    break;
                case "bag":
                    c.Fill(Ring(0.5f, 0.62f, 0.15f, 0.04f), Line);
                    c.Fill(RoundBox(0.5f, 0.38f, 0.3f, 0.24f, 0.08f), Col("#F7C77E"), Line, w);
                    c.Fill(Circle(0.5f, 0.4f, 0.09f), Col("#FFF3DC"), Line, w * 0.6f);
                    c.Fill(Circle(0.5f, 0.4f, 0.04f), Col("#E9A24C"));
                    break;
                case "cat":
                    c.Fill(Triangle(new Vector2(0.2f, 0.6f), new Vector2(0.42f, 0.74f), new Vector2(0.24f, 0.9f), 0.03f), Col("#F7BE7A"), Line, w);
                    c.Fill(Triangle(new Vector2(0.8f, 0.6f), new Vector2(0.58f, 0.74f), new Vector2(0.76f, 0.9f), 0.03f), Col("#F7BE7A"), Line, w);
                    c.Fill(Ellipse(0.5f, 0.48f, 0.34f, 0.28f), Col("#F7BE7A"), Line, w);
                    c.Fill(Circle(0.38f, 0.5f, 0.035f), Line);
                    c.Fill(Circle(0.62f, 0.5f, 0.035f), Line);
                    c.Fill(Circle(0.5f, 0.42f, 0.025f), Col("#E58A8A"));
                    break;
                case "yarn":
                    c.Fill(Circle(0.5f, 0.5f, 0.32f), Col("#B9A3E0"), Line, w);
                    c.Fill(Intersect(Circle(0.5f, 0.5f, 0.32f), Capsule(0.24f, 0.3f, 0.76f, 0.7f, 0.025f)), Col("#9C84CC"));
                    c.Fill(Intersect(Circle(0.5f, 0.5f, 0.32f), Capsule(0.24f, 0.58f, 0.66f, 0.24f, 0.025f)), Col("#9C84CC"));
                    c.Fill(Capsule(0.76f, 0.28f, 0.9f, 0.14f, 0.02f), Line);
                    break;
                case "note":
                case "note_off":
                    c.Fill(Circle(0.36f, 0.3f, 0.12f), Col("#8EC5E8"), Line, w);
                    c.Fill(Circle(0.7f, 0.38f, 0.12f), Col("#8EC5E8"), Line, w);
                    c.Fill(RoundBox(0.46f, 0.55f, 0.03f, 0.26f, 0.01f), Line);
                    c.Fill(RoundBox(0.8f, 0.62f, 0.03f, 0.26f, 0.01f), Line);
                    c.Fill(Polygon(new[] { new Vector2(0.43f, 0.82f), new Vector2(0.83f, 0.9f), new Vector2(0.83f, 0.78f), new Vector2(0.43f, 0.7f) }, 0.01f), Line);
                    if (id == "note_off") c.Fill(Capsule(0.18f, 0.16f, 0.84f, 0.86f, 0.045f), Col("#D2694A"), Line, w * 0.6f);
                    break;
                case "sparkle":
                    c.Fill(Star(0.5f, 0.5f, 0.34f, 4, 0.35f, 0.02f), Col("#FFD36B"), Line, w * 0.8f);
                    c.Fill(Star(0.78f, 0.78f, 0.12f, 4, 0.4f, 0.01f), Col("#FFE9A6"));
                    break;
                default:
                    c.Fill(Circle(0.5f, 0.5f, 0.3f), Col("#DDD"), Line, w);
                    break;
            }
            s = c.ToSprite("ui_" + id, 1f, new Vector2(0.5f, 0.5f));
            _icons[id] = s;
            return s;
        }
    }
}
