using System;
using System.Collections.Generic;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;

namespace CatHealingHouse.Economy
{
    public enum ShopResult { Ok, NotEnoughMoney, NotEnoughStock, MaxSize, NotForSale, Failed }

    /// <summary>
    /// 5단계 가격표. 수입 기준: 집 고양이 4마리가 트레이로 분당 약 25원.
    /// 작은 가구는 몇 분, 큰 가구·확장은 수십 분~한 시간 모아야 사는 정도로 잡았다.
    /// </summary>
    [Serializable]
    public sealed class ShopTuning
    {
        public long StartingMoney = 300;
        public int StartingKibble = 3;

        public int KibblePackSize = 5;
        public long KibblePackPrice = 40;

        // 확장 비용: 시작 크기에서 한 칸씩 3번 (실내 4→5→6→7, 마당 3→4→5→6)
        public long[] IndoorExpandCost = { 600, 1800, 4500 };
        public long[] OutdoorExpandCost = { 500, 1500, 4000 };

        public readonly Dictionary<string, long> FurniturePrice = new Dictionary<string, long>
        {
            { "wall_steps", 250 }, { "wall_hammock", 300 }, { "wall_box", 350 }, { "wall_catwalk", 500 },
            { "box_1", 60 },
            { "cushion", 90 },
            { "scratcher", 120 },
            { "food_bowl", 120 },
            { "box_2", 130 },
            { "litter_box", 150 },
            { "hiding_house", 200 },
            { "rug_big", 220 },
            { "bookshelf", 260 },
            { "bench", 300 },
            { "bed", 380 },
            { "cat_tower_3", 450 },
            { "cat_wheel", 650 },
            { "cat_tower_5", 950 },
        };
    }

    /// <summary>
    /// 상점과 보관함. 가구는 사면 보관함에 들어가고, 배치하면 보관함에서 빠지고, 철거하면 보관함으로 돌아온다 (환불 없음).
    /// 확장은 반지름 한 칸씩 돈으로 산다. 사료는 마당 밥그릇을 채울 때 하나씩 쓴다.
    /// </summary>
    public sealed class Shop
    {
        public readonly HouseWorld World;
        public readonly HouseEconomy Economy;
        public readonly ShopTuning Tuning;
        private readonly Dictionary<string, int> _storage = new Dictionary<string, int>();
        private readonly int _indoorStart, _outdoorStart; // 상점이 생길 때의 크기 = 확장 비용의 기준

        public event Action Changed;

        public Wallet Wallet => Economy.Wallet;

        public Shop(HouseWorld world, HouseEconomy economy, ShopTuning tuning = null, bool grantStart = true)
        {
            World = world;
            Economy = economy;
            Tuning = tuning ?? new ShopTuning();
            _indoorStart = world.Indoor.Radius;
            _outdoorStart = world.Outdoor.Radius;
            if (grantStart)
            {
                Wallet.Add(ItemKind.Money, Tuning.StartingMoney);
                Wallet.Add(ItemKind.Kibble, Tuning.StartingKibble);
            }
        }

        // ───────────────────────── 가구 ─────────────────────────

        public long PriceOf(FurnitureDef def) => def != null && Tuning.FurniturePrice.TryGetValue(def.Id, out var p) ? p : -1;

        public bool ForSale(FurnitureDef def) => PriceOf(def) >= 0;

        public int Stock(FurnitureDef def) => def != null && _storage.TryGetValue(def.Id, out var n) ? n : 0;

        public IEnumerable<KeyValuePair<string, int>> Storage => _storage;

        public void AddToStorage(FurnitureDef def, int count = 1)
        {
            _storage[def.Id] = Stock(def) + count;
            Changed?.Invoke();
        }

        public ShopResult BuyFurniture(FurnitureDef def)
        {
            long price = PriceOf(def);
            if (price < 0) return ShopResult.NotForSale;
            if (!Wallet.TrySpend(ItemKind.Money, price)) return ShopResult.NotEnoughMoney;
            AddToStorage(def);
            return ShopResult.Ok;
        }

        /// <summary>보관함에서 꺼내 배치. 보관함에 없으면 NotEnoughStock 과 함께 PlaceResult 는 Ok 가 아니다.</summary>
        public PlaceResult PlaceFromStorage(FurnitureDef def, BoardId board, HexCoord origin, bool mirrored, out FurnitureInstance placed, out ShopResult shop)
        {
            placed = null;
            if (Stock(def) <= 0) { shop = ShopResult.NotEnoughStock; return PlaceResult.Occupied; }
            var r = World.TryPlace(def, board, origin, mirrored, out placed);
            shop = r == PlaceResult.Ok ? ShopResult.Ok : ShopResult.Failed;
            if (r == PlaceResult.Ok)
            {
                _storage[def.Id] = Stock(def) - 1;
                Changed?.Invoke();
            }
            return r;
        }

        /// <summary>보관함에 있거나 살 돈이 있어서 지금 놓을 수 있는지.</summary>
        public bool CanAcquire(FurnitureDef def) => Stock(def) > 0 || (ForSale(def) && Wallet.Money >= PriceOf(def));

        /// <summary>
        /// 상점에서 고른 가구를 바로 놓기. 보관함에 있으면 그걸 쓰고(무료), 없으면 놓는 순간 산다.
        /// 못 놓는 칸이면 돈은 빠지지 않는다. paid = 이번에 낸 돈 (보관함에서 꺼냈으면 0).
        /// </summary>
        public PlaceResult BuyOrPlace(FurnitureDef def, BoardId board, HexCoord origin, bool mirrored, out FurnitureInstance placed, out ShopResult shop, out long paid)
        {
            paid = 0;
            if (Stock(def) > 0) return PlaceFromStorage(def, board, origin, mirrored, out placed, out shop);
            placed = null;
            long price = PriceOf(def);
            if (price < 0) { shop = ShopResult.NotForSale; return PlaceResult.Occupied; }
            if (Wallet.Money < price) { shop = ShopResult.NotEnoughMoney; return PlaceResult.Occupied; }
            var r = World.TryPlace(def, board, origin, mirrored, out placed);
            if (r != PlaceResult.Ok) { shop = ShopResult.Failed; return r; }
            Wallet.TrySpend(ItemKind.Money, price);
            paid = price;
            shop = ShopResult.Ok;
            Changed?.Invoke();
            return r;
        }

        /// <summary>철거: 가구를 치우고 보관함으로 돌려놓는다.</summary>
        public bool Store(int furnitureId)
        {
            var f = World.GetFurniture(furnitureId);
            if (f == null) return false;
            Economy.ForgetFurniture(furnitureId);
            if (!World.Remove(furnitureId)) return false;
            AddToStorage(f.Def);
            return true;
        }

        // ───────────────────────── 확장 ─────────────────────────

        private long[] CostsFor(BoardId id) => id == BoardId.Indoor ? Tuning.IndoorExpandCost : Tuning.OutdoorExpandCost;

        public int MaxRadius(BoardId id) => (id == BoardId.Indoor ? _indoorStart : _outdoorStart) + CostsFor(id).Length;

        /// <summary>다음 확장 비용. 더 넓힐 수 없으면 -1.</summary>
        public long ExpandCost(BoardId id)
        {
            var board = World.GetBoard(id);
            int idx = board.Radius - (id == BoardId.Indoor ? _indoorStart : _outdoorStart);
            var costs = CostsFor(id);
            if (idx < 0) idx = 0;
            if (idx >= costs.Length || !board.CanExpand) return -1;
            return costs[idx];
        }

        public ShopResult BuyExpansion(BoardId id)
        {
            long cost = ExpandCost(id);
            if (cost < 0) return ShopResult.MaxSize;
            if (!Wallet.TrySpend(ItemKind.Money, cost)) return ShopResult.NotEnoughMoney;
            if (!World.Expand(id))
            {
                Wallet.Add(ItemKind.Money, cost);
                return ShopResult.Failed;
            }
            // 넓히면 벽이 바깥으로 밀려나므로 벽 가구는 새 벽의 가장 가까운 자리로 옮겨 건다. 자리가 없으면 보관함으로.
            foreach (var d in World.LastDisplaced) AddToStorage(d);
            Changed?.Invoke();
            return ShopResult.Ok;
        }

        // ───────────────────────── 사료 ─────────────────────────

        public ShopResult BuyKibble()
        {
            if (!Wallet.TrySpend(ItemKind.Money, Tuning.KibblePackPrice)) return ShopResult.NotEnoughMoney;
            Wallet.Add(ItemKind.Kibble, Tuning.KibblePackSize);
            Changed?.Invoke();
            return ShopResult.Ok;
        }

        /// <summary>사료 하나로 마당 밥그릇을 채운다. 이미 가득이면 사료를 쓰지 않는다.</summary>
        public ShopResult FillBowl(StraySystem strays, int bowlId)
        {
            if (Wallet.Kibble <= 0) return ShopResult.NotEnoughStock;
            if (!strays.FillBowl(bowlId)) return ShopResult.Failed;
            Wallet.Add(ItemKind.Kibble, -1);
            Changed?.Invoke();
            return ShopResult.Ok;
        }
    }
}
