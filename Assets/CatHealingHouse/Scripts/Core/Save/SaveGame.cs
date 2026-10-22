using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.World;

namespace CatHealingHouse.Save
{
    // ───────────────────────── 저장 데이터 (JSON 으로 바로 직렬화되는 단순 필드만) ─────────────────────────

    [Serializable] public sealed class FurnitureSave { public int Id; public string Def; public int Board; public int Q, R; public bool Mirrored; public int Pee, Poop, BowlFood; }
    [Serializable] public sealed class TraySave { public int FurnitureId; public int Kind; public string Label; public int Count; }
    [Serializable] public sealed class DropSave { public int Board; public int Q, R; public int Kind; }
    [Serializable] public sealed class CatSave
    {
        public string Name; public string LookKey; public int StrayId; public string Personality; public string Costume;
        public double Hunger, Bladder, Energy, Fun, Mood; public int Board; public int Q, R;
        public double PetFatigue, PetFatigueAgo, PetHeartWait;
        public int IntimacyLevel; public double IntimacyExp; public int SkillPoints, SnackDay, SnacksToday;
        public List<CountSave> CatSkills = new List<CountSave>();
    }
    [Serializable] public sealed class StraySave { public int Id; public double Affection; public int Visits, Meals; public bool Discovered, Adopted; }
    [Serializable] public sealed class CountSave { public string Id; public int Count; }

    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;
        public long SavedAtUtcTicks;
        public double ClockElapsed;
        public long Money, Hearts, Furballs, Whiskers, Kibble;
        public int IndoorRadius, OutdoorRadius;
        public List<FurnitureSave> Furniture = new List<FurnitureSave>();
        public List<TraySave> Tray = new List<TraySave>();
        public List<DropSave> Drops = new List<DropSave>();
        public List<CatSave> Cats = new List<CatSave>();
        public List<StraySave> Strays = new List<StraySave>();
        public List<CountSave> Storage = new List<CountSave>();
        public List<CountSave> Closet = new List<CountSave>();
        public List<CountSave> Skills = new List<CountSave>();
    }

    /// <summary>저장·불러오기에 필요한 것들을 한데 묶는다.</summary>
    public sealed class GameState
    {
        public HouseWorld World;
        public CatSimulation Sim;
        public HouseEconomy Economy;
        public StraySystem Strays;
        public Shop Shop;
        public Workshop Workshop;
        public ButlerSkills Skills;
        public DayClock Clock;
    }

    /// <summary>
    /// 저장(Capture)과 불러오기(Restore). 순수 C# 이라 테스트에서도 그대로 돈다.
    /// 저장 대상(기획서): 지갑, 두 보드의 크기와 가구 배치, 트레이, 화장실·밥그릇 상태, 바닥 드롭, 집 고양이, 길냥이 호감도,
    /// 보관함, 옷장, 집사 스킬, 시계, 마지막 저장 시각. 마당에 와 있던 길냥이와 가구 위 하트는 저장하지 않는다 (잠깐 있는 것).
    /// </summary>
    public static class SaveGame
    {
        public static SaveData Capture(GameState g, DateTime utcNow)
        {
            var d = new SaveData
            {
                SavedAtUtcTicks = utcNow.Ticks,
                ClockElapsed = g.Clock != null ? g.Clock.Elapsed : 0,
                Money = g.Economy.Wallet.Money, Hearts = g.Economy.Wallet.Hearts, Furballs = g.Economy.Wallet.Furballs,
                Whiskers = g.Economy.Wallet.Whiskers, Kibble = g.Economy.Wallet.Kibble,
                IndoorRadius = g.World.Indoor.Radius, OutdoorRadius = g.World.Outdoor.Radius,
            };

            var furn = new List<FurnitureInstance>(g.World.Furniture);
            furn.Sort((a, b) => a.Id.CompareTo(b.Id)); // 놓은 순서대로 (그 순서면 다시 놓아도 항상 된다)
            foreach (var f in furn)
            {
                g.Economy.GetLitter(f.Id, out int pee, out int poop);
                d.Furniture.Add(new FurnitureSave
                {
                    Id = f.Id, Def = f.Def.Id, Board = (int)f.Board, Q = f.Origin.Q, R = f.Origin.R, Mirrored = f.Mirrored,
                    Pee = pee, Poop = poop, BowlFood = g.Strays != null ? g.Strays.FoodIn(f.Id) : 0,
                });
            }
            foreach (var t in g.Economy.Tray)
                d.Tray.Add(new TraySave { FurnitureId = t.FurnitureId, Kind = (int)t.Kind, Label = t.Label, Count = t.Count });
            foreach (var x in g.Economy.Drops)
                d.Drops.Add(new DropSave { Board = (int)x.Cell.Board, Q = x.Cell.Coord.Q, R = x.Cell.Coord.R, Kind = (int)x.Kind });

            foreach (var c in g.Sim.Cats)
            {
                if (c.Affiliation != Affiliation.Resident) continue;
                d.Cats.Add(new CatSave
                {
                    Name = c.Name, LookKey = c.LookKey, StrayId = c.StrayId, Personality = c.Personality?.Id, Costume = c.Costume,
                    Hunger = c.Needs.Hunger, Bladder = c.Needs.Bladder, Energy = c.Needs.Energy, Fun = c.Needs.Fun, Mood = c.Mood,
                    Board = (int)c.Cell.Board, Q = c.Cell.Coord.Q, R = c.Cell.Coord.R,
                    PetFatigue = c.PetFatigue, PetFatigueAgo = g.Sim.SimTime - c.PetFatigueAt,
                    PetHeartWait = Math.Max(0, c.PetHeartAt - g.Sim.SimTime),
                    IntimacyLevel = c.IntimacyLevel, IntimacyExp = c.IntimacyExp, SkillPoints = c.SkillPoints,
                    SnackDay = c.SnackDay, SnacksToday = c.SnacksToday,
                    CatSkills = SkillsOf(c),
                });
            }
            if (g.Strays != null)
                foreach (var p in g.Strays.Profiles)
                    d.Strays.Add(new StraySave { Id = p.Id, Affection = p.Affection, Visits = p.Visits, Meals = p.Meals, Discovered = p.Discovered, Adopted = p.Adopted });

            foreach (var kv in g.Shop.Storage) if (kv.Value > 0) d.Storage.Add(new CountSave { Id = kv.Key, Count = kv.Value });
            if (g.Workshop != null)
                foreach (var id in Costumes.All) { int n = g.Workshop.Closet(id); if (n > 0) d.Closet.Add(new CountSave { Id = id, Count = n }); }
            if (g.Skills != null)
                foreach (var s in g.Skills.Defs) { int lv = g.Skills.Level(s.Id); if (lv > 0) d.Skills.Add(new CountSave { Id = s.Id, Count = lv }); }
            return d;
        }

        /// <summary>
        /// 막 만든(가구도 고양이도 없는) 상태에 저장 내용을 되살린다. 길냥이 프로필은 기본 명단이 이미 있어야 한다.
        /// 돌려주는 값: 집 고양이들 (화면 쪽에서 그림을 붙인다).
        /// </summary>
        public static List<Cat> Restore(GameState g, SaveData d)
        {
            var w = g.World;
            while (w.Indoor.Radius < d.IndoorRadius && w.Indoor.CanExpand) w.Expand(BoardId.Indoor);
            while (w.Outdoor.Radius < d.OutdoorRadius && w.Outdoor.CanExpand) w.Expand(BoardId.Outdoor);

            // 가구: 옛 ID → 새 ID
            var idMap = new Dictionary<int, int>();
            foreach (var fs in d.Furniture)
            {
                var def = FurnitureCatalog.ById(fs.Def);
                if (def == null) continue;
                var board = (BoardId)fs.Board;
                if (w.TryPlace(def, board, new HexCoord(fs.Q, fs.R), fs.Mirrored, out var placed) != PlaceResult.Ok)
                {
                    g.Shop.AddToStorage(def); // 어떤 이유로 못 놓으면 잃지 않게 보관함으로
                    continue;
                }
                idMap[fs.Id] = placed.Id;
                if (fs.Pee > 0 || fs.Poop > 0) g.Economy.RestoreLitter(placed.Id, fs.Pee, fs.Poop);
                if (fs.BowlFood > 0 && g.Strays != null) g.Strays.RestoreBowl(placed.Id, fs.BowlFood);
            }
            foreach (var t in d.Tray)
                g.Economy.RestoreTraySlot(idMap.TryGetValue(t.FurnitureId, out var nid) ? nid : -t.FurnitureId, (ItemKind)t.Kind, t.Label, t.Count);
            foreach (var x in d.Drops)
            {
                var c = new Cell((BoardId)x.Board, new HexCoord(x.Q, x.R));
                if (w.GetTile(c) != null) g.Economy.RestoreDrop(c, (ItemKind)x.Kind);
            }

            // 지갑
            var wal = g.Economy.Wallet;
            wal.Money = d.Money; wal.Hearts = d.Hearts; wal.Furballs = d.Furballs; wal.Whiskers = d.Whiskers; wal.Kibble = d.Kibble;

            // 길냥이 호감도
            if (g.Strays != null)
                foreach (var ss in d.Strays)
                {
                    var p = FindProfile(g.Strays, ss.Id);
                    if (p == null) continue;
                    p.Affection = ss.Affection; p.Visits = ss.Visits; p.Meals = ss.Meals; p.Discovered = ss.Discovered; p.Adopted = ss.Adopted;
                }

            // 집 고양이
            var cats = new List<Cat>();
            foreach (var cs in d.Cats)
            {
                var cell = new Cell((BoardId)cs.Board, new HexCoord(cs.Q, cs.R));
                if (!w.IsTransit(cell) || w.GetTile(cell)?.Kind == TileKind.Door) cell = AnyFloor(w, BoardId.Indoor);
                CatPersonality pers = CatPersonalities.ById(cs.Personality);
                var cat = g.Sim.AddCat(cs.Name, cell, pers);
                cat.LookKey = cs.LookKey;
                cat.StrayId = cs.StrayId;
                cat.Costume = string.IsNullOrEmpty(cs.Costume) ? null : cs.Costume;
                cat.Needs.Hunger = cs.Hunger; cat.Needs.Bladder = cs.Bladder; cat.Needs.Energy = cs.Energy; cat.Needs.Fun = cs.Fun;
                cat.Mood = cs.Mood;
                cat.PetFatigue = cs.PetFatigue;
                cat.PetFatigueAt = g.Sim.SimTime - cs.PetFatigueAgo;
                cat.PetHeartAt = cs.PetHeartWait > 0 ? g.Sim.SimTime + cs.PetHeartWait : double.NegativeInfinity;
                cat.IntimacyLevel = Math.Max(1, cs.IntimacyLevel); // 예전 저장은 0 → Lv.1
                cat.IntimacyExp = cs.IntimacyExp;
                cat.SkillPoints = cs.SkillPoints;
                cat.SnackDay = cs.SnackDay;
                cat.SnacksToday = cs.SnacksToday;
                if (cs.CatSkills != null) foreach (var sk in cs.CatSkills) Cats.CatSkills.SetLevel(cat, sk.Id, sk.Count);
                cats.Add(cat);
            }

            foreach (var s in d.Storage) g.Shop.SetStock(s.Id, s.Count);
            if (g.Workshop != null) foreach (var s in d.Closet) g.Workshop.SetCloset(s.Id, s.Count);
            if (g.Skills != null) foreach (var s in d.Skills) g.Skills.SetLevel(s.Id, s.Count);
            g.Clock?.SetElapsed(d.ClockElapsed);
            g.Economy.NotifyChanged();
            return cats;
        }

        private static List<CountSave> SkillsOf(Cat c)
        {
            var list = new List<CountSave>();
            foreach (var kv in c.Skills) if (kv.Value > 0) list.Add(new CountSave { Id = kv.Key, Count = kv.Value });
            return list;
        }

        private static StrayProfile FindProfile(StraySystem s, int id)
        {
            foreach (var p in s.Profiles) if (p.Id == id) return p;
            return null;
        }

        public static Cell AnyFloor(HouseWorld w, BoardId board)
        {
            Cell? best = null;
            int bestD = int.MaxValue;
            foreach (var t in w.GetBoard(board).Tiles.Values)
            {
                var c = new Cell(board, t.Coord);
                if (t.Kind != TileKind.Floor || !w.IsTransit(c)) continue;
                int dd = t.Coord.Length;
                if (dd < bestD) { bestD = dd; best = c; }
            }
            return best ?? new Cell(board, HexCoord.Zero);
        }
    }
}
