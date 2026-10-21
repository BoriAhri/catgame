using System;
using System.Collections.Generic;
using CatHealingHouse.Cats;
using CatHealingHouse.Economy;
using CatHealingHouse.View.Art;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 가까이 보기: 카메라가 고양이에게 다가가고, 화면 가득 정면 고양이가 나와서 손가락으로 쓰다듬는다.
    /// 판단(만족·짜증·보상)은 Core 의 Petting, 여기서는 연출과 입력만.
    /// </summary>
    public sealed class CloseUpView : MonoBehaviour
    {
        private enum Phase { None, ZoomIn, Shown, Ending, ZoomOut }

        public Camera Cam;
        public Petting Petting;
        public Material Material;
        public Func<float> FitOrtho;
        public Func<Vector2, bool> OverUi;
        /// <summary>끝나고 카메라가 돌아온 뒤 (보상 안내용).</summary>
        public event Action<PetSession> Finished;

        public bool Active => _phase != Phase.None;
        public bool Shown => _phase == Phase.Shown;
        public PetSession Session { get; private set; }
        public CatView Target { get; private set; }
        public string Feedback { get; private set; }
        public float FeedbackAt { get; private set; }

        private Phase _phase;
        private float _t;
        private Vector3 _camFrom, _camTo, _camBack;
        private float _orthoFrom, _orthoTo, _orthoBack;
        private Transform _stage;
        private SpriteRenderer _dim;
        private CloseUpRig _rig;
        private float _enterAt;

        // 입력
        private bool _down, _ignore;
        private Vector2 _last, _downPos;
        private float _downTime, _moved, _pinch0 = -1f;
        private float _lastStrokeAt = -99f, _lastGainAt = -99f, _lastHeartAt, _lastVeinAt, _lastHissAt = -99f;
        private float _nextBlink, _blinkUntil, _boopUntil, _endAt;
        private Vector2 _lastUv;
        private bool _endShown;

        private const int StageOrder = 31000;
        private const float DimAlpha = 0.72f;

        private struct Fx { public SpriteRenderer R; public Vector3 Vel; public float Born, Life, Size; }
        private readonly List<Fx> _fx = new List<Fx>();

        public void Enter(CatView cv)
        {
            if (Active || cv == null || Petting == null) return;
            Target = cv;
            CloseUpArt.Warm(cv.Look);
            Session = Petting.Begin(cv.Cat);
            Feedback = null;
            _endShown = false;
            _camFrom = Cam.transform.position;
            _orthoFrom = Cam.orthographicSize;
            _camBack = _camFrom;
            _orthoBack = Mathf.Max(_orthoFrom, FitOrtho() * 0.6f);
            var c = cv.BodyCenter;
            _camTo = new Vector3(c.x, c.y, _camFrom.z);
            _orthoTo = FitOrtho() * 0.3f;
            BuildStage(cv);
            _phase = Phase.ZoomIn;
            _t = 0;
            _down = false;
            _pinch0 = -1f;
            _nextBlink = Time.time + 2f;
            SoundKit.Play("open", 0.6f);
        }

        /// <summary>집사가 그만둠 (나가기 버튼, 손가락 오므리기).</summary>
        public void RequestExit()
        {
            if (!Active || _phase == Phase.ZoomOut) return;
            if (Session != null && !Session.Done) Petting.Stop(Session);
            BeginZoomOut();
        }

        private void BuildStage(CatView cv)
        {
            if (_stage != null) Destroy(_stage.gameObject);
            var go = new GameObject("CloseUp");
            _stage = go.transform;
            _stage.SetParent(Cam.transform, false);
            _stage.localPosition = new Vector3(0, 0, -Cam.transform.position.z);

            var dGo = new GameObject("Dim");
            dGo.transform.SetParent(_stage, false);
            _dim = dGo.AddComponent<SpriteRenderer>();
            _dim.sprite = CuteArt.WhitePixel();
            _dim.sharedMaterial = Material;
            _dim.sortingOrder = StageOrder;
            _dim.color = new Color(0.16f, 0.11f, 0.09f, 0f);

            var rGo = new GameObject("Bust");
            rGo.transform.SetParent(_stage, false);
            _rig = rGo.AddComponent<CloseUpRig>();
            _rig.Build(cv.Look, Material);
            _rig.Group.sortingOrder = StageOrder + 10;
            _rig.Alpha = 0;
            Layout(0);
        }

        /// <summary>화면에 맞춰 크기·위치 (카메라 자식이라 확대 중에도 화면 크기 유지).</summary>
        private void Layout(float pop)
        {
            float o = Cam.orthographicSize, aspect = Cam.aspect;
            float viewW = o * 2f * aspect, viewH = o * 2f;
            var dt = CuteArt.WidthOf(_dim.sprite);
            _dim.transform.localScale = new Vector3(viewW * 1.2f / dt, viewH * 1.2f / dt, 1);
            float w = Mathf.Min(viewW * 0.95f, viewH * 0.58f) * Mathf.Lerp(0.85f, 1f, pop);
            _rig.transform.localScale = new Vector3(w, w, 1);
            _rig.transform.localPosition = new Vector3(0, -w * 0.47f + viewH * 0.05f, 0);
        }

        private void BeginZoomOut()
        {
            _phase = Phase.ZoomOut;
            _t = 0;
            _camFrom = Cam.transform.position;
            _orthoFrom = Cam.orthographicSize;
        }

        private readonly Queue<KeyValuePair<CatLook, BustPart>> _warm = new Queue<KeyValuePair<CatLook, BustPart>>();

        /// <summary>가까이 보기 그림을 한 프레임에 한 부위씩 미리 그려 둔다 (처음 들어갈 때 끊김 방지).</summary>
        public void WarmLater(CatLook look)
        {
            foreach (BustPart p in Enum.GetValues(typeof(BustPart))) _warm.Enqueue(new KeyValuePair<CatLook, BustPart>(look, p));
        }

        private void Update()
        {
            if (!Active)
            {
                if (_warm.Count > 0 && Time.frameCount > 30) { var w = _warm.Dequeue(); CloseUpArt.Get(w.Key, w.Value); }
                return;
            }
            float dt = Time.deltaTime;
            _t += dt;
            switch (_phase)
            {
                case Phase.ZoomIn:
                {
                    float p = Mathf.Clamp01(_t / 0.45f), e = 1f - Mathf.Pow(1f - p, 3f);
                    Cam.transform.position = Vector3.Lerp(_camFrom, _camTo, e);
                    Cam.orthographicSize = Mathf.Lerp(_orthoFrom, _orthoTo, e);
                    _dim.color = new Color(0.16f, 0.11f, 0.09f, DimAlpha * Mathf.Clamp01((p - 0.3f) / 0.7f));
                    _rig.Alpha = Mathf.Clamp01((p - 0.55f) / 0.45f);
                    Layout(_rig.Alpha);
                    if (p >= 1f) { _phase = Phase.Shown; _enterAt = Time.time; }
                    break;
                }
                case Phase.Shown:
                case Phase.Ending:
                    Layout(1f);
                    _rig.Alpha = 1f;
                    if (_phase == Phase.Shown) HandleInput();
                    Petting.Tick(Session, dt);
                    React();
                    if (Session.Done && !_endShown) OnEnd();
                    if (_phase == Phase.Ending && Time.time >= _endAt) BeginZoomOut();
                    break;
                case Phase.ZoomOut:
                {
                    float p = Mathf.Clamp01(_t / 0.4f), e = p * p * (3f - 2f * p);
                    Cam.transform.position = Vector3.Lerp(_camFrom, _camBack, e);
                    Cam.orthographicSize = Mathf.Lerp(_orthoFrom, _orthoBack, e);
                    _dim.color = new Color(0.16f, 0.11f, 0.09f, DimAlpha * (1f - Mathf.Clamp01(p / 0.6f)));
                    _rig.Alpha = 1f - Mathf.Clamp01(p / 0.5f);
                    Layout(1f);
                    if (p >= 1f)
                    {
                        _phase = Phase.None;
                        if (_stage != null) Destroy(_stage.gameObject);
                        foreach (var f in _fx) if (f.R != null) Destroy(f.R.gameObject);
                        _fx.Clear();
                        var s = Session;
                        Session = null;
                        Target = null;
                        Finished?.Invoke(s);
                    }
                    break;
                }
            }
            UpdateFx(dt);
        }

        // ───────────────────────── 입력 ─────────────────────────

        private void HandleInput()
        {
            int touches = 0;
            Vector2 pos = default;
            bool pressed = false;
            var ts = Touchscreen.current;
            if (ts != null)
            {
                Vector2 a = default, b = default;
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (touches == 0) a = t.position.ReadValue(); else if (touches == 1) b = t.position.ReadValue();
                    touches++;
                }
                if (touches >= 2)
                {
                    float d = Vector2.Distance(a, b);
                    if (_pinch0 < 0) _pinch0 = d;
                    else if (d < _pinch0 * 0.72f) { RequestExit(); return; }
                    _down = false;
                    _ignore = true;
                    return;
                }
                _pinch0 = -1f;
                if (touches == 1) { pressed = true; pos = a; }
            }
            var mouse = Mouse.current;
            if (touches == 0 && mouse != null)
            {
                if (mouse.scroll.ReadValue().y < -0.01f) { RequestExit(); return; }
                if (mouse.leftButton.isPressed) { pressed = true; pos = mouse.position.ReadValue(); }
            }

            if (pressed && !_down)
            {
                _down = true;
                _ignore = OverUi != null && OverUi(pos);
                _downPos = pos;
                _last = pos;
                _downTime = Time.time;
                _moved = 0;
                return;
            }
            if (!pressed && _down)
            {
                _down = false;
                if (!_ignore && _moved < 0.025f && Time.time - _downTime < 0.4f) Tap(_last);
                _ignore = false;
                return;
            }
            if (!pressed || _ignore) return;

            float len = (pos - _last).magnitude / Mathf.Max(1, Screen.width);
            _last = pos;
            if (len <= 0.0005f) return;
            _moved += len;
            var uv = _rig.CanvasAt(Cam.ScreenToWorldPoint(pos));
            var zone = CloseUpArt.ZoneAt(uv);
            if (zone == PetZone.None) return;
            if (zone == PetZone.Nose) zone = PetZone.Cheek; // 코 주변을 문지르면 볼
            double before = Session.Satisfaction;
            Petting.Stroke(Session, zone, len);
            _lastStrokeAt = Time.time;
            _lastUv = uv;
            if (Session.Satisfaction > before) _lastGainAt = Time.time;
            var world = Cam.ScreenToWorldPoint(pos);
            if (Session.LastFeel <= PetFeel.Like && Time.time - _lastHeartAt > 0.2f && Session.Satisfaction > before)
            {
                _lastHeartAt = Time.time;
                Spawn(CuteArt.Item(ItemKind.Heart), world, new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 1f, 0), 0.9f, 0.075f);
            }
            if (Session.LastFeel == PetFeel.Dislike && Time.time - _lastVeinAt > 0.45f)
            {
                _lastVeinAt = Time.time;
                var headTop = _rig.transform.TransformPoint(new Vector3(0.68f, 0.86f, 0) - (Vector3)CloseUpArt.Root);
                Spawn(CloseUpArt.Vein(), headTop, new Vector3(0, 0.15f, 0), 0.6f, 0.085f);
            }
            if (Session.LastFeel <= PetFeel.Like) SoundKit.Play("purr", 0.8f);
            if (Session.Annoyance > 0.45f && Time.time - _lastHissAt > 1.4f)
            {
                _lastHissAt = Time.time;
                SoundKit.Play("meow", 0.55f, 0.72f);
            }
        }

        private void Tap(Vector2 pos)
        {
            var uv = _rig.CanvasAt(Cam.ScreenToWorldPoint(pos));
            var zone = CloseUpArt.ZoneAt(uv);
            if (zone == PetZone.None) return;
            if (zone == PetZone.Nose)
            {
                Petting.Boop(Session);
                _boopUntil = Time.time + 0.45f;
                _rig.EarTwitch = 1f;
                SoundKit.Play("meow", 0.7f, 1.25f);
                SetFeedback("코를 콕! 냥");
            }
            else _rig.EarTwitch = 1f;
        }

        // ───────────────────────── 반응 ─────────────────────────

        private void SetFeedback(string s)
        {
            Feedback = s;
            FeedbackAt = Time.time;
        }

        private void React()
        {
            var s = Session;
            bool stroking = Time.time - _lastStrokeAt < 0.3f;
            var feel = s.LastFeel;

            if (s.Done)
            {
                bool happy = s.End == PetEnd.Satisfied;
                _rig.Eyes = happy ? BustEyes.Happy : s.End == PetEnd.Annoyed ? BustEyes.Angry : BustEyes.Open;
                _rig.EarsFlat = s.End == PetEnd.Annoyed ? 1f : 0f;
                _rig.MouthOpen = s.End == PetEnd.Annoyed;
                _rig.Purr = happy ? 1f : 0f;
                _rig.TailSpeed = s.End == PetEnd.Annoyed ? 3.5f : 1f;
                _rig.Tilt = happy ? 8f * Mathf.Sin(Time.time * 3f) : 0f;
                _rig.Blush = happy ? 1f : 0f;
                return;
            }

            if (stroking)
            {
                float tilt = Mathf.Clamp(-(_lastUv.x - 0.5f) * 60f, -13f, 13f);
                switch (feel)
                {
                    case PetFeel.Love:
                        _rig.Eyes = s.Satisfaction > 0.4f ? BustEyes.Happy : BustEyes.Half;
                        _rig.Purr = 1f; _rig.EarsFlat = 0f; _rig.TailSpeed = 0.7f;
                        _rig.Tilt = tilt; _rig.Lean = new Vector2((_lastUv.x - 0.5f) * 0.05f, 0.006f);
                        if (Time.time - _lastGainAt < 0.3f && (Feedback == null || Time.time - FeedbackAt > 1.5f)) SetFeedback(LoveText(s.LastZone));
                        break;
                    case PetFeel.Like:
                        _rig.Eyes = BustEyes.Half;
                        _rig.Purr = 0.6f; _rig.EarsFlat = 0f; _rig.TailSpeed = 0.9f;
                        _rig.Tilt = tilt * 0.6f; _rig.Lean = new Vector2((_lastUv.x - 0.5f) * 0.03f, 0f);
                        if (Feedback == null || Time.time - FeedbackAt > 1.5f) SetFeedback("기분 좋아해요");
                        break;
                    case PetFeel.Meh:
                        _rig.Eyes = BustEyes.Open;
                        _rig.Purr = 0f; _rig.EarsFlat = 0.3f; _rig.TailSpeed = 1.6f;
                        _rig.Tilt = 0f; _rig.Lean = Vector2.zero;
                        if (Feedback == null || Time.time - FeedbackAt > 1.5f) SetFeedback("그냥 그렇대요");
                        break;
                    default:
                        _rig.Eyes = s.Annoyance > 0.35f ? BustEyes.Angry : BustEyes.Open;
                        _rig.Purr = 0f; _rig.EarsFlat = 1f; _rig.TailSpeed = 3.2f;
                        _rig.Tilt = -tilt * 0.5f; _rig.Lean = new Vector2(-(_lastUv.x - 0.5f) * 0.04f, -0.004f);
                        SetFeedback(s.Annoyance > 0.6f ? "그만! 화나기 직전이에요" : "거긴 싫대요…");
                        break;
                }
                _rig.MouthOpen = feel == PetFeel.Dislike && s.Annoyance > 0.6f;
            }
            else
            {
                // 가만히: 깜빡깜빡, 귀 씰룩, 짜증은 서서히 풀린다
                _rig.Purr = Mathf.Max(0, _rig.Purr - Time.deltaTime * 0.8f);
                _rig.Tilt = 0f; _rig.Lean = Vector2.zero;
                _rig.EarsFlat = s.Annoyance > 0.4f ? 0.7f : 0f;
                _rig.TailSpeed = s.Annoyance > 0.4f ? 2.2f : 1f;
                _rig.MouthOpen = Time.time < _boopUntil && Time.time > _boopUntil - 0.3f;
                if (Time.time >= _nextBlink) { _blinkUntil = Time.time + 0.13f; _nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 5f); if (UnityEngine.Random.value < 0.3f) _rig.EarTwitch = 1f; }
                _rig.Eyes = Time.time < _blinkUntil || Time.time < _boopUntil - 0.2f ? BustEyes.Blink
                    : s.Satisfaction > 0.6f ? BustEyes.Half : s.Annoyance > 0.5f ? BustEyes.Angry : BustEyes.Open;
            }
            _rig.Blush = (float)s.Satisfaction;
        }

        private static string LoveText(PetZone z)
        {
            switch (z)
            {
                case PetZone.Head: return "머리 쓰다듬기 최고!";
                case PetZone.Cheek: return "볼 긁어 주는 거 좋아해요";
                case PetZone.Chin: return "턱 밑이 제일 좋대요";
                case PetZone.Chest: return "가슴 털이 보송보송";
                case PetZone.Belly: return "배를 허락했어요!";
                default: return "골골골…";
            }
        }

        private void OnEnd()
        {
            _endShown = true;
            _phase = Phase.Ending;
            var s = Session;
            if (s.End == PetEnd.Satisfied)
            {
                _endAt = Time.time + 1.9f;
                SoundKit.Play("fanfare", 0.6f);
                var center = _rig.transform.TransformPoint(new Vector3(0.5f, 0.66f, 0) - (Vector3)CloseUpArt.Root);
                for (int i = 0; i < 12; i++)
                {
                    float a = i / 12f * Mathf.PI * 2f;
                    Spawn(CuteArt.Item(ItemKind.Heart), center, new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.8f + 0.6f, 0) * 1.3f, 1.3f, 0.07f);
                }
                SetFeedback(RewardText(s));
            }
            else if (s.End == PetEnd.Annoyed)
            {
                _endAt = Time.time + 1.1f;
                SoundKit.Play("meow", 0.8f, 0.68f);
                SetFeedback(s.IsStray ? "하악! 도망갔어요…" : "삐졌어요… 다음엔 좋아하는 곳을 쓰다듬어 줘요");
            }
            else _endAt = Time.time;
        }

        public static string RewardText(PetSession s)
        {
            var parts = new List<string>();
            if (s.MoodGiven > 0.5) parts.Add($"기분 +{s.MoodGiven:0}");
            if (s.AffectionGiven > 0) parts.Add($"호감도 +{s.AffectionGiven:0}");
            if (s.HeartsGiven > 0) parts.Add($"젤리 하트 +{s.HeartsGiven}");
            string head = "푹 만족했어요!";
            if (parts.Count == 0) return head;
            return head + " " + string.Join(", ", parts);
        }

        // ───────────────────────── 효과 (하트·핏줄) ─────────────────────────

        private void Spawn(Sprite sp, Vector3 world, Vector3 vel, float life, float sizeOfView)
        {
            if (sp == null) return;
            var go = new GameObject("fx");
            go.transform.SetParent(_stage, false);
            go.transform.position = new Vector3(world.x, world.y, _stage.position.z);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sharedMaterial = Material;
            r.sortingOrder = StageOrder + 50;
            _fx.Add(new Fx { R = r, Vel = vel, Born = Time.time, Life = life, Size = sizeOfView });
        }

        private void UpdateFx(float dt)
        {
            float view = Cam.orthographicSize * 2f;
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
                f.R.transform.position += f.Vel * view * 0.12f * dt;
                float s = view * f.Size / CuteArt.WidthOf(f.R.sprite) * (0.6f + 0.4f * Mathf.Min(1f, age * 5f));
                f.R.transform.localScale = new Vector3(s, s, 1);
                f.R.color = new Color(1, 1, 1, 1f - age * age);
            }
        }
    }
}
