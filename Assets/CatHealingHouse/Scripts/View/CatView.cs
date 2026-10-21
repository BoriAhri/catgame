using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.View.Art;
using CatHealingHouse.World;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 고양이 한 마리의 화면 표시. 위치·방향·말랑한 스쿼시는 여기서, 부위별 몸짓은 CatRig 가 맡는다.
    /// </summary>
    public sealed class CatView : MonoBehaviour
    {
        private Cat _cat;
        private CatSimulation _sim;
        private HexLayout _layout;
        private CatRig _rig;
        private TextMesh _label;
        private float _phase;
        private SpriteRenderer _bubble, _bubbleIcon;
        private TextMesh _mark;
        private float _bornAt;

        // ── 말풍선 (쓰다듬기·확대 반응 때만) ──
        private const int MaxSpeaking = 2; // 한꺼번에 말하는 고양이 수
        private static int _speaking;
        private bool _speakingCounted;
        private Transform _speech;
        private SpriteRenderer _speechBox, _speechTail;
        private TextMesh _speechText;
        private MeshRenderer _speechTextR, _labelR;
        private float _speechAt = -99f, _speechDur;


        // ── 자유롭게 보이는 움직임 (판단은 칸, 화면만 부드럽게) ──
        // 칸마다 서는 자리를 조금씩 다르게, 여러 칸을 지날 때 스프링으로 따라가서 모서리를 가로지르고,
        // 가만히 있을 때도 칸 안에서 가끔 어슬렁거린다.
        private System.Random _rng;
        private bool _moveInit;
        private Cell _lastCell;
        private Vector3 _offPrev, _offCur;
        private Vector3 _display, _vel;
        private float _driftAt;
        private bool _drifting;
        private bool _faceLeft;
        private int _lastHeading = -1;

        /// <summary>길냥이면 이름표에 호감도를 보여주려고 둔다.</summary>
        public StraySystem Strays;

        public Cat Cat => _cat;
        public CatLook Look { get; private set; }
        private string _shownCostume;
        private Material _material;
        public Vector3 BodyCenter => _rig.transform.position + new Vector3(0, (float)_layout.Size * 0.4f, 0);

        public void Init(Cat cat, CatSimulation sim, HexLayout layout, Material material, Font font, CatLook look)
        {
            _cat = cat;
            _sim = sim;
            _layout = layout;
            _phase = cat.Id * 1.7f;
            _rng = new System.Random(cat.Id * 7919 + 13);

            var rigGo = new GameObject("Rig");
            rigGo.transform.SetParent(transform, false);
            _rig = rigGo.AddComponent<CatRig>();
            _rig.Build(look, material, _phase);
            _material = material;
            Look = look;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            _label = labelGo.AddComponent<TextMesh>();
            _label.font = font;
            _label.fontSize = 44;
            _label.characterSize = (float)layout.Size * 0.07f;
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = new Color(1f, 0.98f, 0.93f);
            var lr = labelGo.GetComponent<MeshRenderer>();
            lr.sharedMaterial = font.material;
            lr.sortingOrder = BoardView.LabelOrder + 10;

            // 머리 위 반응 말풍선 (! ? 하트)
            var bGo = new GameObject("Reaction");
            bGo.transform.SetParent(transform, false);
            _bubble = bGo.AddComponent<SpriteRenderer>();
            _bubble.sharedMaterial = material;
            _bubble.sprite = CuteArt.Bubble();
            _bubble.sortingOrder = BoardView.LabelOrder + 20;
            var iGo = new GameObject("Icon");
            iGo.transform.SetParent(bGo.transform, false);
            _bubbleIcon = iGo.AddComponent<SpriteRenderer>();
            _bubbleIcon.sharedMaterial = material;
            _bubbleIcon.sortingOrder = BoardView.LabelOrder + 21;
            var mGo = new GameObject("Mark");
            mGo.transform.SetParent(bGo.transform, false);
            _mark = mGo.AddComponent<TextMesh>();
            _mark.font = font;
            _mark.fontSize = 80;
            _mark.fontStyle = FontStyle.Bold;
            _mark.anchor = TextAnchor.MiddleCenter;
            _mark.alignment = TextAlignment.Center;
            _mark.color = SdfCanvas.Col("#D9534F");
            var mr = mGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = BoardView.LabelOrder + 22;
            bGo.SetActive(false);
            _bornAt = Time.time;
            _labelR = lr;
            BuildSpeech(font, material);
        }

        private void SyncReaction(float size, float scale)
        {
            var r = _cat.Reaction;
            var prof = Strays != null && _cat.Affiliation == Affiliation.Stray ? Strays.ProfileOf(_cat) : null;
            // 81 이상으로 문 앞에서 기다리는 중이면 하트를 띄운다
            bool ready = prof != null && _cat.StrayState == StrayState.Peeking && prof.Tier == AffectionTier.Bonded;
            bool show = r != StrayReaction.None || ready;
            if (_bubble.gameObject.activeSelf != show) _bubble.gameObject.SetActive(show);
            if (!show) return;

            float pop = 1f + Mathf.Max(0f, Mathf.Sin(Time.time * 6f)) * 0.06f;
            float bs = size * 0.62f * pop / CuteArt.WidthOf(_bubble.sprite);
            var bt = _bubble.transform;
            bt.localPosition = new Vector3(size * 0.62f, size * 0.62f * scale, 0); // 머리 옆 (이름표와 안 겹치게)
            bt.localScale = new Vector3(bs, bs, 1);

            bool heart = r == StrayReaction.Love || (ready && r == StrayReaction.None);
            _bubbleIcon.gameObject.SetActive(heart);
            _mark.gameObject.SetActive(!heart);
            if (heart)
            {
                _bubbleIcon.sprite = CuteArt.Item(Economy.ItemKind.Heart);
                float hs = 0.5f / CuteArt.WidthOf(_bubbleIcon.sprite);
                _bubbleIcon.transform.localPosition = new Vector3(0, 0.46f, 0);
                _bubbleIcon.transform.localScale = new Vector3(hs, hs, 1);
            }
            else
            {
                _mark.text = r == StrayReaction.Alert ? "!" : "?";
                _mark.color = r == StrayReaction.Alert ? SdfCanvas.Col("#D9534F") : SdfCanvas.Col("#6E95B5");
                _mark.characterSize = 0.075f;
                _mark.transform.localPosition = new Vector3(0, 0.47f, 0);
            }
        }

        private void BuildSpeech(Font font, Material material)
        {
            var go = new GameObject("Speech");
            go.transform.SetParent(transform, false);
            _speech = go.transform;

            _speechBox = new GameObject("Box").AddComponent<SpriteRenderer>();
            _speechBox.transform.SetParent(_speech, false);
            _speechBox.sharedMaterial = material;
            _speechBox.sprite = SpeechArt.Box();
            _speechBox.drawMode = SpriteDrawMode.Sliced;
            _speechBox.sortingOrder = BoardView.LabelOrder + 15;

            _speechTail = new GameObject("Tail").AddComponent<SpriteRenderer>();
            _speechTail.transform.SetParent(_speech, false);
            _speechTail.sharedMaterial = material;
            _speechTail.sprite = SpeechArt.Tail();
            _speechTail.sortingOrder = BoardView.LabelOrder + 16;

            var tGo = new GameObject("Text");
            tGo.transform.SetParent(_speech, false);
            _speechText = tGo.AddComponent<TextMesh>();
            _speechText.font = font;
            _speechText.fontSize = 48;
            _speechText.anchor = TextAnchor.MiddleCenter;
            _speechText.alignment = TextAlignment.Center;
            _speechText.color = SdfCanvas.Col("#5A4032");
            _speechTextR = tGo.GetComponent<MeshRenderer>();
            _speechTextR.sharedMaterial = font.material;
            _speechTextR.sortingOrder = BoardView.LabelOrder + 17;
            go.SetActive(false);
        }

        /// <summary>머리 위에 한마디 띄운다 (혼잣말·쓰다듬기·첫 만남 등).</summary>
        public void Say(string text, float dur = 3.2f)
        {
            if (string.IsNullOrEmpty(text) || _speech == null) return;
            _speechText.text = text;
            _speechAt = Time.time;
            _speechDur = dur;
            if (!_speakingCounted) { _speaking++; _speakingCounted = true; }
        }

        public bool Speaking => Time.time - _speechAt < _speechDur;

        private void StopSpeech()
        {
            if (_speakingCounted) { _speaking--; _speakingCounted = false; }
            if (_speech != null && _speech.gameObject.activeSelf) _speech.gameObject.SetActive(false);
            _speechAt = -99f;
        }

        private void OnDestroy()
        {
            if (_speakingCounted) { _speaking--; _speakingCounted = false; }
        }

        private void SyncSpeech(float size, float scale, FurnitureInstance f)
        {
            float age = Time.time - _speechAt;
            bool reactionUp = _bubble.gameObject.activeSelf;
            if (age >= _speechDur || reactionUp)
            {
                if (_speakingCounted || _speech.gameObject.activeSelf) StopSpeech();
                return;
            }
            if (!_speech.gameObject.activeSelf) _speech.gameObject.SetActive(true);

            // 글자 크기에 맞춰 상자를 늘린다 (9-slice, 단위는 칸 크기)
            _speechText.characterSize = size * 0.1f;
            _label.text = _cat.Name; // 말하는 동안에는 이름만 (덜 복잡하게)
            var tb = _speechTextR.localBounds;
            _speechText.transform.localPosition = new Vector3(-tb.center.x, -tb.center.y, 0);
            float k = size * 1.7f;
            float bw = Mathf.Max(tb.size.x + size * 0.6f, size * 1.2f), bh = tb.size.y + size * 0.42f;
            _speechBox.transform.localScale = new Vector3(k, k, 1);
            _speechBox.size = new Vector2(bw / k, bh / k);
            _speechTail.transform.localScale = new Vector3(k, k, 1);
            _speechTail.transform.localPosition = new Vector3(-bw * 0.18f, -bh * 0.5f + k * 0.045f, 0);

            // 이름표 위에
            float labelTop = size * 1.05f * scale + (_labelR != null ? _labelR.localBounds.max.y : size * 0.3f);
            float y = labelTop + size * 0.08f + k * 0.24f + bh * 0.5f + Mathf.Sin(Time.time * 2.3f + _phase) * size * 0.02f;
            _speech.localPosition = new Vector3(0, y, 0);

            // 톡 튀어나오고 끝에 사르르
            float p = Mathf.Clamp01(age / 0.28f);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float pop = 1f + c3 * Mathf.Pow(p - 1f, 3f) + c1 * Mathf.Pow(p - 1f, 2f);
            float a = Mathf.Clamp01((_speechDur - age) / 0.35f);
            _speech.localScale = new Vector3(pop, pop, 1);
            var wc = new Color(1f, 1f, 1f, a);
            _speechBox.color = wc;
            _speechTail.color = wc;
            var tc = _speechText.color;
            tc.a = a;
            _speechText.color = tc;
        }


        public void Sync(BoardId visibleBoard)
        {
            bool visible = _cat.Cell.Board == visibleBoard;
            if (_rig.gameObject.activeSelf != visible)
            {
                _rig.gameObject.SetActive(visible);
                _label.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                if (_bubble.gameObject.activeSelf) _bubble.gameObject.SetActive(false);
                StopSpeech();
                return;
            }

            float size = (float)_layout.Size;

            // 가구 안: 사용 지점(슬롯)으로 들어간 모습
            float inside = 0f;
            if (_cat.State == CatState.Entering) inside = 1f - (float)(_cat.StateTimer / _sim.Tuning.EnterSeconds);
            else if (_cat.State == CatState.Using) inside = 1f;
            else if (_cat.State == CatState.Exiting) inside = (float)(_cat.StateTimer / _sim.Tuning.ExitSeconds);
            inside = Mathf.Clamp01(inside);

            // 칸이 바뀌면(걸음 시작) 새 칸에서 설 자리를 고른다
            bool crossBoard = _cat.PrevCell.Board != _cat.Cell.Board;
            if (!_moveInit || _cat.Cell != _lastCell)
            {
                _offPrev = _moveInit && _cat.PrevCell == _lastCell ? _offCur : Vector3.zero;
                _offCur = RandomOffset(size, _sim.World.IsTransit(_cat.Cell) ? 1f : 0.35f);
                _lastCell = _cat.Cell;
                _drifting = false;
                _driftAt = Time.time + RandRange(2.5f, 6f);
            }

            // 가만히 있을 때 가끔 칸 안에서 자리를 옮긴다 (가구 안·메뉴 대기·교감 중은 제외)
            bool canDrift = !_cat.IsHopping && inside <= 0f && !_cat.Held && _cat.Activity == 0
                && (_cat.State == CatState.Idle || _cat.State == CatState.Wandering) && _sim.World.IsTransit(_cat.Cell);
            if (canDrift && Time.time >= _driftAt)
            {
                _offPrev = _offCur;
                _offCur = RandomOffset(size, 1f);
                _drifting = true;
                _driftAt = Time.time + RandRange(6f, 14f);
            }
            if (!canDrift) _drifting = false;

            var from = _layout.ToWorld(_cat.PrevCell.Coord);
            var to = _layout.ToWorld(_cat.Cell.Coord);
            float t = Mathf.Clamp01((float)_cat.HopProgress);
            float e = t * t * (3f - 2f * t);
            var offset = Vector3.Lerp(_offPrev, _offCur, _cat.IsHopping ? e : 1f) * (1f - inside);
            var basePos = (crossBoard ? to : Vector3.Lerp(from, to, e)) + offset;
            basePos += new Vector3(0, -size * 0.3f * (float)_layout.YScale, 0);

            var f = _cat.HasTarget ? _sim.World.GetFurniture(_cat.TargetFurnitureId) : null;
            var interaction = f != null ? f.Def.Interaction : InteractionType.None;
            Vector3 slotOffset = Vector3.zero;
            float scale = 1f;
            if (f != null && inside > 0f)
            {
                int cap = Mathf.Max(1, f.Def.Capacity);
                int slot = Mathf.Max(0, _cat.SlotIndex);
                if (cap > 1)
                {
                    float ang = (slot / (float)cap) * Mathf.PI * 2f + 0.6f;
                    slotOffset = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang) * 0.4f, 0) * size * 0.25f;
                }
                if (interaction == InteractionType.Climb)
                    slotOffset += new Vector3(0, size * (f.Def.Size > 1 ? 0.55f + 0.32f * (slot % 3) : 0.2f), 0);
                if (interaction == InteractionType.Enter) { scale = 0.78f; slotOffset += new Vector3(0, size * 0.05f, 0); }
                // 벽 가구: 입구 칸에서 벽에 붙은 발판·선반 자리로 폴짝 (정렬은 입구 칸 기준이라 바닥 물건 뒤에 보인다)
                if (f.Def.WallMounted && WallSpotOf != null)
                {
                    var spot = WallSpotOf(f, slot);
                    if (spot.HasValue) slotOffset = spot.Value + new Vector3(0, -size * 0.05f, 0) - basePos;
                }
                if (interaction == InteractionType.Run) slotOffset += new Vector3(0, size * 0.22f, 0);
            }
            var target = basePos + slotOffset * inside;
            scale = Mathf.Lerp(1f, scale, inside);

            // 화면 위치는 목표를 스프링처럼 따라간다: 칸과 칸 사이를 곡선으로 잇고 모서리를 가로지른다
            if (!_moveInit || crossBoard && _cat.IsHopping || (target - _display).sqrMagnitude > size * size * 9f)
            {
                _display = target;
                _vel = Vector3.zero;
                _moveInit = true;
            }
            else
            {
                float smooth = _drifting ? 0.55f : _cat.IsHopping ? 0.13f : 0.2f;
                _display = Vector3.SmoothDamp(_display, target, ref _vel, smooth, Mathf.Infinity, Time.deltaTime);
            }
            var pos = _display;
            float speed = _vel.magnitude;

            // 몸짓 고르기 (칸 안에서 어슬렁거릴 때도 걸음)
            var pose = PoseFor(_cat, f, inside);
            if (pose == CatPose.Idle && speed > size * 0.35f) pose = CatPose.Walk;

            // 말랑한 스쿼시 앤 스트레치 (몸 전체)
            float time = Time.time;
            float squash = 1f, bob = 0f;
            if (_cat.IsHopping)
            {
                float k = Mathf.Sin((float)_cat.HopProgress * Mathf.PI * 2f);
                bob = Mathf.Abs(k) * size * 0.05f;
                squash = 1f + k * 0.035f;
            }
            else if (pose == CatPose.Run)
            {
                bob = Mathf.Abs(Mathf.Sin((time + _phase) * 14f)) * size * 0.05f;
                squash = 1f + Mathf.Sin((time + _phase) * 14f) * 0.04f;
            }
            if (_cat.State == CatState.Entering || _cat.State == CatState.Exiting)
            {
                // 폴짝: 드나들 때 한 번 눌렸다 늘어난다
                float k = Mathf.Sin(inside * Mathf.PI);
                bob += k * size * 0.12f;
                squash *= 1f + k * 0.08f;
            }

            transform.position = pos + new Vector3(0, bob, 0);

            // 방향: 움직이는 중이면 실제 이동 방향, 멈췄을 때는 마지막 방향(또는 새로 정한 방향)
            if (_cat.Heading != _lastHeading)
            {
                _lastHeading = _cat.Heading;
                _faceLeft = _cat.Heading == 2 || _cat.Heading == 3 || _cat.Heading == 4;
            }
            if (speed > size * 0.3f && Mathf.Abs(_vel.x) > speed * 0.2f && inside <= 0f) _faceLeft = _vel.x < 0;
            bool faceLeft = _faceLeft;
            float w = size * 1.25f * scale;
            _rig.transform.localScale = new Vector3((faceLeft ? -1f : 1f) * w * (2f - squash), w * squash, 1);
            if (_cat.Costume != _shownCostume)
            {
                _shownCostume = _cat.Costume;
                _rig.SetCostume(_shownCostume != null ? CraftArt.Costume(_shownCostume) : null, _shownCostume != null ? CraftArt.PartOf(_shownCostume) : CatPart.Body, _shownCostume != null ? CraftArt.OrderOf(_shownCostume) : 0, _material);
            }
            _rig.Animate(pose, (float)_cat.HopProgress, time);

            // 그리기 순서: 바닥 위치로 정렬, 가구 위에 올라가 있으면 가구보다 앞
            _rig.Group.sortingOrder = BoardView.ObjectOrder(_display.y - slotOffset.y * inside) + 1 + (inside > 0.5f ? 40 : 0);

            _label.transform.localPosition = new Vector3(0, size * 1.05f * scale, 0);
            var profile = Strays != null && _cat.Affiliation == Affiliation.Stray ? Strays.ProfileOf(_cat) : null;
            _label.text = profile != null
                ? $"{_cat.Name} <size=30>({profile.TemperamentName})</size>\n<size=32>{ActivityText(_cat, f)} · 호감 {profile.Affection:0}</size>"
                : $"{_cat.Name}\n<size=32>{ActivityText(_cat, f)}</size>";
            SyncReaction(size, scale);
            SyncSpeech(size, scale, f);
        }

        /// <summary>칸 안의 아무 자리 (육각형 안쪽에 들어오게, 세로는 쿼터뷰 압축).</summary>
        private Vector3 RandomOffset(float size, float k)
        {
            double a = _rng.NextDouble() * System.Math.PI * 2;
            double r = System.Math.Sqrt(_rng.NextDouble()) * size * 0.3f * k;
            return new Vector3((float)(System.Math.Cos(a) * r), (float)(System.Math.Sin(a) * r * _layout.YScale), 0);
        }

        private float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private static CatPose PoseFor(Cat cat, FurnitureInstance f, float inside)
        {
            if (cat.IsHopping) return CatPose.Walk;
            if (cat.State == CatState.Lounging) return cat.Lounge == LoungeKind.Lie ? CatPose.Lie : CatPose.SitIdle;
            if (cat.LoungeVisit && cat.State == CatState.Using && f != null)
            {
                if (f.Def.Interaction == InteractionType.Enter) return CatPose.Hide;
                return cat.Lounge == LoungeKind.Lie ? CatPose.Lie : CatPose.Sit;
            }
            if (cat.Affiliation == Affiliation.Stray && cat.Activity > 0)
            {
                switch ((StrayAction)(cat.Activity - 1))
                {
                    case StrayAction.Snack: return CatPose.Eat;
                    case StrayAction.Play: return CatPose.Run;
                    case StrayAction.Pet: return CatPose.Sleep; // 눈 감고 골골
                }
            }
            switch (cat.State)
            {
                case CatState.Grooming: return CatPose.Groom;
                case CatState.Entering:
                case CatState.Exiting: return CatPose.Idle;
                case CatState.Using:
                    if (f == null) return CatPose.Idle;
                    switch (cat.TargetNeed)
                    {
                        case NeedType.Hunger: return CatPose.Eat;
                        case NeedType.Bladder: return CatPose.Litter;
                        case NeedType.Energy: return f.Def.Interaction == InteractionType.Enter ? CatPose.Hide : CatPose.Sleep;
                        case NeedType.Fun:
                            switch (f.Def.Interaction)
                            {
                                case InteractionType.Run: return CatPose.Run;
                                case InteractionType.Climb: return CatPose.Sit;
                                default: return CatPose.Groom; // 스크래처: 앞발 긁기
                            }
                    }
                    return CatPose.Idle;
                default: return CatPose.Idle;
            }
        }

        /// <summary>벽 가구의 slot 번째 자리 (HouseGame 이 연결). 없으면 null.</summary>
        public static System.Func<FurnitureInstance, int, Vector3?> WallSpotOf;

        public static string ActivityText(Cat cat, FurnitureInstance f)
        {
            if (cat.Affiliation == Affiliation.Stray)
            {
                if (cat.Activity > 0)
                {
                    switch ((StrayAction)(cat.Activity - 1))
                    {
                        case StrayAction.Snack: return "간식 냠냠";
                        case StrayAction.Play: return "신나게 노는 중";
                        case StrayAction.Pet: return "골골송~";
                    }
                }
                if (cat.Held) return "뭐냥?";
                switch (cat.StrayState)
                {
                    case StrayState.Approaching: return cat.IsHopping ? "살금살금" : "두리번";
                    case StrayState.Eating: return "냠냠";
                    case StrayState.ToLoaf: return "쉬러 가는 중";
                    case StrayState.Loafing: return "식빵 굽는 중";
                    case StrayState.ToDoor: return "집 쪽으로";
                    case StrayState.Peeking: return "집 안을 기웃기웃";
                    case StrayState.ReadyToAdopt: return "들어가고 싶어!";
                    case StrayState.Fleeing: return "후다닥!";
                    case StrayState.Leaving: return "안녕~";
                    case StrayState.Adopting: return "우리 집으로!";
                    default: return "";
                }
            }
            if (cat.Sunbathing) return cat.State == CatState.ToSpot ? "햇빛 자리로" : "햇빛 쬐는 중";
            if (cat.State == CatState.Lounging) return cat.Lounge == LoungeKind.Lie ? "누워서 멍~" : "앉아서 멍~";
            if (cat.LoungeVisit && f != null)
            {
                if (cat.State == CatState.Using)
                    return f.Def.Interaction == InteractionType.Enter ? $"{f.Def.DisplayName}에서 멍~"
                         : cat.Lounge == LoungeKind.Lie ? $"{f.Def.DisplayName} 위에 나른" : $"{f.Def.DisplayName} 위에서 멍~";
                if (cat.State == CatState.MovingToTarget) return $"{f.Def.DisplayName}(으)로 쉬러";
            }
            switch (cat.State)
            {
                case CatState.Idle: return "멍~";
                case CatState.Wandering: return "산책 중";
                case CatState.Grooming: return "그루밍";
                case CatState.MovingToTarget: return f != null ? $"{f.Def.DisplayName}(으)로" : "이동 중";
                case CatState.Entering: return "들어가는 중";
                case CatState.Exiting: return "나오는 중";
                case CatState.Using:
                    if (f == null) return "…";
                    switch (cat.TargetNeed)
                    {
                        case NeedType.Hunger: return "냠냠";
                        case NeedType.Bladder: return "볼일 중";
                        case NeedType.Energy: return f.Def.Interaction == InteractionType.Enter ? "숨어서 쿨쿨" : "쿨쿨 zZ";
                        case NeedType.Fun:
                            switch (f.Def.Interaction)
                            {
                                case InteractionType.Climb: return f.Def.WallMounted ? $"{f.Def.DisplayName} 위" : "캣타워 위";
                                case InteractionType.Run: return "캣휠 달리기";
                                default: return "스크래칭";
                            }
                    }
                    return f.Def.DisplayName;
            }
            return "";
        }
    }
}
