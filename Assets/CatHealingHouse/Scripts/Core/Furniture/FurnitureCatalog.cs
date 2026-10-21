using System.Collections.Generic;
using CatHealingHouse.Hex;

namespace CatHealingHouse.Furniture
{
    /// <summary>
    /// 1단계용 임시 가구 목록. 나중에 ScriptableObject 데이터로 옮긴다.
    /// 풋프린트 좌표는 기준 칸 (0,0) 에서의 상대 좌표.
    /// </summary>
    public static class FurnitureCatalog
    {
        private static FootprintCell E(int q, int r) => new FootprintCell(q, r, CellRole.Entry);
        private static FootprintCell B(int q, int r) => new FootprintCell(q, r, CellRole.Blocked);
        private static FootprintCell P(int q, int r) => new FootprintCell(q, r, CellRole.Passable);

        public static readonly FurnitureDef FoodBowl = new FurnitureDef("food_bowl", "밥그릇", 1,
            NeedType.Hunger, InteractionType.InPlace, ZoneMask.Both, E(0, 0));

        public static readonly FurnitureDef LitterBox = new FurnitureDef("litter_box", "화장실", 1,
            NeedType.Bladder, InteractionType.InPlace, ZoneMask.Indoor, E(0, 0));

        public static readonly FurnitureDef Scratcher = new FurnitureDef("scratcher", "스크래처", 1,
            NeedType.Fun, InteractionType.InPlace, ZoneMask.Indoor, E(0, 0));

        public static readonly FurnitureDef Cushion = new FurnitureDef("cushion", "방석", 1,
            NeedType.Energy, InteractionType.Climb, ZoneMask.Indoor, E(0, 0));

        public static readonly FurnitureDef BoxSmall = new FurnitureDef("box_1", "1인 박스", 1,
            NeedType.Energy, InteractionType.Enter, ZoneMask.Both, E(0, 0));

        public static readonly FurnitureDef BoxDouble = new FurnitureDef("box_2", "2인 박스", 2,
            NeedType.Energy, InteractionType.Enter, ZoneMask.Both, E(0, 0), E(1, 0));

        public static readonly FurnitureDef HidingHouse = new FurnitureDef("hiding_house", "숨숨집", 1,
            NeedType.Energy, InteractionType.Enter, ZoneMask.Indoor, E(0, 0));

        public static readonly FurnitureDef CatTower3 = new FurnitureDef("cat_tower_3", "3인 캣타워", 3,
            NeedType.Fun, InteractionType.Climb, ZoneMask.Indoor, B(0, 0), E(1, 0));

        public static readonly FurnitureDef CatTower5 = new FurnitureDef("cat_tower_5", "5인 캣타워", 5,
            NeedType.Fun, InteractionType.Climb, ZoneMask.Indoor, B(0, 0), B(1, 0), E(0, 1));

        public static readonly FurnitureDef Bed = new FurnitureDef("bed", "침대", 2,
            NeedType.Energy, InteractionType.Climb, ZoneMask.Indoor, B(0, 0), B(1, 0), E(0, 1));

        public static readonly FurnitureDef CatWheel = new FurnitureDef("cat_wheel", "캣휠", 1,
            NeedType.Fun, InteractionType.Run, ZoneMask.Indoor, B(0, 0), E(1, 0));

        public static readonly FurnitureDef Bookshelf = new FurnitureDef("bookshelf", "책장", 0,
            NeedType.None, InteractionType.None, ZoneMask.Indoor, B(-1, 0), B(0, 0), B(1, 0));

        public static readonly FurnitureDef BigRug = new FurnitureDef("rug_big", "큰 러그", 0,
            NeedType.None, InteractionType.None, ZoneMask.Indoor,
            P(0, 0), P(1, 0), P(1, -1), P(0, -1), P(-1, 0), P(-1, 1), P(0, 1));

        public static readonly FurnitureDef Bench = new FurnitureDef("bench", "벤치", 2,
            NeedType.Energy, InteractionType.Enter, ZoneMask.Outdoor, E(0, 0), E(1, 0));

        // ── 벽 가구 (실내 위쪽 벽: 뒷벽 + 양옆 사선 벽). 아래 바닥 한 칸이 올라가는 입구.
        public static readonly FurnitureDef WallSteps = new FurnitureDef("wall_steps", "벽 스텝", 3,
            NeedType.Fun, InteractionType.Climb, wallWidth: 1);

        public static readonly FurnitureDef WallHammock = new FurnitureDef("wall_hammock", "벽 해먹 선반", 1,
            NeedType.Energy, InteractionType.Climb, wallWidth: 1);

        public static readonly FurnitureDef WallCatwalk = new FurnitureDef("wall_catwalk", "벽 캣워크", 3,
            NeedType.Fun, InteractionType.Climb, wallWidth: 2);

        public static readonly FurnitureDef WallBox = new FurnitureDef("wall_box", "벽 숨숨집", 1,
            NeedType.Energy, InteractionType.Enter, wallWidth: 1);

        // ── 공방 장난감 (상점에서 안 팔고 공방에서 털뭉치·수염으로 만든다)
        public static readonly FurnitureDef FeltBall = new FurnitureDef("felt_ball", "양모펠트 공", 1,
            NeedType.Fun, InteractionType.InPlace, ZoneMask.Indoor, E(0, 0)).WithPower(1.8);

        public static readonly FurnitureDef MouseToy = new FurnitureDef("mouse_toy", "쥐돌이", 1,
            NeedType.Fun, InteractionType.InPlace, ZoneMask.Indoor, E(0, 0)).WithPower(2.5);

        /// <summary>공방에서 만드는 가구 (상점 목록에는 보관함에 있을 때만 보인다).</summary>
        public static readonly IReadOnlyList<FurnitureDef> Crafted = new[] { FeltBall, MouseToy };

        public static readonly IReadOnlyList<FurnitureDef> All = new[]
        {
            FoodBowl, LitterBox, Scratcher, Cushion, BoxSmall, BoxDouble, HidingHouse,
            CatTower3, CatTower5, Bed, CatWheel, Bookshelf, BigRug, Bench,
            WallSteps, WallHammock, WallCatwalk, WallBox,
        };
    
        /// <summary>ID 로 정의 찾기 (상점 가구 + 공방 가구).</summary>
        public static FurnitureDef ById(string id)
        {
            foreach (var d in All) if (d.Id == id) return d;
            foreach (var d in Crafted) if (d.Id == id) return d;
            return null;
        }
    }
}
