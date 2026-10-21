using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>월드에서 화면의 한 점(트레이, 재화 칩)으로 포물선을 그리며 날아가는 아이콘.</summary>
    public sealed class Flyer : MonoBehaviour
    {
        private Camera _cam;
        private Vector3 _from;
        private Vector2 _toScreen;
        private float _delay, _t, _size;
        private SpriteRenderer _sr;
        private const float Duration = 0.75f;

        public static void Launch(Transform parent, Camera cam, Sprite sprite, Material mat, Vector3 from, Vector2 toScreen, float size, float delay)
        {
            var go = new GameObject("Flyer");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<Flyer>();
            f._cam = cam;
            f._from = from;
            f._toScreen = toScreen;
            f._delay = delay;
            f._size = size / Art.CuteArt.WidthOf(sprite);
            f._sr = go.AddComponent<SpriteRenderer>();
            f._sr.sharedMaterial = mat;
            f._sr.sprite = sprite;
            f._sr.sortingOrder = 9500;
            go.transform.position = from;
            go.transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (_delay > 0) { _delay -= Time.deltaTime; return; }
            _t += Time.deltaTime / Duration;
            var to = _cam.ScreenToWorldPoint(new Vector3(_toScreen.x, _toScreen.y, 10));
            to.z = 0;
            float e = _t * _t * (3f - 2f * _t);
            var p = Vector3.Lerp(_from, to, e);
            p.y += Mathf.Sin(Mathf.Clamp01(_t) * Mathf.PI) * _size * 1.2f; // 위로 붕 떴다가
            transform.position = p;
            float pop = _t < 0.2f ? _t / 0.2f : 1f - Mathf.Max(0, _t - 0.8f) / 0.2f * 0.6f;
            transform.localScale = Vector3.one * _size * Mathf.Max(0.05f, pop);
            if (_t >= 1f) Destroy(gameObject);
        }
    }
}
