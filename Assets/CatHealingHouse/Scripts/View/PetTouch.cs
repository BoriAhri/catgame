using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Stray;
using CatHealingHouse.View.Art;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 확대한 상태에서 판 위 고양이를 손가락으로 쓰다듬기 (화면 연출 담당).
    /// 판정(만족·짜증·보상)은 Core 의 Petting. HouseGame 이 터치를 넘겨주면 여기서 부위를 찾고 반응을 보여준다.
    /// 쓰다듬는 고양이는 제자리에 멈추고, 손을 떼고 2초가 지나면 다시 제 할 일을 한다.
    /// </summary>
    public sealed class PetTouch : MonoBehaviour
    {
        public Camera Cam;
        public Petting Petting;
        public StraySystem Strays;
        public Material Material;
        public float CellSize = 1f;
        public Action<string> Say;

        public PetSession Session { get; private set; }
        public CatView View { get; private set; }
        public bool Active => Session != null;

        private const float IdleRelease = 2f;
        private float _lastStrokeAt = -99f, _lastHeartAt, _lastVeinAt, _lastHissAt = -99f, _lastSayAt = -99f, _endAt;
        private bool _ending;
        private PetFeel _lastSaidFeel = (PetFeel)(-1);
        private Transform _bar;
        private SpriteRenderer _barBg, _barFill, _barAnnoy;

        private struct Fx { public SpriteRenderer R; public Vector3 Vel; public float Born, Life, Size; }
        private readonly List<Fx> _fx = new List<Fx>();

        /// <summary>이 고양이를 쓰다듬을 수 있나 (집 고양이는 화장실 중만 빼고, 길냥이는 메뉴가 열리는 호감도 41 이상).</summary>
        public bool CanPet(CatView cv)
        {
            if (cv == null || Petting == null) return false;
            var c = cv.Cat;
            if (c.Affiliation == Affiliation.Resident) return !(c.State == CatState.Using && c.TargetNeed == NeedType.Bladder);
            return Strays != null && Strays.MenuAvailable(c);
        }

        /// <summary>그 고양이 위에서 문지르기가 시작될 때. 길냥이는 받아줄지 확률을 굴린다 (싫으면 도망).</summary>
        public bool Begin(CatView cv)
        {
            if (Session != null && View == cv && !_ending) return true;
            if (Session != null) { if (!Session.Done) Petting.Stop(Session); End(); }
            var c = cv.Cat;
            if (c.Affiliation == Affiliation.Stray)
            {
                var r = Strays.Interact(c, StrayAction.Pet);
                if (r.Outcome == InteractOutcome.Refused)
                {
                    Say?.Invoke($"{c.Name}가 싫다고 도망갔어요… (호감 -3, 오늘은 안 와요)");
                    SoundKit.Play("meow", 0.7f, 0.75f);
                    return false;
                }
                if (!r.Ok) return false;
                if (r.AffectionDelta > 0) Say?.Invoke($"{c.Name}가 쓰다듬기를 허락했어요! 호감 +{r.AffectionDelta:0}");
            }
            Session = Petting.Begin(c);
            View = cv;
            _ending = false;
            _lastStrokeAt = Time.time;
            _lastSaidFeel = (PetFeel)(-1);
            EnsureBar();
            return true;
        }

        /// <summary>손가락이 움직인 만큼 쓰다듬는다 (화면 좌표).</summary>
        public void Stroke(Vector2 fromScreen, Vector2 toScreen)
        {
            if (Session == null || _ending || View == null) return;
            float len = (toScreen - fromScreen).magnitude / Mathf.Max(1, Screen.width);
            if (len <= 0.0005f) return;
            var world = Cam.ScreenToWorldPoint(new Vector3(toScreen.x, toScreen.y, 10));
            world.z = 0;
            var zone = ZoneOf(View, world);
            if (zone == PetZone.None) return;

            double before = Session.Satisfaction;
            Petting.Stroke(Session, zone, len);
            _lastStrokeAt = Time.time;
            bool gained = Session.Satisfaction > before;
            var feel = Session.LastFeel;
            float s = CellSize;

            if (gained && feel <= PetFeel.Like && Time.time - _lastHeartAt > 0.2f)
            {
                _lastHeartAt = Time.time;
                Spawn(CuteArt.Item(ItemKind.Heart), world, new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), 1f, 0) * s * 0.6f, 0.9f, s * 0.16f);
            }
            if (feel == PetFeel.Dislike && Time.time - _lastVeinAt > 0.45f)
            {
                _lastVeinAt = Time.time;
                Spawn(Vein(), HeadTop(), new Vector3(0, 0.2f, 0) * s, 0.6f, s * 0.2f);
            }
            if (feel <= PetFeel.Like) SoundKit.Play("purr", 0.8f);
            if (Session.Annoyance > 0.45f && Time.time - _lastHissAt > 1.4f)
            {
                _lastHissAt = Time.time;
                SoundKit.Play("meow", 0.55f, 0.72f);
            }

            // 안내 문구 (자주 바뀌지 않게)
            if (!Session.Done && (feel != _lastSaidFeel || Time.time - _lastSayAt > 3f) && Time.time - _lastSayAt > 1.2f)
            {
                string msg = null;
                switch (feel)
                {
                    case PetFeel.Love: if (gained) msg = LoveText(zone); break;
                    case PetFeel.Like: if (gained) msg = $"{View.Cat.Name}가 기분 좋아해요"; break;
                    case PetFeel.Meh: msg = "거긴 그냥 그렇대요"; break;
                    default: msg = Session.Annoyance > 0.6f ? "그만! 화나기 직전이에요" : "거긴 싫대요…"; break;
                }
                if (msg != null) { Say?.Invoke(msg); _lastSayAt = Time.time; _lastSaidFeel = feel; }
            }
        }

        public static PetZone ZoneOf(CatView cv, Vector3 world)
        {
            var rig = cv.Rig;
            if (rig == null || !rig.gameObject.activeInHierarchy) return PetZone.None;
            return CatParts.ZoneAt(rig.CanvasAt(world, CatPart.Head), rig.CanvasAt(world, CatPart.Body), rig.CanvasAt(world, CatPart.Tail), rig.CanvasAtRoot(world));
        }

        private static string LoveText(PetZone z)
        {
            switch (z)
            {
                case PetZone.Head: return "머리 쓰다듬기 최고!";
                case PetZone.Cheek: return "볼 긁어 주는 거 좋아해요";
                case PetZone.Chin: return "턱 밑이 제일 좋대요";
                case PetZone.Back: return "등 쓸어 주는 거 좋아해요";
                case PetZone.Chest: return "가슴 털이 보송보송";
                case PetZone.Belly: return "배를 허락했어요!";
                default: return "골골골…";
            }
        }

        private Vector3 HeadTop()
        {
            var t = View.Rig.transform;
            return t.TransformPoint(new Vector3(0.66f, 0.76f, 0) - (Vector3)CatParts.Root);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Session != null)
            {
                if (View == null || !View.Rig.gameObject.activeInHierarchy)
                {
                    if (!Session.Done) Petting.Stop(Session);
                    End();
                }
                else
                {
                    Petting.Tick(Session, dt);
                    if (Session.Done && !_ending) OnDone();
                    if (!_ending && Time.time - _lastStrokeAt > IdleRelease) { Petting.Stop(Session); OnDone(); }
                    if (Session != null) { React(); SyncBar(); }
                    if (_ending && Time.time >= _endAt) End();
                }
            }
            UpdateFx(dt);
        }

        private void React()
        {
            var s = Session;
            var rig = View.Rig;
            rig.PetActive = true;
            rig.PetBlush = (float)s.Satisfaction;
            if (s.Done)
            {
                bool happy = s.End == PetEnd.Satisfied;
                rig.PetEyes = happy ? PetEyes.Happy : PetEyes.Normal;
                rig.PetEars = s.End == PetEnd.Annoyed ? 1f : 0f;
                rig.PetPurr = happy ? 1f : 0f;
                rig.PetTail = s.End == PetEnd.Annoyed ? 3.5f : 1f;
                rig.PetTilt = happy ? 8f : 0f;
                if (happy) rig.PetBlush = 1f;
                return;
            }
            bool stroking = Time.time - _lastStrokeAt < 0.3f;
            if (stroking)
            {
                switch (s.LastFeel)
                {
                    case PetFeel.Love:
                        rig.PetEyes = s.Satisfaction > 0.4 ? PetEyes.Happy : PetEyes.Half;
                        rig.PetPurr = 1f; rig.PetEars = 0f; rig.PetTail = 0.7f; rig.PetTilt = 10f;
                        break;
                    case PetFeel.Like:
                        rig.PetEyes = PetEyes.Half;
                        rig.PetPurr = 0.6f; rig.PetEars = 0f; rig.PetTail = 0.9f; rig.PetTilt = 5f;
                        break;
                    case PetFeel.Meh:
                        rig.PetEyes = PetEyes.Normal;
                        rig.PetPurr = 0f; rig.PetEars = 0.3f; rig.PetTail = 1.6f; rig.PetTilt = 0f;
                        break;
                    default:
                        rig.PetEyes = PetEyes.Normal;
                        rig.PetPurr = 0f; rig.PetEars = 1f; rig.PetTail = 3.2f; rig.PetTilt = -8f;
                        break;
                }
            }
            else
            {
                rig.PetPurr = Mathf.Max(0f, rig.PetPurr - Time.deltaTime * 0.8f);
                rig.PetTilt = 0f;
                rig.PetEyes = s.Satisfaction > 0.6 ? PetEyes.Half : PetEyes.Normal;
                rig.PetEars = s.Annoyance > 0.4 ? 0.7f : 0f;
                rig.PetTail = s.Annoyance > 0.4 ? 2.2f : 1f;
            }
        }

        private void OnDone()
        {
            _ending = true;
            var s = Session;
            string n = s.Cat.Name;
            switch (s.End)
            {
                case PetEnd.Satisfied:
                {
                    _endAt = Time.time + 1.6f;
                    SoundKit.Play("fanfare", 0.6f);
                    var c = HeadTop();
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i / 10f * Mathf.PI * 2f;
                        Spawn(CuteArt.Item(ItemKind.Heart), c, new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.8f, 0) * CellSize * 0.9f, 1.2f, CellSize * 0.15f);
                    }
                    Say?.Invoke($"{n}: {RewardText(s)}{(s.HeartOnCooldown ? " (젤리 하트는 조금 있다가)" : "")}");
                    break;
                }
                case PetEnd.Annoyed:
                    _endAt = Time.time + 0.8f;
                    SoundKit.Play("meow", 0.8f, 0.68f);
                    Say?.Invoke(s.IsStray ? $"하악! {n}가 짜증내며 도망갔어요. (호감 -{-s.AffectionGiven:0})" : $"{n}가 삐졌어요… 다음엔 좋아하는 곳을 쓰다듬어 줘요 (기분 -{Petting.Tuning.AnnoyMoodPenalty:0})");
                    break;
                default:
                    _endAt = Time.time;
                    if (s.MoodGiven > 0.5) Say?.Invoke($"{n} 기분 +{s.MoodGiven:0}");
                    break;
            }
        }

        public static string RewardText(PetSession s)
        {
            var parts = new List<string>();
            if (s.MoodGiven > 0.5) parts.Add($"기분 +{s.MoodGiven:0}");
            if (s.AffectionGiven > 0) parts.Add($"호감도 +{s.AffectionGiven:0}");
            if (s.HeartsGiven > 0) parts.Add($"젤리 하트 +{s.HeartsGiven}");
            return parts.Count == 0 ? "푹 만족했어요!" : "푹 만족했어요! " + string.Join(", ", parts);
        }

        private void End()
        {
            if (View != null && View.Rig != null) View.Rig.PetActive = false;
            if (_bar != null) _bar.gameObject.SetActive(false);
            Session = null;
            View = null;
            _ending = false;
        }

        // ───────────────────────── 머리 위 만족 게이지 ─────────────────────────

        private void EnsureBar()
        {
            if (_bar == null)
            {
                var go = new GameObject("PetBar");
                go.transform.SetParent(transform, false);
                _bar = go.transform;
                _barBg = MakeRect("bg", BoardView.LabelOrder + 40, SdfCanvas.Col("#5E4B3C"));
                _barFill = MakeRect("fill", BoardView.LabelOrder + 42, SdfCanvas.Col("#F08A8A"));
                _barAnnoy = MakeRect("annoy", BoardView.LabelOrder + 42, SdfCanvas.Col("#E0574F"));
            }
            _bar.gameObject.SetActive(true);
        }

        private SpriteRenderer MakeRect(string name, int order, Color col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_bar, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = CuteArt.WhitePixel();
            r.sharedMaterial = Material;
            r.sortingOrder = order;
            r.color = col;
            return r;
        }

        private void SyncBar()
        {
            float s = CellSize;
            float zk = Mathf.Max(CatView.ZoomK, 0.45f);
            float w = s * 0.8f * zk, h = s * 0.075f * zk;
            _bar.position = View.transform.position + new Vector3(0, s * 0.98f * View.LastScale, 0);
            float px = CuteArt.WidthOf(_barBg.sprite);
            _barBg.transform.localScale = new Vector3((w + h * 0.5f) / px, (h * 1.5f) / px, 1);
            float k = Mathf.Clamp01((float)Session.Satisfaction);
            _barFill.transform.localScale = new Vector3(Mathf.Max(0.0001f, w * k) / px, h / px, 1);
            _barFill.transform.localPosition = new Vector3(-w * 0.5f + w * k * 0.5f, 0, 0);
            float a = Mathf.Clamp01((float)Session.Annoyance);
            _barAnnoy.enabled = a > 0.02f;
            _barAnnoy.transform.localScale = new Vector3(Mathf.Max(0.0001f, w * a) / px, h * 0.35f / px, 1);
            _barAnnoy.transform.localPosition = new Vector3(-w * 0.5f + w * a * 0.5f, -h * 0.9f, 0);
        }

        // ───────────────────────── 하트·핏줄 효과 ─────────────────────────

        private void Spawn(Sprite sp, Vector3 world, Vector3 vel, float life, float size)
        {
            if (sp == null) return;
            var go = new GameObject("petfx");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(world.x, world.y, 0);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sharedMaterial = Material;
            r.sortingOrder = BoardView.LabelOrder + 45;
            _fx.Add(new Fx { R = r, Vel = vel, Born = Time.time, Life = life, Size = size });
        }

        private void UpdateFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                float age = (Time.time - f.Born) / f.Life;
                if (f.R == null || age >= 1f)
                {
                    if (f.R != null) Destroy(f.R.gameObject);
                    _fx.RemoveAt(i);
                    continue;
                }
                f.R.transform.position += f.Vel * dt;
                float sc = f.Size / CuteArt.WidthOf(f.R.sprite) * (0.6f + 0.4f * Mathf.Min(1f, age * 5f));
                f.R.transform.localScale = new Vector3(sc, sc, 1);
                f.R.color = new Color(1, 1, 1, 1f - age * age);
            }
        }

        private static Sprite _vein;

        /// <summary>짜증 표시 (빨간 십자 핏줄).</summary>
        public static Sprite Vein()
        {
            if (_vein != null && _vein.texture != null) return _vein;
            var c = new SdfCanvas(96);
            var red = SdfCanvas.Col("#E0574F");
            var arcs = SdfCanvas.Union(
                SdfCanvas.Ring(0.27f, 0.27f, 0.17f, 0.045f), SdfCanvas.Ring(0.73f, 0.27f, 0.17f, 0.045f),
                SdfCanvas.Ring(0.27f, 0.73f, 0.17f, 0.045f), SdfCanvas.Ring(0.73f, 0.73f, 0.17f, 0.045f));
            c.Fill(SdfCanvas.Intersect(arcs, SdfCanvas.Subtract(SdfCanvas.Circle(0.5f, 0.5f, 0.42f), SdfCanvas.Circle(0.5f, 0.5f, 0.12f))), red, SdfCanvas.Col("#FFF3EC"), 0.03f);
            var tex = c.ToTexture("vein");
            _vein = Sprite.Create(tex, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 96, 0, SpriteMeshType.FullRect);
            return _vein;
        }
    }
}
