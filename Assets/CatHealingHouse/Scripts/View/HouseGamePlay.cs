using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.View.Art;
using CatHealingHouse.World;
using UnityEngine;
using UnityEngine.InputSystem;
using static CatHealingHouse.View.Art.SdfCanvas;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 놀아주기 화면 (HouseGame 의 일부). 손가락으로 장난감(레이저 점·깃털 낚싯대)을 움직이면
    /// 고양이가 쫓아가서 천천히 멈춘 장난감을 덮친다. 규칙은 PlaySystem.
    ///  - 프로필의 장난감 버튼: 그 고양이 한 마리와 놀기
    ///  - 아래 메뉴의 놀기 버튼: 장난감 근처 고양이들과 다 같이
    /// 놀이 중에는 한 손가락이 장난감을 움직이고(화면 끌기 대신), 두 손가락은 확대.
    /// </summary>
    public sealed partial class HouseGame
    {
        public PlaySystem Play { get; private set; }
        private bool _playMode;
        private Cat _playSolo;
        private ToyKind _toyKind = ToyKind.Laser;
        private float _playEndAt = -1f;

        // 장난감 그림
        private Transform _toyT;
        private SpriteRenderer _toyR;
        private LineRenderer _toyLine;
        private Sprite _laserSprite, _featherSprite;
        private bool _toyDown, _toyShown;
        private Vector3 _toyWorld, _toyLast;
        private float _toySpeed;

        private static readonly string[] CatchLines = { "잡았다!", "냥!", "내 거야!", "한 번 더!", "냥냥!" };

        private void SetupPlay()
        {
            Play = new PlaySystem(Sim, Intimacy);
            Play.Today = TodayIndex;
            Play.Caught += OnToyCaught;
            Play.Tired += OnPlayTired;
            Play.Joined += c =>
            {
                var v = ViewOf(c);
                if (v != null && _uiRng.NextDouble() < 0.4) v.Say(_uiRng.NextDouble() < 0.5 ? "!!" : "사냥 시간이다냥");
            };
        }

        // ───────────────────────── 시작·끝 ─────────────────────────

        private void StartPlay(Cat solo)
        {
            if (CurrentBoard != BoardId.Indoor) ShowBoard(BoardId.Indoor);
            if (_toyKind == ToyKind.Wand && !Shop.HasWand) _toyKind = ToyKind.Laser;
            _profileCat = null;
            CloseMenu();
            _shopOpen = _workshopOpen = _skillsOpen = false;
            if (_mode != Mode.Play) { _mode = Mode.Play; RefreshViews(); }
            _playMode = true;
            _playSolo = solo;
            _playEndAt = -1f;
            _toyDown = false;
            _toyShown = false;
            Play.Start(_toyKind, solo);
            EnsureToyVisual();
            if (solo != null)
            {
                var v = ViewOf(solo);
                if (v != null)
                {
                    var p = v.BodyCenter;
                    _cam.transform.position = new Vector3(p.x, p.y, _cam.transform.position.z);
                }
                Say(Play.IsTired(solo) ? $"{solo.Name}는 지금 지쳐 있어요. 조금 쉬게 해 주세요." : $"{solo.Name}와 놀아요! 화면을 문질러 장난감을 움직여 보세요.");
            }
            else Say("장난감을 고양이 가까이 가져가면 달려와요!");
            SoundKit.Play("open");
        }

        private void StopPlay(string message = null)
        {
            Play.Stop();
            _playMode = false;
            _playSolo = null;
            _toyDown = false;
            _toyShown = false;
            if (_toyT != null) _toyT.gameObject.SetActive(false);
            if (message != null) Say(message);
            _saveSoon = true;
        }

        // ───────────────────────── 입력 ─────────────────────────

        private void HandlePlayPointer(Pointer pointer, Vector2 pos)
        {
            if (pointer.press.wasPressedThisFrame)
            {
                if (OverUi(pos)) return;
                _toyDown = true;
                MoveToy(pos, true);
            }
            else if (_toyDown && pointer.press.isPressed)
            {
                if (_lastPinchDistance > 0f) return; // 두 손가락이면 확대만
                MoveToy(pos, false);
            }
            else if (_toyDown && pointer.press.wasReleasedThisFrame)
            {
                _toyDown = false;
                // 레이저는 손을 떼면 꺼지고, 낚싯대는 그 자리에 대롱대롱 남는다
                if (_toyKind == ToyKind.Laser) _toyShown = false;
            }
        }

        private void MoveToy(Vector2 screen, bool jump)
        {
            var w = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            w.z = 0;
            _toyWorld = w;
            if (jump || !_toyShown) _toyLast = w;
            _toyShown = true;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void UpdatePlay()
        {
            if (!_playMode) return;
            if (CurrentBoard != BoardId.Indoor) { StopPlay(); return; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float size = (float)_layout.Size;

            // 장난감 속도 (초당 칸, 부드럽게)
            float inst = _toyShown ? Vector3.Distance(_toyWorld, _toyLast) / size / dt : 0f;
            _toySpeed = Mathf.Lerp(_toySpeed, inst, 1f - Mathf.Exp(-dt * 8f));
            _toyLast = _toyWorld;

            Cell? cell = null;
            if (_toyShown)
            {
                // 고양이 발은 칸 가운데보다 조금 아래에 그려지므로, 장난감 점에 발이 오는 칸을 고른다
                var coord = _layout.FromWorld(_toyWorld.x, _toyWorld.y + size * 0.3f * (float)_layout.YScale);
                if (World.GetBoard(BoardId.Indoor).Get(coord) != null) cell = new Cell(BoardId.Indoor, coord);
            }
            Play.SetToy(cell, _toySpeed);
            DrawToy(size);

            if (_playEndAt > 0f && Time.time >= _playEndAt) StopPlay();
        }

        private void EnsureToyVisual()
        {
            if (_toyT != null) return;
            var go = new GameObject("Toy");
            go.transform.SetParent(transform, false);
            _toyT = go.transform;
            _toyR = go.AddComponent<SpriteRenderer>();
            _toyR.sharedMaterial = _material;
            _toyR.sortingOrder = BoardView.LabelOrder + 5;

            var lineGo = new GameObject("ToyString");
            lineGo.transform.SetParent(transform, false);
            _toyLine = lineGo.AddComponent<LineRenderer>();
            _toyLine.sharedMaterial = _material;
            _toyLine.positionCount = 2;
            _toyLine.useWorldSpace = true;
            _toyLine.numCapVertices = 2;
            _toyLine.startColor = _toyLine.endColor = SdfCanvas.Col("#6E5240");
            _toyLine.sortingOrder = BoardView.LabelOrder + 4;
            lineGo.transform.SetParent(_toyT, true);

            // 레이저 점: 빨간 점 + 번지는 빛
            var c = new SdfCanvas(64);
            c.Fill(Circle(0.5f, 0.5f, 0.48f), new Color(1f, 0.25f, 0.25f, 0.16f));
            c.Fill(Circle(0.5f, 0.5f, 0.3f), new Color(1f, 0.3f, 0.3f, 0.35f));
            c.Fill(Circle(0.5f, 0.5f, 0.15f), new Color(1f, 0.18f, 0.18f, 1f));
            c.Fill(Circle(0.46f, 0.54f, 0.06f), new Color(1f, 0.88f, 0.88f, 1f));
            _laserSprite = c.ToSprite("toy_laser", 1f, new Vector2(0.5f, 0.5f));

            // 깃털: 줄 끝(위)이 피벗
            var f = new SdfCanvas(96);
            var line = CuteArt.Line;
            f.Fill(Capsule(0.5f, 0.78f, 0.28f, 0.24f, 0.075f), SdfCanvas.Col("#F48A93"), line, 0.025f);
            f.Fill(Capsule(0.5f, 0.78f, 0.72f, 0.24f, 0.075f), SdfCanvas.Col("#8EC5E8"), line, 0.025f);
            f.Fill(Capsule(0.5f, 0.78f, 0.5f, 0.12f, 0.085f), SdfCanvas.Col("#FFD36B"), line, 0.025f);
            f.Fill(Circle(0.5f, 0.8f, 0.08f), SdfCanvas.Col("#E9A24C"), line, 0.025f);
            _featherSprite = f.ToSprite("toy_feather", 1f, new Vector2(0.5f, 0.86f));
            go.SetActive(false);
        }

        private void DrawToy(float size)
        {
            if (_toyT == null) return;
            if (_toyT.gameObject.activeSelf != _toyShown) _toyT.gameObject.SetActive(_toyShown);
            if (!_toyShown) return;
            float t = Time.time;
            if (_toyKind == ToyKind.Laser)
            {
                _toyR.sprite = _laserSprite;
                float pulse = 1f + Mathf.Sin(t * 18f) * 0.08f;
                float s = Mathf.Max(size * 0.6f, _cam.orthographicSize * 0.08f) * pulse; // 멀리서 봐도 보이게
                _toyT.position = _toyWorld;
                _toyT.localScale = new Vector3(s, s, 1);
                _toyT.localRotation = Quaternion.identity;
                _toyLine.enabled = false;
            }
            else
            {
                // 낚싯대: 손을 떼면 대롱대롱 흔들린다
                _toyR.sprite = _featherSprite;
                float sway = _toyDown ? 0f : Mathf.Sin(t * 2.6f) * size * 0.08f;
                var p = _toyWorld + new Vector3(sway, Mathf.Abs(Mathf.Sin(t * 5.2f)) * size * 0.03f, 0);
                float s = Mathf.Max(size * 0.62f, _cam.orthographicSize * 0.1f);
                _toyT.position = p;
                _toyT.localScale = new Vector3(s, s, 1);
                _toyT.localRotation = Quaternion.Euler(0, 0, sway / size * -60f);
                // 줄: 깃털 위에서 화면 위쪽 바깥으로
                float top = _cam.transform.position.y + _cam.orthographicSize + size;
                _toyLine.enabled = true;
                _toyLine.startWidth = _toyLine.endWidth = size * 0.022f;
                _toyLine.SetPosition(0, p + new Vector3(0, size * 0.02f, 0));
                _toyLine.SetPosition(1, new Vector3(p.x - sway * 3f + size * 0.6f, top, 0));
            }
        }

        // ───────────────────────── 사건 ─────────────────────────

        private void OnToyCaught(Cat cat, double exp)
        {
            var v = ViewOf(cat);
            if (v != null && _uiRng.NextDouble() < 0.35) v.Say(CatchLines[_uiRng.Next(CatchLines.Length)], 1.6f);
            Say(exp > 0 ? $"{cat.Name}가 잡았어요! 친밀도 +{exp:0.#}" : $"{cat.Name}가 잡았어요!");
            SoundKit.Play("pop", 0.8f, 0.95f + (float)_uiRng.NextDouble() * 0.15f);
            _saveSoon = true;
        }

        private void OnPlayTired(Cat cat)
        {
            var v = ViewOf(cat);
            if (v != null) v.Say("헥헥… 좀 쉴래", 2.4f);
            Say($"{cat.Name}가 지쳐서 드러누웠어요. 쉬게 해 주세요.");
            SoundKit.Play("meow", 0.7f, 0.85f);
            if (cat == _playSolo) _playEndAt = Time.time + 2.5f;
        }

        // ───────────────────────── 아래 놀기 바 ─────────────────────────

        private void DrawPlayBar(float h)
        {
            var area = new Rect(0, h - BottomBarH, UiWidth, BottomBarH);
            GUI.Box(area, GUIContent.none, _panelStyle);
            string title = _playSolo != null ? $"<b>{_playSolo.Name}</b>와 놀기" : $"다 같이 놀기 · 노는 중 {Play.Players.Count}마리";
            GUI.Label(new Rect(16, area.y + 6, UiWidth - 32, 26), title, _menuTitle);
            GUI.Label(new Rect(16, area.y + 32, UiWidth - 32, 22), "<color=#8E6A4B>화면을 문질러 장난감을 움직여요 · 천천히 멈추면 덮쳐요!</color>", _small);

            float by = area.y + 60, tw = 122, th = 78;
            ToyButton(new Rect(12, by, tw, th), ToyKind.Laser, "레이저", true);
            ToyButton(new Rect(12 + tw + 8, by, tw, th), ToyKind.Wand,
                Shop.HasWand ? "낚싯대" : $"낚싯대\n<size=12>상점 {Shop.Tuning.WandPrice}원</size>", Shop.HasWand);

            // 한 마리와 놀 때: 기력과 친밀도
            if (_playSolo != null)
            {
                float sx = 12 + 2 * (tw + 8) + 4, sw = UiWidth - sx - 132;
                NeedBar(new Rect(sx, by + 8, sw, 24), NeedType.Energy, _playSolo.Needs.Energy);
                var ebar = new Rect(sx, by + 46, sw, 12);
                GUI.DrawTexture(ebar, Solid(SdfCanvas.Col("#EADBC6")));
                GUI.DrawTexture(new Rect(ebar.x, ebar.y, ebar.width * (float)Intimacy.Progress(_playSolo), ebar.height), Solid(SdfCanvas.Col("#F2B05E")));
                GUI.Label(new Rect(sx, by + 56, sw, 22), $"<size=13><color=#E08A3C>친밀도 Lv.{_playSolo.IntimacyLevel}</color></size>", _small);
            }

            var stop = new Rect(UiWidth - 12 - 112, by + 8, 112, 62);
            if (GUI.Button(stop, "그만", _btn)) { StopPlay("놀아주기를 마쳤어요."); SoundKit.Play("click"); }
        }

        private void ToyButton(Rect r, ToyKind kind, string label, bool owned)
        {
            bool on = _toyKind == kind;
            if (GUI.Button(r, GUIContent.none, on ? _btnOn : owned ? _btn : _btnOff))
            {
                if (!owned)
                {
                    Say($"낚싯대는 상점 '용품'에서 살 수 있어요 ({Shop.Tuning.WandPrice}원). 잡을 때 친밀도가 두 배!");
                    SoundKit.Play("click");
                }
                else if (!on)
                {
                    _toyKind = kind;
                    Play.SetToyKind(kind);
                    _toyShown = false;
                    SoundKit.Play("click");
                }
            }
            CuteArt.DrawSprite(new Rect(r.x + 8, r.y + 14, 50, 50), UiKit.Icon(kind == ToyKind.Laser ? "laser" : "wand"));
            GUI.Label(new Rect(r.x + 54, r.y, r.width - 56, r.height), label, _shopNameCenter);
        }

        // ───────────────────────── 상점: 낚싯대 ─────────────────────────

        private void DrawShopWand(Rect r)
        {
            GUI.Box(r, GUIContent.none, _cardStyle);
            CuteArt.DrawSprite(new Rect(r.x + 14, r.y + 14, 64, 64), UiKit.Icon("wand"));
            GUI.Label(new Rect(r.x + 90, r.y + 10, r.width - 100, 26), "<b>깃털 낚싯대</b>", _menuTitle);
            GUI.Label(new Rect(r.x + 90, r.y + 38, r.width - 100, 44), "놀아주기 장난감. 잡을 때 친밀도가 레이저의 두 배. 한 번 사면 계속 써요.", _shopSmall);
            long price = Shop.Tuning.WandPrice;
            var br = new Rect(r.x + 10, r.y + 96, r.width - 20, 44);
            if (Shop.HasWand) { GUI.Label(br, "가지고 있어요", _btnOn); return; }
            bool afford = Economy.Wallet.Money >= price;
            if (GUI.Button(br, afford ? $"{price:N0}원 구매" : $"{price:N0}원 (부족)", afford ? _btn : _btnOff))
            {
                if (Shop.BuyWand() == ShopResult.Ok)
                {
                    Say("깃털 낚싯대를 샀어요! 놀기에서 골라 보세요.");
                    SoundKit.Play("fanfare", 0.8f);
                    _saveSoon = true;
                }
                else Say($"돈이 {price - Economy.Wallet.Money:N0}원 모자라요.");
            }
        }
    }
}
