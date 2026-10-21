using System.Collections.Generic;
using CatHealingHouse.View.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatHealingHouse.View
{
    public enum BustEyes { Open, Half, Happy, Blink, Angry }

    /// <summary>
    /// 가까이 보기 정면 고양이 뼈대. 숨쉬기·머리 기울이기(손 쪽으로)·귀 젖히기·꼬리 흔들기·골골 떨림·볼 빨개짐.
    /// 값은 목표만 정해 주면 Update 에서 부드럽게 따라간다.
    /// </summary>
    public sealed class CloseUpRig : MonoBehaviour
    {
        public SortingGroup Group;
        private readonly Dictionary<BustPart, SpriteRenderer> _r = new Dictionary<BustPart, SpriteRenderer>();
        private Transform _tail, _body, _head, _earL, _earR;
        private float _phase;

        // 목표 값
        public float Tilt;          // 머리 기울기 (도, + 는 반시계)
        public Vector2 Lean;        // 머리 쏠림 (캔버스 단위)
        public float EarsFlat;      // 0~1
        public float Purr;          // 0~1
        public float Blush;         // 0~1
        public float TailSpeed = 1; // 1 = 느긋, 3 = 탁탁
        public BustEyes Eyes;
        public bool MouthOpen;
        public float EarTwitch;     // 0~1 잠깐 씰룩
        public float Alpha = 1;

        private float _tilt, _ears, _purr, _blush, _tailSpeed = 1, _tailT;
        private Vector2 _lean;

        public void Build(CatLook look, Material mat)
        {
            _phase = Random.value * 10f;
            var gGo = gameObject;
            Group = gGo.GetComponent<SortingGroup>();
            if (Group == null) Group = gGo.AddComponent<SortingGroup>();

            Transform Bone(string n, Transform parent, Vector2 joint, Vector2 parentJoint)
            {
                var go = new GameObject(n);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = joint - parentJoint;
                return go.transform;
            }

            var root = transform;
            var J = CloseUpArt.Joint;
            _tail = Bone("Tail", root, J[BustPart.Tail], CloseUpArt.Root);
            _body = Bone("Body", root, J[BustPart.Body], CloseUpArt.Root);
            _head = Bone("Head", _body, J[BustPart.Head], J[BustPart.Body]);
            _earL = Bone("EarL", _head, J[BustPart.EarL], J[BustPart.Head]);
            _earR = Bone("EarR", _head, J[BustPart.EarR], J[BustPart.Head]);
            var shadow = Bone("Shadow", root, J[BustPart.Shadow], CloseUpArt.Root);

            Add(BustPart.Shadow, shadow, look, mat, 0);
            Add(BustPart.Tail, _tail, look, mat, 1);
            Add(BustPart.Body, _body, look, mat, 2);
            Add(BustPart.EarL, _earL, look, mat, 3);
            Add(BustPart.EarR, _earR, look, mat, 3);
            Add(BustPart.Head, _head, look, mat, 4);
            foreach (var p in new[] { BustPart.Blush, BustPart.EyesOpen, BustPart.EyesHalf, BustPart.EyesHappy, BustPart.EyesBlink, BustPart.EyesAngry, BustPart.MouthOpen })
            {
                var t = Bone(p.ToString(), _head, J[p], J[BustPart.Head]);
                Add(p, t, look, mat, p == BustPart.Blush ? 5 : 6);
            }
            Apply(0f);
        }

        private void Add(BustPart p, Transform t, CatLook look, Material mat, int order)
        {
            var r = t.gameObject.AddComponent<SpriteRenderer>();
            r.sprite = CloseUpArt.Get(look, p);
            r.sharedMaterial = mat;
            r.sortingOrder = order;
            _r[p] = r;
        }

        private void Update() => Apply(Time.deltaTime);

        private void Apply(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 10f);
            _tilt = Mathf.Lerp(_tilt, Tilt, k);
            _lean = Vector2.Lerp(_lean, Lean, k);
            _ears = Mathf.Lerp(_ears, EarsFlat, 1f - Mathf.Exp(-dt * 14f));
            _purr = Mathf.Lerp(_purr, Purr, 1f - Mathf.Exp(-dt * 4f));
            _blush = Mathf.Lerp(_blush, Blush, 1f - Mathf.Exp(-dt * 3f));
            _tailSpeed = Mathf.Lerp(_tailSpeed, TailSpeed, 1f - Mathf.Exp(-dt * 3f));
            _tailT += dt * _tailSpeed;
            float t = Time.time + _phase;

            // 숨쉬기 + 골골 떨림
            float breathe = 1f + Mathf.Sin(t * 1.9f) * 0.012f;
            var J = CloseUpArt.Joint;
            _body.localScale = new Vector3(1f + (breathe - 1f) * 0.4f, breathe, 1f);
            var jitter = _purr > 0.05f ? new Vector2(Mathf.Sin(t * 61f), Mathf.Cos(t * 53f)) * 0.0016f * _purr : Vector2.zero;
            _body.localPosition = (Vector3)(J[BustPart.Body] - CloseUpArt.Root + jitter);

            // 머리: 손 쪽으로 기울고 살짝 쏠림, 기분 좋으면 살짝 위로
            float bob = Mathf.Sin(t * 1.9f + 0.6f) * 0.004f;
            _head.localPosition = (Vector3)(J[BustPart.Head] - J[BustPart.Body] + _lean + new Vector2(0, bob));
            _head.localRotation = Quaternion.Euler(0, 0, _tilt + Mathf.Sin(t * 0.7f) * 1.2f);

            // 귀: 젖히면 바깥으로 눕고 납작, 가끔 씰룩
            float tw = EarTwitch > 0 ? Mathf.Sin(EarTwitch * Mathf.PI * 4f) * 10f * EarTwitch : 0f;
            _earL.localRotation = Quaternion.Euler(0, 0, 38f * _ears + tw);
            _earR.localRotation = Quaternion.Euler(0, 0, -38f * _ears - tw * 0.5f);
            var es = new Vector3(1f, 1f - 0.25f * _ears, 1f);
            _earL.localScale = es;
            _earR.localScale = es;
            if (EarTwitch > 0) EarTwitch = Mathf.Max(0, EarTwitch - dt * 2.5f);

            // 꼬리: 느긋하게 살랑, 짜증나면 탁탁
            float amp = Mathf.Lerp(7f, 16f, Mathf.Clamp01((_tailSpeed - 1f) / 2f));
            _tail.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_tailT * 1.6f) * amp);

            // 얼굴
            SetOn(BustPart.EyesOpen, Eyes == BustEyes.Open);
            SetOn(BustPart.EyesHalf, Eyes == BustEyes.Half);
            SetOn(BustPart.EyesHappy, Eyes == BustEyes.Happy);
            SetOn(BustPart.EyesBlink, Eyes == BustEyes.Blink);
            SetOn(BustPart.EyesAngry, Eyes == BustEyes.Angry);
            SetOn(BustPart.MouthOpen, MouthOpen);
            foreach (var kv in _r)
            {
                float a = Alpha * (kv.Key == BustPart.Blush ? Mathf.Clamp01(_blush) : 1f);
                var c = kv.Value.color;
                if (!Mathf.Approximately(c.a, a)) kv.Value.color = new Color(1, 1, 1, a);
            }
        }

        private void SetOn(BustPart p, bool on)
        {
            var r = _r[p];
            if (r.enabled != on) r.enabled = on;
        }

        /// <summary>월드 좌표 → 캔버스 좌표(0~1). 머리 기울기는 머리 부위면 머리 기준으로 되돌린다.</summary>
        public Vector2 CanvasAt(Vector3 world)
        {
            var local = (Vector2)transform.InverseTransformPoint(world) + CloseUpArt.Root;
            // 머리 근처면 머리 회전·쏠림을 되돌려 정확하게
            var headLocal = (Vector2)_head.InverseTransformPoint(world) + CloseUpArt.Joint[BustPart.Head];
            if (headLocal.y > 0.5f) return headLocal;
            return local;
        }
    }
}
