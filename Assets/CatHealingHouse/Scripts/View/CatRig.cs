using System.Collections.Generic;
using CatHealingHouse.View.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatHealingHouse.View
{
    /// <summary>고양이 몸짓 종류. CatView 가 고양이 상태에서 골라 넘긴다.</summary>
    public enum CatPose { Idle, Walk, Run, Sleep, Eat, Litter, Groom, Sit, Hide, Lie, SitIdle,
        // 몸짓: 기지개, 하품, 옆으로 길게 눕기, 뒷발로 귀 긁기, 우다다 질주
        Stretch, Yawn, Sprawl, EarScratch, Zoom }

    /// <summary>쓰다듬을 때 눈 (0 평소, 1 반쯤 감음, 2 웃는 눈, 3 감음).</summary>
    public enum PetEyes { Normal, Half, Happy, Closed }

    /// <summary>쓰다듬을 때 자세 (머리 치켜세우기, 턱 치켜세우기, 엉덩이 치켜세우기). 발라당은 PetBelly 로 따로.</summary>
    public enum PetPose { None, HeadLift, ChinLift, RumpUp }

    /// <summary>
    /// 임시 뼈대(컷아웃) 고양이. 부위별 스프라이트를 관절에 매달고 코드로 회전·이동해서 움직인다.
    /// 2D Animation 패키지의 진짜 리깅으로 바꿀 때도 부위 이름과 관절 위치는 그대로 쓴다.
    /// 루트 피벗은 발바닥 중앙, 전체 그림 폭 = 1 (localScale 로 키운다).
    /// </summary>
    [RequireComponent(typeof(SortingGroup))]
    public sealed class CatRig : MonoBehaviour
    {
        private readonly Dictionary<CatPart, Transform> _bones = new Dictionary<CatPart, Transform>();
        private readonly Dictionary<CatPart, SpriteRenderer> _renderers = new Dictionary<CatPart, SpriteRenderer>();
        private readonly Dictionary<CatPart, Vector3> _restPos = new Dictionary<CatPart, Vector3>();
        private SortingGroup _group;
        private float _phase;
        private float _nextBlink, _nextEarTwitch;
        private int _twitchEar;

        public SortingGroup Group => _group;

        public void Build(CatLook look, Material material, float seed)
        {
            _group = GetComponent<SortingGroup>();
            _phase = seed;
            var lib = ArtLibrary.Active;
            Vector2 Joint(CatPart p) => lib != null ? lib.CatJoint(look.SkinId, p) : CatParts.Joint[p];

            // 부모(머리 등)를 먼저 만들고 자식(귀, 눈)을 나중에 만든다
            var order = new List<CatPart>();
            foreach (CatPart p in System.Enum.GetValues(typeof(CatPart))) if (!CatParts.Parent(p).HasValue) order.Add(p);
            foreach (CatPart p in System.Enum.GetValues(typeof(CatPart))) if (CatParts.Parent(p).HasValue) order.Add(p);
            foreach (var part in order)
            {
                var go = new GameObject(part.ToString());
                var parent = CatParts.Parent(part);
                var parentT = parent.HasValue ? _bones[parent.Value] : transform;
                go.transform.SetParent(parentT, false);

                // 관절 위치: 루트(발바닥) 기준, 부모가 있으면 부모 관절 기준
                var j = Joint(part);
                var origin = parent.HasValue ? Joint(parent.Value) : CatParts.Root;
                var local = (Vector3)(j - origin);
                go.transform.localPosition = local;
                _restPos[part] = local;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = material;
                sr.sprite = CatParts.Get(look, part);
                sr.sortingOrder = CatParts.Order(part);
                _bones[part] = go.transform;
                _renderers[part] = sr;
            }
            _renderers[CatPart.EyesClosed].enabled = false;
            _renderers[CatPart.EyesHalf].enabled = false;
            _renderers[CatPart.EyesHappy].enabled = false;
            _renderers[CatPart.Blush].color = new Color(1, 1, 1, 0);
            if (_renderers.ContainsKey(CatPart.BellyBody)) _renderers[CatPart.BellyBody].enabled = false;
            if (_renderers.ContainsKey(CatPart.MouthOpen)) _renderers[CatPart.MouthOpen].enabled = false;
            // 진짜 그림(AI 그림 등): 표정은 머리 통째 그림을 덮어쓰고, 발라당 그림은 머리까지 들어 있다
            _fullHeadArt = lib != null && lib.CatPartSprite(look.SkinId, CatPart.MouthOpen) != null;
            // 테두리 실루엣: 부위마다 같은 뼈에 붙이고, 모든 색칠보다 아래에 그린다 → 겹치는 안쪽 선은 가려지고 바깥 둘레만 선
            _outlines.Clear();
            if (lib != null)
                foreach (var part in order)
                {
                    var ol = lib.CatPartOutline(look.SkinId, part);
                    if (ol == null) continue;
                    var go = new GameObject(part + "_Outline");
                    go.transform.SetParent(_bones[part], false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sharedMaterial = material;
                    sr.sprite = ol;
                    sr.sortingOrder = -40 + CatParts.Order(part);
                    _outlines[part] = sr;
                }
            if (_outlines.Count > 0) _renderers[CatPart.Shadow].sortingOrder = -60;
            _bellyHasHead = lib != null && lib.CatPartSprite(look.SkinId, CatPart.BellyBody) != null;
            // 자세별 한 장짜리 몸 그림 (앉기·식빵·웅크려 자기·쭉 뻗기): 다리를 접는 자세는 부위를 돌리는 대신 이 그림을 쓴다
            _poseArts.Clear();
            if (lib != null)
                foreach (var pa in lib.PoseArts(look.SkinId))
                    if (pa != null && pa.sprite != null) _poseArts[pa.pose] = pa;
            if (_poseArts.Count > 0 && _poseR == null)
            {
                var go = new GameObject("PoseBody");
                go.transform.SetParent(transform, false);
                _poseR = go.AddComponent<SpriteRenderer>();
                _poseR.sharedMaterial = material;
                _poseR.sortingOrder = CatParts.Order(CatPart.Body);
                var og = new GameObject("PoseBody_Outline");
                og.transform.SetParent(go.transform, false);
                _poseOl = og.AddComponent<SpriteRenderer>();
                _poseOl.sharedMaterial = material;
                _poseOl.sortingOrder = -40 + CatParts.Order(CatPart.Body);
                _poseR.enabled = _poseOl.enabled = false;
            }
        }

        private readonly Dictionary<ArtLibrary.CatPoseArt, ArtLibrary.PoseArt> _poseArts = new Dictionary<ArtLibrary.CatPoseArt, ArtLibrary.PoseArt>();
        private SpriteRenderer _poseR, _poseOl;
        private ArtLibrary.PoseArt _curArt;
        private float _artStart;

        /// <summary>이 자세에서 쓸 한 장짜리 몸 그림 종류 (없으면 부위로 움직인다).</summary>
        private static ArtLibrary.CatPoseArt? ArtFor(CatPose pose)
        {
            switch (pose)
            {
                case CatPose.Sit: case CatPose.SitIdle: case CatPose.Yawn: case CatPose.Litter: case CatPose.Groom: case CatPose.EarScratch:
                    return ArtLibrary.CatPoseArt.Sit;
                case CatPose.Lie: case CatPose.Hide: return ArtLibrary.CatPoseArt.Loaf;
                case CatPose.Sleep: return ArtLibrary.CatPoseArt.Curl;
                case CatPose.Sprawl: return ArtLibrary.CatPoseArt.Sprawl;
                default: return null;
            }
        }

        /// <summary>지금 한 장짜리 자세 그림이 보이는 중.</summary>
        public bool PoseArtShown => _curArt != null && !BellyShown;

        private bool _fullHeadArt, _bellyHasHead;
        private readonly Dictionary<CatPart, SpriteRenderer> _outlines = new Dictionary<CatPart, SpriteRenderer>();

        // 자세가 바뀔 때 직전 모습에서 부드럽게 넘어가기
        private const float BlendSeconds = 0.25f;
        private bool _hasPose, _blending;
        private CatPose _lastPose;
        private float _poseStart;
        private readonly Dictionary<CatPart, (Vector3 pos, Quaternion rot, Vector3 scale)> _snap = new Dictionary<CatPart, (Vector3, Quaternion, Vector3)>();
        private static float Smooth(float x) => x * x * (3f - 2f * x);

        // ── 쓰다듬기 반응 (PetTouch 가 목표를 정하면 부드럽게 따라간다) ──
        public bool PetActive;
        public PetEyes PetEyes;
        public float PetTilt, PetEars, PetPurr, PetBlush, PetTail = 1f;
        public PetPose PetPose;
        /// <summary>발라당 (등을 대고 누워 배를 보인다).</summary>
        public bool PetBelly;
        /// <summary>뒷발 팡팡 (배 함정). 0~1, 저절로 줄어든다.</summary>
        public float PetKick;
        private float _wLift, _wChin, _wRump, _wBelly;
        /// <summary>지금 발라당 그림이 보이는 중 (부위 판정이 달라진다).</summary>
        public bool BellyShown => _wBelly > 0.5f;
        private bool _costumeWorn;
        private CatPart _costumePart;
        private float _pTilt, _pEars, _pPurr, _pBlush, _pTail = 1f, _pTailT, _pWeight;

        /// <summary>뼈 기준 캔버스 좌표 (쓰다듬는 부위 판정용). 방향 뒤집기는 transform 이 처리한다.</summary>
        public Vector2 CanvasAt(Vector3 world, CatPart bone)
        {
            var t = _bones[bone];
            return (Vector2)t.InverseTransformPoint(world) + CatParts.Joint[bone];
        }

        public Vector2 CanvasAtRoot(Vector3 world) => (Vector2)transform.InverseTransformPoint(world) + CatParts.Root;

        private SpriteRenderer _costume;

        /// <summary>코스튬을 입힌다 (null 이면 벗김). 그림 피벗이 관절이라 부위에 매달기만 하면 제자리에 맞는다.</summary>
        public void SetCostume(Sprite sprite, CatPart part, int order, Material material)
        {
            _costumeWorn = sprite != null;
            _costumePart = part;
            if (sprite == null) { if (_costume != null) _costume.enabled = false; return; }
            if (_costume == null)
            {
                var go = new GameObject("Costume");
                _costume = go.AddComponent<SpriteRenderer>();
                _costume.sharedMaterial = material;
            }
            _costume.transform.SetParent(_bones[part], false);
            _costume.transform.localPosition = Vector3.zero;
            _costume.transform.localRotation = Quaternion.identity;
            _costume.transform.localScale = Vector3.one;
            _costume.sprite = sprite;
            _costume.sortingOrder = order;
            _costume.enabled = true;
        }

        private void Rot(CatPart p, float deg) => _bones[p].localRotation = Quaternion.Euler(0, 0, deg);
        private void Pos(CatPart p, Vector3 offset) => _bones[p].localPosition = _restPos[p] + offset;
        private static bool IsLeg(CatPart p) => p == CatPart.LegFrontNear || p == CatPart.LegFrontFar || p == CatPart.LegBackNear || p == CatPart.LegBackFar;

        /// <summary>크기 바꾸기. 다리를 많이 접으면(식빵·앉기) 납작하게 눌린 모양 대신 몸 밑에 숨긴다.</summary>
        private void Scale(CatPart p, float x, float y)
        {
            _bones[p].localScale = new Vector3(x, y, 1);
            if (IsLeg(p)) _renderers[p].enabled = y > 0.7f;
        }

        /// <summary>
        /// 매 프레임 자세를 잡는다. hop 은 걸음 진행도(0~1, 걷는 중일 때), facingLeft 면 좌우 반전.
        /// </summary>
        public void Animate(CatPose pose, float hop, float time)
        {
            float t = time + _phase;

            // 자세가 바뀌었으면 지금 모습을 찍어 두고 그 모습에서 새 자세로 섞는다
            if (!_hasPose || pose != _lastPose)
            {
                _blending = _hasPose;
                _hasPose = true;
                _lastPose = pose;
                _poseStart = time;
                if (_blending) foreach (var kv in _bones) _snap[kv.Key] = (kv.Value.localPosition, kv.Value.localRotation, kv.Value.localScale);
            }
            float pt = time - _poseStart;
            float mouth = 0f;
            int poseEyes = 0;

            // 기본 자세로 되돌리기
            foreach (var kv in _bones)
            {
                if (IsLeg(kv.Key)) _renderers[kv.Key].enabled = true;
                kv.Value.localRotation = Quaternion.identity;
                kv.Value.localScale = Vector3.one;
                kv.Value.localPosition = _restPos[kv.Key];
            }

            // 공통: 꼬리 살랑, 가끔 귀 쫑긋, 눈 깜빡
            float tailSway = Mathf.Sin(t * 1.6f) * 10f;
            Rot(CatPart.Tail, tailSway);
            if (time > _nextEarTwitch + 0.25f)
            {
                _nextEarTwitch = time + Random.Range(2f, 6f);
                _twitchEar = Random.value < 0.5f ? 0 : 1;
            }
            if (time > _nextEarTwitch && time < _nextEarTwitch + 0.25f)
            {
                float k = Mathf.Sin((time - _nextEarTwitch) / 0.25f * Mathf.PI) * 18f;
                Rot(_twitchEar == 0 ? CatPart.EarL : CatPart.EarR, _twitchEar == 0 ? k : -k);
            }
            if (time > _nextBlink + 0.14f) _nextBlink = time + Random.Range(2.5f, 5.5f);
            bool eyesClosed = time > _nextBlink && time < _nextBlink + 0.14f;

            float tipCurl = 0f;
            switch (pose)
            {
                case CatPose.Idle:
                {
                    float breathe = Mathf.Sin(t * 2.2f);
                    Scale(CatPart.Body, 1f - breathe * 0.01f, 1f + breathe * 0.02f);
                    Rot(CatPart.Head, Mathf.Sin(t * 0.7f) * 3f);
                    Pos(CatPart.Head, new Vector3(0, breathe * 0.004f, 0));
                    break;
                }
                case CatPose.Walk:
                case CatPose.Run:
                {
                    bool run = pose == CatPose.Run;
                    float ph = run ? t * 14f : hop * Mathf.PI * 2f;
                    float amp = run ? 34f : 22f;
                    float swing = Mathf.Sin(ph) * amp;
                    // 대각선 다리끼리 같이 움직인다 (앞-가까운 & 뒤-먼 / 앞-먼 & 뒤-가까운)
                    Rot(CatPart.LegFrontNear, swing);
                    Rot(CatPart.LegBackFar, swing);
                    Rot(CatPart.LegFrontFar, -swing);
                    Rot(CatPart.LegBackNear, -swing);
                    float bob = Mathf.Abs(Mathf.Sin(ph)) * (run ? 0.02f : 0.012f);
                    Pos(CatPart.Body, new Vector3(0, bob, 0));
                    Pos(CatPart.Head, new Vector3(0, bob * 1.2f, 0));
                    Rot(CatPart.Head, Mathf.Sin(ph * 2f) * 2f);
                    Rot(CatPart.Tail, 14f + Mathf.Sin(ph) * (run ? 16f : 8f));
                    tipCurl = -14f;
                    break;
                }
                case CatPose.Sleep:
                case CatPose.Hide:
                {
                    // 웅크려 식빵: 다리를 접고, 머리를 낮추고, 꼬리를 감는다
                    float breathe = Mathf.Sin(t * 1.4f);
                    foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar, CatPart.LegBackNear, CatPart.LegBackFar })
                        Scale(leg, 1f, 0.35f);
                    Pos(CatPart.Body, new Vector3(0, -0.055f, 0));
                    Scale(CatPart.Body, 1.04f + breathe * 0.015f, 0.92f + breathe * 0.03f);
                    Pos(CatPart.Head, new Vector3(-0.02f, -0.1f, 0));
                    Rot(CatPart.Head, -6f);
                    Rot(CatPart.EarL, 8f);
                    Rot(CatPart.EarR, -8f);
                    Pos(CatPart.Tail, new Vector3(0.05f, -0.06f, 0));
                    Rot(CatPart.Tail, 70f + Mathf.Sin(t * 0.8f) * 4f);
                    tipCurl = 45f;
                    eyesClosed = true;
                    break;
                }
                case CatPose.Lie:
                {
                    // 누워서 멍: 식빵 자세인데 고개는 들고 눈을 뜬 채 천천히 느린 눈깜빡임
                    float breathe = Mathf.Sin(t * 1.5f);
                    foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar, CatPart.LegBackNear, CatPart.LegBackFar })
                        Scale(leg, 1f, 0.35f);
                    Pos(CatPart.Body, new Vector3(0, -0.055f, 0));
                    Scale(CatPart.Body, 1.04f + breathe * 0.012f, 0.93f + breathe * 0.025f);
                    Pos(CatPart.Head, new Vector3(-0.01f, -0.065f, 0));
                    Rot(CatPart.Head, Mathf.Sin(t * 0.35f) * 5f);
                    Pos(CatPart.Tail, new Vector3(0.04f, -0.06f, 0));
                    Rot(CatPart.Tail, 62f + Mathf.Sin(t * 0.9f) * 6f);
                    tipCurl = 38f;
                    // 스르륵 감았다 뜨기 (반쯤 감은 느낌)
                    float doze = Mathf.Repeat(t, 7f);
                    eyesClosed = eyesClosed || (doze > 5.2f && doze < 6.4f);
                    break;
                }
                case CatPose.SitIdle:
                {
                    // 바닥에 앉아서 멍: 뒷다리 접고, 꼬리를 앞발 쪽으로 말고, 가끔 두리번
                    float breathe = Mathf.Sin(t * 2.0f);
                    Scale(CatPart.LegBackNear, 1f, 0.5f);
                    Scale(CatPart.LegBackFar, 1f, 0.5f);
                    Pos(CatPart.Body, new Vector3(0, -0.015f, 0));
                    Rot(CatPart.Body, 10f);
                    Scale(CatPart.Body, 1f, 1f + breathe * 0.015f);
                    float look = Mathf.Repeat(t, 9f) < 2.5f ? Mathf.Sin(t * 1.3f) * 9f : 0f;
                    Rot(CatPart.Head, -2f + look);
                    Pos(CatPart.Head, new Vector3(-0.01f, 0.005f, 0));
                    Pos(CatPart.Tail, new Vector3(0.06f, -0.1f, 0));
                    Rot(CatPart.Tail, -55f + Mathf.Sin(t * 0.9f) * 6f);
                    tipCurl = -35f;
                    break;
                }
                case CatPose.Eat:
                {
                    // 고개 숙여 냠냠
                    float chew = Mathf.Sin(t * 9f);
                    Pos(CatPart.Head, new Vector3(0.03f, -0.08f + chew * 0.006f, 0));
                    Rot(CatPart.Head, -16f + chew * 2f);
                    Rot(CatPart.Body, -4f);
                    Rot(CatPart.Tail, 20f + Mathf.Sin(t * 3f) * 12f);
                    break;
                }
                case CatPose.Litter:
                {
                    // 쪼그려 앉아 볼일
                    Scale(CatPart.LegBackNear, 1f, 0.55f);
                    Scale(CatPart.LegBackFar, 1f, 0.55f);
                    Pos(CatPart.Body, new Vector3(0, -0.03f, 0));
                    Rot(CatPart.Body, 10f);
                    Pos(CatPart.Head, new Vector3(-0.01f, -0.01f, 0));
                    Rot(CatPart.Tail, 35f);
                    break;
                }
                case CatPose.Groom:
                {
                    // 앞발을 들어 얼굴을 닦는다
                    float lick = Mathf.Sin(t * 6f);
                    Rot(CatPart.LegFrontNear, 120f + lick * 12f);
                    Pos(CatPart.LegFrontNear, new Vector3(0.02f, 0.08f, 0));
                    Rot(CatPart.Head, -12f + lick * 4f);
                    Pos(CatPart.Head, new Vector3(-0.005f, -0.02f, 0));
                    Scale(CatPart.LegBackNear, 1f, 0.6f);
                    Scale(CatPart.LegBackFar, 1f, 0.6f);
                    Rot(CatPart.Body, 6f);
                    eyesClosed = lick > 0.3f;
                    break;
                }
                case CatPose.Sit:
                {
                    // 높은 곳에 앉아 내려다보기
                    Scale(CatPart.LegBackNear, 1f, 0.55f);
                    Scale(CatPart.LegBackFar, 1f, 0.55f);
                    Rot(CatPart.Body, 8f);
                    Rot(CatPart.Head, -4f + Mathf.Sin(t * 0.6f) * 6f);
                    Rot(CatPart.Tail, -30f + Mathf.Sin(t * 1.2f) * 14f);
                    tipCurl = -25f;
                    Pos(CatPart.Tail, new Vector3(0.02f, -0.08f, 0));
                    break;
                }
                case CatPose.Stretch:
                {
                    // 기지개: 앞발을 앞으로 쭉 뻗고 가슴을 낮추고 엉덩이를 든다. 끝날 무렵 천천히 돌아온다
                    float a = Smooth(Mathf.Clamp01(pt / 0.6f)) * Smooth(Mathf.Clamp01((2.4f - pt) / 0.5f));
                    float shiver = Mathf.Sin(t * 30f) * 0.8f * a;
                    Rot(CatPart.Body, -14f * a + shiver * 0.4f);
                    Pos(CatPart.Body, new Vector3(0, 0.012f * a, 0));
                    foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar })
                    {
                        Rot(leg, 66f * a + shiver);
                        Pos(leg, new Vector3(0.01f * a, -0.06f * a, 0));
                        Scale(leg, 1f, 1f + 0.25f * a);
                    }
                    foreach (var leg in new[] { CatPart.LegBackNear, CatPart.LegBackFar })
                    {
                        Pos(leg, new Vector3(0, 0.03f * a, 0));
                        Scale(leg, 1f, 1f + 0.25f * a);
                    }
                    Pos(CatPart.Head, new Vector3(0.05f * a, -0.10f * a, 0));
                    Rot(CatPart.Head, 10f * a);
                    Pos(CatPart.Tail, new Vector3(0, 0.05f * a, 0));
                    Rot(CatPart.Tail, -40f * a + Mathf.Sin(t * 2f) * 4f);
                    tipCurl = -10f * a;
                    eyesClosed = eyesClosed || a > 0.6f;
                    break;
                }
                case CatPose.Yawn:
                {
                    // 하품: 앉아서 고개를 들고 입을 크게 벌린다. 눈은 꼭 감고 귀는 살짝 뒤로
                    float o = Smooth(Mathf.Clamp01((pt - 0.2f) / 0.45f)) * Smooth(Mathf.Clamp01((1.7f - pt) / 0.4f));
                    Scale(CatPart.LegBackNear, 1f, 0.5f);
                    Scale(CatPart.LegBackFar, 1f, 0.5f);
                    Pos(CatPart.Body, new Vector3(0, -0.015f, 0));
                    Rot(CatPart.Body, 10f);
                    Rot(CatPart.Head, 4f + 20f * o);
                    Pos(CatPart.Head, new Vector3(-0.01f, 0.005f + 0.012f * o, 0));
                    Rot(CatPart.EarL, 16f * o);
                    Rot(CatPart.EarR, 8f * o);
                    Pos(CatPart.Tail, new Vector3(0.06f, -0.1f, 0));
                    Rot(CatPart.Tail, -55f + Mathf.Sin(t * 0.9f) * 6f);
                    tipCurl = -35f;
                    mouth = o;
                    eyesClosed = eyesClosed || o > 0.25f;
                    break;
                }
                case CatPose.EarScratch:
                {
                    // 뒷발로 귀 긁기: 앉아서 고개를 숙이고, 가까운 뒷발을 귀 뒤로 올려 빠르게 긁긁
                    float a = Smooth(Mathf.Clamp01(pt / 0.35f));
                    float k = Mathf.Sin(t * 26f);
                    Scale(CatPart.LegBackFar, 1f, 0.5f);
                    Pos(CatPart.Body, new Vector3(0, -0.02f * a, 0));
                    Rot(CatPart.Body, 14f * a);
                    Rot(CatPart.Head, -10f * a + k * 1.5f * a);
                    Pos(CatPart.Head, new Vector3(-0.03f * a, -0.05f * a, 0));
                    Pos(CatPart.LegBackNear, new Vector3(0.04f * a, 0.12f * a, 0));
                    Rot(CatPart.LegBackNear, 165f * a + k * 9f * a);
                    Scale(CatPart.LegBackNear, 1f, 1f + 0.9f * a);
                    Rot(CatPart.EarL, 12f * a);
                    Pos(CatPart.Tail, new Vector3(0.06f * a, -0.1f * a, 0));
                    Rot(CatPart.Tail, -50f * a + Mathf.Sin(t * 1.4f) * 6f);
                    tipCurl = -30f * a;
                    if (a > 0.5f) poseEyes = 1;
                    break;
                }
                case CatPose.Sprawl:
                {
                    // 옆으로 길게: 몸을 늘여 바닥에 붙이고, 앞발은 앞으로 뒷발은 뒤로 쭉, 머리도 바닥에 누인다
                    float br = Mathf.Sin(t * 1.3f);
                    Pos(CatPart.Body, new Vector3(0, -0.07f, 0));
                    Scale(CatPart.Body, 1.16f, 0.66f + br * 0.02f);
                    foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar })
                    {
                        Rot(leg, 82f);
                        Pos(leg, new Vector3(0.07f, -0.085f, 0));
                        Scale(leg, 1f, 1.25f);
                    }
                    foreach (var leg in new[] { CatPart.LegBackNear, CatPart.LegBackFar })
                    {
                        Rot(leg, -78f);
                        Pos(leg, new Vector3(-0.06f, -0.085f, 0));
                        Scale(leg, 1f, 1.2f);
                    }
                    // 눌린 몸의 목 자리를 따라간다 (몸은 x 1.16배로 늘고 y 0.66배로 눌리고 0.07 내려감 → 목 자리 (+0.022, -0.158))
                    Pos(CatPart.Head, new Vector3(0.025f, -0.152f, 0));
                    Rot(CatPart.Head, -10f + Mathf.Sin(t * 0.4f) * 2f);
                    Pos(CatPart.Tail, new Vector3(0f, -0.1f, 0));
                    Rot(CatPart.Tail, 48f + Mathf.Sin(t * 0.7f) * 5f);
                    tipCurl = 8f;
                    eyesClosed = eyesClosed || Mathf.Repeat(t, 9f) < 6.5f;
                    break;
                }
                case CatPose.Zoom:
                {
                    // 우다다: 귀를 뒤로 젖히고 꼬리를 부풀려 세운 채 전력 질주 (앞발끼리, 뒷발끼리 모아 뛴다)
                    float ph = hop * Mathf.PI * 4f;
                    float swing = Mathf.Sin(ph) * 44f;
                    Rot(CatPart.LegFrontNear, swing);
                    Rot(CatPart.LegFrontFar, swing * 0.8f);
                    Rot(CatPart.LegBackNear, -swing);
                    Rot(CatPart.LegBackFar, -swing * 0.8f);
                    float bob = Mathf.Abs(Mathf.Sin(ph)) * 0.025f;
                    Pos(CatPart.Body, new Vector3(0, bob, 0));
                    Rot(CatPart.Body, Mathf.Sin(ph) * 5f);
                    Scale(CatPart.Body, 1.05f, 0.96f);
                    Pos(CatPart.Head, new Vector3(0.015f, bob * 1.2f - 0.01f, 0));
                    Rot(CatPart.Head, -4f);
                    Rot(CatPart.EarL, 38f);
                    Rot(CatPart.EarR, 30f);
                    Scale(CatPart.EarL, 1f, 0.8f);
                    Scale(CatPart.EarR, 1f, 0.8f);
                    Rot(CatPart.Tail, -28f + Mathf.Sin(ph) * 6f);
                    Scale(CatPart.Tail, 1.45f, 1.05f);
                    tipCurl = -10f;
                    break;
                }
            }

            // 꼬리 끝: 한 박자 늦게 휘어 부드러운 S자
            if (_bones.ContainsKey(CatPart.TailTip)) Rot(CatPart.TailTip, Mathf.Sin(t * 1.6f - 0.9f) * 14f + tipCurl);
            // 진짜 그림 꼬리(위로 솟았다 뒤로 말림)는 앉은 자세에서 몸 뒤에 숨어 혹처럼 보이므로 바닥으로 늘어뜨린다
            if (_outlines.Count > 0 && (pose == CatPose.Sit || pose == CatPose.SitIdle || pose == CatPose.Yawn || pose == CatPose.EarScratch || pose == CatPose.Litter))
            {
                Pos(CatPart.Tail, new Vector3(0.015f, -0.05f, 0));
                Rot(CatPart.Tail, 64f + Mathf.Sin(t * 0.9f) * 5f);
            }
            // 한 장짜리 자세 그림: 몸은 그림이 맡고, 머리는 그림의 목 자리에 붙여 숨쉬기·두리번(회전)만 남긴다
            ArtLibrary.PoseArt art = null;
            if (_poseArts.Count > 0) { var kind = ArtFor(pose); if (kind.HasValue) _poseArts.TryGetValue(kind.Value, out art); }
            if (art != _curArt) { _curArt = art; _artStart = time; }
            if (art != null)
            {
                float br = Mathf.Sin(t * (pose == CatPose.Sleep ? 1.4f : 2.0f));
                float settle = Smooth(Mathf.Clamp01((time - _artStart) / 0.22f));
                _poseR.transform.localScale = new Vector3(1f + (1f - settle) * 0.03f, (0.95f + 0.05f * settle) * (1f + br * 0.012f), 1f);
                float lower = pose == CatPose.Groom || pose == CatPose.EarScratch ? -0.012f : 0f;
                _bones[CatPart.Head].localPosition = (Vector3)art.head + new Vector3(0, art.head.y * br * 0.012f + lower, 0);
                // 머리를 크게 돌리면 목 뒤로 그림 가장자리가 비치므로 조금만 돌린다
                float hz = Mathf.DeltaAngle(0f, _bones[CatPart.Head].localEulerAngles.z);
                Rot(CatPart.Head, Mathf.Clamp(hz * 0.6f, -7f, 8f));
            }
            // 진짜 그림은 다리가 보이게 몸을 조금 높게 조립했으므로, 다리를 접는 자세에서는 그만큼 더 내려 바닥에 붙인다
            if (_outlines.Count > 0 && art == null)
            {
                float drop = pose == CatPose.Lie || pose == CatPose.Sleep || pose == CatPose.Hide ? 0.065f
                    : pose == CatPose.Sit || pose == CatPose.SitIdle || pose == CatPose.Yawn || pose == CatPose.Litter || pose == CatPose.Groom || pose == CatPose.EarScratch ? 0.035f : 0f;
                if (drop > 0f)
                    foreach (var part in new[] { CatPart.Body, CatPart.Head, CatPart.Tail })
                        _bones[part].localPosition += new Vector3(0, -drop, 0);
            }

            // 자세 전환: 찍어 둔 직전 모습에서 새 자세로 섞는다
            if (_blending)
            {
                float b = Mathf.Clamp01(pt / BlendSeconds);
                if (b >= 1f) _blending = false;
                else
                {
                    b = Smooth(b);
                    foreach (var kv in _bones)
                    {
                        if (!_snap.TryGetValue(kv.Key, out var sn)) continue;
                        var tr = kv.Value;
                        tr.localPosition = Vector3.Lerp(sn.pos, tr.localPosition, b);
                        tr.localRotation = Quaternion.Slerp(sn.rot, tr.localRotation, b);
                        tr.localScale = Vector3.Lerp(sn.scale, tr.localScale, b);
                    }
                }
            }

            // 쓰다듬기 반응: 자세 위에 얹는다
            float dt = Time.deltaTime;
            _pWeight = Mathf.MoveTowards(_pWeight, PetActive ? 1f : 0f, dt * 4f);
            float kTilt = 1f - Mathf.Exp(-dt * 10f);
            _pTilt = Mathf.Lerp(_pTilt, PetActive ? PetTilt : 0f, kTilt);
            _pEars = Mathf.Lerp(_pEars, PetActive ? PetEars : 0f, 1f - Mathf.Exp(-dt * 14f));
            _pPurr = Mathf.Lerp(_pPurr, PetActive ? PetPurr : 0f, 1f - Mathf.Exp(-dt * 4f));
            _pBlush = Mathf.Lerp(_pBlush, PetActive ? PetBlush : 0f, 1f - Mathf.Exp(-dt * 3f));
            _pTail = Mathf.Lerp(_pTail, PetActive ? PetTail : 1f, 1f - Mathf.Exp(-dt * 3f));
            int eyes = 0;
            if (_pWeight > 0.001f)
            {
                var head = _bones[CatPart.Head];
                head.localRotation = head.localRotation * Quaternion.Euler(0, 0, _pTilt);
                if (_pPurr > 0.03f)
                {
                    var j = new Vector3(Mathf.Sin(time * 61f), Mathf.Cos(time * 53f), 0) * 0.0035f * _pPurr;
                    _bones[CatPart.Body].localPosition += j;
                    head.localPosition += j;
                }
                // 귀를 뒤로 젖힌다 (오른쪽을 본 기준으로 뒤 = 반시계)
                if (_pEars > 0.01f)
                {
                    foreach (var ear in new[] { CatPart.EarL, CatPart.EarR })
                    {
                        var e = _bones[ear];
                        e.localRotation = e.localRotation * Quaternion.Euler(0, 0, 40f * _pEars);
                        e.localScale = new Vector3(1f, 1f - 0.3f * _pEars, 1f);
                    }
                }
                if (PetActive && Mathf.Abs(_pTail - 1f) > 0.05f)
                {
                    _pTailT += dt * _pTail;
                    float amp = Mathf.Lerp(10f, 26f, Mathf.Clamp01((_pTail - 1f) / 2f));
                    _bones[CatPart.Tail].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_pTailT * 2.2f) * amp + 10f);
                }
                if (PetActive && PetEyes != PetEyes.Normal) { eyes = (int)PetEyes; eyesClosed = false; }
            }
            // 쓰다듬기 자세: 머리·턱 치켜세우기, 엉덩이 치켜세우기, 발라당
            float kp = 1f - Mathf.Exp(-dt * 8f);
            _wLift = Mathf.Lerp(_wLift, PetActive && PetPose == PetPose.HeadLift ? 1f : 0f, kp);
            _wChin = Mathf.Lerp(_wChin, PetActive && PetPose == PetPose.ChinLift ? 1f : 0f, kp);
            _wRump = Mathf.Lerp(_wRump, PetActive && PetPose == PetPose.RumpUp ? 1f : 0f, kp);
            _wBelly = Mathf.MoveTowards(_wBelly, PetActive && PetBelly ? 1f : 0f, dt * 3.5f);
            var headT = _bones[CatPart.Head];
            if (_wLift > 0.01f || _wChin > 0.01f)
            {
                // 손에 머리를 밀어붙이듯 고개를 든다 (턱이면 더 젖힌다)
                headT.localRotation = headT.localRotation * Quaternion.Euler(0, 0, 20f * _wLift + 32f * _wChin);
                headT.localPosition += new Vector3(0.006f * _wChin, 0.014f * _wLift + 0.022f * _wChin, 0);
            }
            if (_wRump > 0.01f)
            {
                // 엉덩이만 쭉: 몸 앞을 낮추고 뒤를 들고, 뒷다리를 펴고, 꼬리는 꼿꼿이
                var b = _bones[CatPart.Body];
                b.localRotation = b.localRotation * Quaternion.Euler(0, 0, -16f * _wRump);
                b.localPosition += new Vector3(0, 0.018f * _wRump, 0);
                float lift = 0.06f * _wRump;
                foreach (var leg in new[] { CatPart.LegBackNear, CatPart.LegBackFar })
                {
                    var l = _bones[leg];
                    l.localPosition += new Vector3(0, lift, 0);
                    l.localScale = new Vector3(1f, (0.115f + lift) / 0.115f, 1f);
                    _renderers[leg].enabled = true;
                }
                var tl = _bones[CatPart.Tail];
                tl.localPosition += new Vector3(0.01f * _wRump, 0.075f * _wRump, 0);
                tl.localRotation = Quaternion.Slerp(tl.localRotation, Quaternion.Euler(0, 0, -45f + Mathf.Sin(t * 3f) * 4f), _wRump);
                if (_bones.ContainsKey(CatPart.TailTip)) _bones[CatPart.TailTip].localRotation = Quaternion.Slerp(_bones[CatPart.TailTip].localRotation, Quaternion.Euler(0, 0, -8f), _wRump);
                headT.localPosition += new Vector3(0, -0.03f * _wRump, 0);
                foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar }) { var lf = _bones[leg]; lf.localScale = new Vector3(1f, 1f - 0.15f * _wRump, 1f); }
                headT.localRotation = headT.localRotation * Quaternion.Euler(0, 0, -6f * _wRump);
            }
            if (_wBelly > 0.001f && _renderers.ContainsKey(CatPart.BellyBody))
            {
                // 발라당: 몸·다리·꼬리를 발라당 그림으로 바꾸고, 머리는 거꾸로 젖혀 몸 오른쪽 끝에
                float e = _wBelly * _wBelly * (3f - 2f * _wBelly);
                headT.localPosition = Vector3.Lerp(headT.localPosition, _restPos[CatPart.Head] + new Vector3(0.24f, -0.105f, 0), e);
                headT.localRotation = Quaternion.Slerp(headT.localRotation, Quaternion.Euler(0, 0, 115f + Mathf.Sin(t * 1.3f) * 4f), e);
                var bb = _bones[CatPart.BellyBody];
                float breathe = 1f + Mathf.Sin(t * 1.8f) * 0.02f;
                bb.localScale = new Vector3(1f, (0.75f + 0.25f * e) * breathe, 1f);
                if (PetKick > 0f)
                {
                    bb.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 45f) * 7f * PetKick);
                    bb.localPosition += new Vector3(Mathf.Sin(t * 37f) * 0.01f * PetKick, 0, 0);
                }
            }
            if (PetKick > 0f) PetKick = Mathf.Max(0f, PetKick - dt * 1.6f);
            bool bodyOn = !BellyShown;
            if (_renderers.ContainsKey(CatPart.BellyBody))
            {
                _renderers[CatPart.BellyBody].enabled = !bodyOn;
                _renderers[CatPart.Body].enabled = bodyOn;
                _renderers[CatPart.Tail].enabled = bodyOn;
                if (_renderers.ContainsKey(CatPart.TailTip)) _renderers[CatPart.TailTip].enabled = bodyOn;
                if (!bodyOn) foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar, CatPart.LegBackNear, CatPart.LegBackFar }) _renderers[leg].enabled = false;
                if (_costume != null && _costumeWorn) _costume.enabled = bodyOn || _costumePart == CatPart.Head;
            }

            if (_poseR != null)
            {
                bool artOn = PoseArtShown;
                _poseR.enabled = _poseOl.enabled = artOn;
                if (artOn)
                {
                    if (_poseR.sprite != _curArt.sprite) { _poseR.sprite = _curArt.sprite; _poseOl.sprite = _curArt.outline; }
                    _renderers[CatPart.Body].enabled = false;
                    _renderers[CatPart.Tail].enabled = false;
                    if (_renderers.ContainsKey(CatPart.TailTip)) _renderers[CatPart.TailTip].enabled = false;
                    foreach (var leg in new[] { CatPart.LegFrontNear, CatPart.LegFrontFar, CatPart.LegBackNear, CatPart.LegBackFar }) _renderers[leg].enabled = false;
                    if (!_curArt.hasHead) _bones[CatPart.Head].localScale = Vector3.zero;
                    // 몸에 입힌 옷(망토 등)은 한 장짜리 그림과 모양이 안 맞아 잠시 숨긴다
                    if (_costume != null && _costumeWorn) _costume.enabled = _costumePart == CatPart.Head && _curArt.hasHead;
                }
                else if (_costume != null && _costumeWorn && !BellyShown) _costume.enabled = true;
            }

            if (_renderers.ContainsKey(CatPart.MouthOpen))
            {
                if (_fullHeadArt)
                {
                    // 하품하는 머리 그림을 통째로 덮는다 (늘리지 않음)
                    _renderers[CatPart.MouthOpen].enabled = mouth > 0.3f && !BellyShown;
                    _bones[CatPart.MouthOpen].localScale = Vector3.one;
                }
                else
                {
                    _renderers[CatPart.MouthOpen].enabled = mouth > 0.05f && !BellyShown;
                    _bones[CatPart.MouthOpen].localScale = new Vector3(1f + 0.1f * mouth, 0.3f + 0.7f * mouth, 1f);
                }
            }
            if (eyes == 0 && poseEyes != 0) eyes = poseEyes;
            // 발라당 그림에 머리가 들어 있으면 따로 붙은 머리는 숨긴다
            if (_bellyHasHead && BellyShown) _bones[CatPart.Head].localScale = Vector3.zero;
            _renderers[CatPart.Blush].color = new Color(1, 1, 1, Mathf.Clamp01(_pBlush));
            _renderers[CatPart.EyesOpen].enabled = !eyesClosed && eyes == 0;
            _renderers[CatPart.EyesClosed].enabled = eyesClosed || eyes == 3;
            _renderers[CatPart.EyesHalf].enabled = !eyesClosed && eyes == 1;
            _renderers[CatPart.EyesHappy].enabled = !eyesClosed && eyes == 2;
            foreach (var kv in _outlines) kv.Value.enabled = _renderers[kv.Key].enabled;
        }
    }
}
