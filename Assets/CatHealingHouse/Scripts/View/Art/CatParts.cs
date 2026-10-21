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
        // 쓰다듬기 표정 (머리에 매달림, 평소엔 꺼져 있음)
        Blush, EyesHalf, EyesHappy,
        // 꼬리 끝 마디 (꼬리에 매달려 한 박자 늦게 휜다)
        TailTip,
    }

    /// <summary>
    /// 코드로 그린 고양이 부위. 모든 부위는 같은 캔버스 좌표에 그리고, 스프라이트 피벗을 관절 위치에 둔다.
    /// 그래서 쉬는 자세에서는 관절 위치에 놓기만 해도 원래 그림과 똑같이 맞춰진다. 오른쪽을 본다.
    /// 품종 무늬(치즈·고등어 줄무늬, 턱시도, 삼색, 흰·검은 단색, 샴 포인트), 음영, 털 뭉치, 주둥이, 수염, 눈 하이라이트.
    /// </summary>
    public static class CatParts
    {
        /// <summary>캔버스 좌표(0~1)의 관절 위치. 루트(발바닥 중앙)는 Root.</summary>
        public static readonly Vector2 Root = new Vector2(0.47f, 0.10f);

        /// <summary>부위 그림 해상도 (크게 확대해도 또렷하게).</summary>
        public const int Res = 384;

        public static readonly Dictionary<CatPart, Vector2> Joint = new Dictionary<CatPart, Vector2>
        {
            { CatPart.Shadow, new Vector2(0.45f, 0.10f) },
            { CatPart.Tail, new Vector2(0.22f, 0.28f) },
            { CatPart.TailTip, new Vector2(0.13f, 0.40f) },
            { CatPart.Body, new Vector2(0.44f, 0.14f) },
            { CatPart.Head, new Vector2(0.58f, 0.40f) },
            { CatPart.EarL, new Vector2(0.545f, 0.655f) },
            { CatPart.EarR, new Vector2(0.745f, 0.655f) },
            { CatPart.EyesOpen, new Vector2(0.66f, 0.535f) },
            { CatPart.EyesClosed, new Vector2(0.66f, 0.535f) },
            { CatPart.EyesHalf, new Vector2(0.66f, 0.535f) },
            { CatPart.EyesHappy, new Vector2(0.66f, 0.535f) },
            { CatPart.Blush, new Vector2(0.66f, 0.48f) },
            { CatPart.LegFrontNear, new Vector2(0.56f, 0.215f) },
            { CatPart.LegFrontFar, new Vector2(0.49f, 0.215f) },
            { CatPart.LegBackNear, new Vector2(0.32f, 0.215f) },
            { CatPart.LegBackFar, new Vector2(0.26f, 0.215f) },
        };

        /// <summary>부모 관절. 귀와 눈은 머리를, 꼬리 끝은 꼬리를 따라 움직인다.</summary>
        public static CatPart? Parent(CatPart p)
        {
            switch (p)
            {
                case CatPart.EarL:
                case CatPart.EarR:
                case CatPart.EyesOpen:
                case CatPart.EyesClosed:
                case CatPart.EyesHalf:
                case CatPart.EyesHappy:
                case CatPart.Blush: return CatPart.Head;
                case CatPart.TailTip: return CatPart.Tail;
                default: return null;
            }
        }

        /// <summary>그리기 순서 (작을수록 뒤). 꼬리 끝은 꼬리 뿌리 뒤.</summary>
        public static int Order(CatPart p) => p == CatPart.TailTip ? 0 : p == CatPart.Shadow ? -1 : (int)p;

        private static readonly Color Line = CuteArt.Line;
        private static readonly Color Pink = Col("#F4A9A8");
        private static readonly Color Nose = Col("#E58A8A");
        private static readonly Color Eye = Col("#3A2E28");
        private static readonly Color DarkPatch = Col("#4A4148");
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private static Color A(Color c, float a) { c.a = a; return c; }
        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        private static Color Tint(Color c, float k) => Color.Lerp(c, Color.white, k);

        // 부위 모양 (전체 실루엣 기준 좌표)
        private static readonly Shape BodyShape = Union(
            Ellipse(0.44f, 0.30f, 0.25f, 0.175f),
            // 가슴 털 뭉치 (앞쪽 아래)
            Triangle(new Vector2(0.60f, 0.24f), new Vector2(0.655f, 0.20f), new Vector2(0.66f, 0.27f), 0.008f));
        private static readonly Shape HeadShape = Union(
            Ellipse(0.645f, 0.515f, 0.205f, 0.18f),
            // 볼 털 (뒤쪽 아래로 삐죽)
            Triangle(new Vector2(0.47f, 0.47f), new Vector2(0.432f, 0.41f), new Vector2(0.51f, 0.415f), 0.008f));
        private static readonly Shape TailShape = Capsule(0.22f, 0.27f, 0.13f, 0.40f, 0.046f);
        private static readonly Shape TailTipShape = Union(Capsule(0.13f, 0.40f, 0.14f, 0.48f, 0.044f), Capsule(0.14f, 0.48f, 0.158f, 0.55f, 0.040f));
        private static readonly Shape EarLShape = Triangle(new Vector2(0.475f, 0.61f), new Vector2(0.505f, 0.80f), new Vector2(0.61f, 0.68f), 0.022f);
        private static readonly Shape EarRShape = Triangle(new Vector2(0.675f, 0.68f), new Vector2(0.78f, 0.80f), new Vector2(0.81f, 0.61f), 0.022f);
        private static Shape Leg(float x) => Union(Capsule(x, 0.215f, x, 0.14f, 0.047f), Ellipse(x + 0.008f, 0.128f, 0.054f, 0.03f));

        private static readonly Vector2[] Eyes = { new Vector2(0.605f, 0.535f), new Vector2(0.715f, 0.535f) };

        private static bool IsDark(CatLook l) => l.Base.grayscale < 0.35f;

        private static Color IrisOf(CatLook l)
        {
            if (l.Kind == CatPattern.Point) return Col("#8EC3EE");
            if (IsDark(l)) return Col("#E8C24F");
            if (l.Kind == CatPattern.Calico) return Col("#D9A441");
            if (l.Base.grayscale > 0.9f) return Col("#8FC7A2");
            return Col("#B4BE57");
        }

        /// <summary>
        /// 쓰다듬는 부위 (옆모습, 오른쪽을 본 기준). head · body · tail · root 는 각 뼈 기준 캔버스 좌표
        /// (자세가 바뀌어도 그 부위 그림 위의 자리로 판정하려고). 없으면 None.
        /// </summary>
        public static Cats.PetZone ZoneAt(Vector2 head, Vector2 body, Vector2 tail, Vector2 root)
        {
            const float pad = 0.02f;
            if (EarLShape(head.x, head.y) < pad || EarRShape(head.x, head.y) < pad) return Cats.PetZone.Head;
            if (HeadShape(head.x, head.y) < pad)
            {
                if (head.y > 0.555f) return Cats.PetZone.Head;
                if (head.x > 0.70f && head.y < 0.50f) return Cats.PetZone.Chin;
                return Cats.PetZone.Cheek;
            }
            if (BodyShape(body.x, body.y) < pad)
            {
                if (body.y > 0.33f) return Cats.PetZone.Back;
                if (body.x > 0.58f && body.y > 0.2f) return Cats.PetZone.Chest;
                if (body.y < 0.225f) return Cats.PetZone.Belly;
                return Cats.PetZone.Back;
            }
            if (TailShape(tail.x, tail.y) < pad || TailTipShape(tail.x, tail.y) < pad * 2f) return Cats.PetZone.Tail;
            if (root.y < 0.24f && root.y > 0.08f && root.x > 0.2f && root.x < 0.62f) return Cats.PetZone.Paw;
            return Cats.PetZone.None;
        }

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
            var c = new SdfCanvas(Res);
            bool dark = IsDark(look);
            float w = 0.016f;
            var fur = look.Base;
            var light = Tint(fur, dark ? 0.14f : 0.5f);
            var soft = dark ? Col("#F2E6D6") : Line;
            switch (part)
            {
                case CatPart.Shadow:
                    c.Bounds = new Rect(0.1f, 0f, 0.75f, 0.2f);
                    c.Fill(Ellipse(0.45f, 0.1f, 0.3f, 0.045f), A(Col("#7A6A78"), 0.22f));
                    break;

                case CatPart.Tail:
                    c.Bounds = new Rect(0.05f, 0.2f, 0.25f, 0.27f);
                    c.Fill(TailShape, fur, Line, w);
                    if (look.Kind == CatPattern.Tabby) c.Fill(Intersect(Capsule(0.13f, 0.335f, 0.21f, 0.355f, 0.013f), TailShape), look.Pattern);
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.13f, 0.41f, 0.08f, 0.06f), TailShape), A(look.Pattern, 0.6f));
                    c.Fill(Intersect(Capsule(0.15f, 0.36f, 0.2f, 0.29f, 0.01f), TailShape), A(Color.white, 0.15f));
                    break;

                case CatPart.TailTip:
                    c.Bounds = new Rect(0.05f, 0.33f, 0.18f, 0.28f);
                    c.Fill(TailTipShape, fur, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        c.Fill(Intersect(Union(Capsule(0.09f, 0.45f, 0.19f, 0.46f, 0.013f), Capsule(0.10f, 0.515f, 0.20f, 0.52f, 0.012f)), TailTipShape), look.Pattern);
                    if (look.Kind == CatPattern.Calico) c.Fill(Intersect(Ellipse(0.15f, 0.52f, 0.06f, 0.06f), TailTipShape), DarkPatch);
                    if (look.Kind == CatPattern.Point) c.Fill(TailTipShape, look.Pattern);
                    c.Fill(Intersect(Capsule(0.12f, 0.43f, 0.135f, 0.5f, 0.009f), TailTipShape), A(Color.white, 0.15f));
                    break;

                case CatPart.Body:
                {
                    c.Bounds = new Rect(0.15f, 0.08f, 0.56f, 0.42f);
                    c.Fill(BodyShape, fur, Line, w);
                    // 배 쪽 밝게, 아래 음영, 등 하이라이트
                    if (!dark) c.Fill(Intersect(BodyShape, Ellipse(0.5f, 0.17f, 0.17f, 0.06f)), A(light, 0.75f));
                    c.Fill(Intersect(BodyShape, Ellipse(0.42f, 0.11f, 0.3f, 0.06f)), A(Shade(fur, 0.8f), 0.45f));
                    // 무늬
                    if (look.Kind == CatPattern.Tabby)
                    {
                        var st = Union(
                            Capsule(0.30f, 0.475f, 0.315f, 0.37f, 0.016f), Capsule(0.37f, 0.48f, 0.38f, 0.37f, 0.016f),
                            Capsule(0.44f, 0.478f, 0.445f, 0.375f, 0.016f), Capsule(0.51f, 0.47f, 0.51f, 0.38f, 0.015f),
                            Capsule(0.23f, 0.38f, 0.26f, 0.31f, 0.014f), Capsule(0.57f, 0.445f, 0.565f, 0.39f, 0.013f));
                        c.Fill(Intersect(st, BodyShape), look.Pattern);
                    }
                    if (look.Kind == CatPattern.Tuxedo)
                        c.Fill(Intersect(Union(Ellipse(0.6f, 0.27f, 0.12f, 0.13f), Ellipse(0.47f, 0.165f, 0.16f, 0.06f)), BodyShape), Color.white);
                    if (look.Kind == CatPattern.Calico)
                    {
                        c.Fill(Intersect(Ellipse(0.32f, 0.37f, 0.12f, 0.09f), BodyShape), look.Pattern);
                        c.Fill(Intersect(Ellipse(0.53f, 0.41f, 0.08f, 0.055f), BodyShape), DarkPatch);
                        c.Fill(Intersect(Ellipse(0.22f, 0.26f, 0.05f, 0.06f), BodyShape), look.Pattern);
                    }
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.22f, 0.32f, 0.1f, 0.15f), BodyShape), A(look.Pattern, 0.45f));
                    // 가슴 털 (밝게) + 등 하이라이트
                    c.Fill(Intersect(BodyShape, Union(Ellipse(0.63f, 0.26f, 0.06f, 0.07f), Triangle(new Vector2(0.60f, 0.24f), new Vector2(0.655f, 0.20f), new Vector2(0.66f, 0.27f), 0.008f))),
                        look.Kind == CatPattern.Tuxedo ? Color.white : A(light, dark ? 0.5f : 0.85f));
                    c.Fill(Intersect(BodyShape, Ellipse(0.42f, 0.445f, 0.17f, 0.03f)), A(Color.white, 0.18f));
                    break;
                }

                case CatPart.Head:
                {
                    c.Bounds = new Rect(0.41f, 0.3f, 0.48f, 0.44f);
                    c.Fill(HeadShape, fur, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        c.Fill(Intersect(Union(
                            Capsule(0.62f, 0.69f, 0.625f, 0.625f, 0.012f), Capsule(0.665f, 0.692f, 0.665f, 0.63f, 0.012f), Capsule(0.708f, 0.685f, 0.703f, 0.63f, 0.011f),
                            Capsule(0.465f, 0.525f, 0.515f, 0.52f, 0.011f), Capsule(0.46f, 0.49f, 0.505f, 0.49f, 0.011f)), HeadShape), look.Pattern);
                    if (look.Kind == CatPattern.Tuxedo)
                        c.Fill(Intersect(Union(Ellipse(0.665f, 0.45f, 0.11f, 0.07f), Triangle(new Vector2(0.645f, 0.5f), new Vector2(0.675f, 0.5f), new Vector2(0.66f, 0.63f), 0.012f)), HeadShape), Color.white);
                    if (look.Kind == CatPattern.Calico)
                    {
                        c.Fill(Intersect(Ellipse(0.56f, 0.6f, 0.1f, 0.09f), HeadShape), look.Pattern);
                        c.Fill(Intersect(Ellipse(0.73f, 0.64f, 0.09f, 0.07f), HeadShape), DarkPatch);
                    }
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.665f, 0.49f, 0.115f, 0.09f), HeadShape), A(look.Pattern, 0.85f));
                    // 하이라이트·턱 아래 음영
                    c.Fill(Intersect(HeadShape, Ellipse(0.63f, 0.64f, 0.12f, 0.04f)), A(Color.white, 0.16f));
                    c.Fill(Intersect(HeadShape, Ellipse(0.62f, 0.345f, 0.2f, 0.05f)), A(Shade(fur, 0.8f), 0.3f));
                    // 주둥이 (밝은 볼록)
                    var muzzleCol = look.Kind == CatPattern.Tuxedo ? Color.white : look.Kind == CatPattern.Point ? Tint(look.Pattern, 0.25f) : Tint(fur, dark ? 0.15f : 0.55f);
                    c.Fill(Union(Circle(0.643f, 0.468f, 0.031f), Circle(0.677f, 0.468f, 0.031f)), A(muzzleCol, 0.95f));
                    c.Fill(Ellipse(0.565f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
                    c.Fill(Ellipse(0.765f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
                    c.Fill(Triangle(new Vector2(0.648f, 0.497f), new Vector2(0.672f, 0.497f), new Vector2(0.66f, 0.482f), 0.004f), Nose);
                    c.Fill(Circle(0.656f, 0.494f, 0.003f), A(Color.white, 0.7f));
                    c.Fill(Union(Capsule(0.645f, 0.472f, 0.652f, 0.462f, 0.004f), Capsule(0.652f, 0.462f, 0.66f, 0.472f, 0.004f),
                                 Capsule(0.66f, 0.472f, 0.668f, 0.462f, 0.004f), Capsule(0.668f, 0.462f, 0.675f, 0.472f, 0.004f)),
                        look.Kind == CatPattern.Tuxedo ? Line : soft);
                    // 수염
                    var wc = dark ? A(Col("#F2E6D6"), 0.7f) : A(Line, 0.45f);
                    c.Fill(Union(
                        Capsule(0.615f, 0.472f, 0.525f, 0.49f, 0.0022f), Capsule(0.615f, 0.463f, 0.52f, 0.458f, 0.0022f),
                        Capsule(0.705f, 0.472f, 0.80f, 0.49f, 0.0022f), Capsule(0.705f, 0.463f, 0.805f, 0.456f, 0.0022f)), wc);
                    break;
                }

                case CatPart.EarL:
                case CatPart.EarR:
                {
                    bool left = part == CatPart.EarL;
                    c.Bounds = left ? new Rect(0.43f, 0.57f, 0.22f, 0.29f) : new Rect(0.63f, 0.57f, 0.23f, 0.29f);
                    var earCol = look.Kind == CatPattern.Point ? look.Pattern
                        : look.Kind == CatPattern.Calico ? (left ? look.Pattern : DarkPatch) : fur;
                    c.Fill(left ? EarLShape : EarRShape, earCol, Line, w);
                    var inner = left
                        ? Triangle(new Vector2(0.505f, 0.655f), new Vector2(0.52f, 0.755f), new Vector2(0.585f, 0.685f), 0.008f)
                        : Triangle(new Vector2(0.70f, 0.685f), new Vector2(0.765f, 0.755f), new Vector2(0.78f, 0.655f), 0.008f);
                    c.Fill(inner, A(Pink, 0.9f));
                    var tuft = left
                        ? Union(Capsule(0.525f, 0.66f, 0.535f, 0.70f, 0.0035f), Capsule(0.54f, 0.665f, 0.553f, 0.695f, 0.0035f))
                        : Union(Capsule(0.755f, 0.66f, 0.745f, 0.70f, 0.0035f), Capsule(0.74f, 0.665f, 0.727f, 0.695f, 0.0035f));
                    c.Fill(tuft, A(Color.white, 0.85f));
                    break;
                }

                case CatPart.EyesOpen:
                {
                    c.Bounds = new Rect(0.55f, 0.48f, 0.22f, 0.11f);
                    var iris = IrisOf(look);
                    foreach (var e in Eyes)
                    {
                        var eye = Ellipse(e.x, e.y, 0.024f, 0.033f);
                        c.Fill(eye, iris, Line, 0.0045f);
                        c.Fill(Intersect(Ellipse(e.x, e.y - 0.015f, 0.02f, 0.017f), eye), A(Tint(iris, 0.4f), 0.65f));
                        c.Fill(Intersect(Ellipse(e.x + 0.002f, e.y - 0.002f, 0.016f, 0.026f), eye), Eye);
                        c.Fill(Circle(e.x + 0.008f, e.y + 0.012f, 0.0085f), Color.white);
                        c.Fill(Circle(e.x - 0.007f, e.y - 0.013f, 0.0042f), A(Color.white, 0.8f));
                    }
                    break;
                }

                case CatPart.EyesHalf:
                {
                    // 지그시 감은 눈 (‿): 기분 좋을 때
                    c.Bounds = new Rect(0.55f, 0.48f, 0.22f, 0.11f);
                    foreach (var e in Eyes)
                        c.Fill(Union(Capsule(e.x - 0.022f, 0.541f, e.x, 0.527f, 0.0072f), Capsule(e.x, 0.527f, e.x + 0.022f, 0.541f, 0.0072f)), dark ? Col("#F2E6D6") : Eye);
                    break;
                }

                case CatPart.EyesHappy:
                {
                    c.Bounds = new Rect(0.55f, 0.48f, 0.22f, 0.11f);
                    foreach (var e in Eyes)
                        c.Fill(Union(Capsule(e.x - 0.021f, 0.526f, e.x, 0.546f, 0.0068f), Capsule(e.x, 0.546f, e.x + 0.021f, 0.526f, 0.0068f)), dark ? Col("#F2E6D6") : Eye);
                    break;
                }

                case CatPart.Blush:
                    c.Bounds = new Rect(0.5f, 0.42f, 0.33f, 0.12f);
                    c.Fill(Ellipse(0.572f, 0.478f, 0.042f, 0.026f), A(Col("#F48A93"), 0.7f));
                    c.Fill(Ellipse(0.758f, 0.478f, 0.042f, 0.026f), A(Col("#F48A93"), 0.7f));
                    break;

                case CatPart.EyesClosed:
                    c.Bounds = new Rect(0.55f, 0.48f, 0.22f, 0.11f);
                    c.Fill(Union(Capsule(0.585f, 0.535f, 0.605f, 0.522f, 0.007f), Capsule(0.605f, 0.522f, 0.625f, 0.535f, 0.007f),
                                 Capsule(0.695f, 0.535f, 0.715f, 0.522f, 0.007f), Capsule(0.715f, 0.522f, 0.735f, 0.535f, 0.007f)),
                           dark ? Col("#F2E6D6") : Eye);
                    break;

                default:
                {
                    // 다리: 앞쪽(near)은 몸 색, 뒤쪽(far)은 살짝 어둡게. 발은 둥글게, 발가락 선.
                    float x = Joint[part].x;
                    c.Bounds = new Rect(x - 0.08f, 0.06f, 0.18f, 0.25f);
                    bool far = part == CatPart.LegFrontFar || part == CatPart.LegBackFar;
                    bool front = part == CatPart.LegFrontNear || part == CatPart.LegFrontFar;
                    var col = far ? Shade(fur, 0.86f) : fur;
                    var leg = Leg(x);
                    c.Fill(leg, col, dark ? Tint(fur, 0.3f) : Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        c.Fill(Intersect(Union(Capsule(x - 0.05f, 0.19f, x + 0.05f, 0.185f, 0.009f), Capsule(x - 0.05f, 0.165f, x + 0.05f, 0.16f, 0.009f)), leg), far ? Shade(look.Pattern, 0.86f) : look.Pattern);
                    if (look.Kind == CatPattern.Tuxedo && front)
                        c.Fill(Intersect(Ellipse(x + 0.008f, 0.128f, 0.06f, 0.04f), leg), far ? Shade(Color.white, 0.9f) : Color.white);
                    if (look.Kind == CatPattern.Point)
                        c.Fill(Intersect(Ellipse(x + 0.008f, 0.14f, 0.06f, 0.05f), leg), far ? Shade(look.Pattern, 0.86f) : look.Pattern);
                    var toe = dark ? A(Col("#F2E6D6"), 0.55f) : A(Line, 0.55f);
                    c.Fill(Union(Capsule(x + 0.006f, 0.104f, x + 0.006f, 0.12f, 0.0032f), Capsule(x + 0.03f, 0.106f, x + 0.03f, 0.12f, 0.0032f)), toe);
                    break;
                }
            }
            var j = Joint[part];
            return c.ToSprite($"cat_{part}", 1f, j);
        }
    }
}
