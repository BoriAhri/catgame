using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;

namespace CatHealingHouse.Stray
{
    /// <summary>길냥이 생김새. 화면 쪽이 이걸 보고 무늬를 고른다. Siamese 는 희귀.</summary>
    public enum Breed { Cheese, Mackerel, Tuxedo, Calico, White, Black, Siamese }

    /// <summary>호감도 구간. 0~20 / 21~40 / 41~60 / 61~80 / 81~100.</summary>
    public enum AffectionTier { Wary, Shy, Friendly, Close, Bonded }

    /// <summary>길냥이 성격 (처음부터 보인다). 쓰다듬기·집으로 들이기를 받아줄 확률을 정한다.</summary>
    public enum Temperament { Sassy, Normal, Cuddly }

    /// <summary>터치 메뉴 (호감도 41 이상).</summary>
    public enum StrayAction { Snack, Pet, Play, Adopt }

    public enum InteractOutcome { Unavailable, AlreadyDone, Accepted, Refused, Adopting }

    public struct InteractResult
    {
        public InteractOutcome Outcome;
        public double AffectionDelta;
        public double Chance;
        public bool Ok => Outcome == InteractOutcome.Accepted || Outcome == InteractOutcome.Adopting;
    }

    /// <summary>길냥이 한 마리의 영구 기록. 다녀가도 호감도는 남는다.</summary>
    public sealed class StrayProfile
    {
        public int Id;
        public string Name;
        public Breed Breed;
        public double Affection;          // 0~100
        public int Visits;
        public int Meals;
        public bool Discovered;           // 한 번이라도 왔었다
        public bool Adopted;
        public double LastLeftAt = double.NegativeInfinity;
        public Func<CatPersonality> Personality;
        public Temperament Temperament = Temperament.Normal;
        public double SpawnWeight = 1;
        /// <summary>거절하고 도망갔으면 그날(이 시각까지)은 다시 안 온다.</summary>
        public double NoReturnUntil = double.NegativeInfinity;

        public string TemperamentName => StraySystem.NameOf(Temperament);

        public AffectionTier Tier => StraySystem.TierOf(Affection);
        public void AddAffection(double d) => Affection = Math.Max(0, Math.Min(100, Affection + d));
    }

    /// <summary>바닥에 남은 발자국 (화면에서 서서히 흐려진다).</summary>
    public struct PawPrint
    {
        public Cell Cell;
        public int Heading;
        public double CreatedAt;
    }

    [Serializable]
    public sealed class StrayTuning
    {
        public int MaxOnBoard = 2;
        public double SpawnMinSeconds = 15, SpawnMaxSeconds = 35;
        public double ReturnCooldown = 60;          // 다녀간 고양이는 이만큼 지나야 다시 온다
        public int BowlPortions = 3;                // 밥그릇 한 번 채우면 몇 끼

        public double WaryHopScale = 1.7;           // 0~20: 살금살금
        public double ShyHopScale = 1.45;
        public double FriendlyHopScale = 1.2;
        public double CloseHopScale = 1.0;
        public double FleeHopScale = 0.45;
        public double LowPauseChance = 0.45;        // 한 칸마다 멈춰서 두리번
        public double PauseMin = 0.6, PauseMax = 1.6;

        public double EatSeconds = 6;
        public double MealAffection = 8;
        public double CalmBonus = 2;                // 이번 방문에 놀라지 않았으면 추가
        public double FleePenalty = 3;
        public double SnackAffection = 5;           // 간식주기 (방문당 1번)
        public double PlayAffection = 3;            // 놀아주기 (방문당 1번)
        public double PetAffection = 4;             // 쓰다듬기 성공 (방문당 처음 한 번만 오름)
        public double RefusePenalty = 3;            // 거절하고 도망
        public double RefuseNoReturnSeconds = 600;  // 거절하면 그날은 안 옴
        public double SnackSeconds = 3.5, PlaySeconds = 4, PetSeconds = 3;
        public double BondedWaitMin = 60, BondedWaitMax = 90; // 81~100: 문 앞에서 기다리는 시간
        public double LoafMin = 25, LoafMax = 40;
        public double PeekMin = 15, PeekMax = 25;
        public double ReadyWaitSeconds = 120;       // 영입 대기 (안 데려가면 다음에)
        public double WaitForSlotSeconds = 12;
        public double GiftChance = 0.3;             // 중간 이상 호감에서 떠날 때

        public int WaryAlertRadius = 3;             // 0~20: 이 반경 안 터치면 도망
        public int ShyAlertRadius = 1;              // 21~40: 고양이 칸과 바로 옆 칸까지
        public double ReactionSeconds = 1.6;
        public double FlinchSeconds = 2.0;
        public double GraceSeconds = 3;             // 야외로 막 나왔을 땐 기척이 없다
        public double PawPrintLife = 90;
        public int MaxPawPrints = 40;
    }

    /// <summary>
    /// 4단계 길냥이 두뇌. CatSimulation.StrayBrain 에 붙어서 같은 시계로 돈다.
    ///
    /// 밥그릇에 밥이 있으면 → 마당 테두리(수풀·담벼락)에서 등장 → 밥그릇으로 (호감 낮으면 살금살금)
    /// → 먹는다 (호감도 +) → 호감도 구간별 행동
    ///    0~20   : 마당으로 나가면 "!" 하고 12시로 도망. 터치 3칸 안도 도망. 먹고 바로 떠남.
    ///    21~40  : 나가도 안 도망. 고양이나 바로 옆 칸을 터치하면 도망. 먹고 떠남.
    ///    41~60  : 터치해도 안 도망. 고양이를 터치하면 메뉴 (간식·쓰다듬기·놀아주기·집으로 들이기).
    ///             쓰다듬기·들이기는 성격(싸가지없음/보통/개냥이)에 따른 확률. 박스·벤치에서 식빵.
    ///    61~80  : 문 앞까지 와서 기웃. 확률 상승.
    ///    81~100 : 문 앞에서 오래 기다린다. 거의 바로 들일 수 있다 (싸가지없음은 90 까지 가끔 튕김).
    /// 거절하면 호감도 -3, 그날은 다시 안 온다. 떠날 때 발자국을 남기고, 가끔 선물을 두고 간다.
    /// </summary>
    public sealed class StraySystem
    {
        public readonly CatSimulation Sim;
        public readonly HouseEconomy Economy;
        public readonly StrayTuning Tuning;

        private readonly List<StrayProfile> _profiles = new List<StrayProfile>();
        private readonly Dictionary<int, int> _bowlFood = new Dictionary<int, int>(); // 야외 밥그릇 → 남은 끼니
        private readonly List<PawPrint> _prints = new List<PawPrint>();
        private readonly Dictionary<int, List<Cell>> _paths = new Dictionary<int, List<Cell>>(); // 고양이 → 남은 경로
        private readonly Random _rng;
        private double _spawnTimer;
        private double _graceUntil = double.NegativeInfinity;

        public IReadOnlyList<StrayProfile> Profiles => _profiles;
        public IReadOnlyList<PawPrint> PawPrints => _prints;

        public event Action<Cat, StrayProfile> Arrived;
        public event Action<StrayProfile> FirstDiscovered;
        public event Action<Cat, StrayProfile> BecameReady;
        public event Action<Cat, StrayProfile> Adopted;
        public event Action<Cat, StrayReaction> Reacted;
        public event Action<StrayProfile> Left;

        public StraySystem(CatSimulation sim, HouseEconomy economy, StrayTuning tuning = null, int seed = 4242, bool defaultRoster = true)
        {
            Sim = sim;
            Economy = economy;
            Tuning = tuning ?? new StrayTuning();
            _rng = new Random(seed);
            sim.StrayBrain = Think;
            sim.Stepped += OnStepped;
            _spawnTimer = Tuning.SpawnMinSeconds * 0.5;
            if (defaultRoster) AddDefaultRoster();
        }

        private double Now => Sim.SimTime;

        public static AffectionTier TierOf(double affection)
        {
            double a = Math.Round(affection);
            return a <= 20 ? AffectionTier.Wary : a <= 40 ? AffectionTier.Shy : a <= 60 ? AffectionTier.Friendly
                 : a <= 80 ? AffectionTier.Close : AffectionTier.Bonded;
        }

        public static string NameOf(Temperament t) => t == Temperament.Sassy ? "싸가지없음" : t == Temperament.Cuddly ? "개냥이" : "보통";

        public static string NameOf(StrayAction a) =>
            a == StrayAction.Snack ? "간식주기" : a == StrayAction.Pet ? "쓰다듬기" : a == StrayAction.Play ? "놀아주기" : "집으로 들이기";

        /// <summary>
        /// 쓰다듬기·집으로 들이기를 받아줄 확률 (기획서 표). 간식·놀아주기는 항상 1.
        /// 싸가지없음은 81~90 에서 가끔 튕기고 91~100 은 무조건 받아준다.
        /// </summary>
        public static double AcceptChance(StrayAction action, double affection, Temperament t)
        {
            if (action == StrayAction.Snack || action == StrayAction.Play) return 1;
            double a = Math.Round(affection);
            int ti = (int)t; // Sassy 0, Normal 1, Cuddly 2
            bool pet = action == StrayAction.Pet;
            switch (TierOf(a))
            {
                case AffectionTier.Friendly: return pet ? new[] { 0.25, 0.50, 0.80 }[ti] : new[] { 0.05, 0.15, 0.35 }[ti];
                case AffectionTier.Close: return pet ? new[] { 0.45, 0.75, 0.95 }[ti] : new[] { 0.20, 0.40, 0.70 }[ti];
                case AffectionTier.Bonded:
                    if (t != Temperament.Sassy || a > 90) return 1;
                    return pet ? 0.85 : 0.85;
                default: return 0;
            }
        }

        // ───────────────────────── 명단 ─────────────────────────

        public StrayProfile AddProfile(string name, Breed breed, double affection, Func<CatPersonality> personality,
            double spawnWeight = 1, Temperament temperament = Temperament.Normal)
        {
            var p = new StrayProfile
            {
                Id = _profiles.Count + 1, Name = name, Breed = breed, Affection = affection,
                Personality = personality ?? CatPersonality.Normal, SpawnWeight = spawnWeight, Temperament = temperament,
            };
            _profiles.Add(p);
            return p;
        }

        private void AddDefaultRoster()
        {
            AddProfile("노랑이", Breed.Cheese, 5, CatPersonalities.Foodie, temperament: Temperament.Cuddly);
            AddProfile("회색이", Breed.Mackerel, 0, CatPersonalities.Shy, temperament: Temperament.Sassy);
            AddProfile("양말이", Breed.Tuxedo, 10, CatPersonalities.Curious, temperament: Temperament.Normal);
            AddProfile("얼룩이", Breed.Calico, 0, CatPersonalities.Sleepyhead, temperament: Temperament.Sassy);
            AddProfile("두부", Breed.White, 3, CatPersonalities.TowerKing, temperament: Temperament.Cuddly);
            AddProfile("밤톨이", Breed.Black, 0, CatPersonalities.Energizer, temperament: Temperament.Normal);
            AddProfile("샴샴이", Breed.Siamese, 0, CatPersonalities.Curious, spawnWeight: 0.15, temperament: Temperament.Sassy);
        }

        public StrayProfile ProfileOf(Cat cat)
        {
            if (cat == null || cat.StrayId <= 0 || cat.StrayId > _profiles.Count) return null;
            return _profiles[cat.StrayId - 1];
        }

        public IEnumerable<Cat> StraysOn(BoardId board)
        {
            foreach (var c in Sim.Cats)
                if (c.Affiliation == Affiliation.Stray && c.Cell.Board == board) yield return c;
        }

        public int StrayCount
        {
            get { int n = 0; foreach (var c in Sim.Cats) if (c.Affiliation == Affiliation.Stray) n++; return n; }
        }

        // ───────────────────────── 밥그릇 ─────────────────────────

        public int FoodIn(int bowlId) => _bowlFood.TryGetValue(bowlId, out var n) ? n : 0;

        public bool IsOutdoorBowl(FurnitureInstance f) =>
            f != null && f.Board == BoardId.Outdoor && f.Def.Satisfies == NeedType.Hunger;

        /// <summary>야외 밥그릇을 탭해서 채운다. 이미 가득이면 false.</summary>
        public bool FillBowl(int bowlId)
        {
            var f = Sim.World.GetFurniture(bowlId);
            if (!IsOutdoorBowl(f) || FoodIn(bowlId) >= Tuning.BowlPortions) return false;
            _bowlFood[bowlId] = Tuning.BowlPortions;
            if (_spawnTimer > Tuning.SpawnMinSeconds * 0.4) _spawnTimer = Tuning.SpawnMinSeconds * 0.4; // 곧 누군가 온다
            return true;
        }

        private FurnitureInstance PickBowlWithFood()
        {
            FurnitureInstance best = null;
            foreach (var f in Sim.World.Furniture)
                if (IsOutdoorBowl(f) && FoodIn(f.Id) > 0 && f.HasFreeSlot && (best == null || FoodIn(f.Id) > FoodIn(best.Id))) best = f;
            return best;
        }

        // ───────────────────────── 등장 ─────────────────────────

        private void OnStepped(double dt)
        {
            // 발자국 정리
            for (int i = _prints.Count - 1; i >= 0; i--)
                if (Now - _prints[i].CreatedAt > Tuning.PawPrintLife) _prints.RemoveAt(i);

            _spawnTimer -= dt;
            if (_spawnTimer > 0) return;
            _spawnTimer = Range(Tuning.SpawnMinSeconds, Tuning.SpawnMaxSeconds);
            TrySpawn();
        }

        /// <summary>조건이 맞으면 한 마리를 테두리에 등장시킨다. 테스트와 오프라인 계산에서도 직접 부른다.</summary>
        public Cat TrySpawn()
        {
            if (StrayCount >= Tuning.MaxOnBoard) return null;
            var bowl = PickBowlWithFood();
            if (bowl == null) return null;

            var profile = PickProfile();
            if (profile == null) return null;

            var map = Sim.Maps.Get(bowl.Id);
            // 집(문) 반대편, 마당 위쪽 수풀에서만 나타난다
            var edges = new List<Cell>();
            foreach (var c in FarEdgeCells())
                if (map.Get(c) != DistanceMap.Unreachable) edges.Add(c);
            if (edges.Count == 0) return null;
            var at = edges[_rng.Next(edges.Count)];

            var cat = Sim.AddStray(profile.Name, at, profile.Id);
            profile.Visits++;
            bool first = !profile.Discovered;
            profile.Discovered = true;
            Sim.ReserveFor(cat, bowl, NeedType.Hunger);
            cat.StrayState = StrayState.Approaching;
            cat.State = CatState.Idle;
            cat.StrayTimer = Range(0.5, 1.2);
            cat.AteThisVisit = false;
            cat.AlertAccum = 0;
            cat.VisitFlags = 0;
            cat.Activity = 0;
            cat.Held = false;
            Arrived?.Invoke(cat, profile);
            if (first) FirstDiscovered?.Invoke(profile);
            return cat;
        }

        private StrayProfile PickProfile()
        {
            var onBoard = new HashSet<int>();
            foreach (var c in Sim.Cats) if (c.Affiliation == Affiliation.Stray) onBoard.Add(c.StrayId);

            var pool = new List<StrayProfile>();
            double total = 0;
            foreach (var p in _profiles)
            {
                if (p.Adopted || onBoard.Contains(p.Id) || Now - p.LastLeftAt < Tuning.ReturnCooldown || Now < p.NoReturnUntil) continue;
                pool.Add(p);
                // 이미 친해진 고양이는 더 자주 온다
                total += p.SpawnWeight * (1 + p.Affection / 50.0);
            }
            if (pool.Count == 0) return null;
            double roll = _rng.NextDouble() * total;
            foreach (var p in pool)
            {
                roll -= p.SpawnWeight * (1 + p.Affection / 50.0);
                if (roll <= 0) return p;
            }
            return pool[pool.Count - 1];
        }

        // ───────────────────────── 기척 ─────────────────────────

        /// <summary>
        /// 플레이어가 집 문을 열고 마당으로 나왔다. 호감도 낮은 길냥이는 문 여는 소리에 "!" 하고
        /// 마당 맨 위(12시)로 도망간다 (호감도는 안 깎임, 밥을 다 못 먹었을 뿐).
        /// 그 뒤 잠깐 동안은 탭 기척이 없다. 도망간 고양이 수를 돌려준다.
        /// </summary>
        public int NotifyBoardEntered(BoardId board)
        {
            if (board != BoardId.Outdoor) return 0;
            int fled = 0;
            foreach (var cat in new List<Cat>(StraysOn(BoardId.Outdoor)))
            {
                var p = ProfileOf(cat);
                if (p == null || p.Tier != AffectionTier.Wary) continue; // 0~20 만
                if (cat.StrayState == StrayState.Fleeing || cat.StrayState == StrayState.Adopting) continue;
                BeginFlee(cat, p, penalty: false);
                fled++;
            }
            _graceUntil = Now + Tuning.GraceSeconds;
            return fled;
        }

        /// <summary>
        /// 마당 위쪽(집 반대편) 테두리. 길냥이는 여기서 나오고 여기로 돌아간다.
        /// topRowOnly 면 맨 윗줄(12시 쪽)만: 도망칠 때.
        /// </summary>
        public List<Cell> FarEdgeCells(bool topRowOnly = false)
        {
            int n = Sim.World.Outdoor.Radius + 1;
            var list = new List<Cell>();
            foreach (var t in Sim.World.Outdoor.Tiles.Values)
            {
                if (t.Kind != TileKind.Edge) continue;
                if (topRowOnly ? t.Coord.R != -n : t.Coord.R >= 0) continue;
                list.Add(new Cell(BoardId.Outdoor, t.Coord));
            }
            return list;
        }

        public bool InGrace => Now < _graceUntil;

        /// <summary>터치에 도망가는 반경. 41 이상은 터치해도 안 도망간다 (-1).</summary>
        public int AlertRadius(AffectionTier tier) =>
            tier == AffectionTier.Wary ? Tuning.WaryAlertRadius : tier == AffectionTier.Shy ? Tuning.ShyAlertRadius : -1;

        /// <summary>
        /// 터치(또는 크게 확대) 지점에 기척이 생겼다.
        ///   0~20 : 3칸 안이면 "!" 하고 도망 (호감도 -3)
        ///   21~40: 고양이 칸과 바로 옆 칸이면 도망 (호감도 -3)
        ///   41~  : 안 도망간다 (고양이를 직접 터치하면 화면이 메뉴를 연다)
        /// 돌려주는 값: 도망간 고양이 수.
        /// </summary>
        public int Presence(Cell at, Cat tappedCat = null)
        {
            if (at.Board != BoardId.Outdoor || InGrace) return 0;
            int fled = 0;
            foreach (var cat in new List<Cat>(StraysOn(BoardId.Outdoor)))
            {
                var p = ProfileOf(cat);
                if (p == null || cat.StrayState == StrayState.Fleeing || cat.StrayState == StrayState.Adopting) continue;
                int radius = AlertRadius(p.Tier);
                if (radius < 0) continue;
                int d = HexCoord.Distance(cat.Cell.Coord, at.Coord);
                if (d > radius && cat != tappedCat) continue;
                BeginFlee(cat, p);
                fled++;
            }
            return fled;
        }

        private void React(Cat cat, StrayReaction r)
        {
            cat.Reaction = r;
            cat.ReactionTimer = Tuning.ReactionSeconds;
            Reacted?.Invoke(cat, r);
        }

        private void BeginFlee(Cat cat, StrayProfile p, bool penalty = true)
        {
            React(cat, StrayReaction.Alert);
            if (penalty) p.AddAffection(-Tuning.FleePenalty);
            cat.AlertAccum += 1;
            Sim.ReleaseFor(cat);
            cat.StrayState = StrayState.Fleeing;
            cat.StrayTimer = 0.25; // "!" 를 보여주고 튄다
            cat.State = CatState.Idle;
            _paths.Remove(cat.Id);
        }

        // ───────────────────────── 영입 ─────────────────────────

        /// <summary>터치 메뉴를 열 수 있는지 (호감도 41 이상, 도망·입양 중이 아님).</summary>
        public bool MenuAvailable(Cat cat)
        {
            var p = ProfileOf(cat);
            if (p == null || cat.Affiliation != Affiliation.Stray || cat.Cell.Board != BoardId.Outdoor) return false;
            if (cat.StrayState == StrayState.Fleeing || cat.StrayState == StrayState.Adopting) return false;
            return p.Tier >= AffectionTier.Friendly;
        }

        public bool AlreadyDone(Cat cat, StrayAction a) => a != StrayAction.Adopt && (cat.VisitFlags & (1 << (int)a)) != 0;

        /// <summary>메뉴가 열려 있는 동안 고양이를 제자리에 잡아둔다 (먹기·식빵 타이머도 멈춘다).</summary>
        public void Hold(Cat cat, bool hold) { if (cat != null) cat.Held = hold; }

        /// <summary>
        /// 메뉴 행동. roll 을 넣으면 그 값(0~1)으로 성공을 정한다 (테스트용).
        ///   간식주기·놀아주기: 항상 받아줌, 방문당 한 번.
        ///   쓰다듬기: 확률. 성공하면 하트 (호감도는 방문당 처음 한 번만), 실패하면 도망.
        ///   집으로 들이기: 확률. 성공하면 문을 지나 집 고양이가 된다, 실패하면 도망.
        ///   도망: 호감도 -3, 그날은 다시 안 옴.
        /// </summary>
        public InteractResult Interact(Cat cat, StrayAction action, double? roll = null)
        {
            var r = new InteractResult { Outcome = InteractOutcome.Unavailable };
            if (!MenuAvailable(cat)) return r;
            var p = ProfileOf(cat);
            r.Chance = AcceptChance(action, p.Affection, p.Temperament);
            if (AlreadyDone(cat, action) && action != StrayAction.Pet) { r.Outcome = InteractOutcome.AlreadyDone; return r; }

            double before = p.Affection;
            bool accepted = (roll ?? _rng.NextDouble()) < r.Chance;
            if (!accepted)
            {
                Refuse(cat, p);
                r.Outcome = InteractOutcome.Refused;
                r.AffectionDelta = p.Affection - before;
                return r;
            }

            switch (action)
            {
                case StrayAction.Snack:
                    p.AddAffection(Tuning.SnackAffection);
                    StartActivity(cat, action, Tuning.SnackSeconds);
                    break;
                case StrayAction.Play:
                    p.AddAffection(Tuning.PlayAffection);
                    StartActivity(cat, action, Tuning.PlaySeconds);
                    break;
                case StrayAction.Pet:
                    if (!AlreadyDone(cat, action)) p.AddAffection(Tuning.PetAffection);
                    StartActivity(cat, action, Tuning.PetSeconds);
                    break;
                case StrayAction.Adopt:
                    BeginAdopt(cat);
                    r.Outcome = InteractOutcome.Adopting;
                    r.AffectionDelta = 0;
                    return r;
            }
            cat.VisitFlags |= 1 << (int)action;
            React(cat, StrayReaction.Love);
            r.Outcome = InteractOutcome.Accepted;
            r.AffectionDelta = p.Affection - before;
            return r;
        }

        private void StartActivity(Cat cat, StrayAction a, double seconds)
        {
            cat.Activity = (int)a + 1;
            cat.ActivityTimer = seconds;
        }

        private void Refuse(Cat cat, StrayProfile p)
        {
            cat.Held = false;
            cat.Activity = 0;
            p.AddAffection(-Tuning.RefusePenalty);
            p.NoReturnUntil = Now + Tuning.RefuseNoReturnSeconds;
            BeginFlee(cat, p, penalty: false);
        }

        private void BeginAdopt(Cat cat)
        {
            cat.Held = false;
            cat.Activity = 0;
            if (cat.HasTarget) Sim.ReleaseFor(cat);
            cat.StrayState = StrayState.Adopting;
            cat.State = CatState.Idle;
            cat.StrayTimer = 0.6;
            cat.Reaction = StrayReaction.Love;
            cat.ReactionTimer = Tuning.ReactionSeconds * 2;
            _paths.Remove(cat.Id);
        }

        // ───────────────────────── 두뇌 ─────────────────────────

        private void Think(Cat cat, double dt)
        {
            if (cat.ReactionTimer > 0)
            {
                cat.ReactionTimer -= dt;
                if (cat.ReactionTimer <= 0) cat.Reaction = StrayReaction.None;
            }
            if (cat.IsHopping) return;

            var p = ProfileOf(cat);
            if (p == null) { Sim.RemoveCat(cat); return; }

            // 교감 동작 중이거나 메뉴가 열려 있으면 제자리 (먹기·식빵·기다림 타이머도 멈춤)
            if (cat.ActivityTimer > 0)
            {
                cat.ActivityTimer -= dt;
                if (cat.ActivityTimer <= 0) cat.Activity = 0;
                if (cat.State == CatState.MovingToTarget) cat.State = CatState.Idle;
                return;
            }
            if (cat.Held)
            {
                if (cat.State == CatState.MovingToTarget) cat.State = CatState.Idle;
                return;
            }

            if (cat.StrayTimer > 0)
            {
                cat.StrayTimer -= dt;
                if (cat.StrayTimer > 0)
                {
                    if (cat.State == CatState.MovingToTarget || cat.State == CatState.Wandering) cat.State = CatState.Idle;
                    return;
                }
            }

            switch (cat.StrayState)
            {
                case StrayState.Approaching: Approach(cat, p); break;
                case StrayState.Waiting: WaitForSlot(cat, p); break;
                case StrayState.Eating: FinishEating(cat, p); break;
                case StrayState.ToLoaf: MoveToReserved(cat, p, StrayState.Loafing, Range(Tuning.LoafMin, Tuning.LoafMax)); break;
                case StrayState.Loafing: Sim.ReleaseFor(cat); BeginLeave(cat, p); break;
                case StrayState.ToDoor: ToDoor(cat, p); break;
                case StrayState.Peeking: AfterPeek(cat, p); break;
                case StrayState.ReadyToAdopt: BeginLeave(cat, p); break; // 기다리다 지쳐서
                case StrayState.Leaving: Leave(cat, p, StrayState.Leaving); break;
                case StrayState.Fleeing: Leave(cat, p, StrayState.Fleeing); break;
                case StrayState.Adopting: Adopting(cat, p); break;
                default: BeginLeave(cat, p); break;
            }
        }

        private double HopScale(StrayProfile p)
        {
            switch (p.Tier)
            {
                case AffectionTier.Wary: return Tuning.WaryHopScale;
                case AffectionTier.Shy: return Tuning.ShyHopScale;
                case AffectionTier.Friendly: return Tuning.FriendlyHopScale;
                default: return Tuning.CloseHopScale;
            }
        }

        private void Step(Cat cat, Cell next, double scale)
        {
            Sim.Hop(cat, next, scale);
            cat.State = CatState.MovingToTarget;
            AddPrint(cat);
        }

        private void AddPrint(Cat cat)
        {
            _prints.Add(new PawPrint { Cell = cat.PrevCell, Heading = cat.Heading, CreatedAt = Now });
            if (_prints.Count > Tuning.MaxPawPrints) _prints.RemoveAt(0);
        }

        private void Approach(Cat cat, StrayProfile p)
        {
            var bowl = Sim.World.GetFurniture(cat.TargetFurnitureId);
            if (bowl == null || FoodIn(bowl.Id) <= 0) { BeginLeave(cat, p); return; }
            var map = Sim.Maps.Get(bowl.Id);
            if (map.Get(cat.Cell) == 0)
            {
                cat.StrayState = StrayState.Eating;
                cat.State = CatState.Using;
                cat.StrayTimer = Tuning.EatSeconds;
                return;
            }
            if (!map.TryStep(Sim.World, cat.Cell, out var next, _rng.NextDouble(), throughDoor: false)) { BeginLeave(cat, p); return; }
            Step(cat, next, HopScale(p));
            double pause = p.Tier == AffectionTier.Wary ? Tuning.LowPauseChance : p.Tier == AffectionTier.Shy ? Tuning.LowPauseChance * 0.5 : 0;
            if (_rng.NextDouble() < pause)
                cat.StrayTimer = Range(Tuning.PauseMin, Tuning.PauseMax); // 한 걸음 가고 두리번 (걸음이 끝난 뒤부터 센다)
        }

        private void WaitForSlot(Cat cat, StrayProfile p)
        {
            // 지금은 등장할 때 자리를 예약하므로 거의 안 쓰인다. 자리가 없으면 떠난다.
            BeginLeave(cat, p);
        }

        private void FinishEating(Cat cat, StrayProfile p)
        {
            var bowl = Sim.World.GetFurniture(cat.TargetFurnitureId);
            if (bowl != null && FoodIn(bowl.Id) > 0) _bowlFood[bowl.Id] = FoodIn(bowl.Id) - 1;
            Sim.ReleaseFor(cat);
            cat.AteThisVisit = true;
            p.Meals++;
            p.AddAffection(Tuning.MealAffection + (cat.AlertAccum <= 0 ? Tuning.CalmBonus : 0));
            cat.State = CatState.Idle;

            // 61 이상: 문 앞까지 / 41~60: 박스·벤치에서 식빵 / 40 이하: 떠난다
            if (p.Tier >= AffectionTier.Close) { BeginToDoor(cat, p); return; }
            if (p.Tier == AffectionTier.Friendly && TryReserveLoaf(cat)) { cat.StrayState = StrayState.ToLoaf; return; }
            BeginLeave(cat, p);
        }

        private bool TryReserveLoaf(Cat cat)
        {
            FurnitureInstance best = null;
            int bestD = int.MaxValue;
            foreach (var f in Sim.World.Furniture)
            {
                if (f.Board != BoardId.Outdoor || f.Def.Satisfies != NeedType.Energy || !f.HasFreeSlot) continue;
                var m = Sim.Maps.Get(f.Id);
                int d = m.Get(cat.Cell);
                if (d == DistanceMap.Unreachable)
                {
                    foreach (var n in Sim.World.Neighbors(cat.Cell, throughDoor: false))
                    {
                        int nd = m.Get(n);
                        if (nd != DistanceMap.Unreachable && nd + 1 < d) d = nd + 1;
                    }
                }
                if (d < bestD) { bestD = d; best = f; }
            }
            if (best == null || bestD == DistanceMap.Unreachable) return false;
            Sim.ReserveFor(cat, best, NeedType.Energy);
            return true;
        }

        private void MoveToReserved(Cat cat, StrayProfile p, StrayState arrive, double stay)
        {
            var f = Sim.World.GetFurniture(cat.TargetFurnitureId);
            if (f == null) { BeginLeave(cat, p); return; }
            var map = Sim.Maps.Get(f.Id);
            if (map.Get(cat.Cell) == 0)
            {
                cat.StrayState = arrive;
                cat.State = CatState.Using;
                cat.StrayTimer = stay;
                return;
            }
            if (!TryStepOnMap(cat, map, out var next)) { Sim.ReleaseFor(cat); BeginLeave(cat, p); return; }
            Step(cat, next, HopScale(p));
        }

        /// <summary>거리맵 위에서 한 걸음. 다른 가구 진입 칸처럼 맵에 없는 칸에 서 있으면 이웃 중 가장 가까운 곳으로.</summary>
        private bool TryStepOnMap(Cat cat, DistanceMap map, out Cell next)
        {
            if (map.TryStep(Sim.World, cat.Cell, out next, _rng.NextDouble(), throughDoor: false)) return true;
            if (map.Get(cat.Cell) != DistanceMap.Unreachable) return false;
            int best = DistanceMap.Unreachable;
            foreach (var n in Sim.World.Neighbors(cat.Cell, throughDoor: false))
            {
                int d = map.Get(n);
                if (d < best) { best = d; next = n; }
            }
            return best != DistanceMap.Unreachable;
        }

        // ── 문 앞 ──

        private void BeginToDoor(Cat cat, StrayProfile p)
        {
            var path = PathTo(cat.Cell, DoorSideCells());
            if (path == null) { BeginLeave(cat, p); return; }
            _paths[cat.Id] = path;
            cat.StrayState = StrayState.ToDoor;
        }

        /// <summary>야외 문 바로 옆의 지나갈 수 있는 칸들 (문 칸 자체는 제외).</summary>
        public List<Cell> DoorSideCells()
        {
            var board = Sim.World.Outdoor;
            var door = new Cell(BoardId.Outdoor, board.DoorCoord);
            var list = new List<Cell>();
            foreach (var n in Sim.World.Neighbors(door, throughDoor: false))
            {
                var t = Sim.World.GetTile(n);
                if (t != null && t.Kind != TileKind.Door && Sim.World.IsTransit(n)) list.Add(n);
            }
            return list;
        }

        private void ToDoor(Cat cat, StrayProfile p)
        {
            if (FollowPath(cat, HopScale(p))) return;
            // 도착: 집 쪽(문)을 바라보고 기웃
            var door = Sim.World.Outdoor.DoorCoord;
            var diff = door - cat.Cell.Coord;
            for (int i = 0; i < 6; i++) if (HexCoord.Directions[i] == diff) { cat.Heading = i; break; }
            cat.State = CatState.Idle;
            cat.StrayState = StrayState.Peeking;
            if (p.Tier == AffectionTier.Bonded)
            {
                // 바로 들일 수 있을 만큼 친하다: 문 앞에서 오래 기다린다
                cat.StrayTimer = Range(Tuning.BondedWaitMin, Tuning.BondedWaitMax);
                BecameReady?.Invoke(cat, p);
            }
            else cat.StrayTimer = Range(Tuning.PeekMin, Tuning.PeekMax);
        }

        private void AfterPeek(Cat cat, StrayProfile p) => BeginLeave(cat, p);

        private void Adopting(Cat cat, StrayProfile p)
        {
            var world = Sim.World;
            var door = new Cell(BoardId.Outdoor, world.Outdoor.DoorCoord);
            if (cat.Cell.Board == BoardId.Outdoor)
            {
                if (cat.Cell == door)
                {
                    world.TryGetPortal(door, out var inside);
                    Sim.Hop(cat, inside, 1.2);
                    return;
                }
                if (!_paths.ContainsKey(cat.Id))
                {
                    var path = PathTo(cat.Cell, new List<Cell> { door });
                    if (path == null) { BeginLeave(cat, p); return; }
                    _paths[cat.Id] = path;
                }
                if (!FollowPath(cat, 1.0)) _paths.Remove(cat.Id);
                return;
            }

            // 실내 문 칸에 도착: 이제 우리 집 고양이
            _paths.Remove(cat.Id);
            p.Adopted = true;
            Sim.MakeResident(cat, p.Personality?.Invoke());
            Adopted?.Invoke(cat, p);
        }

        // ── 떠나기 ──

        private void BeginLeave(Cat cat, StrayProfile p)
        {
            if (cat.HasTarget) Sim.ReleaseFor(cat);
            cat.StrayState = StrayState.Leaving;
            cat.State = CatState.Idle;
            _paths.Remove(cat.Id);
            if (cat.AteThisVisit && p.Tier >= AffectionTier.Friendly && _rng.NextDouble() < Tuning.GiftChance)
                Economy?.AddGift(cat.Cell);
        }

        private void Leave(Cat cat, StrayProfile p, StrayState mode)
        {
            // 도망은 맨 윗줄(12시)로, 보통 떠날 때는 위쪽 테두리 아무 데로
            var exits = FarEdgeCells(topRowOnly: mode == StrayState.Fleeing);
            if (exits.Contains(cat.Cell))
            {
                _paths.Remove(cat.Id);
                p.LastLeftAt = Now;
                Sim.RemoveCat(cat);
                Left?.Invoke(p);
                return;
            }
            if (!_paths.ContainsKey(cat.Id))
            {
                var path = PathTo(cat.Cell, exits);
                if (path == null) { p.LastLeftAt = Now; Sim.RemoveCat(cat); Left?.Invoke(p); return; }
                _paths[cat.Id] = path;
            }
            double scale = mode == StrayState.Fleeing ? Tuning.FleeHopScale : HopScale(p);
            if (!FollowPath(cat, scale)) _paths.Remove(cat.Id);
        }

        // (퇴장 지점은 FarEdgeCells)


        // ───────────────────────── 경로 ─────────────────────────

        /// <summary>경로의 다음 칸으로 한 걸음. 남은 경로가 없으면 false (= 도착).</summary>
        private bool FollowPath(Cat cat, double scale)
        {
            if (!_paths.TryGetValue(cat.Id, out var path) || path.Count == 0) return false;
            var next = path[0];
            path.RemoveAt(0);
            if (!Sim.World.IsTransit(next) && Sim.World.GetTile(next)?.Kind != TileKind.Door)
            {
                // 그 사이 가구가 놓여 막혔다: 다시 찾는다
                _paths.Remove(cat.Id);
                return true;
            }
            Step(cat, next, scale);
            return true;
        }

        /// <summary>
        /// 야외 보드 안에서 목표 칸들 중 가장 가까운 곳까지의 경로 (BFS). 문은 넘지 않는다.
        /// 출발 칸이 목표면 빈 경로. 길이 없으면 null.
        /// </summary>
        public List<Cell> PathTo(Cell from, List<Cell> goals)
        {
            var goalSet = new HashSet<Cell>(goals);
            if (goalSet.Contains(from)) return new List<Cell>();
            var prev = new Dictionary<Cell, Cell>();
            var queue = new Queue<Cell>();
            prev[from] = from;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in Sim.World.Neighbors(cur, throughDoor: false))
                {
                    if (prev.ContainsKey(n)) continue;
                    bool isGoal = goalSet.Contains(n);
                    if (!isGoal && !Sim.World.IsTransit(n)) continue;
                    var tile = Sim.World.GetTile(n);
                    if (!isGoal && tile != null && tile.Kind == TileKind.Door) continue; // 문은 영입 때만
                    prev[n] = cur;
                    if (isGoal)
                    {
                        var path = new List<Cell>();
                        var c = n;
                        while (c != from) { path.Add(c); c = prev[c]; }
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(n);
                }
            }
            return null;
        }

        private double Range(double min, double max) => min + _rng.NextDouble() * (max - min);
    }
}
