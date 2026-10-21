using System.Collections.Generic;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    /// <summary>가까이 보기용 정면 상반신 부위.</summary>
    public enum BustPart
    {
        Shadow, Tail, Body, Head, EarL, EarR,
        EyesOpen, EyesHalf, EyesHappy, EyesBlink, EyesAngry, MouthOpen, Blush,
    }

    /// <summary>
    /// 가까이 보기(쓰다듬기)용 정면 고양이. 코드로 그리되 무늬·음영·털 뭉치·눈 하이라이트를 넣어 크게 봐도 예쁘게.
    /// 모든 부위는 같은 정사각 캔버스(0~1) 좌표에 그리고 스프라이트 피벗을 관절에 둔다 (CatParts 와 같은 방식).
    /// 스프라이트 한 장의 월드 폭은 1.
    /// </summary>
    public static class CloseUpArt
    {
        public const int Res = 384;
        public static readonly Vector2 Root = new Vector2(0.5f, 0.04f);

        public static readonly Dictionary<BustPart, Vector2> Joint = new Dictionary<BustPart, Vector2>
        {
            { BustPart.Shadow, new Vector2(0.5f, 0.04f) },
            { BustPart.Tail, new Vector2(0.70f, 0.10f) },
            { BustPart.Body, new Vector2(0.5f, 0.04f) },
            { BustPart.Head, new Vector2(0.5f, 0.48f) },
            { BustPart.EarL, new Vector2(0.35f, 0.79f) },
            { BustPart.EarR, new Vector2(0.65f, 0.79f) },
            { BustPart.EyesOpen, new Vector2(0.5f, 0.672f) },
            { BustPart.EyesHalf, new Vector2(0.5f, 0.672f) },
            { BustPart.EyesHappy, new Vector2(0.5f, 0.672f) },
            { BustPart.EyesBlink, new Vector2(0.5f, 0.672f) },
            { BustPart.EyesAngry, new Vector2(0.5f, 0.672f) },
            { BustPart.MouthOpen, new Vector2(0.5f, 0.59f) },
            { BustPart.Blush, new Vector2(0.5f, 0.6f) },
        };

        public static bool OnHead(BustPart p) => p >= BustPart.EarL;

        private static readonly Color Line = CuteArt.Line;
        private static readonly Color Pink = Col("#F4A9A8");
        private static readonly Color NoseCol = Col("#E58A8A");
        private static readonly Color Pupil = Col("#2B211C");
        private static readonly Color DarkPatch = Col("#4A4148");
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private static Color A(Color c, float a) { c.a = a; return c; }
        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        private static Color Tint(Color c, float k) => Color.Lerp(c, Color.white, k);

        // ───── 실루엣 ─────
        private static readonly Shape HeadShape = Union(
            Ellipse(0.5f, 0.655f, 0.235f, 0.195f),
            Ellipse(0.5f, 0.612f, 0.262f, 0.148f),
            Triangle(new Vector2(0.25f, 0.625f), new Vector2(0.212f, 0.556f), new Vector2(0.275f, 0.552f), 0.006f),
            Triangle(new Vector2(0.75f, 0.625f), new Vector2(0.788f, 0.556f), new Vector2(0.725f, 0.552f), 0.006f));
        private static readonly Shape EarLShape = Triangle(new Vector2(0.285f, 0.74f), new Vector2(0.30f, 0.935f), new Vector2(0.43f, 0.83f), 0.02f);
        private static readonly Shape EarRShape = Triangle(new Vector2(0.715f, 0.74f), new Vector2(0.70f, 0.935f), new Vector2(0.57f, 0.83f), 0.02f);
        private static readonly Shape TorsoShape = Union(
            Ellipse(0.5f, 0.27f, 0.255f, 0.235f),
            Ellipse(0.5f, 0.42f, 0.19f, 0.16f),
            Ellipse(0.31f, 0.12f, 0.105f, 0.09f),
            Ellipse(0.69f, 0.12f, 0.105f, 0.09f));
        private static Shape LegShape(float x) => Union(Capsule(x, 0.265f, x - 0.004f, 0.095f, 0.054f), Ellipse(x - 0.002f, 0.075f, 0.064f, 0.048f));
        private static readonly Shape TailShape = Union(
            Capsule(0.70f, 0.10f, 0.84f, 0.16f, 0.043f),
            Capsule(0.84f, 0.16f, 0.90f, 0.30f, 0.042f),
            Capsule(0.90f, 0.30f, 0.875f, 0.44f, 0.040f),
            Capsule(0.875f, 0.44f, 0.82f, 0.50f, 0.038f));

        public static readonly Vector2 EyeL = new Vector2(0.405f, 0.672f), EyeR = new Vector2(0.595f, 0.672f);

        private static bool IsDark(CatLook l) => l.Base.grayscale < 0.35f;

        private static Color IrisOf(CatLook l)
        {
            if (l.Kind == CatPattern.Point) return Col("#8EC3EE");
            if (IsDark(l)) return Col("#E8C24F");
            if (l.Kind == CatPattern.Calico) return Col("#D9A441");
            if (l.Base.grayscale > 0.9f) return Col("#8FC7A2");
            return Col("#B4BE57");
        }

        public static Sprite Get(CatLook look, BustPart part)
        {
            string key = $"{look.Key}_bust_{part}";
            if (_cache.TryGetValue(key, out var s) && s != null && s.texture != null) return s;
            s = Draw(look, part);
            _cache[key] = s;
            return s;
        }

        /// <summary>처음 들어갈 때 끊기지 않게 미리 그려 둔다.</summary>
        public static void Warm(CatLook look)
        {
            foreach (BustPart p in System.Enum.GetValues(typeof(BustPart))) Get(look, p);
        }

        private static Sprite Draw(CatLook look, BustPart part)
        {
            var c = new SdfCanvas(Res);
            float w = 0.0085f;
            bool dark = IsDark(look);
            var fur = look.Base;
            var light = Tint(fur, dark ? 0.12f : 0.45f);
            var lineSoft = dark ? A(Col("#F2E6D6"), 0.85f) : Line;
            switch (part)
            {
                case BustPart.Shadow:
                    c.Bounds = new Rect(0.1f, 0f, 0.8f, 0.1f);
                    c.Fill(Ellipse(0.5f, 0.035f, 0.34f, 0.035f), A(Col("#3A2E38"), 0.28f));
                    break;

                case BustPart.Tail:
                {
                    c.Bounds = new Rect(0.6f, 0.03f, 0.38f, 0.53f);
                    c.Fill(TailShape, fur, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                        foreach (var y in new[] { 0.22f, 0.32f, 0.42f })
                            c.Fill(Intersect(Ellipse(0.89f, y, 0.06f, 0.016f), TailShape), look.Pattern);
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.86f, 0.46f, 0.08f, 0.12f), TailShape), look.Pattern);
                    if (look.Kind == CatPattern.Calico) c.Fill(Intersect(Ellipse(0.85f, 0.47f, 0.06f, 0.06f), TailShape), DarkPatch);
                    if (look.Kind == CatPattern.Tuxedo) c.Fill(Intersect(Circle(0.825f, 0.50f, 0.04f), TailShape), Color.white);
                    c.Fill(Intersect(Capsule(0.88f, 0.24f, 0.895f, 0.36f, 0.012f), TailShape), A(Color.white, 0.18f));
                    break;
                }

                case BustPart.Body:
                {
                    c.Bounds = new Rect(0.15f, 0f, 0.7f, 0.62f);
                    c.Fill(TorsoShape, fur, Line, w);
                    // 음영: 아래쪽과 오른쪽 옆구리
                    c.Fill(Intersect(TorsoShape, Ellipse(0.5f, 0.03f, 0.42f, 0.13f)), A(Shade(fur, 0.82f), 0.45f));
                    c.Fill(Intersect(TorsoShape, Subtract(Ellipse(0.5f, 0.27f, 0.255f, 0.235f), Ellipse(0.47f, 0.30f, 0.225f, 0.225f))), A(Shade(fur, 0.8f), 0.35f));
                    // 무늬 (몸통)
                    if (look.Kind == CatPattern.Tabby)
                    {
                        var st = Union(
                            Capsule(0.265f, 0.30f, 0.335f, 0.275f, 0.014f), Capsule(0.255f, 0.22f, 0.325f, 0.205f, 0.014f), Capsule(0.28f, 0.37f, 0.34f, 0.345f, 0.012f),
                            Capsule(0.735f, 0.30f, 0.665f, 0.275f, 0.014f), Capsule(0.745f, 0.22f, 0.675f, 0.205f, 0.014f), Capsule(0.72f, 0.37f, 0.66f, 0.345f, 0.012f),
                            Capsule(0.24f, 0.13f, 0.30f, 0.12f, 0.012f), Capsule(0.76f, 0.13f, 0.70f, 0.12f, 0.012f));
                        c.Fill(Intersect(st, TorsoShape), look.Pattern);
                    }
                    if (look.Kind == CatPattern.Calico)
                    {
                        c.Fill(Intersect(Ellipse(0.31f, 0.24f, 0.12f, 0.13f), TorsoShape), look.Pattern);
                        c.Fill(Intersect(Ellipse(0.70f, 0.34f, 0.10f, 0.085f), TorsoShape), DarkPatch);
                    }
                    // 가슴 털 (밝은 털 뭉치, 아래 끝이 들쭉날쭉)
                    var chestCol = look.Kind == CatPattern.Tuxedo ? Color.white : A(light, 0.9f);
                    var chest = Union(Ellipse(0.5f, 0.405f, look.Kind == CatPattern.Tuxedo ? 0.13f : 0.11f, look.Kind == CatPattern.Tuxedo ? 0.17f : 0.12f),
                        Triangle(new Vector2(0.43f, 0.31f), new Vector2(0.455f, 0.255f), new Vector2(0.48f, 0.31f), 0.006f),
                        Triangle(new Vector2(0.475f, 0.30f), new Vector2(0.5f, 0.24f), new Vector2(0.525f, 0.30f), 0.006f),
                        Triangle(new Vector2(0.52f, 0.31f), new Vector2(0.545f, 0.255f), new Vector2(0.57f, 0.31f), 0.006f));
                    c.Fill(Intersect(chest, TorsoShape), chestCol);
                    // 앞다리와 발
                    foreach (var x in new[] { 0.43f, 0.57f })
                    {
                        var leg = LegShape(x);
                        // 검은 고양이는 다리 경계가 묻히지 않게 조금 밝은 선
                        c.Fill(leg, fur, dark ? Tint(fur, 0.3f) : Line, w);
                        c.Fill(Intersect(leg, Ellipse(x + 0.03f, 0.2f, 0.03f, 0.12f)), A(Shade(fur, 0.85f), 0.35f));
                        if (look.Kind == CatPattern.Tabby)
                            c.Fill(Intersect(Union(Capsule(x - 0.06f, 0.22f, x - 0.02f, 0.215f, 0.01f), Capsule(x - 0.06f, 0.17f, x - 0.025f, 0.165f, 0.01f)), leg), look.Pattern);
                        var paw = Intersect(leg, Ellipse(x, 0.075f, 0.07f, 0.05f));
                        if (look.Kind == CatPattern.Tuxedo) c.Fill(paw, Color.white);
                        if (look.Kind == CatPattern.Point) c.Fill(Intersect(leg, Ellipse(x, 0.1f, 0.07f, 0.08f)), look.Pattern);
                        c.Fill(Union(Capsule(x - 0.018f, 0.04f, x - 0.018f, 0.068f, 0.0035f), Capsule(x + 0.014f, 0.04f, x + 0.014f, 0.068f, 0.0035f)), A(lineSoft, 0.65f));
                    }
                    break;
                }

                case BustPart.Head:
                {
                    c.Bounds = new Rect(0.18f, 0.43f, 0.64f, 0.45f);
                    c.Fill(HeadShape, fur, Line, w);
                    if (look.Kind == CatPattern.Tabby)
                    {
                        var m = Union(
                            Capsule(0.5f, 0.825f, 0.5f, 0.755f, 0.01f), Capsule(0.455f, 0.82f, 0.462f, 0.765f, 0.009f), Capsule(0.545f, 0.82f, 0.538f, 0.765f, 0.009f),
                            Capsule(0.41f, 0.805f, 0.425f, 0.775f, 0.008f), Capsule(0.59f, 0.805f, 0.575f, 0.775f, 0.008f),
                            Capsule(0.255f, 0.665f, 0.325f, 0.655f, 0.009f), Capsule(0.25f, 0.63f, 0.315f, 0.628f, 0.009f),
                            Capsule(0.745f, 0.665f, 0.675f, 0.655f, 0.009f), Capsule(0.75f, 0.63f, 0.685f, 0.628f, 0.009f));
                        c.Fill(Intersect(m, HeadShape), look.Pattern);
                    }
                    if (look.Kind == CatPattern.Calico)
                    {
                        c.Fill(Intersect(Ellipse(0.355f, 0.755f, 0.135f, 0.115f), HeadShape), look.Pattern);
                        c.Fill(Intersect(Ellipse(0.665f, 0.79f, 0.10f, 0.07f), HeadShape), DarkPatch);
                    }
                    if (look.Kind == CatPattern.Tuxedo)
                    {
                        c.Fill(Intersect(Union(Ellipse(0.5f, 0.565f, 0.165f, 0.095f), Triangle(new Vector2(0.47f, 0.62f), new Vector2(0.53f, 0.62f), new Vector2(0.5f, 0.77f), 0.012f)), HeadShape), Color.white);
                    }
                    if (look.Kind == CatPattern.Point) c.Fill(Intersect(Ellipse(0.5f, 0.6f, 0.16f, 0.125f), HeadShape), A(look.Pattern, 0.85f));
                    // 머리 위 하이라이트, 볼 아래 음영
                    c.Fill(Intersect(HeadShape, Ellipse(0.47f, 0.775f, 0.13f, 0.055f)), A(Color.white, 0.16f));
                    c.Fill(Intersect(HeadShape, Ellipse(0.5f, 0.47f, 0.3f, 0.06f)), A(Shade(fur, 0.8f), 0.3f));
                    // 주둥이
                    var muzzle = Union(Circle(0.462f, 0.593f, 0.05f), Circle(0.538f, 0.593f, 0.05f));
                    var muzzleCol = look.Kind == CatPattern.Tuxedo ? Color.white : look.Kind == CatPattern.Point ? Tint(look.Pattern, 0.25f) : Tint(fur, dark ? 0.15f : 0.55f);
                    c.Fill(muzzle, muzzleCol, A(Shade(muzzleCol, 0.8f), 0.6f), 0.003f);
                    // 수염 점
                    foreach (var p in new[] { new Vector2(0.44f, 0.6f), new Vector2(0.452f, 0.585f), new Vector2(0.56f, 0.6f), new Vector2(0.548f, 0.585f) })
                        c.Fill(Circle(p.x, p.y, 0.0035f), A(lineSoft, 0.5f));
                    // 코
                    c.Fill(Triangle(new Vector2(0.474f, 0.633f), new Vector2(0.526f, 0.633f), new Vector2(0.5f, 0.606f), 0.008f), NoseCol);
                    c.Fill(Circle(0.492f, 0.628f, 0.0055f), A(Color.white, 0.7f));
                    // 입 (ω)
                    var mouth = Union(Capsule(0.5f, 0.606f, 0.5f, 0.592f, 0.0042f),
                        Capsule(0.5f, 0.592f, 0.486f, 0.582f, 0.0042f), Capsule(0.486f, 0.582f, 0.47f, 0.59f, 0.0042f),
                        Capsule(0.5f, 0.592f, 0.514f, 0.582f, 0.0042f), Capsule(0.514f, 0.582f, 0.53f, 0.59f, 0.0042f));
                    c.Fill(mouth, look.Kind == CatPattern.Tuxedo ? Line : lineSoft);
                    // 수염
                    var wcol = dark ? A(Col("#F2E6D6"), 0.75f) : A(Line, 0.5f);
                    c.Fill(Union(
                        Capsule(0.415f, 0.598f, 0.255f, 0.625f, 0.0026f), Capsule(0.415f, 0.588f, 0.245f, 0.59f, 0.0026f), Capsule(0.415f, 0.578f, 0.26f, 0.552f, 0.0026f),
                        Capsule(0.585f, 0.598f, 0.745f, 0.625f, 0.0026f), Capsule(0.585f, 0.588f, 0.755f, 0.59f, 0.0026f), Capsule(0.585f, 0.578f, 0.74f, 0.552f, 0.0026f)), wcol);
                    break;
                }

                case BustPart.EarL:
                case BustPart.EarR:
                {
                    bool left = part == BustPart.EarL;
                    c.Bounds = left ? new Rect(0.25f, 0.7f, 0.22f, 0.27f) : new Rect(0.53f, 0.7f, 0.22f, 0.27f);
                    var earCol = look.Kind == CatPattern.Point ? look.Pattern
                        : look.Kind == CatPattern.Calico ? (left ? look.Pattern : DarkPatch) : fur;
                    c.Fill(left ? EarLShape : EarRShape, earCol, Line, w);
                    var inner = left
                        ? Triangle(new Vector2(0.312f, 0.77f), new Vector2(0.322f, 0.895f), new Vector2(0.405f, 0.83f), 0.012f)
                        : Triangle(new Vector2(0.688f, 0.77f), new Vector2(0.678f, 0.895f), new Vector2(0.595f, 0.83f), 0.012f);
                    c.Fill(inner, A(Pink, 0.9f));
                    // 귓속 털
                    var tuft = left
                        ? Union(Capsule(0.33f, 0.78f, 0.345f, 0.83f, 0.004f), Capsule(0.35f, 0.785f, 0.36f, 0.825f, 0.004f))
                        : Union(Capsule(0.67f, 0.78f, 0.655f, 0.83f, 0.004f), Capsule(0.65f, 0.785f, 0.64f, 0.825f, 0.004f));
                    c.Fill(tuft, A(Color.white, 0.85f));
                    break;
                }

                case BustPart.EyesOpen:
                case BustPart.EyesHalf:
                {
                    c.Bounds = new Rect(0.33f, 0.6f, 0.34f, 0.14f);
                    var iris = IrisOf(look);
                    foreach (var e in new[] { EyeL, EyeR })
                    {
                        var eye = Ellipse(e.x, e.y, 0.047f, 0.055f);
                        Shape vis = eye;
                        if (part == BustPart.EyesHalf) vis = Intersect(eye, (x, y) => y - (e.y + 0.004f));
                        c.Fill(vis, iris, Line, 0.006f);
                        c.Fill(Intersect(Ellipse(e.x, e.y - 0.025f, 0.04f, 0.028f), vis), A(Tint(iris, 0.4f), 0.65f));
                        c.Fill(Intersect(Ellipse(e.x, e.y - 0.003f, 0.029f, 0.042f), vis), Pupil);
                        if (part == BustPart.EyesOpen)
                        {
                            c.Fill(Circle(e.x + 0.016f, e.y + 0.021f, 0.013f), Color.white);
                            c.Fill(Circle(e.x - 0.015f, e.y - 0.019f, 0.0065f), A(Color.white, 0.8f));
                        }
                        else
                        {
                            c.Fill(Circle(e.x + 0.016f, e.y - 0.008f, 0.009f), Color.white);
                            c.Fill(Capsule(e.x - 0.052f, e.y + 0.004f, e.x + 0.052f, e.y + 0.004f, 0.0055f), Line);
                        }
                    }
                    break;
                }

                case BustPart.EyesHappy:
                case BustPart.EyesBlink:
                case BustPart.EyesAngry:
                {
                    c.Bounds = new Rect(0.33f, 0.6f, 0.34f, 0.14f);
                    var col = dark ? Col("#F2E6D6") : Line;
                    foreach (var e in new[] { EyeL, EyeR })
                    {
                        bool left = e.x < 0.5f;
                        Shape s;
                        if (part == BustPart.EyesHappy)
                            s = Union(Capsule(e.x - 0.04f, e.y - 0.012f, e.x, e.y + 0.018f, 0.0075f), Capsule(e.x, e.y + 0.018f, e.x + 0.04f, e.y - 0.012f, 0.0075f));
                        else if (part == BustPart.EyesBlink)
                            s = Union(Capsule(e.x - 0.043f, e.y + 0.004f, e.x, e.y - 0.012f, 0.0072f), Capsule(e.x, e.y - 0.012f, e.x + 0.043f, e.y + 0.004f, 0.0072f));
                        else
                        {
                            float d = left ? 1f : -1f; // 왼눈 >, 오른눈 <
                            s = Union(Capsule(e.x - 0.034f * d, e.y + 0.022f, e.x + 0.03f * d, e.y, 0.0078f), Capsule(e.x + 0.03f * d, e.y, e.x - 0.034f * d, e.y - 0.022f, 0.0078f));
                        }
                        c.Fill(s, col);
                    }
                    break;
                }

                case BustPart.MouthOpen:
                    c.Bounds = new Rect(0.45f, 0.54f, 0.1f, 0.07f);
                    c.Fill(Ellipse(0.5f, 0.574f, 0.024f, 0.02f), Col("#8E4A4A"), Line, 0.0042f);
                    c.Fill(Ellipse(0.5f, 0.565f, 0.014f, 0.008f), Pink);
                    break;

                case BustPart.Blush:
                    c.Bounds = new Rect(0.28f, 0.55f, 0.44f, 0.1f);
                    c.Fill(Ellipse(0.352f, 0.6f, 0.042f, 0.023f), A(Pink, 0.6f));
                    c.Fill(Ellipse(0.648f, 0.6f, 0.042f, 0.023f), A(Pink, 0.6f));
                    break;
            }
            var tex = c.ToTexture($"bust_{part}");
            var j = Joint[part];
            var sp = Sprite.Create(tex, new Rect(0, 0, Res, Res), j, Res, 0, SpriteMeshType.FullRect);
            sp.name = $"bust_{part}";
            return sp;
        }

        // ───── 효과 그림 ─────
        private static Sprite _vein;

        /// <summary>짜증 표시 (빨간 십자 핏줄).</summary>
        public static Sprite Vein()
        {
            if (_vein != null && _vein.texture != null) return _vein;
            var c = new SdfCanvas(96);
            var red = Col("#E0574F");
            var arcs = Union(
                Ring(0.27f, 0.27f, 0.17f, 0.045f), Ring(0.73f, 0.27f, 0.17f, 0.045f),
                Ring(0.27f, 0.73f, 0.17f, 0.045f), Ring(0.73f, 0.73f, 0.17f, 0.045f));
            c.Fill(Intersect(arcs, Subtract(Circle(0.5f, 0.5f, 0.42f), Circle(0.5f, 0.5f, 0.12f))), red, Col("#FFF3EC"), 0.03f);
            var tex = c.ToTexture("vein");
            _vein = Sprite.Create(tex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 96, 0, SpriteMeshType.FullRect);
            return _vein;
        }

        /// <summary>
        /// 캔버스 좌표(u,v)가 어느 부위인지. 정면 상반신 기준.
        /// </summary>
        public static Cats.PetZone ZoneAt(Vector2 uv)
        {
            float u = uv.x, v = uv.y;
            float du = u - 0.5f;
            if ((uv - new Vector2(0.5f, 0.622f)).magnitude < 0.04f) return Cats.PetZone.Nose;
            // 귀 · 머리
            if (EarLShape(u, v) < 0.01f || EarRShape(u, v) < 0.01f) return Cats.PetZone.Head;
            if (HeadShape(u, v) < 0.012f)
            {
                if (v > 0.71f) return Cats.PetZone.Head;
                if (v < 0.565f && Mathf.Abs(du) < 0.14f) return Cats.PetZone.Chin;
                return Cats.PetZone.Cheek;
            }
            // 꼬리
            if (TailShape(u, v) < 0.012f && TorsoShape(u, v) > 0f) return Cats.PetZone.Tail;
            if (TorsoShape(u, v) < 0.012f || LegShape(0.43f)(u, v) < 0.01f || LegShape(0.57f)(u, v) < 0.01f)
            {
                if (v < 0.13f && Mathf.Abs(du) < 0.15f) return Cats.PetZone.Paw;
                if (v > 0.34f) return Cats.PetZone.Chest;
                return Cats.PetZone.Belly;
            }
            return Cats.PetZone.None;
        }
    }
}
