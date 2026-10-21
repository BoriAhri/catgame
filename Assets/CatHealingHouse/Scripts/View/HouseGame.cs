using System.Collections.Generic;
using System.Linq;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.View.Art;
using CatHealingHouse.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 게임 진입점. 세계(두 보드), 고양이, 경제를 만들고 보드별 뷰·고양이 뷰·세로 화면 카메라·임시 UI 를 붙인다.
    /// 조작 UI 는 임시 IMGUI 이고, 8단계에서 진짜 UI 로 바꾼다.
    /// </summary>
    public sealed class HouseGame : MonoBehaviour
    {
        private enum Mode { Play, Place, Remove, Distance, Shop }

        [Header("레이아웃")]
        [SerializeField] private float hexSize = 0.5f;
        [SerializeField] private float yScale = 0.75f;

        [Header("시작 크기")]
        [SerializeField] private int indoorRadius = 5;
        [SerializeField] private int outdoorRadius = 4;

        [Header("그림")]
        [Tooltip("진짜 그림을 끼워 넣는 에셋. 비어 있는 칸은 코드로 그린 임시 그림을 씁니다.")]
        [SerializeField] private ArtLibrary artLibrary;

        [Header("카메라")]
        [SerializeField] private Color indoorBackground = new Color(0.36f, 0.30f, 0.27f);
        [SerializeField] private Color outdoorBackground = new Color(0.29f, 0.36f, 0.27f);

        // 기획서: 화면 전환 직후 몇 초는 길냥이 감지를 유예. 4단계에서 쓴다.
        public float LastBoardSwitchTime { get; private set; }

        public HouseWorld World { get; private set; }
        public DistanceMapCache DistanceMaps { get; private set; }
        public CatSimulation Sim { get; private set; }
        public HouseEconomy Economy { get; private set; }
        public StraySystem Strays { get; private set; }
        public Shop Shop { get; private set; }
        public Workshop Workshop { get; private set; }
        public ButlerSkills Skills { get; private set; }
        private bool _workshopOpen, _skillsOpen;
        private GUIStyle _popRight;
        private int _workshopTab;
        private Vector2 _popScroll;
        private float _popDragged;
        private Rect _workshopButtonGui, _skillButtonGui;
        public DayClock Clock { get; private set; }
        private EnvironmentView _env;
        private HashSet<HexCoord> _lit = new HashSet<HexCoord>();
        private int _litBucket = -1, _litVersion = -1;

        /// <summary>지금 창문 빛이 드는 실내 칸 (게임 3분마다, 또는 가구·크기가 바뀌면 다시 계산).</summary>
        private HashSet<HexCoord> LitNow()
        {
            int bucket = (int)(Clock.Hour * 20);
            if (bucket != _litBucket || World.Version != _litVersion)
            {
                _litBucket = bucket;
                _litVersion = World.Version;
                _lit = WindowLight.LitCells(World.Indoor, Clock.Hour);
            }
            return _lit;
        }

        private int _shopTab;            // 0 가구, 1 확장, 2 사료
        private Vector2 _shopScroll;
        private bool _shopOpen;
        private float _shopDragged;
        private Rect _shopButtonGui;
        public BoardId CurrentBoard { get; private set; } = BoardId.Indoor;

        private HexLayout _layout;
        private readonly Dictionary<BoardId, BoardView> _views = new Dictionary<BoardId, BoardView>();
        private readonly List<CatView> _catViews = new List<CatView>();
        private Camera _cam;
        private Material _material;
        private Font _font;

        private Mode _mode = Mode.Play;
        private FurnitureDef _selectedDef = FurnitureCatalog.FoodBowl;
        private bool _mirrored;
        private int _distanceFurnitureId = -1;
        private float _speed = 1f;
        private bool _showCatPanel = false; // 기본은 접어서 방을 크게
        private bool _viewsDirty;
        private int _foodSignature = -1;
        private int _fledOnEntry;

        // 길냥이 터치 메뉴 (호감도 41 이상)
        private Cat _menuCat;
        private Rect _menuRectGui;      // GUI 좌표 (가상 540 폭)
        private const float MenuW = 300f, MenuH = 196f;
        private float _lastZoomPresence;
        private int ResidentCount => Sim == null ? 0 : Sim.Cats.Count(c => c.Affiliation == Affiliation.Resident);

        private string _message = "고양이들을 구경해 보세요. 하트와 털뭉치를 탭하면 주워요.";
        private float _messageTime;
        private Vector2 _paletteScroll, _trayScroll;

        // 입력 상태
        private bool _pressing;
        private bool _dragging;
        private Vector2 _pressPos;
        private Vector2 _lastPointerPos;
        private float _lastPinchDistance = -1f;
        private const float DragThresholdPx = 18f;

        // UI 배치 (가상 해상도 540 폭 기준)
        private const float UiWidth = 540f;
        private const float TopBarH = 108f;
        private const float BottomBarH = 150f;
        private const float CatLineH = 50f;
        private float UiScale => Screen.width / UiWidth;
        private float TopUiH => TopBarH + 42 + (_showCatPanel && Sim != null ? ResidentCount * CatLineH + 8 : 0);

        private float _fitOrthoSize;
        private int _lastScreenW, _lastScreenH;

        // ───────────────────────── 시작 ─────────────────────────

        private void Awake()
        {
            // 에디터 창이 포커스를 잃어도 고양이들은 계속 살아있게 (모바일에서는 영향 없음)
            Application.runInBackground = true;
            ArtLibrary.Active = artLibrary;

            _layout = new HexLayout(hexSize, yScale);
            World = new HouseWorld(indoorRadius, outdoorRadius);
            DistanceMaps = new DistanceMapCache(World);

            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _cam = camGo.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.transform.position = new Vector3(0, 0, -10);
            _cam.transform.rotation = Quaternion.identity;

            _material = new Material(Shader.Find("Sprites/Default")) { name = "CuteArt (runtime)" };
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            SeedSampleLayout();

            Sim = new CatSimulation(World, DistanceMaps, new CatTuning(), seed: System.Environment.TickCount);
            // 하루 시계: 현실 24분 = 게임 하루, 아침 7시에 시작. 시뮬레이션 시간으로 돌아서 배속도 따라간다.
            Clock = new DayClock(7);
            Sim.Stepped += dt => Clock.Advance(dt);
            Sim.IsSunny = c => c.Board == BoardId.Indoor && Daylight.Kind(Clock.Hour) == LightKind.Sun && LitNow().Contains(c.Coord);
            Economy = new HouseEconomy(Sim, seed: System.Environment.TickCount + 1);
            Economy.Changed += () => _viewsDirty = true;
            Economy.ItemSentToTray += OnItemSentToTray;

            Strays = new StraySystem(Sim, Economy, seed: System.Environment.TickCount + 2);
            // 시작 돈·사료 + 보관함에 여분 가구 두 개 (배치 모드를 바로 써 볼 수 있게)
            Shop = new Shop(World, Economy);
            Shop.AddToStorage(FurnitureCatalog.BoxSmall);
            Shop.AddToStorage(FurnitureCatalog.Cushion);
            Workshop = new Workshop(Economy.Wallet, Shop);
            Workshop.Changed += () => _viewsDirty = true;
            Skills = new ButlerSkills(Economy, Strays);
            Sim.CatAdded += OnCatAdded;
            Sim.CatRemoved += OnCatRemoved;
            Strays.Arrived += (c, p) => { if (CurrentBoard == BoardId.Indoor) Say($"마당에 누가 왔어요! 살짝 나가 볼까요?"); };
            Strays.FirstDiscovered += p => Say($"새 길냥이 발견: {p.Name}!");
            Strays.BecameReady += (c, p) => Say($"{p.Name}가 문 앞에서 기다려요. 탭해서 들여보내 주세요!");
            Strays.Adopted += (c, p) => { Say($"{p.Name}가 우리 집 고양이가 됐어요! ({c.Personality.DisplayName})"); FitCamera(); };
            Strays.Left += p => { if (CurrentBoard == BoardId.Outdoor && p.Affection >= 30) Say($"{p.Name}가 돌아갔어요. 또 올 거예요."); };

            foreach (BoardId id in new[] { BoardId.Indoor, BoardId.Outdoor })
            {
                var go = new GameObject(id == BoardId.Indoor ? "Board (실내)" : "Board (마당)");
                go.transform.SetParent(transform, false);
                var view = go.AddComponent<BoardView>();
                view.Init(World, Economy, id, _layout, _material, _font);
                view.Strays = Strays;
                view.VariantOf = PropVariant;
                _views[id] = view;
            }

            SpawnCats();

            var envGo = new GameObject("Environment");
            envGo.transform.SetParent(transform, false);
            _env = envGo.AddComponent<EnvironmentView>();
            _env.IndoorBase = indoorBackground;
            _env.OutdoorBase = outdoorBackground;
            _env.Init(World, Clock, _layout, _material, _cam, _views[BoardId.Indoor].transform, LitNow);
            CatView.WallSpotOf = (f, slot) => _views.TryGetValue(f.Board, out var bv) && bv.TryWallSpot(f, slot, out var sp) ? sp : (Vector3?)null;

            World.Changed += RefreshViews;
            ShowBoard(BoardId.Indoor);
        }

        private void OnDestroy()
        {
            if (World != null) World.Changed -= RefreshViews;
            if (_material != null) Destroy(_material);
        }

        private void SeedSampleLayout()
        {
            World.TryPlace(FurnitureCatalog.CatTower5, BoardId.Indoor, new HexCoord(-2, -1), false, out _);
            World.TryPlace(FurnitureCatalog.Bed, BoardId.Indoor, new HexCoord(1, -2), false, out _);
            World.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Indoor, new HexCoord(2, 0), false, out _);
            World.TryPlace(FurnitureCatalog.LitterBox, BoardId.Indoor, new HexCoord(-3, 2), false, out _);
            World.TryPlace(FurnitureCatalog.BigRug, BoardId.Indoor, new HexCoord(0, 1), false, out _);
            PlaceFirstFit(FurnitureCatalog.CatWheel, BoardId.Indoor, new HexCoord(-2, 3), new HexCoord(-3, 1), new HexCoord(3, -1));
            World.TryPlace(FurnitureCatalog.HidingHouse, BoardId.Indoor, new HexCoord(2, -3), false, out _);
            World.TryPlace(FurnitureCatalog.Scratcher, BoardId.Indoor, new HexCoord(-1, -2), false, out _);

            World.TryPlace(FurnitureCatalog.FoodBowl, BoardId.Outdoor, new HexCoord(0, 1), false, out _);
            World.TryPlace(FurnitureCatalog.Bench, BoardId.Outdoor, new HexCoord(-2, 2), false, out _);
            World.TryPlace(FurnitureCatalog.BoxSmall, BoardId.Outdoor, new HexCoord(2, -1), false, out _);
        }

        private int PropVariant(FurnitureInstance f)
        {
            if (RewardTable.IsLitterBox(f.Def)) return (int)Economy.StageOfLitter(f.Id);
            if (Strays.IsOutdoorBowl(f)) return Strays.FoodIn(f.Id) > 0 ? 0 : 1;
            return 0;
        }

        private int FoodSignature()
        {
            int s = 17;
            foreach (var f in World.Furniture) if (Strays.IsOutdoorBowl(f)) s = s * 31 + (Strays.FoodIn(f.Id) > 0 ? 1 : 0) + f.Id * 7;
            return s;
        }

        // ───────────────────────── 길냥이 화면 ─────────────────────────

        public static CatLook LookOf(Breed b)
        {
            switch (b)
            {
                case Breed.Cheese: return new CatLook(SdfCanvas.Col("#F5C27F"), CatPattern.Tabby, SdfCanvas.Col("#E0954F"), "stray_cheese");
                case Breed.Mackerel: return new CatLook(SdfCanvas.Col("#A9A39A"), CatPattern.Tabby, SdfCanvas.Col("#625B54"), "stray_mackerel");
                case Breed.Tuxedo: return new CatLook(SdfCanvas.Col("#4A444F"), CatPattern.Tuxedo, SdfCanvas.Col("#FBF3E6"), "stray_tuxedo");
                case Breed.Calico: return new CatLook(SdfCanvas.Col("#FBF3E6"), CatPattern.Calico, SdfCanvas.Col("#E89A5B"), "stray_calico");
                case Breed.White: return new CatLook(SdfCanvas.Col("#FFFCF6"), CatPattern.Solid, SdfCanvas.Col("#FFFFFF"), "stray_white");
                case Breed.Black: return new CatLook(SdfCanvas.Col("#3B3640"), CatPattern.Solid, Color.black, "stray_black");
                case Breed.Siamese: return new CatLook(SdfCanvas.Col("#F4E7D2"), CatPattern.Point, SdfCanvas.Col("#6B4E3D"), "stray_siamese");
                default: return new CatLook(Color.gray, CatPattern.Solid, Color.gray);
            }
        }

        private void OnCatAdded(Cat cat)
        {
            if (cat.Affiliation != Affiliation.Stray) return; // 집 고양이는 SpawnCats 가 직접 붙인다
            var p = Strays.ProfileOf(cat);
            AddCatView(cat, LookOf(p != null ? p.Breed : Breed.Cheese));
        }

        private void OnCatRemoved(Cat cat)
        {
            for (int i = _catViews.Count - 1; i >= 0; i--)
            {
                if (_catViews[i].Cat != cat) continue;
                if (_catViews[i] != null) Destroy(_catViews[i].gameObject);
                _catViews.RemoveAt(i);
            }
        }

        private CatView AddCatView(Cat cat, CatLook look)
        {
            var go = new GameObject($"Cat {cat.Name}");
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<CatView>();
            view.Strays = Strays;
            view.Init(cat, Sim, _layout, _material, _font, look);
            _catViews.Add(view);
            return view;
        }

        private static string TierText(StrayProfile p)
        {
            switch (p.Tier)
            {
                case AffectionTier.Wary: return "사람을 무서워해요";
                case AffectionTier.Shy: return "가까이 가면 도망가요";
                case AffectionTier.Friendly: return "조금 익숙해졌어요";
                case AffectionTier.Close: return "꽤 친해졌어요";
                default: return "우리 집에 올 준비가 됐어요";
            }
        }

        // ───────────────────────── 길냥이 터치 메뉴 ─────────────────────────

        private void OpenMenu(Cat cat)
        {
            _menuCat = cat;
            Strays.Hold(cat, true);
        }

        private void CloseMenu()
        {
            if (_menuCat != null) Strays.Hold(_menuCat, false);
            _menuCat = null;
            _menuRectGui = Rect.zero;
        }

        private void DoMenuAction(StrayAction a)
        {
            var cat = _menuCat;
            var p = Strays.ProfileOf(cat);
            CloseMenu();
            if (cat == null || p == null) return;
            var r = Strays.Interact(cat, a);
            string n = cat.Name;
            switch (r.Outcome)
            {
                case InteractOutcome.AlreadyDone: Say("이번에는 이미 했어요. 다음에 또 와요!"); break;
                case InteractOutcome.Unavailable: Say($"{n}가 지금은 바빠 보여요."); break;
                case InteractOutcome.Refused:
                    Say(a == StrayAction.Adopt ? $"{n}가 아직은 싫대요… 도망갔어요. (호감 -3, 오늘은 안 와요)"
                                               : $"{n}가 싫다고 도망갔어요… (호감 -3, 오늘은 안 와요)");
                    break;
                case InteractOutcome.Adopting: Say($"{n}가 졸졸 따라 들어와요!"); break;
                case InteractOutcome.Accepted:
                    string gain = r.AffectionDelta > 0 ? $" 호감 +{r.AffectionDelta:0}" : " (오늘은 충분히 쓰다듬었어요)";
                    Say(a == StrayAction.Snack ? $"{n}가 간식을 냠냠!{gain}"
                      : a == StrayAction.Play ? $"{n}와 신나게 놀았어요!{gain}"
                      : $"골골골… {n}가 좋아해요!{gain}");
                    break;
            }
        }

        private static string ChanceText(double c) =>
            c >= 1 ? "무조건 받아줘요" : $"받아줄 확률 {c * 100:0}%";

        private void DrawStrayMenu()
        {
            if (_menuCat == null) return;
            var cv = _catViews.FirstOrDefault(v => v != null && v.Cat == _menuCat);
            if (cv == null || !Strays.MenuAvailable(_menuCat) || CurrentBoard != BoardId.Outdoor) { CloseMenu(); return; }
            var p = Strays.ProfileOf(_menuCat);
            float s = UiScale;
            var sp = _cam.WorldToScreenPoint(cv.BodyCenter);
            float gx = sp.x / s, gy = (Screen.height - sp.y) / s;
            float h = Screen.height / s;
            var r = new Rect(gx - MenuW / 2, gy - MenuH - 40, MenuW, MenuH);
            if (r.y < TopUiH + 4) r.y = gy + 50;                  // 위가 막히면 고양이 아래로
            r.x = Mathf.Clamp(r.x, 6, UiWidth - MenuW - 6);
            r.y = Mathf.Clamp(r.y, TopUiH + 4, h - BottomBarH - MenuH - 4);
            _menuRectGui = r;

            GUI.DrawTexture(r, _toastTex, ScaleMode.StretchToFill);
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 56, 26), $"<b>{_menuCat.Name}</b> · {p.TemperamentName} · 호감 {p.Affection:0}", _menuTitle);
            if (GUI.Button(new Rect(r.xMax - 42, r.y + 6, 34, 30), "X", _btn)) { CloseMenu(); return; }

            var acts = new[] { StrayAction.Snack, StrayAction.Pet, StrayAction.Play, StrayAction.Adopt };
            float bw = (r.width - 36) / 2, bh = 70;
            for (int i = 0; i < acts.Length; i++)
            {
                var a = acts[i];
                var br = new Rect(r.x + 12 + (i % 2) * (bw + 12), r.y + 42 + (i / 2) * (bh + 8), bw, bh);
                string sub = Strays.AlreadyDone(_menuCat, a) && a != StrayAction.Pet ? "이번에 했어요"
                    : a == StrayAction.Snack || a == StrayAction.Play ? "항상 좋아해요"
                    : ChanceText(StraySystem.AcceptChance(a, p.Affection, p.Temperament));
                if (GUI.Button(br, $"{StraySystem.NameOf(a)}\n<size=13>{sub}</size>", _menuBtn)) { DoMenuAction(a); return; }
            }
        }
        // (구간 설명은 위 TierText)

        private void PlaceFirstFit(FurnitureDef def, BoardId board, params HexCoord[] candidates)
        {
            foreach (var c in candidates)
                foreach (bool m in new[] { false, true })
                    if (World.TryPlace(def, board, c, m, out _) == PlaceResult.Ok) return;
            Debug.LogWarning($"[HouseGame] 샘플 배치 실패: {def.DisplayName}");
        }

        private void SpawnCats()
        {
            var cats = new[]
            {
                ("치즈", new HexCoord(0, 0), new CatLook(SdfCanvas.Col("#F7BE7A"), CatPattern.Tabby, SdfCanvas.Col("#E3914A"), "cheese"), CatPersonalities.Foodie()),
                ("까망이", new HexCoord(2, 1), new CatLook(SdfCanvas.Col("#4A444F"), CatPattern.Solid, Color.black, "kkamang"), CatPersonalities.TowerKing()),
                ("고등어", new HexCoord(-1, 2), new CatLook(SdfCanvas.Col("#B3ACA1"), CatPattern.Tabby, SdfCanvas.Col("#6F675F"), "godeungeo"), CatPersonalities.Sleepyhead()),
                ("삼색이", new HexCoord(1, 2), new CatLook(SdfCanvas.Col("#FBF3E6"), CatPattern.Calico, SdfCanvas.Col("#F0A766"), "samsaek"), CatPersonalities.Energizer()),
            };
            var rng = new System.Random();
            foreach (var (name, coord, look, personality) in cats)
            {
                var cell = new Cell(BoardId.Indoor, coord);
                if (!World.IsTransit(cell))
                    cell = World.Indoor.Tiles.Values.Where(t => t.Kind == TileKind.Floor && World.IsTransit(new Cell(BoardId.Indoor, t.Coord)))
                        .Select(t => new Cell(BoardId.Indoor, t.Coord)).First();
                var cat = Sim.AddCat(name, cell, personality);
                // 시작 욕구를 섞어서 고양이들이 동시에 같은 행동을 하지 않게
                foreach (var n in CatNeeds.All) cat.Needs.Set(n, 50 + rng.NextDouble() * 50);

                AddCatView(cat, look);
            }
        }

        // ───────────────────────── 보드 전환·카메라 ─────────────────────────

        public void ShowBoard(BoardId id)
        {
            CloseMenu();
            CurrentBoard = id;
            _env?.SetBoard(id);
            LastBoardSwitchTime = Time.time;
            _fledOnEntry = Strays != null ? Strays.NotifyBoardEntered(id) : 0;
            if (_fledOnEntry > 0) Say(_fledOnEntry == 1 ? "문 여는 소리에 길냥이가 놀라 도망갔어요!" : $"문 여는 소리에 길냥이 {_fledOnEntry}마리가 도망갔어요!");
            foreach (var kv in _views) kv.Value.gameObject.SetActive(kv.Key == id);
            _cam.backgroundColor = id == BoardId.Indoor ? indoorBackground : outdoorBackground;
            FitCamera();
            RefreshViews();
        }

        private void FitCamera()
        {
            var board = World.GetBoard(CurrentBoard);
            int rings = board.Radius + 1;
            float width = (float)_layout.CellWidth * (2 * rings + 1);
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            float usableFrac = 1f - (TopUiH + BottomBarH) * UiScale / Mathf.Max(1, Screen.height);
            float sizeByWidth = width * 1.04f / aspect / 2f;
            float height = (float)(_layout.Size * 1.5 * _layout.YScale) * (2 * rings) + (float)(_layout.Size * 4.6);
            float sizeByHeight = height * 1.04f / 2f / Mathf.Max(0.2f, usableFrac);
            _fitOrthoSize = Mathf.Max(sizeByWidth, sizeByHeight);
            _cam.orthographicSize = _fitOrthoSize;

            float worldPerPx = 2f * _fitOrthoSize / Mathf.Max(1, Screen.height);
            float yOffset = (BottomBarH - TopUiH) * UiScale * 0.5f * worldPerPx;
            // 실내는 위로 높은 벽이 있으니 그만큼 카메라를 올려 방 전체가 가운데 오게
            float lift = CurrentBoard == BoardId.Indoor ? 1.9f : 0.3f;
            _cam.transform.position = new Vector3(0, -yOffset + (float)_layout.Size * lift, -10);
        }

        private void RefreshViews()
        {
            _viewsDirty = false;
            var map = _distanceFurnitureId > 0 ? DistanceMaps.Get(_distanceFurnitureId) : null;
            if (map == null) _distanceFurnitureId = -1;
            foreach (var v in _views.Values)
            {
                // 육각형 칸은 가구를 놓을 때(와 거리맵)만 보인다
                v.ShowGrid = _mode == Mode.Place || _mode == Mode.Distance;
                v.MarkOccupied = _mode == Mode.Place;
                if (_mode != Mode.Place) v.HidePreview();
                v.Refresh(_mode == Mode.Distance ? map : null, _distanceFurnitureId);
                v.ShowWallSlots(_mode == Mode.Place ? _selectedDef : null);
            }
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void Update()
        {
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                _lastScreenW = Screen.width;
                _lastScreenH = Screen.height;
                FitCamera();
            }

            // 시뮬레이션은 보고 있는 보드와 상관없이 항상 진행
            Sim.Advance(Time.deltaTime * _speed);
            int food = FoodSignature();
            if (food != _foodSignature) { _foodSignature = food; _viewsDirty = true; }
            if (_viewsDirty) RefreshViews();
            foreach (var v in _catViews.ToArray()) if (v != null) v.Sync(CurrentBoard);

            HandleZoom();
            HandlePointer();
        }

        private void Say(string msg)
        {
            _message = msg;
            _messageTime = Time.time;
        }

        // ───────────────────────── 날아가는 결과물 ─────────────────────────

        private void OnItemSentToTray(FurnitureInstance f, ItemKind kind, int count)
        {
            if (f.Board != CurrentBoard) return;
            var from = _views[f.Board].HeartPosition(f) - new Vector3(0, (float)_layout.Size * 0.4f, 0);
            for (int i = 0; i < Mathf.Min(count, 5); i++)
                Flyer.Launch(transform, _cam, CuteArt.Item(kind), _material, from, TrayTargetScreen(), (float)_layout.Size * 0.6f, i * 0.12f);
        }

        private Vector2 TrayTargetScreen() => new Vector2(Screen.width * 0.5f, BottomBarH * UiScale * 0.45f);

        private Vector2 MoneyChipScreen() => new Vector2(30 * UiScale, Screen.height - 76 * UiScale);

        // ───────────────────────── 입력 ─────────────────────────

        private bool OverUi(Vector2 screenPos)
        {
            float yFromTop = Screen.height - screenPos.y;
            if (_menuCat != null && _menuRectGui.Contains(new Vector2(screenPos.x / UiScale, yFromTop / UiScale))) return true;
            if (_shopOpen || _workshopOpen || _skillsOpen) return true; // 창이 열려 있으면 보드는 터치 안 받음
            var gui = new Vector2(screenPos.x / UiScale, yFromTop / UiScale);
            if (_shopButtonGui.Contains(gui) || _workshopButtonGui.Contains(gui) || _skillButtonGui.Contains(gui)) return true;
            return yFromTop < TopUiH * UiScale || screenPos.y < BottomBarH * UiScale;
        }

        private void HandleZoom()
        {
            float factor = 1f;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !OverUi(mouse.position.ReadValue())) factor = scroll > 0 ? 0.9f : 1.1f;
            }

            var ts = Touchscreen.current;
            if (ts != null)
            {
                var pressed = ts.touches.Where(t => t.press.isPressed).Take(2).ToList();
                if (pressed.Count == 2)
                {
                    float dist = Vector2.Distance(pressed[0].position.ReadValue(), pressed[1].position.ReadValue());
                    if (_lastPinchDistance > 0f && dist > 1f) factor = _lastPinchDistance / dist;
                    _lastPinchDistance = dist;
                    _dragging = true; // 핀치 중에는 탭으로 치지 않음
                }
                else _lastPinchDistance = -1f;
            }

            if (!Mathf.Approximately(factor, 1f))
            {
                _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize * factor, _fitOrthoSize * 0.35f, _fitOrthoSize * 1.25f);
                // 기획서: 확대는 가까이 다가가는 것. 많이 확대하면 화면 가운데에 기척이 생긴다.
                if (factor < 1f && CurrentBoard == BoardId.Outdoor && _cam.orthographicSize < _fitOrthoSize * 0.6f && Time.time - _lastZoomPresence > 1.2f)
                {
                    _lastZoomPresence = Time.time;
                    var c = _cam.transform.position;
                    int n = Strays.Presence(new Cell(BoardId.Outdoor, _layout.FromWorld(c.x, c.y)));
                    if (n > 0) Say("너무 가까이 다가갔어요!");
                }
            }
        }

        private void HandlePointer()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            if (_mode == Mode.Place && _selectedDef != null) { HandlePlacePointer(pointer, pos); return; }

            if (pointer.press.wasPressedThisFrame)
            {
                if (OverUi(pos)) return;
                _pressing = true;
                _dragging = false;
                _pressPos = pos;
                _lastPointerPos = pos;
            }
            else if (_pressing && pointer.press.isPressed)
            {
                if (!_dragging && Vector2.Distance(pos, _pressPos) > DragThresholdPx) _dragging = true;
                if (_dragging && _lastPinchDistance < 0f)
                {
                    var a = _cam.ScreenToWorldPoint(new Vector3(_lastPointerPos.x, _lastPointerPos.y, 10));
                    var b = _cam.ScreenToWorldPoint(new Vector3(pos.x, pos.y, 10));
                    _cam.transform.position += a - b;
                }
                _lastPointerPos = pos;
            }
            else if (_pressing && pointer.press.wasReleasedThisFrame)
            {
                _pressing = false;
                if (!_dragging) OnTap(pos);
            }
        }

        // ───────────────────────── 가구 놓기 (미리보기) ─────────────────────────

        private bool _placePressing, _placePinched, _hasPreview;
        private HexCoord _previewCoord;

        /// <summary>
        /// 놓기 모드의 터치: 누르고 있는 동안 손가락 칸에 미리보기가 따라다니고,
        /// 손을 떼면 그 칸에 바로 놓는다 (짧게 탭해도 같다). 두 손가락(확대)이면 놓지 않는다.
        /// </summary>
        private void HandlePlacePointer(Pointer pointer, Vector2 pos)
        {
            if (pointer.press.wasPressedThisFrame)
            {
                if (OverUi(pos)) return;
                _placePressing = true;
                _placePinched = false;
                UpdatePreviewAt(pos);
            }
            else if (_placePressing && pointer.press.isPressed)
            {
                if (_lastPinchDistance > 0f) _placePinched = true;
                if (!_placePinched) UpdatePreviewAt(pos);
            }
            else if (_placePressing && pointer.press.wasReleasedThisFrame)
            {
                _placePressing = false;
                if (!_placePinched && _hasPreview) PlaceSelectedAt(_previewCoord);
            }
        }

        private void UpdatePreviewAt(Vector2 screenPos)
        {
            var wp = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10));
            var coord = _layout.FromWorld(wp.x, wp.y);
            if (_selectedDef != null && _selectedDef.WallMounted)
            {
                var slot = _views[CurrentBoard].NearestWallSlot(wp);
                if (!slot.HasValue) return;
                coord = slot.Value;
            }
            if (World.GetBoard(CurrentBoard).Get(coord) == null) return;
            ShowPreviewAt(coord);
        }

        private void ShowPreviewAt(HexCoord coord)
        {
            _previewCoord = coord;
            _hasPreview = true;
            bool ok = World.CanPlace(_selectedDef, CurrentBoard, coord, _mirrored) == PlaceResult.Ok;
            _views[CurrentBoard].ShowPreview(_selectedDef, coord, _mirrored, ok);
        }

        /// <summary>놓기를 시작하면 보드 가운데에서 가장 가까운 놓을 수 있는 자리에 미리보기를 띄운다 (안내용).</summary>
        private void ShowInitialPreview()
        {
            _hasPreview = false;
            if (_selectedDef == null) return;
            var board = World.GetBoard(CurrentBoard);
            HexCoord? best = null;
            int bestD = int.MaxValue;
            foreach (var t in board.Tiles.Values)
            {
                if (_selectedDef.WallMounted ? t.Kind != TileKind.Wall : (t.Kind != TileKind.Floor || t.Furniture != null)) continue;
                int d = _selectedDef.WallMounted ? Mathf.Abs(t.Coord.Q + t.Coord.R / 2) + (t.Coord.R + 99) : HexCoord.Distance(t.Coord, HexCoord.Zero);
                if (d >= bestD) continue;
                if (World.CanPlace(_selectedDef, CurrentBoard, t.Coord, _mirrored) != PlaceResult.Ok) continue;
                best = t.Coord; bestD = d;
            }
            if (best.HasValue) ShowPreviewAt(best.Value);
            else Say($"{_selectedDef.DisplayName}을(를) 놓을 자리가 없어요. 가구를 치우거나 넓혀 보세요.");
        }

        private void PlaceSelectedAt(HexCoord coord)
        {
            var r = Shop.BuyOrPlace(_selectedDef, CurrentBoard, coord, _mirrored, out _, out var sr, out long paid);
            if (r == PlaceResult.Ok)
            {
                Say(paid > 0 ? $"{_selectedDef.DisplayName} 구입해서 놓았어요! (-{paid:N0}원)" : $"보관함에 있던 {_selectedDef.DisplayName}을(를) 놓았어요");
                SetMode(Mode.Play);
            }
            else if (sr == ShopResult.NotEnoughMoney) { Say($"돈이 모자라요 ({Shop.PriceOf(_selectedDef):N0}원)"); SetMode(Mode.Play); }
            else Say($"여기엔 못 놓아요: {Explain(r)}");
        }

        /// <summary>
        /// 탭 우선순위 (기획서): 문 → 바닥 드롭 → 가구 위 하트 → 고양이 → 가구.
        /// 배치·철거·거리맵 모드에서는 해당 기능만.
        /// </summary>
        private void OnTap(Vector2 screenPos)
        {
            var wp = (Vector2)_cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10));
            var coord = _layout.FromWorld(wp.x, wp.y);
            var view = _views[CurrentBoard];
            var tile = World.GetBoard(CurrentBoard).Get(coord);

            // 메뉴가 열려 있을 때 바깥을 터치하면 닫기만 한다
            if (_menuCat != null) { CloseMenu(); return; }

            if (tile != null && tile.Kind == TileKind.Door)
            {
                ShowBoard(CurrentBoard == BoardId.Indoor ? BoardId.Outdoor : BoardId.Indoor);
                if (_fledOnEntry == 0) Say(CurrentBoard == BoardId.Indoor ? "집 안으로 들어왔어요." : "마당으로 나왔어요. 밥그릇을 탭해서 채워 두면 길냥이가 와요.");
                return;
            }

            switch (_mode)
            {
                case Mode.Play: TapPlay(wp, coord, view); break;
                case Mode.Place:
                {
                    if (tile == null) return;
                    if (_selectedDef == null) { SetMode(Mode.Play); break; }
                    PlaceSelectedAt(coord);
                    break;
                }
                case Mode.Remove:
                {
                    var f = view.HitFurniture(wp, coord);
                    if (f != null)
                    {
                        Shop.Store(f.Id);
                        Say($"{f.Def.DisplayName}을(를) 보관함으로 옮겼어요");
                    }
                    else Say("여기엔 가구가 없어요.");
                    break;
                }
                case Mode.Distance:
                {
                    var f = view.HitFurniture(wp, coord);
                    if (f != null && f.Def.Capacity > 0)
                    {
                        _distanceFurnitureId = f.Id;
                        Say($"{f.Def.DisplayName}까지의 거리 (문 너머까지 이어짐)");
                    }
                    else
                    {
                        _distanceFurnitureId = -1;
                        Say("고양이가 쓸 수 있는 가구를 탭하면 거리맵을 보여줘요.");
                    }
                    RefreshViews();
                    break;
                }
            }
        }

        private CatView HitCat(Vector2 wp)
        {
            CatView best = null;
            float bestD = (float)_layout.Size * 0.6f;
            foreach (var cv in _catViews)
            {
                if (cv == null || cv.Cat.Cell.Board != CurrentBoard) continue;
                float d = Vector2.Distance(wp, cv.BodyCenter);
                if (d < bestD) { bestD = d; best = cv; }
            }
            return best;
        }

        private void TapPlay(Vector2 wp, HexCoord coord, BoardView view)
        {
            // 마당: 터치 지점이 곧 기척. 문 앞에서 기다리는 고양이는 탭하면 영입.
            if (CurrentBoard == BoardId.Outdoor)
            {
                var hit = HitCat(wp);
                var stray = hit != null && hit.Cat.Affiliation == Affiliation.Stray ? hit.Cat : null;
                // 41 이상: 터치하면 메뉴
                if (stray != null && Strays.MenuAvailable(stray)) { OpenMenu(stray); return; }
                var before = stray != null ? stray.StrayState : StrayState.None;
                int fled = Strays.Presence(new Cell(BoardId.Outdoor, coord), stray);
                if (stray != null)
                {
                    var p = Strays.ProfileOf(stray);
                    if (stray.StrayState == StrayState.Fleeing && before != StrayState.Fleeing) Say($"{stray.Name}가 놀라서 도망갔어요… 멀리서 지켜봐 주세요. (호감 -3)");
                    else if (p != null) Say($"{stray.Name} · {p.TemperamentName} · 호감도 {p.Affection:0} · {TierText(p)}");
                    return;
                }
                if (fled > 0) Say("인기척에 길냥이가 놀라 도망갔어요… (호감 -3)");
            }
            if (view.TryHitDrop(wp, out int dropId))
            {
                var d = Economy.Drops.FirstOrDefault(x => x.Id == dropId);
                if (d != null && Economy.PickDrop(dropId))
                {
                    Flyer.Launch(transform, _cam, CuteArt.Item(d.Kind), _material, _layout.ToWorld(d.Cell.Coord), MoneyChipScreen() + new Vector2(d.Kind == ItemKind.Furball ? 175 : d.Kind == ItemKind.Gift ? 93 : 257, 0) * UiScale, (float)_layout.Size * 0.5f, 0);
                    Say($"{RewardTable.Name(d.Kind)} +1");
                }
                return;
            }
            if (view.TryHitHeart(wp, out int heartFurniture))
            {
                var f = World.GetFurniture(heartFurniture);
                if (Economy.CollectHeart(heartFurniture))
                {
                    if (f != null) Flyer.Launch(transform, _cam, CuteArt.Item(ItemKind.Heart), _material, view.HeartPosition(f), MoneyChipScreen() + new Vector2(93, 0) * UiScale, (float)_layout.Size * 0.6f, 0);
                    Say("젤리 하트 +1  고마워냥!");
                }
                return;
            }
            foreach (var cv in _catViews)
            {
                if (cv.Cat.Cell.Board != CurrentBoard) continue;
                if (Vector2.Distance(wp, cv.BodyCenter) < (float)_layout.Size * 0.6f)
                {
                    var c = cv.Cat;
                    var tf = c.HasTarget ? World.GetFurniture(c.TargetFurnitureId) : null;
                    Say($"{c.Name} ({c.Personality.DisplayName}) · 기분 {c.Mood:0} · {CatView.ActivityText(c, tf)}");
                    return;
                }
            }
            var furniture = view.HitFurniture(wp, coord);
            if (furniture != null)
            {
                if (Strays.IsOutdoorBowl(furniture))
                {
                    var fr = Shop.FillBowl(Strays, furniture.Id);
                    if (fr == ShopResult.Ok) Say($"사료를 부었어요 (남은 사료 {Economy.Wallet.Kibble}). 멀리서 기다려 볼까요?");
                    else if (fr == ShopResult.NotEnoughStock) Say("사료가 없어요. 상점에서 사 오세요.");
                    else Say($"밥이 아직 남아 있어요 ({Strays.FoodIn(furniture.Id)}끼).");
                    return;
                }
                if (RewardTable.IsLitterBox(furniture.Def))
                {
                    int uses = Economy.UsesOfLitter(furniture.Id);
                    if (Economy.CleanLitter(furniture.Id)) Say($"화장실 청소 완료! 감자·맛동산 {uses}개가 트레이로. 고양이들 기분 UP");
                    else Say("화장실이 깨끗해요!");
                }
                else Say($"{furniture.Def.DisplayName} · 사용 중 {furniture.OccupiedSlots}/{furniture.Def.Capacity}");
            }
        }

        private static string Explain(PlaceResult r)
        {
            switch (r)
            {
                case PlaceResult.WrongZone: return "이 구역에는 놓을 수 없는 가구";
                case PlaceResult.OutOfBoard: return "보드 밖으로 나감";
                case PlaceResult.NotFloor: return "벽·수풀·문 위";
                case PlaceResult.Occupied: return "다른 가구와 겹침";
                case PlaceResult.BlocksPath: return "길이 막히거나 고립되는 칸이 생김";
                case PlaceResult.NotWall: return "벽 가구는 위쪽 벽에만 (아래 바닥이 입구)";
                case PlaceResult.Window: return "창문 자리";
                default: return r.ToString();
            }
        }

        // ───────────────────────── 임시 UI (IMGUI) ─────────────────────────

        private GUIStyle _btn, _btnOn, _btnOff, _label, _small, _chip, _trayCount, _trayValue, _toast, _menuTitle, _menuBtn, _menuTitleLight, _shopSmall,
            _shopHint, _shopHintCenter, _shopNameCenter, _shopBtnLabel, _placeTitle, _clockStyle;
        private Texture2D _panelTex, _slotTex, _toastTex;

        private void EnsureStyles()
        {
            if (_btn != null) return;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 17, wordWrap = true };
            _btnOn = new GUIStyle(_btn);
            _btnOn.normal = _btnOn.active;
            _btnOn.normal.textColor = new Color(1f, 0.85f, 0.55f);
            _btnOn.fontStyle = FontStyle.Bold;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _small.normal.textColor = Color.white;
            _chip = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _chip.normal.textColor = new Color(1f, 0.97f, 0.9f);
            _trayCount = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            _trayCount.normal.textColor = SdfCanvas.Col("#5E4B3C");
            _trayValue = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerCenter };
            _trayValue.normal.textColor = SdfCanvas.Col("#5E4B3C");
            _toast = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _toast.normal.textColor = SdfCanvas.Col("#5E4B3C");
            _menuTitle = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.MiddleLeft };
            _menuTitle.normal.textColor = SdfCanvas.Col("#5E4B3C");
            _menuTitleLight = new GUIStyle(_menuTitle) { fontSize = 20, alignment = TextAnchor.MiddleRight };
            _menuTitleLight.normal.textColor = new Color(1f, 0.97f, 0.9f);
            _shopSmall = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            _shopSmall.normal.textColor = SdfCanvas.Col("#7A6352");
            _shopHint = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _shopHint.normal.textColor = new Color(1f, 0.95f, 0.86f);
            _shopHintCenter = new GUIStyle(_shopSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _shopNameCenter = new GUIStyle(_menuTitle) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _shopBtnLabel = new GUIStyle(_menuTitle) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _placeTitle = new GUIStyle(_menuTitleLight) { alignment = TextAnchor.MiddleLeft };
            _clockStyle = new GUIStyle(_chip) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
            _btnOff = new GUIStyle(_btn);
            _btnOff.normal.textColor = new Color(0.75f, 0.68f, 0.62f);
            _btnOff.hover.textColor = _btnOff.normal.textColor;
            _menuBtn = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true, wordWrap = true, fontStyle = FontStyle.Bold };
            _panelTex = CuteArt.Panel(SdfCanvas.Col("#B98A62"), SdfCanvas.Col("#5E4B3C"));
            _slotTex = CuteArt.Panel(SdfCanvas.Col("#FBF4E6"), SdfCanvas.Col("#8E6A4B"));
            _toastTex = CuteArt.Panel(new Color(0.98f, 0.95f, 0.89f, 0.95f), SdfCanvas.Col("#B98A62"));
        }

        private void OnGUI()
        {
            EnsureStyles();
            float s = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float h = Screen.height / s;

            // ── 위쪽 바: 보드 전환 + 모드
            GUI.Box(new Rect(0, 0, UiWidth, TopBarH), GUIContent.none);
            float x = 6, y = 6, bh = 42;
            int outside = Strays.StrayCount;
            string boardLabel = CurrentBoard == BoardId.Indoor ? (outside > 0 ? $"마당에 냥{outside} ▶" : "실내 ▶ 마당") : "마당 ▶ 실내";
            if (GUI.Button(new Rect(x, y, 116, bh), boardLabel, CurrentBoard == BoardId.Indoor && outside > 0 ? _btnOn : _btn))
                ShowBoard(CurrentBoard == BoardId.Indoor ? BoardId.Outdoor : BoardId.Indoor);
            x += 120;
            ModeButton(new Rect(x, y, 100, bh), "구경", Mode.Play); x += 104;
            ModeButton(new Rect(x, y, 100, bh), "치우기", Mode.Remove);

            // ── 시계 (게임 하루 = 현실 24분)
            {
                var kind = Daylight.Kind(Clock.Hour);
                var icon = kind == LightKind.Moon ? CuteArt.Moon() : CuteArt.Disk();
                var old = GUI.color;
                GUI.color = kind == LightKind.Moon ? SdfCanvas.Col("#FFF3C4") : kind == LightKind.Sun ? SdfCanvas.Col("#FFD36B") : SdfCanvas.Col("#B9A3D6");
                CuteArt.DrawSprite(new Rect(UiWidth - 198, 12, 28, 28), icon);
                GUI.color = old;
                GUI.Label(new Rect(UiWidth - 166, 4, 162, 42), $"{Clock.Day}일차 {Clock.Label}", _clockStyle);
            }

            // ── 재화
            y = 56;
            x = 8;
            Chip(ref x, y, ItemKind.Money, Economy.Wallet.Money);
            Chip(ref x, y, ItemKind.Heart, Economy.Wallet.Hearts);
            Chip(ref x, y, ItemKind.Furball, Economy.Wallet.Furballs);
            Chip(ref x, y, ItemKind.Whisker, Economy.Wallet.Whiskers);
            Chip(ref x, y, ItemKind.Kibble, Economy.Wallet.Kibble);

            var board = World.GetBoard(CurrentBoard);
            if (_mode == Mode.Place || _mode == Mode.Remove || _mode == Mode.Distance)
            {
                if (GUI.Button(new Rect(UiWidth - 104, y, 98, bh), _mode == Mode.Distance ? "거리맵 끄기" : "거리맵", _mode == Mode.Distance ? _btnOn : _btn))
                {
                    _mode = _mode == Mode.Distance ? Mode.Place : Mode.Distance;
                    Say(_mode == Mode.Distance ? "고양이가 쓸 수 있는 가구를 탭하세요." : "가구를 골라 놓을 칸을 탭하세요.");
                    RefreshViews();
                }
            }

            // ── 고양이 정보 + 배속 (+ 배치 모드에선 확장·반전)
            DrawCatPanel(board);

            // ── 알림
            if (!string.IsNullOrEmpty(_message) && Time.time - _messageTime < 4f)
            {
                var r = new Rect(30, TopUiH + 6, UiWidth - 60, 40);
                GUI.DrawTexture(r, _toastTex, ScaleMode.StretchToFill);
                GUI.Label(r, _message, _toast);
            }

            // ── 아래쪽 바
            if (_mode == Mode.Place || _mode == Mode.Distance) DrawPlaceBar(h);
            else DrawTray(h);

            // ── 상점 버튼 (오른쪽 아래, 트레이 위) ── 구경 모드에서만
            _shopButtonGui = _workshopButtonGui = _skillButtonGui = Rect.zero;
            if (_mode == Mode.Play && !_shopOpen && !_workshopOpen && !_skillsOpen)
            {
                _shopButtonGui = FloatButton(new Rect(UiWidth - 122, h - BottomBarH - 66, 112, 58), ItemKind.Money, "상점");
                if (GUI.Button(_shopButtonGui, GUIContent.none, GUIStyle.none)) { _shopOpen = true; _shopScroll = Vector2.zero; CloseMenu(); }
                _workshopButtonGui = FloatButton(new Rect(UiWidth - 240, h - BottomBarH - 66, 112, 58), ItemKind.Furball, "공방");
                if (GUI.Button(_workshopButtonGui, GUIContent.none, GUIStyle.none)) { _workshopOpen = true; _popScroll = Vector2.zero; CloseMenu(); }
                _skillButtonGui = FloatButton(new Rect(UiWidth - 358, h - BottomBarH - 66, 112, 58), ItemKind.Heart, "스킬");
                if (GUI.Button(_skillButtonGui, GUIContent.none, GUIStyle.none)) { _skillsOpen = true; _popScroll = Vector2.zero; CloseMenu(); }
            }
            if (_shopOpen) DrawShopPopup(h);
            if (_workshopOpen) DrawWorkshopPopup(h);
            if (_skillsOpen) DrawSkillsPopup(h);

            // ── 길냥이 메뉴 (맨 위에)
            DrawStrayMenu();
        }

        private void ModeButton(Rect r, string label, Mode m)
        {
            bool on = _mode == m || (m == Mode.Place && _mode == Mode.Distance);
            if (GUI.Button(r, label, on ? _btnOn : _btn) && _mode != m)
            {
                _mode = m;
                Say(m == Mode.Play ? "구경 모드: 하트·털뭉치를 줍고, 화장실을 탭하면 청소해요."
                    : m == Mode.Shop ? "상점: 가구를 사면 보관함으로 가요. 배치에서 꺼내 놓으세요."
                    : m == Mode.Place ? "보관함의 가구를 골라 놓을 칸을 탭하세요."
                    : "치울 가구를 탭하세요. 보관함으로 돌아가요.");
                RefreshViews();
                FitCamera();
            }
        }

        private void Chip(ref float x, float y, ItemKind kind, long value)
        {
            CuteArt.DrawIcon(new Rect(x, y + 6, 30, 30), kind);
            GUI.Label(new Rect(x + 32, y, 56, 42), value.ToString("N0"), _chip);
            x += kind == ItemKind.Money ? 100 : 82;
        }

        private void DrawCatPanel(HexBoard board)
        {
            float y = TopBarH + 4;
            if (GUI.Button(new Rect(6, y, 128, 34), _showCatPanel ? "고양이 정보 ▲" : "고양이 정보 ▼", _btn))
            {
                _showCatPanel = !_showCatPanel;
                FitCamera();
            }
            // (반전 버튼은 아래 놓기 막대로 옮김)
            if (GUI.Button(new Rect(UiWidth - 104, y, 98, 34), $"배속 x{_speed:0}", _btn))
                _speed = _speed >= 10f ? 1f : _speed >= 4f ? 10f : 4f;
            if (!_showCatPanel) return;

            y += 38;
            GUI.Box(new Rect(4, y, UiWidth - 8, ResidentCount * CatLineH + 8), GUIContent.none);
            foreach (var cat in Sim.Cats)
            {
                if (cat.Affiliation != Affiliation.Resident) continue;
                var f = cat.HasTarget ? World.GetFurniture(cat.TargetFurnitureId) : null;
                string Need(string label, double v) => v < Sim.Tuning.NeedThreshold ? $"<color=#FFB08A>{label} {v:0}</color>" : $"{label} {v:0}";
                string mood = cat.Mood >= 80 ? " 행복" : cat.Mood < 50 ? " 시무룩" : "";
                GUI.Label(new Rect(12, y + 4, UiWidth - 24, CatLineH),
                    $"<b>{cat.Name}</b> <color=#C9B8A6>({cat.Personality.DisplayName})</color>  {CatView.ActivityText(cat, f)}  <color=#F4C2C2>기분 {cat.Mood:0}{mood}</color>\n" +
                    $"{Need("배고픔", cat.Needs.Hunger)} · {Need("배변", cat.Needs.Bladder)} · {Need("에너지", cat.Needs.Energy)} · {Need("즐거움", cat.Needs.Fun)}",
                    _small);
                y += CatLineH;
            }
        }

        private void DrawTray(float h)
        {
            var area = new Rect(0, h - BottomBarH, UiWidth, BottomBarH);
            GUI.DrawTexture(area, _panelTex, ScaleMode.StretchToFill);
            GUI.Label(new Rect(14, area.y + 6, UiWidth - 28, 22), Economy.Tray.Count == 0
                ? "트레이 · 고양이가 가구를 쓰면 여기에 쌓여요"
                : $"트레이 · 탭한 칸만 돈으로 바뀌어요 (칸마다 최대 {Economy.Tuning.TraySlotCap}개)", _small);

            float slotW = 92, slotH = 100;
            var view = new Rect(8, area.y + 32, UiWidth - 16, slotH + 14);
            var content = new Rect(0, 0, Mathf.Max(view.width - 4, Economy.Tray.Count * (slotW + 6)), slotH);
            _trayScroll = GUI.BeginScrollView(view, _trayScroll, content, false, false);
            for (int i = 0; i < Economy.Tray.Count; i++)
            {
                var slot = Economy.Tray[i];
                var r = new Rect(i * (slotW + 6), 0, slotW, slotH);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                CuteArt.DrawIcon(new Rect(r.x + 18, r.y + 10, 56, 56), slot.Kind);
                GUI.Label(new Rect(r.x, r.y + 6, r.width - 10, 22), $"×{slot.Count}", _trayCount);
                GUI.Label(new Rect(r.x, r.y, r.width, r.height - 8), $"{RewardTable.Name(slot.Kind)}\n{slot.Value}원", _trayValue);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    int index = i;
                    long gained = Economy.CollectTraySlot(index);
                    if (gained > 0)
                    {
                        var start = SlotWorld(r, view);
                        for (int k = 0; k < 3; k++)
                            Flyer.Launch(transform, _cam, CuteArt.Item(ItemKind.Money), _material, start, MoneyChipScreen(), (float)_layout.Size * 0.5f, k * 0.08f);
                        Say($"{RewardTable.Name(slot.Kind)} {slot.Count}개 → +{gained}원");
                    }
                    break;
                }
            }
            GUI.EndScrollView();
        }

        private Vector3 SlotWorld(Rect slotRect, Rect view)
        {
            float s = UiScale;
            float sx = (view.x + slotRect.center.x - _trayScroll.x) * s;
            float sy = Screen.height - (view.y + slotRect.center.y) * s;
            var w = _cam.ScreenToWorldPoint(new Vector3(sx, sy, 10));
            w.z = 0;
            return w;
        }

        // (가구 고르기는 상점 창에서: 보관함에 있는 가구도 거기 표시된다)

        // ───────────────────────── 상점 ─────────────────────────

        private static string ZoneText(FurnitureDef d) =>
            d.AllowedOn(BoardId.Indoor) && d.AllowedOn(BoardId.Outdoor) ? "실내·마당" : d.AllowedOn(BoardId.Outdoor) ? "마당 전용" : "실내 전용";

        /// <summary>화면 가운데 상점 창. 가구를 누르면 창이 닫히고 놓을 칸을 고른다.</summary>
        private void DrawShopPopup(float h)
        {
            // 뒷배경을 살짝 어둡게
            GUI.DrawTexture(new Rect(0, 0, UiWidth, h), Solid(new Color(0.12f, 0.09f, 0.07f, 0.55f)));

            float pw = UiWidth - 28, ph = Mathf.Min(660, h - 160);
            var area = new Rect(14, (h - ph) * 0.5f, pw, ph);
            // 불투명한 바탕 + 둥근 테두리
            GUI.DrawTexture(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16), Solid(SdfCanvas.Col("#B98A62")));
            GUI.DrawTexture(area, _panelTex, ScaleMode.StretchToFill);
            string[] tabs = { "가구", "넓히기", "사료" };
            for (int i = 0; i < tabs.Length; i++)
                if (GUI.Button(new Rect(area.x + 12 + i * 98, area.y + 12, 92, 40), tabs[i], _shopTab == i ? _btnOn : _btn)) { _shopTab = i; _shopScroll = Vector2.zero; }
            GUI.Label(new Rect(area.x + 300, area.y + 14, area.width - 360, 36), $"<b>{Economy.Wallet.Money:N0}원</b>", _menuTitleLight);
            if (GUI.Button(new Rect(area.xMax - 50, area.y + 12, 40, 40), "X", _btn)) { _shopOpen = false; return; }

            var body = new Rect(area.x + 10, area.y + 62, area.width - 20, area.height - 72);
            if (_shopTab == 0) DrawShopFurniture(body);
            else if (_shopTab == 1) DrawShopExpand(body);
            else DrawShopKibble(body);

            // 창 바깥을 누르면 닫기
            var e = Event.current;
            if (e.type == EventType.MouseDown && !area.Contains(e.mousePosition)) { _shopOpen = false; e.Use(); }
        }

        private Rect FloatButton(Rect r, ItemKind icon, string label)
        {
            GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
            CuteArt.DrawIcon(new Rect(r.x + 8, r.y + 11, 36, 36), icon);
            GUI.Label(new Rect(r.x + 44, r.y, 64, 58), $"<b>{label}</b>", _shopBtnLabel);
            return r;
        }

        /// <summary>가운데 팝업 틀: 어두운 뒷배경 + 패널 + 탭 + 닫기. 닫기를 누르거나 바깥을 누르면 false.</summary>
        private bool PopupFrame(float h, string[] tabs, ref int tab, string right, out Rect body)
        {
            GUI.DrawTexture(new Rect(0, 0, UiWidth, h), Solid(new Color(0.12f, 0.09f, 0.07f, 0.55f)));
            float pw = UiWidth - 28, ph = Mathf.Min(660, h - 160);
            var area = new Rect(14, (h - ph) * 0.5f, pw, ph);
            GUI.DrawTexture(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16), Solid(SdfCanvas.Col("#B98A62")));
            GUI.DrawTexture(area, _panelTex, ScaleMode.StretchToFill);
            for (int i = 0; i < tabs.Length; i++)
                if (GUI.Button(new Rect(area.x + 12 + i * 98, area.y + 12, 92, 40), tabs[i], tab == i ? _btnOn : _btn)) { tab = i; _popScroll = Vector2.zero; }
            if (_popRight == null) _popRight = new GUIStyle(_menuTitleLight) { fontSize = 17, alignment = TextAnchor.MiddleRight, wordWrap = false };
            GUI.Label(new Rect(area.x + 12 + tabs.Length * 98, area.y + 14, area.width - 74 - tabs.Length * 98, 36), right, _popRight);
            body = new Rect(area.x + 10, area.y + 62, area.width - 20, area.height - 72);
            if (GUI.Button(new Rect(area.xMax - 50, area.y + 12, 40, 40), "X", _btn)) return false;
            var e = Event.current;
            if (e.type == EventType.MouseDown && !area.Contains(e.mousePosition)) { e.Use(); return false; }
            return true;
        }

        /// <summary>폰에서도 손가락으로 끌리는 스크롤 영역 시작 (IMGUI 기본 스크롤은 터치로 안 끌린다).</summary>
        private void BeginDragScroll(Rect view, float contentH)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && view.Contains(ev.mousePosition)) _popDragged = 0f;
            if (ev.type == EventType.MouseDrag && view.Contains(ev.mousePosition))
            {
                _popScroll.y = Mathf.Clamp(_popScroll.y - ev.delta.y, 0, Mathf.Max(0, contentH - view.height));
                _popDragged += Mathf.Abs(ev.delta.y);
                ev.Use();
            }
            _popScroll = GUI.BeginScrollView(view, _popScroll, new Rect(0, 0, view.width - 20, contentH), false, true);
        }

        private bool Tapped(Rect r) => GUI.Button(r, GUIContent.none, GUIStyle.none) && _popDragged < 12f;

        // ───────────────────────── 공방 ─────────────────────────

        private void DrawWorkshopPopup(float h)
        {
            var w = Economy.Wallet;
            string right = $"<b>털뭉치 {w.Furballs}  수염 {w.Whiskers}</b>";
            if (!PopupFrame(h, new[] { "장난감", "코스튬", "옷장" }, ref _workshopTab, right, out var body)) { _workshopOpen = false; return; }
            if (_workshopTab == 2) { DrawCloset(body); return; }

            var kind = _workshopTab == 0 ? RecipeKind.Toy : RecipeKind.Costume;
            var recipes = Workshop.Recipes.Where(r => r.Kind == kind).ToList();
            GUI.Label(new Rect(body.x + 4, body.y, body.width - 8, 22),
                kind == RecipeKind.Toy ? "만든 장난감은 보관함으로 가요 · 상점 가구 목록에서 꺼내 놓아요" : "만든 코스튬은 옷장으로 가요 · 옷장 탭에서 입혀요", _shopHint);
            var view = new Rect(body.x, body.y + 26, body.width, body.height - 26);
            const float rh = 118;
            BeginDragScroll(view, recipes.Count * (rh + 8));
            for (int i = 0; i < recipes.Count; i++)
            {
                var rc = recipes[i];
                var r = new Rect(0, i * (rh + 8), view.width - 20, rh);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                var sprite = kind == RecipeKind.Toy ? CuteArt.Furniture(rc.Furniture) : CraftArt.CostumeIcon(rc.CostumeId);
                CuteArt.DrawSprite(new Rect(r.x + 12, r.y + 14, 84, 84), sprite);
                GUI.Label(new Rect(r.x + 108, r.y + 8, r.width - 120, 26), $"<b>{rc.Name}</b>", _menuTitle);
                GUI.Label(new Rect(r.x + 108, r.y + 34, r.width - 120, 22), rc.Desc, _shopSmall);
                string fur = w.Furballs >= rc.Furballs ? $"털뭉치 {rc.Furballs}" : $"<color=#B0655A>털뭉치 {rc.Furballs}</color>";
                string whi = rc.Whiskers == 0 ? "" : w.Whiskers >= rc.Whiskers ? $" · 수염 {rc.Whiskers}" : $" · <color=#B0655A>수염 {rc.Whiskers}</color>";
                int have = kind == RecipeKind.Toy ? Shop.Stock(rc.Furniture) : Workshop.Closet(rc.CostumeId);
                GUI.Label(new Rect(r.x + 108, r.y + 58, r.width - 120, 22), fur + whi + $"   <color=#4E8A55>({(kind == RecipeKind.Toy ? "보관함" : "옷장")} {have}개)</color>", _shopSmall);
                bool can = Workshop.Check(rc) == CraftResult.Ok;
                var br = new Rect(r.xMax - 132, r.y + 70, 120, 40);
                GUI.Label(br, can ? "만들기" : "재료 부족", can ? _btn : _btnOff);
                if (Tapped(br))
                {
                    var res = Workshop.Craft(rc);
                    if (res == CraftResult.Ok) Say(kind == RecipeKind.Toy ? $"{rc.Name} 완성! 보관함에 넣었어요." : $"{rc.Name} 완성! 옷장 탭에서 입혀 보세요.");
                    else Say(res == CraftResult.NotEnoughWhiskers ? "수염이 모자라요. 고양이가 가끔 떨어뜨려요." : "털뭉치가 모자라요. 바닥에 떨어진 털뭉치를 주워 보세요.");
                }
            }
            GUI.EndScrollView();
        }

        private void DrawCloset(Rect body)
        {
            var cats = Sim.Cats.Where(c => c.Affiliation == Affiliation.Resident).ToList();
            GUI.Label(new Rect(body.x + 4, body.y, body.width - 8, 22), "고양이마다 코스튬을 하나씩 입힐 수 있어요 · 갈아입히면 원래 옷은 옷장으로", _shopHint);
            var view = new Rect(body.x, body.y + 26, body.width, body.height - 26);
            const float rh = 104;
            BeginDragScroll(view, cats.Count * (rh + 8));
            for (int i = 0; i < cats.Count; i++)
            {
                var cat = cats[i];
                var r = new Rect(0, i * (rh + 8), view.width - 20, rh);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                GUI.Label(new Rect(r.x + 14, r.y + 8, 200, 26), $"<b>{cat.Name}</b>", _menuTitle);
                GUI.Label(new Rect(r.x + 14, r.y + 34, 200, 22), cat.Costume != null ? $"입은 옷: {Costumes.NameOf(cat.Costume)}" : "입은 옷 없음", _shopSmall);
                float bx = r.x + 12;
                foreach (var id in Costumes.All)
                {
                    var b = new Rect(bx, r.y + 58, 74, 40);
                    bool wearing = cat.Costume == id;
                    int n = Workshop.Closet(id);
                    GUI.Label(b, "", wearing ? _btnOn : n > 0 ? _btn : _btnOff);
                    CuteArt.DrawSprite(new Rect(b.x + 4, b.y + 4, 32, 32), CraftArt.CostumeIcon(id));
                    GUI.Label(new Rect(b.x + 36, b.y, 36, 40), wearing ? "입음" : $"x{n}", _shopSmall);
                    if (Tapped(b) && !wearing)
                    {
                        if (Workshop.Wear(cat, id)) Say($"{cat.Name}에게 {Costumes.NameOf(id)}을(를) 입혔어요!");
                        else Say($"옷장에 {Costumes.NameOf(id)}이(가) 없어요. 공방에서 만들어 보세요.");
                    }
                    bx += 80;
                }
                var off = new Rect(r.xMax - 104, r.y + 58, 92, 40);
                GUI.Label(off, "벗기기", cat.Costume != null ? _btn : _btnOff);
                if (Tapped(off) && cat.Costume != null) { Workshop.TakeOff(cat); Say($"{cat.Name}의 옷을 벗겼어요."); }
            }
            GUI.EndScrollView();
        }

        // ───────────────────────── 집사 스킬 ─────────────────────────

        private int _skillTabDummy;

        private void DrawSkillsPopup(float h)
        {
            if (!PopupFrame(h, new[] { "집사 스킬" }, ref _skillTabDummy, $"<b>젤리 하트 {Economy.Wallet.Hearts}</b>", out var body)) { _skillsOpen = false; return; }
            GUI.Label(new Rect(body.x + 4, body.y, body.width - 8, 22), "가구 위 하트를 모아 스킬을 올려요", _shopHint);
            var view = new Rect(body.x, body.y + 26, body.width, body.height - 26);
            const float rh = 128;
            BeginDragScroll(view, Skills.Defs.Count * (rh + 8));
            for (int i = 0; i < Skills.Defs.Count; i++)
            {
                var d = Skills.Defs[i];
                int lv = Skills.Level(d.Id);
                long cost = Skills.NextCost(d.Id);
                var r = new Rect(0, i * (rh + 8), view.width - 20, rh);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                CuteArt.DrawIcon(new Rect(r.x + 14, r.y + 18, 60, 60), ItemKind.Heart);
                GUI.Label(new Rect(r.x + 86, r.y + 8, r.width - 100, 26), $"<b>{d.Name}</b>  Lv {lv}/{d.MaxLevel}", _menuTitle);
                GUI.Label(new Rect(r.x + 86, r.y + 34, r.width - 100, 22), d.Desc, _shopSmall);
                string eff = cost < 0 ? $"지금: {d.EffectAt(lv)} (최대)" : $"지금: {d.EffectAt(lv)} → 다음: {d.EffectAt(lv + 1)}";
                GUI.Label(new Rect(r.x + 86, r.y + 58, r.width - 100, 22), eff, _shopSmall);
                var br = new Rect(r.xMax - 172, r.y + 82, 160, 38);
                string label = d.Locked ? "준비 중" : cost < 0 ? "최대 레벨" : $"하트 {cost}개로 올리기";
                bool can = !d.Locked && cost >= 0 && Economy.Wallet.Hearts >= cost;
                GUI.Label(br, label, can ? _btn : _btnOff);
                if (Tapped(br))
                {
                    var res = Skills.LevelUp(d.Id);
                    if (res == SkillResult.Ok) Say($"{d.Name} Lv {Skills.Level(d.Id)}! {d.EffectAt(Skills.Level(d.Id))}");
                    else if (res == SkillResult.NotEnoughHearts) Say($"젤리 하트가 {cost - Economy.Wallet.Hearts}개 모자라요.");
                    else if (res == SkillResult.Locked) Say("특제 간식이 생기면 열려요.");
                }
            }
            GUI.EndScrollView();
        }

        private void DrawShopFurniture(Rect body)
        {
            // 지금 보는 보드에 놓을 수 있는 가구만 (마당 가구는 마당에서)
            // 상점 가구 + 공방에서 만들어 보관함에 있는 장난감
            var defs = FurnitureCatalog.All.Concat(FurnitureCatalog.Crafted)
                .Where(d => (Shop.ForSale(d) || Shop.Stock(d) > 0) && d.AllowedOn(CurrentBoard))
                .OrderBy(d => Shop.ForSale(d) ? Shop.PriceOf(d) : -1).ToList();
            GUI.Label(new Rect(body.x + 4, body.y, body.width - 8, 22),
                CurrentBoard == BoardId.Indoor ? "실내에 놓을 가구 · 누르면 놓을 칸을 골라요" : "마당에 놓을 가구 · 누르면 놓을 칸을 골라요", _shopHint);
            var view = new Rect(body.x, body.y + 26, body.width, body.height - 26);
            const int cols = 3;
            float cw = (view.width - 20 - (cols - 1) * 8) / cols, ch = 150;
            int rows = Mathf.CeilToInt(defs.Count / (float)cols);
            var content = new Rect(0, 0, view.width - 20, rows * (ch + 8));
            // 폰에서는 IMGUI 스크롤이 손가락으로 안 끌려서 직접 드래그를 받는다
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && view.Contains(ev.mousePosition)) _shopDragged = 0f;
            if (ev.type == EventType.MouseDrag && view.Contains(ev.mousePosition))
            {
                _shopScroll.y = Mathf.Clamp(_shopScroll.y - ev.delta.y, 0, Mathf.Max(0, content.height - view.height));
                _shopDragged += Mathf.Abs(ev.delta.y);
                ev.Use();
            }
            _shopScroll = GUI.BeginScrollView(view, _shopScroll, content, false, true);
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                int stock = Shop.Stock(d);
                long price = Shop.PriceOf(d);
                bool can = Shop.CanAcquire(d);
                var r = new Rect((i % cols) * (cw + 8), (i / cols) * (ch + 8), cw, ch);
                var old = GUI.color;
                if (!can) GUI.color = new Color(1, 1, 1, 0.55f);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                var sprite = CuteArt.Furniture(d);
                if (sprite != null) CuteArt.DrawSprite(new Rect(r.x + 14, r.y + 8, r.width - 28, 72), sprite);
                else GUI.Label(new Rect(r.x, r.y + 30, r.width, 30), "(바닥 무늬)", _shopHintCenter);
                GUI.Label(new Rect(r.x + 4, r.y + 80, r.width - 8, 22), $"<b>{d.DisplayName}</b>", _shopNameCenter);
                GUI.Label(new Rect(r.x + 4, r.y + 100, r.width - 8, 20), (d.WallMounted ? $"벽 {d.WallWidth}자리 · {d.Capacity}마리" : $"{d.Size}칸 · {d.Capacity}마리"), _shopHintCenter);
                GUI.Label(new Rect(r.x + 4, r.y + 120, r.width - 8, 24),
                    stock > 0 ? $"<color=#4E8A55><b>보관함 {stock}개</b></color>" : can ? $"<b>{price:N0}원</b>" : $"<color=#B0655A>{price:N0}원</color>", _shopNameCenter);
                GUI.color = old;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && _shopDragged < 12f)
                {
                    if (!can) { Say($"돈이 {price - Economy.Wallet.Money:N0}원 모자라요. 트레이를 모아 보세요."); continue; }
                    _selectedDef = d;
                    _shopOpen = false;
                    SetMode(Mode.Place);
                    Say(stock > 0 ? $"{d.DisplayName}을(를) 놓을 칸을 탭하세요 (보관함에서 꺼냄)" : $"{d.DisplayName}을(를) 놓을 칸을 탭하세요 (놓으면 {price:N0}원)");
                }
            }
            GUI.EndScrollView();
        }

        /// <summary>가구를 놓는 중에 트레이 자리에 뜨는 막대: 무엇을 놓는지, 반전, 취소.</summary>
        private void DrawPlaceBar(float h)
        {
            var area = new Rect(0, h - BottomBarH, UiWidth, BottomBarH);
            GUI.DrawTexture(area, _panelTex, ScaleMode.StretchToFill);
            var d = _selectedDef;
            if (d == null) return;
            int stock = Shop.Stock(d);
            var sprite = CuteArt.Furniture(d);
            if (sprite != null) CuteArt.DrawSprite(new Rect(14, area.y + 16, 80, 70), sprite);
            GUI.Label(new Rect(104, area.y + 10, 300, 28), $"<b>{d.DisplayName}</b>", _placeTitle);
            GUI.Label(new Rect(104, area.y + 38, 320, 56),
                (stock > 0 ? $"보관함에서 꺼내요 ({stock}개)" : $"놓으면 {Shop.PriceOf(d):N0}원") + "\n탭하면 바로 놓여요 · 누른 채 끌면 미리보기", _label);
            if (GUI.Button(new Rect(UiWidth - 230, area.y + 96, 104, 44), _mirrored ? "반전 켬" : "반전", _mirrored ? _btnOn : _btn)) { _mirrored = !_mirrored; if (_hasPreview) ShowPreviewAt(_previewCoord); }
            if (GUI.Button(new Rect(UiWidth - 118, area.y + 96, 108, 44), "취소", _btn)) { SetMode(Mode.Play); Say("놓기를 취소했어요."); }
            if (GUI.Button(new Rect(14, area.y + 96, 150, 44), "다른 가구", _btn)) { SetMode(Mode.Play); _shopOpen = true; _shopTab = 0; }
        }

        private readonly Dictionary<Color, Texture2D> _solid = new Dictionary<Color, Texture2D>();

        /// <summary>한 가지 색 1×1 텍스처 (IMGUI 배경용).</summary>
        private Texture2D Solid(Color c)
        {
            if (_solid.TryGetValue(c, out var t) && t != null) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            _solid[c] = t;
            return t;
        }

        private void SetMode(Mode m)
        {
            _mode = m;
            RefreshViews();
            FitCamera();
            if (m == Mode.Place) ShowInitialPreview();
        }

        private void DrawShopExpand(Rect body)
        {
            float ch = 120;
            var ids = new[] { BoardId.Indoor, BoardId.Outdoor };
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var board = World.GetBoard(id);
                long cost = Shop.ExpandCost(id);
                var r = new Rect(body.x, body.y + i * (ch + 12), body.width, ch);
                GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
                string name = id == BoardId.Indoor ? "실내 넓히기" : "마당 넓히기";
                GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 26), $"<b>{name}</b>", _menuTitle);
                if (cost < 0)
                {
                    GUI.Label(new Rect(r.x + 12, r.y + 38, r.width - 24, 24), $"최대 크기예요 (반지름 {board.Radius}, 바닥 {board.FloorCount}칸)", _shopSmall);
                    continue;
                }
                int nextFloor = 3 * (board.Radius + 1) * (board.Radius + 2) + 1;
                GUI.Label(new Rect(r.x + 12, r.y + 38, r.width - 24, 24),
                    $"반지름 {board.Radius} → {board.Radius + 1} · 바닥 {board.FloorCount} → 약 {nextFloor}칸 · 문도 바깥으로 이동", _shopSmall);
                bool afford = Economy.Wallet.Money >= cost;
                if (GUI.Button(new Rect(r.x + 10, r.y + 70, r.width - 20, 40), afford ? $"{cost:N0}원으로 넓히기" : $"{cost:N0}원 (부족)", afford ? _btn : _btnOff))
                {
                    var res = Shop.BuyExpansion(id);
                    if (res == ShopResult.Ok)
                    {
                        if (CurrentBoard != id) ShowBoard(id);
                        FitCamera();
                        Say($"{(id == BoardId.Indoor ? "집" : "마당")}이 넓어졌어요! 반지름 {World.GetBoard(id).Radius}");
                    }
                    else if (res == ShopResult.NotEnoughMoney) Say($"돈이 {cost - Economy.Wallet.Money:N0}원 모자라요.");
                    else Say("더 넓힐 수 없어요.");
                }
            }
        }

        private void DrawShopKibble(Rect body)
        {
            var r = new Rect(body.x, body.y, body.width, 150);
            GUI.DrawTexture(r, _slotTex, ScaleMode.StretchToFill);
            CuteArt.DrawIcon(new Rect(r.x + 14, r.y + 14, 64, 64), ItemKind.Kibble);
            GUI.Label(new Rect(r.x + 90, r.y + 10, r.width - 100, 26), $"<b>길냥이 사료 {Shop.Tuning.KibblePackSize}개</b>", _menuTitle);
            GUI.Label(new Rect(r.x + 90, r.y + 38, r.width - 100, 44), $"마당 밥그릇을 한 번 채우는 데 1개 (3끼). 지금 {Economy.Wallet.Kibble}개", _shopSmall);
            long price = Shop.Tuning.KibblePackPrice;
            bool afford = Economy.Wallet.Money >= price;
            if (GUI.Button(new Rect(r.x + 10, r.y + 96, r.width - 20, 44), afford ? $"{price:N0}원 구매" : $"{price:N0}원 (부족)", afford ? _btn : _btnOff))
            {
                if (Shop.BuyKibble() == ShopResult.Ok) Say($"사료 {Shop.Tuning.KibblePackSize}개 구매! (지금 {Economy.Wallet.Kibble}개)");
                else Say($"돈이 {price - Economy.Wallet.Money:N0}원 모자라요.");
            }
            GUI.Label(new Rect(body.x + 6, body.y + 162, body.width - 12, 60), "가구는 사면 보관함으로 가고, 철거해도 보관함으로 돌아와요 (환불 없음).", _label);
        }
    }

}
