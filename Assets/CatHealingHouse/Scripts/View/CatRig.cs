using System.Collections.Generic;
using CatHealingHouse.View.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatHealingHouse.View
{
    /// <summary>고양이 몸짓 종류. CatView 가 고양이 상태에서 골라 넘긴다.</summary>
    public enum CatPose { Idle, Walk, Run, Sleep, Eat, Litter, Groom, Sit, Hide, Lie, SitIdle }

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
        }

        private SpriteRenderer _costume;

        /// <summary>코스튬을 입힌다 (null 이면 벗김). 그림 피벗이 관절이라 부위에 매달기만 하면 제자리에 맞는다.</summary>
        public void SetCostume(Sprite sprite, CatPart part, int order, Material material)
        {
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
        private void Scale(CatPart p, float x, float y) => _bones[p].localScale = new Vector3(x, y, 1);

        /// <summary>
        /// 매 프레임 자세를 잡는다. hop 은 걸음 진행도(0~1, 걷는 중일 때), facingLeft 면 좌우 반전.
        /// </summary>
        public void Animate(CatPose pose, float hop, float time)
        {
            float t = time + _phase;

            // 기본 자세로 되돌리기
            foreach (var kv in _bones)
            {
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
                    Pos(CatPart.Tail, new Vector3(0.02f, -0.08f, 0));
                    break;
                }
            }

            _renderers[CatPart.EyesOpen].enabled = !eyesClosed;
            _renderers[CatPart.EyesClosed].enabled = eyesClosed;
        }
    }
}
