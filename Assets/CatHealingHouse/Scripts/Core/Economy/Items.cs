using System;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Economy
{
    /// <summary>게임에 등장하는 결과물과 재화 아이콘 종류.</summary>
    public enum ItemKind
    {
        None,
        // 트레이 결과물 (탭하면 돈)
        FishBone,     // 밥그릇
        Cloud,        // 캣타워
        BellBall,     // 장난감
        Star,         // 캣휠
        CottonBall,   // 숨숨집·박스
        DreamBubble,  // 침대·방석·벤치
        ClawShell,    // 스크래처
        Potato,       // 화장실 청소 (소변)
        Mattdongsan,  // 화장실 청소 (대변)
        // 바닥 드롭 (공방 재료)
        Furball,
        Whisker,
        // 길냥이 선물 (주우면 젤리 하트 2개)
        Gift,
        // 재화
        Heart,
        Money,
        // 소모품 (상점)
        Kibble,       // 사료: 마당 밥그릇을 한 번 채울 때 하나
    }

    /// <summary>가구 → 결과물, 결과물 → 값. 밸런싱 표.</summary>
    public static class RewardTable
    {
        /// <summary>가구를 쓰면 트레이로 날아가는 결과물. 화장실은 None (청소할 때만 나온다).</summary>
        public static ItemKind TrayItemFor(FurnitureDef def)
        {
            if (def == null) return ItemKind.None;
            switch (def.Id)
            {
                case "food_bowl": return ItemKind.FishBone;
                case "litter_box": return ItemKind.None;
                case "scratcher": return ItemKind.ClawShell;
                case "cat_wheel": return ItemKind.Star;
                case "cat_tower_3":
                case "cat_tower_5": return ItemKind.Cloud;
                case "hiding_house":
                case "box_1":
                case "box_2": return ItemKind.CottonBall;
                case "bed":
                case "cushion":
                case "bench": return ItemKind.DreamBubble;
            }
            switch (def.Satisfies)
            {
                case NeedType.Hunger: return ItemKind.FishBone;
                case NeedType.Energy: return ItemKind.DreamBubble;
                case NeedType.Fun: return ItemKind.BellBall;
                default: return ItemKind.None;
            }
        }

        public static bool IsLitterBox(FurnitureDef def) => def != null && def.Id == "litter_box";

        public static int UnitValue(ItemKind k)
        {
            switch (k)
            {
                case ItemKind.FishBone: return 5;
                case ItemKind.Cloud: return 8;
                case ItemKind.BellBall: return 6;
                case ItemKind.Star: return 8;
                case ItemKind.CottonBall: return 6;
                case ItemKind.DreamBubble: return 7;
                case ItemKind.ClawShell: return 6;
                case ItemKind.Potato: return 6;
                case ItemKind.Mattdongsan: return 9;
                default: return 0;
            }
        }

        public static string Name(ItemKind k)
        {
            switch (k)
            {
                case ItemKind.FishBone: return "생선 뼈";
                case ItemKind.Cloud: return "구름";
                case ItemKind.BellBall: return "방울공";
                case ItemKind.Star: return "별";
                case ItemKind.CottonBall: return "솜뭉치";
                case ItemKind.DreamBubble: return "꿈방울";
                case ItemKind.ClawShell: return "발톱 껍질";
                case ItemKind.Potato: return "감자";
                case ItemKind.Mattdongsan: return "맛동산";
                case ItemKind.Furball: return "털뭉치";
                case ItemKind.Whisker: return "수염";
                case ItemKind.Gift: return "길냥이의 선물";
                case ItemKind.Heart: return "젤리 하트";
                case ItemKind.Money: return "돈";
                case ItemKind.Kibble: return "사료";
                default: return "";
            }
        }
    }

    /// <summary>플레이어 지갑.</summary>
    [Serializable]
    public sealed class Wallet
    {
        public long Money;
        public long Hearts;
        public long Furballs;
        public long Whiskers;
        public long Kibble;

        public event Action Changed;

        public void Add(ItemKind kind, long amount)
        {
            switch (kind)
            {
                case ItemKind.Money: Money += amount; break;
                case ItemKind.Heart: Hearts += amount; break;
                case ItemKind.Furball: Furballs += amount; break;
                case ItemKind.Whisker: Whiskers += amount; break;
                case ItemKind.Kibble: Kibble += amount; break;
                default: throw new ArgumentException($"{kind} 는 지갑에 넣는 재화가 아닙니다.");
            }
            Changed?.Invoke();
        }

        public long Get(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Money: return Money;
                case ItemKind.Heart: return Hearts;
                case ItemKind.Furball: return Furballs;
                case ItemKind.Whisker: return Whiskers;
                case ItemKind.Kibble: return Kibble;
                default: return 0;
            }
        }

        /// <summary>모자라면 아무것도 빼지 않고 false.</summary>
        public bool TrySpend(ItemKind kind, long amount)
        {
            if (amount < 0 || Get(kind) < amount) return false;
            Add(kind, -amount);
            return true;
        }
    }
}
