using System.Collections.Generic;
using CatHealingHouse.Hex;
using CatHealingHouse.View.Art;
using CatHealingHouse.World;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 집 분위기와 하루의 흐름.
    ///  - 실내: 위쪽 테두리에 벽지 벽을 세우고, 가운데 두 칸에 창문. 창밖 하늘은 시간 따라 바뀌고 해·달·별이 뜬다.
    ///  - 창문으로 들어오는 빛 조각(해/달)이 바닥 위를 천천히 미끄러진다 (WindowLight 가 정한 칸).
    ///  - 실내·마당 모두 시간에 맞춰 화면 전체 색감(새벽 보라, 노을 주황, 밤 남색)을 입힌다.
    /// 그림은 코드로 그린 임시 그림. 나중에 아트 라이브러리로 바꿔 끼운다.
    /// </summary>
    public sealed class EnvironmentView : MonoBehaviour
    {
        private HouseWorld _world;
        private DayClock _clock;
        private HexLayout _layout;
        private Material _material;
        private Camera _cam;
        private Transform _indoorRoot;
        private System.Func<HashSet<HexCoord>> _litNow;

        private SpriteRenderer _floorTint, _objectTint;
        private readonly List<GameObject> _walls = new List<GameObject>();
        private SpriteRenderer _vignette;
        private readonly List<SpriteRenderer> _flies = new List<SpriteRenderer>();
        private readonly List<Vector4> _flySeed = new List<Vector4>();
        private BoardView _indoorView;
        private MaterialPropertyBlock _mpb;
        private int _builtRadius = -1;
        private BoardId _board;

        public Color IndoorBase = new Color(0.36f, 0.30f, 0.27f);
        public Color OutdoorBase = new Color(0.29f, 0.36f, 0.27f);

        private float Size => (float)_layout.Size;

        public void Init(HouseWorld world, DayClock clock, HexLayout layout, Material material, Camera cam, Transform indoorRoot, System.Func<HashSet<HexCoord>> litNow)
        {
            _world = world;
            _clock = clock;
            _layout = layout;
            _material = material;
            _cam = cam;
            _indoorRoot = indoorRoot;
            _litNow = litNow;
            _mpb = new MaterialPropertyBlock();

            _floorTint = Overlay("Tint (floor)", BoardView.TileBase + 1700);
            _objectTint = Overlay("Tint (objects)", BoardView.LabelOrder - 200);
            _indoorView = indoorRoot.GetComponent<BoardView>();
            _vignette = Overlay("Vignette", BoardView.LabelOrder - 150);
            _vignette.sprite = DecorArt.Vignette();
            var rnd = new System.Random(9);
            for (int i = 0; i < 16; i++)
            {
                var f = Overlay("Firefly", BoardView.LabelOrder - 149);
                f.sprite = DecorArt.SoftBlob();
                _flies.Add(f);
                _flySeed.Add(new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble() * 10f, 0.5f + (float)rnd.NextDouble()));
            }
            BuildWalls();
        }

        public void SetBoard(BoardId b) => _board = b;

        private SpriteRenderer Overlay(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _material;
            sr.sprite = CuteArt.WhitePixel();
            sr.sortingOrder = order;
            go.transform.localScale = new Vector3(400, 400, 1);
            return sr;
        }

        // ───────────────────────── 벽과 창문 ─────────────────────────

        private sealed class Win
        {
            public float X, BaseY, Width, GlassTop, GlassBottom;
            public SpriteRenderer Glass;
            public readonly List<SpriteRenderer> Stars = new List<SpriteRenderer>();
            public MeshRenderer Pool, Shaft;
            public Mesh PoolMesh, ShaftMesh;
        }
        private readonly List<Win> _wins = new List<Win>();

        private void BuildWalls()
        {
            foreach (var w in _walls) if (w != null) Destroy(w);
            _walls.Clear();
            foreach (var w in _wins) { if (w.PoolMesh != null) Destroy(w.PoolMesh); if (w.ShaftMesh != null) Destroy(w.ShaftMesh); }
            _wins.Clear();

            var board = _world.Indoor;
            var ground = _indoorView != null && _indoorView.GroundRadius == board.Radius ? _indoorView.Ground : null;
            if (ground == null) return; // 바닥 그림이 아직 없으면 다음 프레임에
            _builtRadius = board.Radius;
            float cellW = (float)_layout.CellWidth;
            // 벽은 바닥 그림(GroundPainter)이 방 윤곽을 따라 그려 둔다. 여기서는 그 벽에 세로로 긴 통창을 단다.
            var cells = new List<HexCoord>(WindowLight.WindowCells(board));
            cells.Sort((x, y) => _layout.ToWorld(x).x.CompareTo(_layout.ToWorld(y).x));
            var rnd = new System.Random(4);
            var wood = SdfCanvas.Col("#C49468");
            var woodDark = SdfCanvas.Col("#A87650");
            foreach (var c in cells)
            {
                float x = _layout.ToWorld(c).x;
                float baseY = ground.WallBaseAt(x);
                if (float.IsNaN(baseY)) continue;
                var go = new GameObject($"Window {c}");
                go.transform.SetParent(_indoorRoot, false);
                _walls.Add(go);
                int o = BoardView.TileBase - 40;
                var win = new Win { X = x, BaseY = baseY, Width = cellW * 0.96f };
                win.GlassBottom = baseY + Size * 0.3f;
                win.GlassTop = baseY + ground.WallHeight - Size * 0.32f;
                float ft = Size * 0.075f;
                float mid = (win.GlassTop + win.GlassBottom) * 0.5f, hgt = win.GlassTop - win.GlassBottom;
                Rect(go, "Frame", x, mid, win.Width, hgt, wood, o);
                win.Glass = Rect(go, "Glass", x, mid, win.Width - ft * 2, hgt - ft * 2, Color.white, o + 1);
                for (int i = 0; i < 5; i++)
                {
                    var s = Child(go, "Star", CuteArt.Disk(), o + 2);
                    float sx = x + ((float)rnd.NextDouble() - 0.5f) * (win.Width - ft * 4);
                    float sy = win.GlassBottom + ft * 2 + (float)rnd.NextDouble() * (hgt - ft * 4);
                    s.transform.localPosition = new Vector3(sx, sy, 0);
                    float k = Size * (0.05f + 0.03f * (i % 2));
                    s.transform.localScale = new Vector3(k, k, 1);
                    s.color = SdfCanvas.Col("#FFF6D0");
                    win.Stars.Add(s);
                }
                Rect(go, "Mullion", x, mid, ft * 0.8f, hgt, wood, o + 3);
                Rect(go, "Transom", x, win.GlassBottom + hgt * 0.68f, win.Width, ft * 0.8f, wood, o + 3);
                Rect(go, "Sill", x, win.GlassBottom - ft * 0.2f, win.Width * 1.14f, ft * 1.5f, woodDark, o + 4);
                // 유리 반사 한 줄
                var shine = Rect(go, "Shine", x - win.Width * 0.24f, mid + hgt * 0.1f, ft * 0.5f, hgt * 0.35f, new Color(1, 1, 1, 0.3f), o + 2);
                shine.transform.localRotation = Quaternion.Euler(0, 0, -12f);

                // 바닥에 떨어지는 빛 모양 + 공기 중 빛줄기
                win.PoolMesh = NewQuad(); win.ShaftMesh = NewQuad();
                win.Pool = MeshChild(go, "Light Pool", win.PoolMesh, DecorArt.SoftRectTex(), BoardView.TileBase + 1850);
                win.Shaft = MeshChild(go, "Light Shaft", win.ShaftMesh, DecorArt.BeamTex(), BoardView.LabelOrder - 190);
                _wins.Add(win);
            }
        }

        private SpriteRenderer Rect(GameObject parent, string name, float cx, float cy, float w, float h, Color col, int order)
        {
            var sr = Child(parent, name, CuteArt.WhitePixel(), order);
            sr.transform.localPosition = new Vector3(cx, cy, 0);
            sr.transform.localScale = new Vector3(w, h, 1);
            sr.color = col;
            return sr;
        }

        private static Mesh NewQuad()
        {
            var m = new Mesh { name = "light quad" };
            m.vertices = new Vector3[4];
            m.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.MarkDynamic();
            return m;
        }

        private MeshRenderer MeshChild(GameObject parent, string name, Mesh mesh, Texture2D tex, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.sortingOrder = order;
            _mpb.Clear();
            _mpb.SetTexture("_MainTex", tex);
            mr.SetPropertyBlock(_mpb);
            return mr;
        }

        private SpriteRenderer Child(GameObject parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _material;
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void LateUpdate()
        {
            if (_world == null) return;
            if (_world.Indoor.Radius != _builtRadius) BuildWalls();
            double hour = _clock.Hour;

            // 화면 전체 색감
            var tint = TintAt(hour);
            _floorTint.color = tint;
            _objectTint.color = new Color(tint.r, tint.g, tint.b, tint.a * 0.55f);
            var center = _cam.transform.position;
            _floorTint.transform.position = new Vector3(center.x, center.y, 0);
            _objectTint.transform.position = new Vector3(center.x, center.y, 0);
            var baseCol = _board == BoardId.Indoor ? GroundPainter.IndoorOutside : GroundPainter.OutdoorOutside;
            UpdateVignette(hour);
            UpdateFireflies(hour);
            _cam.backgroundColor = Color.Lerp(baseCol, new Color(tint.r, tint.g, tint.b), tint.a * 0.8f);

            UpdateWindows(hour);
        }

        /// <summary>
        /// 창밖 하늘색(해·달은 그리지 않는다)과 창으로 들어오는 빛.
        /// 바닥엔 창 폭만 한 빛 모양이 시간 따라 비스듬히 늘어나고(WindowLight 와 같은 방향·길이),
        /// 공기 중엔 창에서 그 빛까지 이어지는 옅은 빛줄기가 생긴다.
        /// </summary>
        private void UpdateWindows(double hour)
        {
            var sky = SkyAt(hour);
            var kind = Daylight.Kind(hour);
            float strength = (float)Daylight.Strength(hour);
            float prog = (float)Daylight.Progress(hour);
            float side = 2f * prog - 1f;
            float night = NightAmount(hour);
            Color lightCol = kind == LightKind.Moon ? new Color(0.72f, 0.82f, 1f)
                : Color.Lerp(SdfCanvas.Col("#FFC27A"), SdfCanvas.Col("#FFF7D6"), Mathf.Sin(prog * Mathf.PI));
            float rows = 1.6f + 3.2f * Mathf.Abs(side);
            if (kind == LightKind.Moon) rows *= 0.85f;
            strength = Mathf.Sqrt(strength); // 해 질 녘·해 뜰 녘에도 빛이 또렷하게
            float poolA = kind == LightKind.None ? 0f : strength * (kind == LightKind.Moon ? 0.45f : 0.85f);
            float shaftA = kind == LightKind.None ? 0f : strength * (kind == LightKind.Moon ? 0.14f : 0.32f);
            var dir = new Vector3(side * 1.15f * Size, -1.5f * Size * (float)_layout.YScale, 0);
            float len = rows + 0.5f;

            foreach (var w in _wins)
            {
                w.Glass.color = kind == LightKind.Sun ? Color.Lerp(sky, Color.white, 0.25f * strength) : sky;
                foreach (var s in w.Stars) { s.enabled = night > 0.02f; s.color = new Color(1f, 0.96f, 0.82f, night); }

                float hw = w.Width * 0.5f;
                var near = new Vector3(w.X, w.BaseY - Size * 0.1f, 0);
                var far = near + dir * len;
                float spread = 1.3f;
                var pv = new[] { new Vector3(near.x - hw, near.y, 0), new Vector3(near.x + hw, near.y, 0), new Vector3(far.x + hw * spread, far.y, 0), new Vector3(far.x - hw * spread, far.y, 0) };
                w.PoolMesh.vertices = pv;
                w.PoolMesh.colors = new[] { Color.white, Color.white, new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0.7f) };
                w.PoolMesh.RecalculateBounds();
                // 빛줄기: 유리 위쪽에서 바닥 빛 끝까지. 위가 진하고 아래로 갈수록 옅게.
                var sv = new[] { new Vector3(w.X - hw * 0.85f, w.GlassTop, 0), new Vector3(w.X + hw * 0.85f, w.GlassTop, 0), pv[2], pv[3] };
                w.ShaftMesh.vertices = sv;
                w.ShaftMesh.colors = new[] { Color.white, Color.white, new Color(1, 1, 1, 0.1f), new Color(1, 1, 1, 0.1f) };
                w.ShaftMesh.RecalculateBounds();

                w.Pool.enabled = poolA > 0.004f;
                w.Shaft.enabled = shaftA > 0.004f;
                w.Pool.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", new Color(lightCol.r, lightCol.g, lightCol.b, poolA));
                w.Pool.SetPropertyBlock(_mpb);
                w.Shaft.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", new Color(lightCol.r, lightCol.g, lightCol.b, shaftA));
                w.Shaft.SetPropertyBlock(_mpb);
            }
        }

        // ───────────────────────── 비네트·반딧불 ─────────────────────────

        private void UpdateVignette(double hour)
        {
            float hgt = _cam.orthographicSize * 2f, wid = hgt * _cam.aspect;
            var p = _cam.transform.position;
            _vignette.transform.position = new Vector3(p.x, p.y, 0);
            _vignette.transform.localScale = new Vector3(wid * 1.05f, hgt * 1.05f, 1);
            float night = NightAmount(hour);
            var c = _board == BoardId.Indoor ? new Color(0.2f, 0.12f, 0.07f) : new Color(0.08f, 0.14f, 0.08f);
            c.a = 0.42f + 0.2f * night;
            _vignette.color = c;
        }

        private void UpdateFireflies(double hour)
        {
            float night = NightAmount(hour);
            bool on = _board == BoardId.Outdoor && night > 0.05f;
            float range = (float)_layout.CellWidth * (_world.Outdoor.Radius + 1.5f);
            float t = Time.time;
            for (int i = 0; i < _flies.Count; i++)
            {
                var f = _flies[i];
                f.enabled = on;
                if (!on) continue;
                var s = _flySeed[i];
                float x = (s.x * 2 - 1) * range + Mathf.Sin(t * 0.21f * s.w + s.z) * Size * 1.2f;
                float y = (s.y * 2 - 1) * range * (float)_layout.YScale + Mathf.Sin(t * 0.33f * s.w + s.z * 1.7f) * Size * 0.8f;
                float blink = 0.5f + 0.5f * Mathf.Sin(t * 1.6f * s.w + s.z * 3f);
                f.transform.position = new Vector3(x, y, 0);
                float k = Size * (0.32f + 0.12f * blink);
                f.transform.localScale = new Vector3(k, k, 1);
                f.color = new Color(1f, 0.93f, 0.55f, night * (0.25f + 0.65f * blink));
            }
        }

        // ───────────────────────── 색 ─────────────────────────

        private struct Key { public float H; public Color C; public Key(float h, Color c) { H = h; C = c; } }

        // 화면 위에 덮는 색 (알파 = 세기)
        private static readonly Key[] TintKeys =
        {
            new Key(0f, new Color(0.10f, 0.12f, 0.32f, 0.46f)),
            new Key(4.5f, new Color(0.12f, 0.14f, 0.34f, 0.42f)),
            new Key(5.8f, new Color(0.50f, 0.38f, 0.62f, 0.24f)),
            new Key(7.5f, new Color(1.00f, 0.92f, 0.80f, 0.04f)),
            new Key(12f, new Color(1.00f, 1.00f, 1.00f, 0.00f)),
            new Key(15.5f, new Color(1.00f, 0.86f, 0.55f, 0.07f)),
            new Key(17.6f, new Color(1.00f, 0.55f, 0.30f, 0.22f)),
            new Key(19f, new Color(0.40f, 0.28f, 0.52f, 0.32f)),
            new Key(20.5f, new Color(0.10f, 0.12f, 0.32f, 0.46f)),
            new Key(24f, new Color(0.10f, 0.12f, 0.32f, 0.46f)),
        };

        // 창밖 하늘
        private static readonly Key[] SkyKeys =
        {
            new Key(0f, SdfCanvas.Col("#1E2552")),
            new Key(4.8f, SdfCanvas.Col("#2A2F62")),
            new Key(6f, SdfCanvas.Col("#B9A3D6")),
            new Key(7.5f, SdfCanvas.Col("#A8D8F0")),
            new Key(12f, SdfCanvas.Col("#8CC8F0")),
            new Key(15.5f, SdfCanvas.Col("#BFD9E8")),
            new Key(17.3f, SdfCanvas.Col("#F2C78C")),
            new Key(18.2f, SdfCanvas.Col("#F59A6B")),
            new Key(19.2f, SdfCanvas.Col("#6A5A9A")),
            new Key(20.5f, SdfCanvas.Col("#1E2552")),
            new Key(24f, SdfCanvas.Col("#1E2552")),
        };

        private static Color Sample(Key[] keys, double hour)
        {
            float h = (float)hour;
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (h < keys[i].H || h > keys[i + 1].H) continue;
                float t = Mathf.InverseLerp(keys[i].H, keys[i + 1].H, h);
                t = t * t * (3 - 2 * t);
                return Color.Lerp(keys[i].C, keys[i + 1].C, t);
            }
            return keys[0].C;
        }

        public static Color TintAt(double hour) => Sample(TintKeys, hour);
        public static Color SkyAt(double hour) => Sample(SkyKeys, hour);

        private static float NightAmount(double h)
        {
            if (h >= 20.5 || h < 4.8) return 1f;
            if (h >= 19 && h < 20.5) return Mathf.InverseLerp(19f, 20.5f, (float)h);
            if (h >= 4.8 && h < 6) return 1f - Mathf.InverseLerp(4.8f, 6f, (float)h);
            return 0f;
        }
    }
}
