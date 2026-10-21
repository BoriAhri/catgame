using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Economy
{
    public enum RecipeKind { Toy, Costume }

    /// <summary>공방 레시피: 털뭉치·수염으로 만든다. 장난감은 가구(보관함으로), 코스튬은 옷장으로.</summary>
    public sealed class Recipe
    {
        public string Id;
        public string Name;
        public string Desc;
        public RecipeKind Kind;
        public int Furballs;
        public int Whiskers;
        public FurnitureDef Furniture;   // 장난감
        public string CostumeId;         // 코스튬
    }

    /// <summary>고양이에게 입히는 코스튬 종류.</summary>
    public static class Costumes
    {
        public const string Ribbon = "ribbon";
        public const string Beanie = "beanie";
        public const string Cape = "cape";

        public static readonly string[] All = { Ribbon, Beanie, Cape };

        public static string NameOf(string id)
        {
            switch (id)
            {
                case Ribbon: return "리본";
                case Beanie: return "털모자";
                case Cape: return "망토";
                default: return "";
            }
        }
    }

    public enum CraftResult { Ok, NotEnoughFurballs, NotEnoughWhiskers, Unknown }

    /// <summary>
    /// 공방. 상점에서 살 수 없는 것만 만든다: 수제 장난감(즐거움 회복이 큰 가구), 코스튬(외형).
    /// 재료 개수는 초안 (밸런싱 단계에서 표로 조정).
    /// </summary>
    public sealed class Workshop
    {
        public readonly Wallet Wallet;
        public readonly Shop Shop;
        public readonly List<Recipe> Recipes = new List<Recipe>();
        private readonly Dictionary<string, int> _closet = new Dictionary<string, int>();

        public event Action Changed;

        public Workshop(Wallet wallet, Shop shop)
        {
            Wallet = wallet;
            Shop = shop;
            Recipes.Add(new Recipe { Id = "felt_ball", Name = "양모펠트 공", Desc = "즐거움이 빨리 차는 장난감", Kind = RecipeKind.Toy, Furballs = 5, Furniture = FurnitureCatalog.FeltBall });
            Recipes.Add(new Recipe { Id = "mouse_toy", Name = "쥐돌이", Desc = "즐거움이 아주 빨리 차는 장난감", Kind = RecipeKind.Toy, Furballs = 8, Whiskers = 1, Furniture = FurnitureCatalog.MouseToy });
            Recipes.Add(new Recipe { Id = "ribbon", Name = "리본", Desc = "목에 다는 리본", Kind = RecipeKind.Costume, Furballs = 6, Whiskers = 1, CostumeId = Costumes.Ribbon });
            Recipes.Add(new Recipe { Id = "beanie", Name = "털모자", Desc = "방울 달린 털모자", Kind = RecipeKind.Costume, Furballs = 10, Whiskers = 2, CostumeId = Costumes.Beanie });
            Recipes.Add(new Recipe { Id = "cape", Name = "망토", Desc = "등에 두르는 망토", Kind = RecipeKind.Costume, Furballs = 14, Whiskers = 3, CostumeId = Costumes.Cape });
        }

        public Recipe Get(string id) => Recipes.Find(r => r.Id == id);

        public CraftResult Check(Recipe r)
        {
            if (r == null) return CraftResult.Unknown;
            if (Wallet.Furballs < r.Furballs) return CraftResult.NotEnoughFurballs;
            if (Wallet.Whiskers < r.Whiskers) return CraftResult.NotEnoughWhiskers;
            return CraftResult.Ok;
        }

        public CraftResult Craft(Recipe r)
        {
            var c = Check(r);
            if (c != CraftResult.Ok) return c;
            Wallet.Add(ItemKind.Furball, -r.Furballs);
            if (r.Whiskers > 0) Wallet.Add(ItemKind.Whisker, -r.Whiskers);
            if (r.Kind == RecipeKind.Toy) Shop.AddToStorage(r.Furniture);
            else _closet[r.CostumeId] = Closet(r.CostumeId) + 1;
            Changed?.Invoke();
            return CraftResult.Ok;
        }

        /// <summary>옷장에 남은(아무도 안 입은) 코스튬 개수.</summary>
        public int Closet(string costumeId) => costumeId != null && _closet.TryGetValue(costumeId, out var n) ? n : 0;

        /// <summary>코스튬을 입힌다. 입고 있던 것은 옷장으로 돌아간다.</summary>
        public bool Wear(Cat cat, string costumeId)
        {
            if (cat == null || costumeId == null) return false;
            if (cat.Costume == costumeId) return true;
            if (Closet(costumeId) <= 0) return false;
            _closet[costumeId] = Closet(costumeId) - 1;
            if (cat.Costume != null) _closet[cat.Costume] = Closet(cat.Costume) + 1;
            cat.Costume = costumeId;
            Changed?.Invoke();
            return true;
        }

        public void TakeOff(Cat cat)
        {
            if (cat?.Costume == null) return;
            _closet[cat.Costume] = Closet(cat.Costume) + 1;
            cat.Costume = null;
            Changed?.Invoke();
        }
    }
}
