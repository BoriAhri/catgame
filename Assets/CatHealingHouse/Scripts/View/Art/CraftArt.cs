using System.Collections.Generic;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 공방 그림: 수제 장난감(가구 그림 규칙: 아래 가운데 피벗, 폭 1)과 코스튬.
    /// 코스튬은 고양이 부위(CatParts)와 같은 256 캔버스 좌표로 그리고 피벗을 붙을 관절에 둬서,
    /// 관절에 매달기만 하면 원래 그림 위 제자리에 맞는다.
    /// </summary>
    public static class CraftArt
    {
        private static readonly Color Line = CuteArt.Line;
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (_cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            _cache[key] = s;
            return s;
        }

        public static bool IsToy(string id) => id == "felt_ball" || id == "mouse_toy";

        public static Sprite Toy(string id) => Cached("toy_" + id, () =>
        {
            var c = new SdfCanvas(192);
            float w = 0.016f;
            var shadow = new Color(0.48f, 0.42f, 0.47f, 0.2f);
            if (id == "felt_ball")
            {
                c.Fill(Ellipse(0.5f, 0.16f, 0.3f, 0.05f), shadow);
                c.Fill(Circle(0.5f, 0.36f, 0.2f), Col("#F2A7B5"), Line, w);
                // 양모 줄무늬
                c.Fill(Intersect(Circle(0.5f, 0.36f, 0.2f), Capsule(0.3f, 0.3f, 0.7f, 0.44f, 0.035f)), Col("#FFF1C9"));
                c.Fill(Intersect(Circle(0.5f, 0.36f, 0.2f), Capsule(0.36f, 0.5f, 0.66f, 0.22f, 0.03f)), Col("#9CC7E8"));
                c.Fill(Circle(0.43f, 0.44f, 0.035f), new Color(1, 1, 1, 0.55f));
                // 굴러다니는 작은 공
                c.Fill(Circle(0.8f, 0.2f, 0.07f), Col("#BFE3B4"), Line, w * 0.8f);
            }
            else
            {
                c.Fill(Ellipse(0.5f, 0.15f, 0.36f, 0.05f), shadow);
                // 꼬리
                c.Fill(Capsule(0.24f, 0.22f, 0.12f, 0.3f, 0.012f), Line);
                // 몸
                c.Fill(Ellipse(0.48f, 0.27f, 0.24f, 0.13f), Col("#C9C3BE"), Line, w);
                c.Fill(Ellipse(0.46f, 0.31f, 0.16f, 0.06f), Col("#DCD7D2"));
                // 귀
                c.Fill(Circle(0.62f, 0.4f, 0.06f), Col("#F4A9A8"), Line, w * 0.8f);
                c.Fill(Circle(0.52f, 0.41f, 0.055f), Col("#F4A9A8"), Line, w * 0.8f);
                // 코·눈·수염
                c.Fill(Circle(0.73f, 0.27f, 0.025f), Col("#E58A8A"));
                c.Fill(Circle(0.64f, 0.3f, 0.018f), Col("#3A2E28"));
                c.Fill(Capsule(0.72f, 0.24f, 0.84f, 0.22f, 0.005f), Line);
                c.Fill(Capsule(0.72f, 0.26f, 0.85f, 0.28f, 0.005f), Line);
            }
            return c.ToSprite("toy_" + id, 1f, new Vector2(0.5f, 0.1f));
        });

        /// <summary>코스튬이 붙는 고양이 부위 (CatPart 이름).</summary>
        public static CatPart PartOf(string costumeId) => costumeId == "beanie" ? CatPart.Head : CatPart.Body;

        /// <summary>그리는 순서: 리본은 몸·다리 위 귀 아래, 모자는 머리·눈 위, 망토는 몸 바로 위.</summary>
        public static int OrderOf(string costumeId) => costumeId == "beanie" ? 20 : costumeId == "ribbon" ? 7 : 5;

        public static Sprite Costume(string id) => Cached("costume_" + id, () =>
        {
            var c = new SdfCanvas(256);
            float w = 0.008f;
            var joint = CatParts.Joint[PartOf(id)];
            switch (id)
            {
                case "ribbon":
                {
                    var red = Col("#E86A7A");
                                        c.Fill(Triangle(new Vector2(0.58f, 0.34f), new Vector2(0.46f, 0.405f), new Vector2(0.46f, 0.275f), 0.015f), red, Line, w);
                    c.Fill(Triangle(new Vector2(0.58f, 0.34f), new Vector2(0.7f, 0.405f), new Vector2(0.7f, 0.275f), 0.015f), red, Line, w);
                    c.Fill(Circle(0.58f, 0.34f, 0.032f), Col("#F7A2AE"), Line, w);
                    break;
                }
                case "beanie":
                {
                    var wool = Col("#7FB5DD");
                    var band = Col("#F3EEE4");
                    var cap = Intersect(Ellipse(0.645f, 0.64f, 0.19f, 0.13f), RoundBox(0.645f, 0.72f, 0.25f, 0.09f, 0f));
                    c.Fill(cap, wool, Line, w);
                    for (int i = 0; i < 4; i++) c.Fill(Intersect(cap, RoundBox(0.53f + i * 0.075f, 0.7f, 0.008f, 0.1f, 0f)), Col("#6AA2CB"));
                    c.Fill(RoundBox(0.645f, 0.645f, 0.2f, 0.03f, 0.03f), band, Line, w);
                    c.Fill(Circle(0.645f, 0.79f, 0.045f), band, Line, w);                                   // 방울
                    break;
                }
                default: // cape
                {
                    var cape = Col("#B58AD6");
                    c.Fill(Union(Ellipse(0.42f, 0.36f, 0.2f, 0.11f), Triangle(new Vector2(0.22f, 0.3f), new Vector2(0.58f, 0.38f), new Vector2(0.26f, 0.2f), 0.02f)), cape, Line, w);
                    c.Fill(Ellipse(0.44f, 0.4f, 0.13f, 0.04f), Col("#C9A6E4"));
                    for (int i = 0; i < 3; i++) c.Fill(Star(0.3f + i * 0.1f, 0.3f + (i % 2) * 0.04f, 0.018f, 5, 0.45f, 0.002f), Col("#FFE38A"));
                    c.Fill(Circle(0.58f, 0.37f, 0.02f), Col("#FFE38A"), Line, w);                           // 단추
                    break;
                }
            }
            return c.ToSprite("costume_" + id, 1f, joint);
        });

        /// <summary>코스튬 아이콘 (UI용): 그림의 해당 부분만 크게.</summary>
        public static Sprite CostumeIcon(string id) => Cached("costume_icon_" + id, () =>
        {
            var c = new SdfCanvas(128);
            float w = 0.02f;
            switch (id)
            {
                case "ribbon":
                    c.Fill(Triangle(new Vector2(0.5f, 0.5f), new Vector2(0.18f, 0.72f), new Vector2(0.18f, 0.28f), 0.03f), Col("#E86A7A"), Line, w);
                    c.Fill(Triangle(new Vector2(0.5f, 0.5f), new Vector2(0.82f, 0.72f), new Vector2(0.82f, 0.28f), 0.03f), Col("#E86A7A"), Line, w);
                    c.Fill(Circle(0.5f, 0.5f, 0.09f), Col("#F7A2AE"), Line, w);
                    break;
                case "beanie":
                    c.Fill(Intersect(Ellipse(0.5f, 0.38f, 0.36f, 0.36f), RoundBox(0.5f, 0.6f, 0.5f, 0.24f, 0f)), Col("#7FB5DD"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.38f, 0.38f, 0.07f, 0.06f), Col("#F3EEE4"), Line, w);
                    c.Fill(Circle(0.5f, 0.8f, 0.09f), Col("#F3EEE4"), Line, w);
                    break;
                default:
                    c.Fill(Union(Ellipse(0.5f, 0.66f, 0.32f, 0.14f), Triangle(new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.62f), new Vector2(0.5f, 0.12f), 0.04f)), Col("#B58AD6"), Line, w);
                    c.Fill(Star(0.5f, 0.42f, 0.07f, 5, 0.45f, 0.01f), Col("#FFE38A"));
                    break;
            }
            return c.ToSprite("costume_icon_" + id, 1f, new Vector2(0.5f, 0.5f));
        });
    }
}
