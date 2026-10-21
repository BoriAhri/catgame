using System.Collections.Generic;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 뼈대 애니메이션용 고양이 부위. 이름은 아트 디렉션 문서의 부위 분리 규칙과 같다.
    /// 나중에 진짜 PSB 그림이 오면 같은 이름의 부위로 그림만 바꿔 끼운다.
    /// </summary>
    public enum CatPart
    {
        Shadow, Tail, LegBackFar, LegFrontFar, Body, LegBackNear, LegFrontNear, EarL, EarR, Head, EyesOpen, EyesClosed,
    }

    /// <summary>
    /// 코드로 그린 고양이 부위. 모든 부위는 같은 256×256 캔버스 좌표에 그리고, 스프라이트 피벗을 관절 위치에 둔다.
    /// 그래서 쉬는 자세에서는 관절 위치에 놓기만 해도 원래 그림과 똑같이 맞춰진다. 오른쪽을 본다.
    /// </summary>
    public static class CatParts
    {
        /// <summary>캔버스 좌표(0~1)의 관절 위치. 루트(발바닥 중앙)는 Root.</summary>
        public static readonly Vector2 Root = new Vector2(0.47f, 0.10f);

        public static readonly Dictionary<CatPart, Vector2> Joint = new Dictionary<CatPart, Vector2>
        {
            { CatPart.Shadow, new Vector2(0.45f, 0.10f) },
            { CatPart.Tail, new Vector2(0.22f, 0.28f) },
            { CatPart.Body, new Vector2(0.44f, 0.14f) },
            { CatPart.Head, new Vector2(0.58f, 0.40f) },
            { CatPart.EarL, new Vector2(0.545f, 0.655f) },
            { CatPart.EarR, new Vector2(0.745f, 0.655f) },
            { CatPart.EyesOpen, new Vector2(0.66f, 0.535f) },
            { CatPart.EyesClosed, new Vector2(0.66f, 0.535f) },
            { CatPart.LegFrontNear, new Vector2(0.56f, 0.215f) },
            { CatPart.LegFrontFar, new Vector2(0.49f, 0.215f) },
            { CatPart.LegBackNear, new Vector2(0.32f, 0.215f) },
            { CatPart.LegBackFar, new Vector2(0.26f, 0.215f) },
        };

        /// <summary>부모 관절. 귀와 눈은 머리를 따라 움직인다.</summary>
        public static CatPart? Parent(CatPart p)
        {
            switch (p)
            {
                case CatPart.EarL:
                case CatPart.EarR:
                case CatPart.EyesOpen:
                case CatPart.EyesClosed: return CatPart.Head;
                default: return null;
            }
        }

        /// <summary>그리기 순서 (작을수록 뒤).</summary>
        public static int Order(CatPart p) => (int)p;

        private static readonly Color Line = CuteArt.Line;
        private static readonly Color Pink = Col("#F4A9A8");
        private static readonly Color Nose = Col("#E58A8A");
        private static readonly Color Eye = Col("#3A2E28");
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private static Color A(Color c, float a) { c.a = a; return c; }
        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // 부위 모양 (전체 실루엣 기준 좌표)
        private static readonly Shape BodyShape = Ellipse(0.44f, 0.30f, 0.25f, 0.175f);
        private static readonly Shape HeadShape = Ellipse(0.645f, 0.515f, 0.205f, 0.18f);
        private static readonly Shape TailShape = Union(Capsule(0.22f, 0.27f, 0.13f, 0.40f, 0.045f), Capsule(0.13f, 0.40f, 0.15f, 0.55f, 0.045f));
        private static readonly Shape EarLShape = Triangle(new Vector2(0.475f, 0.61f), new Vector2(0.505f, 0.80f), new Vector2(0.61f, 0.68f), 0.022f);
        private static readonly Shape EarRShape = Triangle(new Vector2(0.675f, 0.68f), new Vector2(0.78f, 0.80f), new Vector2(0.81f, 0.61f), 0.022f);
        private static Shape Leg(float x) => Capsule(x, 0.215f, x, 0.13f, 0.048f);

        public static Sprite Get(CatLook look, CatPart part)
        {
            // 아트 라이브러리에 진짜 그림이 있으면 그걸 쓴다
            var lib = ArtLibrary.Active != null ? ArtLibrary.Active.CatPartSprite(look.SkinId, part) : null;
            if (lib != null) return lib;

            string key = $"{look.Key}_{part}";
            if (_cache.TryGetValue(key, out var s) && s != null && s.texture != null) return s;
            s = Draw(look, part);
            _cache[key] = s;
            return s;
        }

        private static Sprite Draw(CatLook look, CatPart part)
        {
            var c = new SdfCanvas(256);
            bool dark = look.Base.grayscale < 0.35f;
            float w = 0.018f;
            switch (part)
            {
                case CatPart.Shadow:
                    c.Fill(Ellipse(0.45f, 0.1f, 0.3f, 0.045f), A(Col("#7A6A78"), 0.22f));
                    break;

                case CatPart.Tail:
                    c.Fill(TailShape, look.Base, Line, w);
                    if (look.Kind == CatPattern.Tabby) c.Fill(Intersect(Capsule(0.10f, 0.46f, 0.19f, 0.47f, 0.016f), TailShape), look.Pattern);
                    if (look.Kind == CatPattern.Calico) c.Fill(Intersect(Ellipse(0.15f, 0.5f, 0.06f, 0.08f), TailShape), Col("#4A4148"));
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.15f, 0.5f, 0.1f, 0.2f), TailShape), look.Pattern);
                    break;

                case CatPart.Body:
                    c.Fill(BodyShape, look.Base, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        c.Fill(Intersect(Union(Capsule(0.37f, 0.45f, 0.39f, 0.36f, 0.018f), Capsule(0.45f, 0.47f, 0.47f, 0.37f, 0.018f), Capsule(0.29f, 0.42f, 0.31f, 0.34f, 0.016f)), BodyShape), look.Pattern);
                    if (look.Kind == CatPattern.Tuxedo) c.Fill(Intersect(Ellipse(0.6f, 0.3f, 0.11f, 0.15f), BodyShape), look.Pattern);
                    if (look.Kind == CatPattern.Calico) c.Fill(Intersect(Ellipse(0.34f, 0.37f, 0.13f, 0.1f), BodyShape), look.Pattern);
                    c.Fill(Intersect(Ellipse(0.52f, 0.25f, 0.14f, 0.08f), BodyShape), A(Color.white, 0.22f));
                    break;

                case CatPart.Head:
                    c.Fill(HeadShape, look.Base, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        c.Fill(Intersect(Union(Capsule(0.62f, 0.68f, 0.625f, 0.62f, 0.012f), Capsule(0.67f, 0.685f, 0.67f, 0.625f, 0.012f)), HeadShape), look.Pattern);
                    if (look.Kind == CatPattern.Tuxedo) c.Fill(Intersect(Ellipse(0.67f, 0.45f, 0.1f, 0.065f), HeadShape), look.Pattern);
                    if (look.Kind == CatPattern.Calico) c.Fill(Intersect(Ellipse(0.72f, 0.64f, 0.11f, 0.09f), HeadShape), Col("#4A4148"));
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.66f, 0.49f, 0.11f, 0.085f), HeadShape), A(look.Pattern, 0.85f));
                    c.Fill(Ellipse(0.565f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
                    c.Fill(Ellipse(0.765f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
                    c.Fill(Triangle(new Vector2(0.648f, 0.497f), new Vector2(0.672f, 0.497f), new Vector2(0.66f, 0.482f), 0.004f), Nose);
                    var mouth = dark ? Col("#F2E6D6") : Line;
                    c.Fill(Union(Capsule(0.645f, 0.472f, 0.652f, 0.462f, 0.004f), Capsule(0.652f, 0.462f, 0.66f, 0.472f, 0.004f),
                                 Capsule(0.66f, 0.472f, 0.668f, 0.462f, 0.004f), Capsule(0.668f, 0.462f, 0.675f, 0.472f, 0.004f)), mouth);
                    break;

                case CatPart.EarL:
                case CatPart.EarR:
                {
                    bool left = part == CatPart.EarL;
                    c.Fill(left ? EarLShape : EarRShape, look.Kind == CatPattern.Point ? look.Pattern : look.Base, Line, w);
                    var inner = left
                        ? Triangle(new Vector2(0.505f, 0.655f), new Vector2(0.52f, 0.755f), new Vector2(0.585f, 0.685f), 0.008f)
                        : Triangle(new Vector2(0.70f, 0.685f), new Vector2(0.765f, 0.755f), new Vector2(0.78f, 0.655f), 0.008f);
                    c.Fill(inner, A(Pink, 0.9f));
                    break;
                }

                case CatPart.EyesOpen:
                {
                    var eyeCol = dark ? Col("#F2D06B") : Eye;
                    c.Fill(Ellipse(0.605f, 0.535f, 0.022f, 0.032f), eyeCol);
                    c.Fill(Ellipse(0.715f, 0.535f, 0.022f, 0.032f), eyeCol);
                    if (dark)
                    {
                        c.Fill(Ellipse(0.607f, 0.532f, 0.008f, 0.024f), Eye);
                        c.Fill(Ellipse(0.717f, 0.532f, 0.008f, 0.024f), Eye);
                    }
                    c.Fill(Circle(0.612f, 0.548f, 0.0085f), Color.white);
                    c.Fill(Circle(0.722f, 0.548f, 0.0085f), Color.white);
                    break;
                }

                case CatPart.EyesClosed:
                    c.Fill(Union(Capsule(0.585f, 0.535f, 0.605f, 0.522f, 0.007f), Capsule(0.605f, 0.522f, 0.625f, 0.535f, 0.007f),
                                 Capsule(0.695f, 0.535f, 0.715f, 0.522f, 0.007f), Capsule(0.715f, 0.522f, 0.735f, 0.535f, 0.007f)),
                           dark ? Col("#F2E6D6") : Eye);
                    break;

                default:
                {
                    // 다리: 앞쪽(near)은 몸 색, 뒤쪽(far)은 살짝 어둡게
                    float x = Joint[part].x;
                    bool far = part == CatPart.LegFrontFar || part == CatPart.LegBackFar;
                    var col = far ? Shade(look.Base, 0.86f) : look.Base;
                    c.Fill(Leg(x), col, Line, w);
                    if (look.Kind == CatPattern.Tuxedo && (part == CatPart.LegFrontNear || part == CatPart.LegFrontFar))
                        c.Fill(Intersect(Capsule(x, 0.15f, x, 0.13f, 0.046f), Leg(x)), look.Pattern);
                    if (look.Kind == CatPattern.Point)
                        c.Fill(Intersect(Capsule(x, 0.16f, x, 0.13f, 0.046f), Leg(x)), far ? Shade(look.Pattern, 0.86f) : look.Pattern);
                    break;
                }
            }
            var j = Joint[part];
            return c.ToSprite($"cat_{part}", 1f, j);
        }
    }
}
