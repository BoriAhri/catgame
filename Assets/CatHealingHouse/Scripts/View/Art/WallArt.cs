using System.Collections.Generic;
using UnityEngine;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View.Art
{
    /// <summary>
    /// 벽 가구 그림과 "고양이가 앉는 자리" 위치. 그림과 자리를 한곳에서 정해서 서로 어긋나지 않게 한다.
    /// 모든 그림은 정사각 캔버스, 아래 가운데 피벗, 월드 폭 1. 크기·높이는 칸 크기(size) 단위.
    /// </summary>
    public static class WallArt
    {
        public sealed class Spec
        {
            public float Width;          // 그림 폭 (size 단위)
            public float Lift;           // 벽 밑동에서 그림 바닥까지 높이 (size 단위)
            public Vector2[] Spots;      // 고양이 자리 (캔버스 0~1 좌표). 슬롯 번호 순.
            public bool FollowSlope;     // 사선 벽에서 벽 기울기를 따라 기울일지 (가로로 긴 것)
        }

        private static readonly Color Line = CuteArt.Line;
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // 그림은 자기 벽 자리(칸 폭) 안에만 들어가게 한다: 옆 자리의 창문·다른 벽 가구와 겹치지 않도록.
        public static Spec SpecOf(string id)
        {
            switch (id)
            {
                case "wall_steps":
                    return new Spec { Width = 4.2f, Lift = 0.1f, Spots = new[] { new Vector2(0.4f, 0.26f), new Vector2(0.6f, 0.54f), new Vector2(0.4f, 0.82f) } };
                case "wall_hammock":
                    return new Spec { Width = 2.2f, Lift = 1.2f, Spots = new[] { new Vector2(0.5f, 0.42f) } };
                case "wall_catwalk":
                    return new Spec { Width = 3.6f, Lift = 2.55f, FollowSlope = true, Spots = new[] { new Vector2(0.24f, 0.17f), new Vector2(0.5f, 0.17f), new Vector2(0.76f, 0.17f) } };
                case "wall_box":
                    return new Spec { Width = 2.2f, Lift = 1.5f, Spots = new[] { new Vector2(0.5f, 0.38f) } };
                default:
                    return new Spec { Width = 1.5f, Lift = 1.5f, Spots = new[] { new Vector2(0.5f, 0.5f) } };
            }
        }

        public static Sprite SpriteOf(string id)
        {
            if (_cache.TryGetValue(id, out var s) && s != null) return s;
            s = Draw(id);
            _cache[id] = s;
            return s;
        }

        private static Sprite Draw(string id)
        {
            var c = new SdfCanvas(256);
            float w = 0.012f;
            var wood = Col("#D29A62");
            var woodTop = Col("#E8B884");
            var bracket = Col("#9E7250");
            var shadow = new Color(0.35f, 0.25f, 0.2f, 0.18f);
            switch (id)
            {
                case "wall_steps":
                {
                    Vector2[] at = { new Vector2(0.4f, 0.24f), new Vector2(0.6f, 0.52f), new Vector2(0.4f, 0.8f) };
                    foreach (var p in at)
                    {
                        c.Fill(RoundBox(p.x + 0.01f, p.y - 0.045f, 0.095f, 0.02f, 0.02f), shadow);        // 벽에 지는 그림자
                        c.Fill(Triangle(new Vector2(p.x - 0.07f, p.y - 0.02f), new Vector2(p.x - 0.025f, p.y - 0.02f), new Vector2(p.x - 0.07f, p.y - 0.08f), 0.006f), bracket, Line, w * 0.7f);
                        c.Fill(Triangle(new Vector2(p.x + 0.07f, p.y - 0.02f), new Vector2(p.x + 0.025f, p.y - 0.02f), new Vector2(p.x + 0.07f, p.y - 0.08f), 0.006f), bracket, Line, w * 0.7f);
                        c.Fill(RoundBox(p.x, p.y, 0.1f, 0.025f, 0.015f), wood, Line, w);
                        c.Fill(RoundBox(p.x, p.y + 0.013f, 0.085f, 0.008f, 0.006f), woodTop);
                        c.Fill(RoundBox(p.x, p.y + 0.03f, 0.08f, 0.012f, 0.012f), Col("#F3D7C6"), Line, w * 0.7f); // 펠트 방석
                    }
                    break;
                }
                case "wall_hammock":
                {
                    c.Fill(RoundBox(0.5f, 0.3f, 0.36f, 0.025f, 0.01f), shadow);
                    // 양쪽 걸이
                    c.Fill(RoundBox(0.16f, 0.5f, 0.03f, 0.18f, 0.015f), bracket, Line, w);
                    c.Fill(RoundBox(0.84f, 0.5f, 0.03f, 0.18f, 0.015f), bracket, Line, w);
                    c.Fill(RoundBox(0.5f, 0.66f, 0.37f, 0.025f, 0.012f), wood, Line, w);
                    // 천 (아래로 처진 반달)
                    var cloth = Intersect(Ellipse(0.5f, 0.5f, 0.32f, 0.16f), RoundBox(0.5f, 0.42f, 0.4f, 0.08f, 0f));
                    c.Fill(cloth, Col("#9CC7E8"), Line, w);
                    c.Fill(Intersect(Ellipse(0.5f, 0.5f, 0.27f, 0.11f), RoundBox(0.5f, 0.44f, 0.4f, 0.06f, 0f)), Col("#B9DAF2"));
                    for (int i = 0; i < 4; i++) c.Fill(Circle(0.33f + i * 0.11f, 0.4f, 0.012f), Col("#FFF8EA"));
                    c.Fill(Capsule(0.18f, 0.5f, 0.2f, 0.62f, 0.008f), Line);
                    c.Fill(Capsule(0.82f, 0.5f, 0.8f, 0.62f, 0.008f), Line);
                    break;
                }
                case "wall_catwalk":
                {
                    c.Fill(RoundBox(0.5f, 0.1f, 0.44f, 0.02f, 0.01f), shadow);
                    foreach (float bx in new[] { 0.14f, 0.5f, 0.86f })
                        c.Fill(Triangle(new Vector2(bx - 0.035f, 0.135f), new Vector2(bx + 0.035f, 0.135f), new Vector2(bx, 0.04f), 0.006f), bracket, Line, w * 0.7f);
                    c.Fill(RoundBox(0.5f, 0.15f, 0.46f, 0.022f, 0.012f), wood, Line, w);
                    c.Fill(RoundBox(0.5f, 0.162f, 0.44f, 0.006f, 0.004f), woodTop);
                    // 작은 난간 기둥과 끈
                    for (int i = 0; i < 5; i++) c.Fill(Circle(0.1f + i * 0.2f, 0.15f, 0.01f), Col("#B5824F"));
                    break;
                }
                case "wall_box":
                {
                    c.Fill(RoundBox(0.52f, 0.2f, 0.3f, 0.03f, 0.02f), shadow);
                    c.Fill(Triangle(new Vector2(0.3f, 0.2f), new Vector2(0.42f, 0.2f), new Vector2(0.3f, 0.08f), 0.008f), bracket, Line, w * 0.8f);
                    c.Fill(Triangle(new Vector2(0.7f, 0.2f), new Vector2(0.58f, 0.2f), new Vector2(0.7f, 0.08f), 0.008f), bracket, Line, w * 0.8f);
                    c.Fill(RoundBox(0.5f, 0.42f, 0.3f, 0.22f, 0.05f), Col("#E6B78A"), Line, w);
                    c.Fill(RoundBox(0.5f, 0.62f, 0.3f, 0.035f, 0.03f), Col("#C98D5C"), Line, w);   // 지붕 테
                    c.Fill(Circle(0.5f, 0.4f, 0.12f), Col("#5E4B3C"));                              // 구멍
                    c.Fill(Heart(0.5f, 0.27f, 0.03f), Col("#F4A9A8"));
                    break;
                }
                default:
                    c.Fill(RoundBox(0.5f, 0.5f, 0.3f, 0.05f, 0.02f), wood, Line, w);
                    break;
            }
            return c.ToSprite("wall_" + id, 1f, new Vector2(0.5f, 0f));
        }
    }
}
