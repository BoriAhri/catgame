using System.Collections.Generic;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    public enum CatPattern { Solid, Tabby, Tuxedo, Calico, Point }

    /// <summary>고양이 한 마리의 외형 (임시 그래픽용).</summary>
    public struct CatLook
    {
        public Color Base;
        public Color Pattern;
        public CatPattern Kind;
        /// <summary>아트 라이브러리에서 찾을 고양이 스킨 ID (예: cheese). 진짜 그림이 없으면 코드 그림.</summary>
        public string SkinId;

        public CatLook(Color b, CatPattern kind, Color pattern, string skinId = "") { Base = b; Kind = kind; Pattern = pattern; SkinId = skinId; }
        public string Key => $"{ColorUtility.ToHtmlStringRGB(Base)}_{Kind}_{ColorUtility.ToHtmlStringRGB(Pattern)}";
    }

    /// <summary>
    /// 코드로 그린 임시 그래픽. 아트 디렉션 문서의 팔레트(선 갈색, 크림, 파스텔)를 따른다.
    /// 처음 요청할 때 한 번 그리고 캐시한다. 8단계에서 일러스트로 바꿀 때 이 클래스만 갈아끼우면 된다.
    /// </summary>
    public static class CuteArt
    {
        public static readonly Color Line = Col("#5E4B3C");
        private static readonly Color Cream = Col("#FFF8EA");
        private static readonly Color Pink = Col("#F4A9A8");
        private static readonly Color Nose = Col("#E58A8A");
        private static readonly Color Eye = Col("#3A2E28");

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private static readonly HashSet<string> _noSprite = new HashSet<string>();

        /// <summary>
        /// 캐시. 에디터에서 도메인 리로드 없이 플레이를 다시 시작하면 static 은 남고 텍스처는 지워지므로,
        /// 지워진(Unity 기준 null) 스프라이트는 다시 그린다. 그림이 없는 가구는 따로 기억.
        /// </summary>
        private static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (_noSprite.Contains(key)) return null;
            if (_cache.TryGetValue(key, out var s) && s != null && s.texture != null) return s;
            s = make();
            if (s == null) _noSprite.Add(key);
            else _cache[key] = s;
            return s;
        }

        private static Color A(Color c, float a) { c.a = a; return c; }

        // ───────────────────────── 고양이 ─────────────────────────

        /// <summary>오른쪽을 보는 앉은 고양이. 발바닥 중앙이 피벗. 월드 폭 1.</summary>
        public static Sprite Cat(CatLook look, bool eyesClosed) =>
            Cached($"cat_{look.Key}_{eyesClosed}", () => DrawCat(look, eyesClosed));

        private static Sprite DrawCat(CatLook look, bool closed)
        {
            var c = new SdfCanvas(256);
            var body = Ellipse(0.44f, 0.30f, 0.25f, 0.175f);
            var head = Ellipse(0.645f, 0.515f, 0.205f, 0.18f);
            var earL = Triangle(new Vector2(0.475f, 0.61f), new Vector2(0.505f, 0.80f), new Vector2(0.61f, 0.68f), 0.022f);
            var earR = Triangle(new Vector2(0.675f, 0.68f), new Vector2(0.78f, 0.80f), new Vector2(0.81f, 0.61f), 0.022f);
            var tail = Union(Capsule(0.22f, 0.27f, 0.13f, 0.40f, 0.045f), Capsule(0.13f, 0.40f, 0.15f, 0.55f, 0.045f));
            var legF = Capsule(0.56f, 0.13f, 0.56f, 0.2f, 0.048f);
            var legF2 = Capsule(0.48f, 0.13f, 0.48f, 0.2f, 0.048f);
            var legB = Capsule(0.30f, 0.13f, 0.30f, 0.2f, 0.05f);
            var silhouette = Union(body, head, earL, earR, tail, legF, legF2, legB);

            // 바닥 그림자
            c.Fill(Ellipse(0.45f, 0.1f, 0.3f, 0.045f), A(Col("#7A6A78"), 0.22f));
            c.Fill(silhouette, look.Base, Line, 0.018f);

            bool dark = look.Base.grayscale < 0.35f;
            switch (look.Kind)
            {
                case CatPattern.Tabby:
                {
                    var stripes = Union(
                        Capsule(0.37f, 0.45f, 0.39f, 0.36f, 0.018f),
                        Capsule(0.45f, 0.47f, 0.47f, 0.37f, 0.018f),
                        Capsule(0.29f, 0.42f, 0.31f, 0.34f, 0.016f),
                        Capsule(0.62f, 0.68f, 0.625f, 0.62f, 0.012f),
                        Capsule(0.67f, 0.685f, 0.67f, 0.625f, 0.012f),
                        Capsule(0.12f, 0.46f, 0.18f, 0.47f, 0.016f));
                    c.Fill(Intersect(stripes, silhouette), look.Pattern);
                    break;
                }
                case CatPattern.Tuxedo:
                {
                    var white = Union(Ellipse(0.6f, 0.3f, 0.11f, 0.15f), Ellipse(0.67f, 0.45f, 0.1f, 0.065f),
                        Capsule(0.56f, 0.13f, 0.56f, 0.15f, 0.046f), Capsule(0.48f, 0.13f, 0.48f, 0.15f, 0.046f));
                    c.Fill(Intersect(white, silhouette), look.Pattern);
                    break;
                }
                case CatPattern.Calico:
                {
                    c.Fill(Intersect(Ellipse(0.34f, 0.37f, 0.13f, 0.1f), silhouette), look.Pattern);
                    c.Fill(Intersect(Union(Ellipse(0.72f, 0.64f, 0.11f, 0.09f), Ellipse(0.15f, 0.5f, 0.06f, 0.08f)), silhouette), Col("#4A4148"));
                    break;
                }
            }

            // 배 쪽 밝은 결
            c.Fill(Intersect(Ellipse(0.52f, 0.25f, 0.14f, 0.08f), body), A(Color.white, 0.22f));
            // 귀 안쪽
            c.Fill(Triangle(new Vector2(0.505f, 0.655f), new Vector2(0.52f, 0.755f), new Vector2(0.585f, 0.685f), 0.008f), A(Pink, 0.9f));
            c.Fill(Triangle(new Vector2(0.70f, 0.685f), new Vector2(0.765f, 0.755f), new Vector2(0.78f, 0.655f), 0.008f), A(Pink, 0.9f));

            // 눈
            var eyeCol = dark ? Col("#F2D06B") : Eye;
            if (closed)
            {
                var lid = Union(Capsule(0.585f, 0.535f, 0.605f, 0.522f, 0.007f), Capsule(0.605f, 0.522f, 0.625f, 0.535f, 0.007f),
                                Capsule(0.695f, 0.535f, 0.715f, 0.522f, 0.007f), Capsule(0.715f, 0.522f, 0.735f, 0.535f, 0.007f));
                c.Fill(lid, dark ? Col("#F2E6D6") : Eye);
            }
            else
            {
                c.Fill(Ellipse(0.605f, 0.535f, 0.022f, 0.032f), eyeCol);
                c.Fill(Ellipse(0.715f, 0.535f, 0.022f, 0.032f), eyeCol);
                if (dark)
                {
                    c.Fill(Ellipse(0.607f, 0.532f, 0.008f, 0.024f), Eye);
                    c.Fill(Ellipse(0.717f, 0.532f, 0.008f, 0.024f), Eye);
                }
                c.Fill(Circle(0.612f, 0.548f, 0.0085f), Color.white);
                c.Fill(Circle(0.722f, 0.548f, 0.0085f), Color.white);
            }

            // 볼터치, 코, 입 (ω)
            c.Fill(Ellipse(0.565f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
            c.Fill(Ellipse(0.765f, 0.475f, 0.036f, 0.022f), A(Pink, dark ? 0.45f : 0.6f));
            c.Fill(Triangle(new Vector2(0.648f, 0.497f), new Vector2(0.672f, 0.497f), new Vector2(0.66f, 0.482f), 0.004f), Nose);
            var mouthCol = dark ? Col("#F2E6D6") : Line;
            c.Fill(Union(Capsule(0.645f, 0.472f, 0.652f, 0.462f, 0.004f), Capsule(0.652f, 0.462f, 0.66f, 0.472f, 0.004f),
                         Capsule(0.66f, 0.472f, 0.668f, 0.462f, 0.004f), Capsule(0.668f, 0.462f, 0.675f, 0.472f, 0.004f)), mouthCol);

            return c.ToSprite("cat", 1f, new Vector2(0.47f, 0.1f));
        }

        // ───────────────────────── 가구 ─────────────────────────

        /// <summary>가구 소품 그림. variant 는 화장실 오염 단계 (0~3). 바닥 중앙 피벗, 월드 폭 1. 그림이 없는 가구는 null.</summary>
        public static Sprite Furniture(FurnitureDef def, int variant = 0)
        {
            if (def == null) return null;
            if (def.WallMounted) return WallArt.SpriteOf(def.Id);
            var lib = ArtLibrary.Active != null ? ArtLibrary.Active.FurnitureSprite(def.Id, variant) : null;
            if (lib != null) return lib;
            return Cached($"furn_{def.Id}_{variant}", () => DrawFurniture(def, variant));
        }

        private static Sprite DrawFurniture(FurnitureDef def, int v)
        {
            var c = new SdfCanvas(192);
            float w = 0.016f;
            var shadow = A(Col("#7A6A78"), 0.2f);
            switch (def.Id)
            {
                case "food_bowl":
                    c.Fill(Ellipse(0.5f, 0.17f, 0.36f, 0.06f), shadow);
                    c.Fill(Union(Intersect(Ellipse(0.5f, 0.36f, 0.33f, 0.22f), RoundBox(0.5f, 0.2f, 0.4f, 0.16f, 0f)), Ellipse(0.5f, 0.36f, 0.33f, 0.08f)), Col("#9CC7E8"), Line, w);
                    // variant 1 = 빈 그릇 (야외 밥그릇을 채워야 할 때)
                    c.Fill(Ellipse(0.5f, 0.37f, 0.27f, 0.055f), v == 1 ? Col("#6E95B5") : Col("#C08552"));
                    if (v != 1)
                        for (int i = 0; i < 5; i++) c.Fill(Circle(0.32f + i * 0.09f, 0.39f + (i % 2) * 0.02f, 0.035f), Col("#D49A64"), Line, 0.008f);
                    c.Fill(Ellipse(0.38f, 0.25f, 0.06f, 0.025f), A(Color.white, 0.5f));
                    c.Fill(Heart(0.5f, 0.24f, 0.035f), A(Col("#FFF8EA"), 0.9f));
                    break;

                case "litter_box":
                {
                    c.Fill(Ellipse(0.5f, 0.15f, 0.42f, 0.06f), shadow);
                    c.Fill(RoundBox(0.5f, 0.27f, 0.41f, 0.13f, 0.07f), Col("#F2C4A8"), Line, w);
                    var sand = new[] { Col("#F7E9CC"), Col("#EAD5A9"), Col("#D8C092"), Col("#C2A57C") };
                    c.Fill(Ellipse(0.5f, 0.38f, 0.35f, 0.07f), sand[Mathf.Clamp(v, 0, 3)], Line, 0.01f);
                    if (v >= 1) c.Fill(Circle(0.38f, 0.395f, 0.04f), Col("#B9945F"), Line, 0.008f);
                    if (v >= 2) c.Fill(Circle(0.6f, 0.385f, 0.035f), Col("#9C7A4E"), Line, 0.008f);
                    if (v >= 3)
                    {
                        c.Fill(Circle(0.5f, 0.41f, 0.03f), Col("#8A6A44"), Line, 0.008f);
                        var stink = A(Col("#9FB08A"), 0.85f);
                        c.Fill(Union(Capsule(0.4f, 0.52f, 0.36f, 0.6f, 0.014f), Capsule(0.36f, 0.6f, 0.4f, 0.68f, 0.014f)), stink);
                        c.Fill(Union(Capsule(0.6f, 0.52f, 0.64f, 0.6f, 0.014f), Capsule(0.64f, 0.6f, 0.6f, 0.68f, 0.014f)), stink);
                    }
                    if (v == 0) c.Fill(Star(0.7f, 0.5f, 0.045f, 4, 0.35f, 0.004f), A(Color.white, 0.95f));
                    break;
                }

                case "scratcher":
                    c.Fill(Ellipse(0.5f, 0.12f, 0.32f, 0.05f), shadow);
                    c.Fill(RoundBox(0.5f, 0.15f, 0.3f, 0.06f, 0.04f), Col("#C9A27E"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.48f, 0.1f, 0.3f, 0.05f), Col("#E8D2A6"), Line, w);
                    for (int i = 0; i < 6; i++) c.Fill(Intersect(Capsule(0.38f, 0.24f + i * 0.09f, 0.62f, 0.27f + i * 0.09f, 0.008f), RoundBox(0.5f, 0.48f, 0.095f, 0.29f, 0.05f)), Col("#C9A27E"));
                    c.Fill(Circle(0.5f, 0.84f, 0.06f), Col("#F4A9A8"), Line, w);
                    break;

                case "cushion":
                    c.Fill(Ellipse(0.5f, 0.17f, 0.4f, 0.06f), shadow);
                    c.Fill(Ellipse(0.5f, 0.3f, 0.4f, 0.16f), Col("#F2B8B8"), Line, w);
                    c.Fill(Ellipse(0.5f, 0.33f, 0.28f, 0.09f), Col("#F8D3D0"));
                    c.Fill(Circle(0.5f, 0.33f, 0.025f), Col("#E58A8A"), Line, 0.008f);
                    break;

                case "box_1":
                case "box_2":
                {
                    float hw = def.Id == "box_2" ? 0.43f : 0.32f;
                    c.Fill(Ellipse(0.5f, 0.12f, hw + 0.04f, 0.05f), shadow);
                    var flapL = Polygon(new[] { new Vector2(0.5f - hw, 0.5f), new Vector2(0.5f - hw - 0.08f, 0.62f), new Vector2(0.5f - hw * 0.25f, 0.62f), new Vector2(0.5f - hw * 0.2f, 0.5f) }, 0.01f);
                    var flapR = Polygon(new[] { new Vector2(0.5f + hw * 0.2f, 0.5f), new Vector2(0.5f + hw * 0.25f, 0.62f), new Vector2(0.5f + hw + 0.08f, 0.62f), new Vector2(0.5f + hw, 0.5f) }, 0.01f);
                    c.Fill(flapL, Col("#E9BE84"), Line, w);
                    c.Fill(flapR, Col("#E9BE84"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.32f, hw, 0.19f, 0.03f), Col("#D9A86C"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.47f, hw - 0.02f, 0.035f, 0.02f), Col("#7C5B3E"));
                    c.Fill(RoundBox(0.5f, 0.3f, hw * 0.3f, 0.025f, 0.012f), Col("#F3D9A8"));
                    break;
                }

                case "hiding_house":
                    c.Fill(Ellipse(0.5f, 0.12f, 0.38f, 0.05f), shadow);
                    c.Fill(Union(Circle(0.5f, 0.45f, 0.32f), RoundBox(0.5f, 0.27f, 0.32f, 0.15f, 0.05f)), Col("#C9B6E4"), Line, w);
                    c.Fill(Union(Circle(0.5f, 0.36f, 0.14f), RoundBox(0.5f, 0.26f, 0.14f, 0.1f, 0.02f)), Col("#5B4B6B"), Line, 0.01f);
                    c.Fill(Triangle(new Vector2(0.36f, 0.72f), new Vector2(0.4f, 0.86f), new Vector2(0.47f, 0.76f), 0.015f), Col("#C9B6E4"), Line, w);
                    c.Fill(Triangle(new Vector2(0.53f, 0.76f), new Vector2(0.6f, 0.86f), new Vector2(0.64f, 0.72f), 0.015f), Col("#C9B6E4"), Line, w);
                    c.Fill(Ellipse(0.32f, 0.55f, 0.05f, 0.08f), A(Color.white, 0.35f));
                    break;

                case "cat_tower_3":
                case "cat_tower_5":
                {
                    bool big = def.Id == "cat_tower_5";
                    c.Fill(Ellipse(0.5f, 0.1f, 0.38f, 0.05f), shadow);
                    c.Fill(RoundBox(0.5f, 0.12f, 0.34f, 0.05f, 0.03f), Col("#B98A62"), Line, w);
                    c.Fill(RoundBox(0.5f, big ? 0.5f : 0.42f, 0.055f, big ? 0.38f : 0.3f, 0.03f), Col("#E8D2A6"), Line, w);
                    var plat = Col("#F2C4A8");
                    c.Fill(RoundBox(0.36f, big ? 0.36f : 0.4f, 0.2f, 0.035f, 0.03f), plat, Line, w);
                    c.Fill(RoundBox(0.64f, big ? 0.6f : 0.7f, 0.2f, 0.035f, 0.03f), plat, Line, w);
                    if (big)
                    {
                        c.Fill(RoundBox(0.42f, 0.84f, 0.22f, 0.04f, 0.03f), plat, Line, w);
                        c.Fill(Ellipse(0.42f, 0.89f, 0.15f, 0.035f), Col("#F8D3D0"), Line, 0.01f);
                        c.Fill(Circle(0.25f, 0.3f, 0.04f), Col("#F2C66D"), Line, 0.01f);
                        c.Fill(Capsule(0.25f, 0.34f, 0.25f, 0.36f, 0.004f), Line);
                    }
                    else c.Fill(Ellipse(0.64f, 0.75f, 0.14f, 0.03f), Col("#F8D3D0"), Line, 0.01f);
                    break;
                }

                case "bed":
                    c.Fill(Ellipse(0.5f, 0.12f, 0.44f, 0.05f), shadow);
                    c.Fill(RoundBox(0.5f, 0.25f, 0.43f, 0.12f, 0.06f), Col("#B98A62"), Line, w);
                    c.Fill(RoundBox(0.13f, 0.36f, 0.05f, 0.18f, 0.04f), Col("#B98A62"), Line, w);
                    c.Fill(RoundBox(0.53f, 0.36f, 0.38f, 0.08f, 0.06f), Col("#FFF1DE"), Line, w);
                    c.Fill(RoundBox(0.62f, 0.34f, 0.29f, 0.1f, 0.07f), Col("#9CC7E8"), Line, w);
                    c.Fill(Ellipse(0.27f, 0.43f, 0.1f, 0.055f), Cream, Line, w);
                    for (int i = 0; i < 3; i++) c.Fill(Circle(0.48f + i * 0.13f, 0.36f, 0.018f), A(Color.white, 0.7f));
                    break;

                case "cat_wheel":
                    c.Fill(Ellipse(0.5f, 0.1f, 0.36f, 0.05f), shadow);
                    c.Fill(Union(Capsule(0.5f, 0.52f, 0.28f, 0.12f, 0.03f), Capsule(0.5f, 0.52f, 0.72f, 0.12f, 0.03f), Capsule(0.22f, 0.11f, 0.78f, 0.11f, 0.03f)), Col("#B98A62"), Line, w);
                    c.Fill(Ring(0.5f, 0.54f, 0.32f, 0.04f), Col("#E9A27C"), Line, w);
                    c.Fill(Ring(0.5f, 0.54f, 0.32f, 0.012f), Col("#F7C9A9"));
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        c.Fill(Capsule(0.5f, 0.54f, 0.5f + Mathf.Cos(a) * 0.28f, 0.54f + Mathf.Sin(a) * 0.28f, 0.008f), A(Col("#E9A27C"), 0.7f));
                    }
                    c.Fill(Circle(0.5f, 0.54f, 0.04f), Col("#F2C66D"), Line, w);
                    break;

                case "bookshelf":
                {
                    c.Fill(Ellipse(0.5f, 0.1f, 0.44f, 0.05f), shadow);
                    c.Fill(RoundBox(0.5f, 0.45f, 0.43f, 0.35f, 0.03f), Col("#B98A62"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.45f, 0.39f, 0.31f, 0.02f), Col("#8E6A4B"));
                    var books = new[] { Col("#9CC7E8"), Col("#F4A9A8"), Col("#B7C49A"), Col("#F2C66D"), Col("#C9B6E4") };
                    for (int row = 0; row < 3; row++)
                    {
                        float y = 0.22f + row * 0.21f;
                        c.Fill(RoundBox(0.5f, y - 0.07f, 0.39f, 0.012f, 0.005f), Col("#B98A62"));
                        for (int i = 0; i < 7; i++)
                        {
                            float x = 0.18f + i * 0.1f + (row % 2) * 0.03f;
                            if (x > 0.84f) continue;
                            c.Fill(RoundBox(x, y, 0.03f, 0.06f + ((i + row) % 3) * 0.008f, 0.01f), books[(i + row * 2) % books.Length], Line, 0.006f);
                        }
                    }
                    break;
                }

                case "bench":
                    c.Fill(Ellipse(0.5f, 0.1f, 0.44f, 0.05f), shadow);
                    c.Fill(Union(Capsule(0.18f, 0.1f, 0.18f, 0.32f, 0.025f), Capsule(0.82f, 0.1f, 0.82f, 0.32f, 0.025f)), Col("#8E6A4B"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.35f, 0.42f, 0.05f, 0.025f), Col("#C9A27E"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.58f, 0.42f, 0.045f, 0.025f), Col("#C9A27E"), Line, w);
                    c.Fill(Union(Capsule(0.2f, 0.4f, 0.2f, 0.56f, 0.018f), Capsule(0.8f, 0.4f, 0.8f, 0.56f, 0.018f)), Col("#8E6A4B"), Line, w);
                    break;

                default:
                    return null; // 러그처럼 바닥 무늬로만 보이는 가구
            }
            return c.ToSprite(def.Id, 1f, new Vector2(0.5f, 0.12f));
        }

        // ───────────────────────── 아이콘 ─────────────────────────

        /// <summary>결과물·재화 아이콘. 가운데 피벗, 월드 폭 1.</summary>
        public static Sprite Item(ItemKind kind)
        {
            var lib = ArtLibrary.Active != null ? ArtLibrary.Active.ItemSprite(kind) : null;
            if (lib != null) return lib;
            return Cached($"item_{kind}", () => DrawItem(kind).ToSprite(kind.ToString(), 1f, new Vector2(0.5f, 0.5f)));
        }

        /// <summary>IMGUI 용 텍스처.</summary>
        public static Texture2D ItemTexture(ItemKind kind) => Item(kind)?.texture;

        /// <summary>IMGUI 에 아이콘을 그린다. 아트 라이브러리 그림이 아틀라스에 들어 있어도 제 영역만 그린다.</summary>
        public static void DrawIcon(Rect r, ItemKind kind)
        {
            var s = Item(kind);
            if (s == null || s.texture == null) return;
            var tr = s.textureRect;
            var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height, tr.width / s.texture.width, tr.height / s.texture.height);
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            var fit = r;
            if (aspect > 1f) { fit.height = r.width / aspect; fit.y += (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x += (r.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, s.texture, uv);
        }

        /// <summary>IMGUI 에 스프라이트를 비율 유지해서 그린다 (상점 카드의 가구 그림 등).</summary>
        public static void DrawSprite(Rect r, Sprite s)
        {
            if (s == null || s.texture == null) return;
            var tr = s.textureRect;
            var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height, tr.width / s.texture.width, tr.height / s.texture.height);
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            var fit = r;
            if (aspect > r.width / r.height) { fit.height = r.width / aspect; fit.y += (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x += (r.width - fit.width) * 0.5f; }
            GUI.DrawTextureWithTexCoords(fit, s.texture, uv);
        }

        /// <summary>스프라이트의 월드 폭. 코드 그림은 1, 아트 라이브러리 그림은 PPU 에 따라 다르다.</summary>
        public static float WidthOf(Sprite s) => s == null ? 1f : Mathf.Max(0.0001f, s.bounds.size.x);

        private static SdfCanvas DrawItem(ItemKind kind)
        {
            var c = new SdfCanvas(96);
            float w = 0.03f;
            switch (kind)
            {
                case ItemKind.FishBone:
                    c.Fill(Union(Capsule(0.28f, 0.5f, 0.7f, 0.5f, 0.035f),
                                 Capsule(0.38f, 0.38f, 0.42f, 0.62f, 0.025f), Capsule(0.5f, 0.36f, 0.52f, 0.64f, 0.025f), Capsule(0.62f, 0.38f, 0.62f, 0.62f, 0.025f),
                                 Triangle(new Vector2(0.7f, 0.5f), new Vector2(0.88f, 0.66f), new Vector2(0.88f, 0.34f), 0.03f),
                                 Triangle(new Vector2(0.3f, 0.5f), new Vector2(0.14f, 0.64f), new Vector2(0.14f, 0.36f), 0.03f)), Cream, Line, w);
                    c.Fill(Circle(0.8f, 0.53f, 0.025f), Line);
                    break;
                case ItemKind.Cloud:
                    c.Fill(Union(Circle(0.36f, 0.45f, 0.16f), Circle(0.55f, 0.55f, 0.2f), Circle(0.7f, 0.43f, 0.14f), RoundBox(0.53f, 0.38f, 0.27f, 0.1f, 0.1f)), Col("#EEF6FC"), Col("#7E9DB5"), w);
                    c.Fill(Ellipse(0.5f, 0.6f, 0.08f, 0.04f), A(Color.white, 0.9f));
                    break;
                case ItemKind.BellBall:
                    c.Fill(Circle(0.5f, 0.47f, 0.3f), Col("#F2C66D"), Line, w);
                    c.Fill(Capsule(0.3f, 0.45f, 0.7f, 0.45f, 0.025f), Line);
                    c.Fill(Circle(0.5f, 0.32f, 0.05f), Line);
                    c.Fill(Ring(0.5f, 0.82f, 0.06f, 0.02f), Line);
                    c.Fill(Ellipse(0.4f, 0.6f, 0.07f, 0.04f), A(Color.white, 0.7f));
                    break;
                case ItemKind.Star:
                    c.Fill(Star(0.5f, 0.48f, 0.4f, 5, 0.48f, 0.03f), Col("#F7D774"), Col("#B8873E"), w);
                    c.Fill(Ellipse(0.42f, 0.56f, 0.05f, 0.03f), A(Color.white, 0.8f));
                    break;
                case ItemKind.CottonBall:
                    c.Fill(Union(Circle(0.38f, 0.45f, 0.18f), Circle(0.6f, 0.47f, 0.19f), Circle(0.5f, 0.62f, 0.17f), Circle(0.5f, 0.34f, 0.15f)), Cream, Col("#B8A58E"), w);
                    break;
                case ItemKind.DreamBubble:
                    c.Fill(Circle(0.5f, 0.5f, 0.34f), A(Col("#D9CCF2"), 0.85f), Col("#8E7CB8"), w);
                    c.Fill(Ellipse(0.38f, 0.62f, 0.08f, 0.05f), A(Color.white, 0.9f));
                    c.Fill(Union(Capsule(0.48f, 0.5f, 0.6f, 0.5f, 0.018f), Capsule(0.6f, 0.5f, 0.48f, 0.36f, 0.018f), Capsule(0.48f, 0.36f, 0.6f, 0.36f, 0.018f)), Col("#8E7CB8"));
                    break;
                case ItemKind.ClawShell:
                    c.Fill(Subtract(Circle(0.5f, 0.5f, 0.32f), Circle(0.62f, 0.62f, 0.3f)), Col("#F5EADC"), Line, w);
                    break;
                case ItemKind.Potato:
                    c.Fill(Ellipse(0.5f, 0.48f, 0.34f, 0.25f), Col("#D9B27C"), Line, w);
                    c.Fill(Circle(0.38f, 0.52f, 0.025f), Col("#A57F4E"));
                    c.Fill(Circle(0.58f, 0.42f, 0.025f), Col("#A57F4E"));
                    c.Fill(Circle(0.64f, 0.58f, 0.02f), Col("#A57F4E"));
                    c.Fill(Ellipse(0.4f, 0.62f, 0.07f, 0.03f), A(Color.white, 0.5f));
                    break;
                case ItemKind.Mattdongsan:
                    c.Fill(Union(Capsule(0.22f, 0.4f, 0.78f, 0.56f, 0.13f)), Col("#A0703F"), Line, w);
                    for (int i = 0; i < 6; i++) c.Fill(Circle(0.27f + i * 0.095f, 0.47f + i * 0.027f + (i % 2) * 0.05f - 0.025f, 0.035f), Col("#E9D2A8"));
                    break;
                case ItemKind.Furball:
                    c.Fill(Circle(0.5f, 0.5f, 0.3f), Col("#D9CBB8"), Col("#8A7A6A"), w);
                    c.Fill(Union(Capsule(0.32f, 0.45f, 0.6f, 0.62f, 0.014f), Capsule(0.4f, 0.32f, 0.7f, 0.5f, 0.014f), Capsule(0.3f, 0.6f, 0.45f, 0.72f, 0.014f)), Col("#A89886"));
                    c.Fill(Union(Capsule(0.78f, 0.5f, 0.9f, 0.56f, 0.012f), Capsule(0.2f, 0.4f, 0.1f, 0.35f, 0.012f)), Col("#8A7A6A"));
                    break;
                case ItemKind.Whisker:
                    c.Fill(Circle(0.5f, 0.5f, 0.32f), A(Col("#F2E6D6"), 0.9f), Col("#B8A58E"), 0.02f);
                    c.Fill(Union(Capsule(0.24f, 0.62f, 0.78f, 0.7f, 0.012f), Capsule(0.22f, 0.5f, 0.8f, 0.5f, 0.012f), Capsule(0.24f, 0.38f, 0.78f, 0.3f, 0.012f)), Line);
                    break;
                case ItemKind.Kibble:
                    // 사료 봉지: 위가 접힌 크라프트 봉투에 생선 그림, 옆에 알갱이 두 개
                    c.Fill(RoundBox(0.47f, 0.44f, 0.25f, 0.3f, 0.05f), Col("#E3B27A"), Line, w);
                    c.Fill(RoundBox(0.47f, 0.76f, 0.26f, 0.05f, 0.02f), Col("#C98F55"), Line, w);
                    c.Fill(Union(Ellipse(0.45f, 0.44f, 0.12f, 0.07f), Triangle(new Vector2(0.56f, 0.44f), new Vector2(0.66f, 0.52f), new Vector2(0.66f, 0.36f), 0.01f)), Col("#9CC7E8"), Line, 0.02f);
                    c.Fill(Circle(0.39f, 0.46f, 0.015f), Line);
                    c.Fill(Circle(0.78f, 0.22f, 0.06f), Col("#C08552"), Line, 0.02f);
                    c.Fill(Circle(0.86f, 0.3f, 0.05f), Col("#C08552"), Line, 0.02f);
                    break;
                case ItemKind.Gift:
                    c.Fill(RoundBox(0.5f, 0.4f, 0.3f, 0.22f, 0.05f), Col("#9CC7E8"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.63f, 0.34f, 0.07f, 0.04f), Col("#B7D6EE"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.45f, 0.06f, 0.26f, 0.02f), Col("#F4A9A8"));
                    c.Fill(Union(Ellipse(0.4f, 0.76f, 0.1f, 0.06f), Ellipse(0.6f, 0.76f, 0.1f, 0.06f)), Col("#F4A9A8"), Line, 0.02f);
                    c.Fill(Circle(0.5f, 0.74f, 0.04f), Col("#E58A8A"), Line, 0.015f);
                    break;
                case ItemKind.Heart:
                    c.Fill(Heart(0.5f, 0.5f, 0.28f), Col("#EE9A9A"), Col("#B85C6A"), w);
                    c.Fill(Ellipse(0.37f, 0.62f, 0.06f, 0.04f), A(Color.white, 0.85f));
                    break;
                case ItemKind.Money:
                    c.Fill(Circle(0.5f, 0.5f, 0.38f), Col("#F2C66D"), Col("#B8873E"), w);
                    c.Fill(Ring(0.5f, 0.5f, 0.3f, 0.012f), Col("#D9A54B"));
                    c.Fill(Ellipse(0.5f, 0.42f, 0.1f, 0.08f), Col("#B8873E"));
                    c.Fill(Circle(0.36f, 0.54f, 0.04f), Col("#B8873E"));
                    c.Fill(Circle(0.45f, 0.62f, 0.04f), Col("#B8873E"));
                    c.Fill(Circle(0.55f, 0.62f, 0.04f), Col("#B8873E"));
                    c.Fill(Circle(0.64f, 0.54f, 0.04f), Col("#B8873E"));
                    break;
                default:
                    c.Fill(Circle(0.5f, 0.5f, 0.3f), Color.gray, Line, w);
                    break;
            }
            return c;
        }

        // ───────────────────────── 집 (벽·창문·하늘) ─────────────────────────
        // 벽 패널과 창문 층은 모두 같은 1×1 캤버스(아래 가운데 피벗)라서 같은 자리에 겹쳐 놓기만 하면 된다.
        // 창문 유리(하늘)는 x 0.16~0.84, y 0.36~0.84.

        public static Sprite WhitePixel() => Cached("white_px", () =>
        {
            var c = new SdfCanvas(8);
            c.Fill(RoundBox(0.5f, 0.5f, 0.6f, 0.6f, 0f), Color.white);
            return c.ToSprite("white", 1f, new Vector2(0.5f, 0.5f));
        });

        public static Sprite WallPanel() => Cached("wall_panel", () =>
        {
            var c = new SdfCanvas(128);
            c.Fill(RoundBox(0.5f, 0.5f, 0.52f, 0.52f, 0f), Col("#EFE2CF"));
            for (int i = 0; i < 8; i++) c.Fill(RoundBox(0.0625f + i * 0.125f, 0.55f, 0.018f, 0.4f, 0f), Col("#E7D6BE"));
            c.Fill(RoundBox(0.5f, 0.97f, 0.52f, 0.03f, 0f), Col("#D9C2A3"));
            c.Fill(RoundBox(0.5f, 0.05f, 0.52f, 0.05f, 0f), Col("#B98A62"));
            c.Fill(RoundBox(0.5f, 0.105f, 0.52f, 0.008f, 0f), Col("#9C7350"));
            return c.ToSprite("wall", 1f, new Vector2(0.5f, 0f));
        });

        public static Sprite WindowGlass() => Cached("win_glass", () =>
        {
            var c = new SdfCanvas(128);
            c.Fill(RoundBox(0.5f, 0.6f, 0.34f, 0.24f, 0.03f), Color.white);
            return c.ToSprite("glass", 1f, new Vector2(0.5f, 0f));
        });

        public static Sprite WindowFrame() => Cached("win_frame", () =>
        {
            var c = new SdfCanvas(192);
            var wood = Col("#C09068");
            c.Fill(Subtract(RoundBox(0.5f, 0.6f, 0.39f, 0.29f, 0.05f), RoundBox(0.5f, 0.6f, 0.34f, 0.24f, 0.03f)), wood, Line, 0.012f);
            c.Fill(RoundBox(0.5f, 0.6f, 0.012f, 0.24f, 0f), wood);
            c.Fill(RoundBox(0.5f, 0.6f, 0.34f, 0.012f, 0f), wood);
            c.Fill(RoundBox(0.5f, 0.3f, 0.44f, 0.03f, 0.015f), Col("#B07E58"), Line, 0.01f);
            // 유리 반사
            c.Fill(Capsule(0.24f, 0.74f, 0.32f, 0.8f, 0.012f), A(Color.white, 0.35f));
            return c.ToSprite("frame", 1f, new Vector2(0.5f, 0f));
        });

        public static Sprite Stars() => Cached("win_stars", () =>
        {
            var c = new SdfCanvas(128);
            var pts = new[] { new Vector2(0.24f, 0.78f), new Vector2(0.4f, 0.7f), new Vector2(0.62f, 0.8f), new Vector2(0.75f, 0.66f), new Vector2(0.3f, 0.5f), new Vector2(0.68f, 0.46f), new Vector2(0.52f, 0.55f) };
            for (int i = 0; i < pts.Length; i++) c.Fill(Star(pts[i].x, pts[i].y, 0.018f + (i % 3) * 0.006f, 4, 0.4f, 0.002f), Col("#FFF6D0"));
            return c.ToSprite("stars", 1f, new Vector2(0.5f, 0f));
        });

        public static Sprite Disk() => Cached("disk", () =>
        {
            var c = new SdfCanvas(64);
            c.Fill(Circle(0.5f, 0.5f, 0.42f), Color.white);
            return c.ToSprite("disk", 1f, new Vector2(0.5f, 0.5f));
        });

        public static Sprite Moon() => Cached("moon", () =>
        {
            var c = new SdfCanvas(64);
            c.Fill(Subtract(Circle(0.5f, 0.5f, 0.42f), Circle(0.68f, 0.62f, 0.36f)), Col("#FFF3C4"));
            return c.ToSprite("moon", 1f, new Vector2(0.5f, 0.5f));
        });

        /// <summary>길냥이 발자국. 가운데 피벗, 월드 폭 1.</summary>
        public static Sprite PawPrint() => Cached("paw", () =>
        {
            var c = new SdfCanvas(64);
            var col = A(Col("#5E6B4E"), 0.85f);
            c.Fill(Ellipse(0.5f, 0.38f, 0.2f, 0.16f), col);
            c.Fill(Circle(0.27f, 0.6f, 0.08f), col);
            c.Fill(Circle(0.42f, 0.72f, 0.08f), col);
            c.Fill(Circle(0.58f, 0.72f, 0.08f), col);
            c.Fill(Circle(0.73f, 0.6f, 0.08f), col);
            return c.ToSprite("paw", 1f, new Vector2(0.5f, 0.5f));
        });

        /// <summary>머리 위 말풍선 (느낌표·물음표는 글자로 올린다). 아래 꼬리 피벗.</summary>
        public static Sprite Bubble() => Cached("bubble", () =>
        {
            var c = new SdfCanvas(96);
            c.Fill(Union(Circle(0.5f, 0.58f, 0.34f), Triangle(new Vector2(0.4f, 0.32f), new Vector2(0.5f, 0.12f), new Vector2(0.6f, 0.32f), 0.02f)), Cream, Line, 0.03f);
            return c.ToSprite("bubble", 1f, new Vector2(0.5f, 0.12f));
        });

        /// <summary>둥근 사각형 패널 배경 (IMGUI 용, 9-slice 없이 늘려 쓴다).</summary>
        public static Texture2D Panel(Color fill, Color border) =>
            Cached($"panel_{ColorUtility.ToHtmlStringRGBA(fill)}_{ColorUtility.ToHtmlStringRGBA(border)}", () =>
            {
                var c = new SdfCanvas(64);
                c.Fill(RoundBox(0.5f, 0.5f, 0.46f, 0.46f, 0.2f), fill, border, 0.03f);
                return c.ToSprite("panel", 1f, new Vector2(0.5f, 0.5f));
            }).texture;
    }
}
