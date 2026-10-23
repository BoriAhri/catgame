using System.Collections.Generic;
using CatHealingHouse.Economy;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.Stray;
using CatHealingHouse.View.Art;
using CatHealingHouse.World;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 보드 하나를 그린다: 두께감 있는 둥근 육각 타일, 가구 소품, 가구 위 하트, 바닥 드롭, 거리맵 숫자.
    /// 시뮬레이션은 항상 돌고, 이 뷰는 현재 보고 있는 보드일 때만 켜진다.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        // 그리기 순서 (Y-Sorting 은 각 구간 안에서 화면 아래쪽일수록 큰 값)
        public const int TileBase = 100;
        public const int ObjectBase = 3000;   // 가구·고양이·드롭이 같은 구간에서 y 로 정렬
        public const int HeartOrder = 8000;
        public const int LabelOrder = 9000;

        public static int ObjectOrder(float worldY) => ObjectBase + Mathf.RoundToInt(-worldY * 20f) + 500;

        private sealed class TileGo
        {
            public GameObject Go;
            public MeshRenderer Top;
            public MeshRenderer Side;
            public MeshFilter TopFilter, SideFilter;
            public TextMesh Label;
        }

        private HouseWorld _world;
        private HouseEconomy _eco;
        private BoardId _boardId;
        private HexLayout _layout;
        private Material _material;
        private Font _font;
        private MaterialPropertyBlock _mpb;
        private readonly Dictionary<HexCoord, TileGo> _tiles = new Dictionary<HexCoord, TileGo>();
        private readonly Dictionary<int, SpriteRenderer> _props = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _hearts = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, SpriteRenderer> _drops = new Dictionary<int, SpriteRenderer>();
        private readonly List<SpriteRenderer> _paws = new List<SpriteRenderer>();
        private Transform _propRoot, _fxRoot;

        /// <summary>가구 그림 변형 (화장실 오염 단계, 빈 밥그릇 등). 비어 있으면 화장실만.</summary>
        public System.Func<FurnitureInstance, int> VariantOf;

        /// <summary>발자국을 그릴 길냥이 시스템 (없으면 안 그림).</summary>
        public StraySystem Strays;

        /// <summary>
        /// 칸 무늬를 보여줄지. 평소에는 꺼서 바닥이 이어진 한 장처럼 보이고, 가구를 놓을 때만 육각형 칸이 보인다.
        /// 바꾼 뒤 Refresh 를 부르면 적용된다.
        /// </summary>
        public bool ShowGrid;

        /// <summary>놓기 중: 이미 가구가 있는 칸을 빨간색으로 표시한다.</summary>
        public bool MarkOccupied;
        private static readonly Color OccupiedTint = SdfCanvas.Col("#E98A80");
        /// <summary>놓기 모드: 고른 가구 (칸마다 여기에 놓을 수 있는지 연두/빨강으로 칠한다).</summary>
        public FurnitureDef PlaceDef;
        public bool PlaceMirrored;
        private static readonly Color PlaceableTint = SdfCanvas.Col("#B8F07A");
        private static readonly Color BlockedTint = SdfCanvas.Col("#E5534B");
        private static readonly Color PreviewOk = new Color(0.55f, 0.85f, 0.45f, 0.62f);
        private static readonly Color PreviewBad = new Color(0.92f, 0.32f, 0.28f, 0.62f);

        // 미리보기 (고스트): 반투명 가구 그림 + 차지할 칸 표시 + 입구 칸 발바닥
        private SpriteRenderer _ghost;
        private readonly List<MeshRenderer> _previewCells = new List<MeshRenderer>();
        private readonly List<SpriteRenderer> _previewPaws = new List<SpriteRenderer>();
        private Mesh _previewMesh;
        private Mesh _gridTop, _seamlessTop;

        // 칸 대신 보이는 바닥 그림 한 장 + 장식 (덤불·소나무·풀꽃) + 가구 밑 발판
        public GroundResult Ground { get; private set; }
        public int GroundRadius { get; private set; } = -1;
        private SpriteRenderer _groundSr;
        private Transform _decorRoot;
        private readonly Dictionary<int, SpriteRenderer> _patches = new Dictionary<int, SpriteRenderer>();

        public BoardId BoardId => _boardId;
        public HexBoard Board => _world.GetBoard(_boardId);

        public void Init(HouseWorld world, HouseEconomy eco, BoardId boardId, HexLayout layout, Material material, Font font)
        {
            _world = world;
            _eco = eco;
            _boardId = boardId;
            _layout = layout;
            _material = material;
            _font = font;
            _mpb = new MaterialPropertyBlock();
            _propRoot = new GameObject("Furniture").transform;
            _propRoot.SetParent(transform, false);
            _fxRoot = new GameObject("Hearts & Drops").transform;
            _fxRoot.SetParent(transform, false);
            SyncTiles();
        }

        private float Size => (float)_layout.Size;
        private float CellW => (float)_layout.CellWidth;

        // ───────────────────────── 바닥 그림·장식 ─────────────────────────

        private void PaintGround()
        {
            bool indoor = _boardId == BoardId.Indoor;
            GroundRadius = Board.Radius;
            Ground = GroundPainter.Paint(Board, _layout, indoor, indoor ? 3 : 17);
            if (_groundSr == null)
            {
                var go = new GameObject("Ground");
                go.transform.SetParent(transform, false);
                _groundSr = go.AddComponent<SpriteRenderer>();
                _groundSr.sharedMaterial = _material;
                _groundSr.sortingOrder = TileBase - 60;
            }
            else if (_groundSr.sprite != null)
            {
                Destroy(_groundSr.sprite.texture);
                Destroy(_groundSr.sprite);
            }
            _groundSr.sprite = Ground.Sprite;

            if (_decorRoot != null) Destroy(_decorRoot.gameObject);
            _bushes.Clear();
            _decorRoot = new GameObject("Decor").transform;
            _decorRoot.SetParent(transform, false);
            if (!indoor) BuildOutdoorDecor();
            // 문 발판 (칸 대신)
            var doorPos = _layout.ToWorld(Board.DoorCoord);
            Decor("Door Mat", DecorArt.DoorMat(!indoor), doorPos + new Vector3(0, -Size * 0.05f, 0), CellW * 1.05f, TileBase + 1640);
        }

        private SpriteRenderer Decor(string name, Sprite s, Vector3 pos, float width, int order, bool flip = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_decorRoot, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _material;
            sr.sprite = s;
            sr.flipX = flip;
            sr.sortingOrder = order;
            float k = width / CuteArt.WidthOf(s);
            go.transform.localScale = new Vector3(k, k, 1);
            return sr;
        }

        private void BuildOutdoorDecor()
        {
            var rnd = new System.Random(5);
            var board = Board;
            int n = board.Radius + 1;
            var door = _layout.ToWorld(board.DoorCoord);
            float cw = CellW;
            System.Func<Vector3, bool> onPath = p => p.y < door.y + Size * 0.6f && Mathf.Abs(p.x - door.x) < cw * 1.25f;

            // 테두리 덤불: 칸보다 살짝 바깥으로 밀어서, 가장자리를 걷는 고양이가 덤불 앞을 지나가게
            foreach (var t in board.Tiles.Values)
            {
                if (t.Kind != TileKind.Edge) continue;
                var p = _layout.ToWorld(t.Coord);
                var outward = p.sqrMagnitude > 0.001f ? p.normalized : Vector3.up;
                p += outward * Size * 0.55f;
                if (onPath(p)) continue;
                float w = cw * (1.05f + (float)rnd.NextDouble() * 0.35f);
                var bush = Decor("Bush", DecorArt.Bush(rnd.Next(3)), p, w, ObjectOrder(p.y), rnd.NextDouble() < 0.5);
                _bushes.Add(new BushState { T = bush.transform, BaseScale = bush.transform.localScale, BasePos = bush.transform.localPosition, Until = -1f });
            }

            // 바깥 숲: 소나무 여러 줄. 집 문으로 가는 흙길만 비운다.
            for (int ring = n + 1; ring <= n + 6; ring++)
            {
                foreach (var c in HexCoord.Ring(HexCoord.Zero, ring))
                {
                    if (rnd.NextDouble() < (ring == n + 1 ? 0.35 : 0.15)) continue;
                    var p = _layout.ToWorld(c) + new Vector3((float)(rnd.NextDouble() - 0.5) * cw * 0.7f, (float)(rnd.NextDouble() - 0.5) * Size * 0.6f, 0);
                    if (onPath(p)) continue;
                    // 앞쪽(아래)은 공터를 가리지 않게 한 줄 더 바깥부터, 더 드문드문
                    if (p.y < 0 && (ring <= n + 1 || rnd.NextDouble() < 0.35)) continue;
                    float w = cw * (1.5f + (float)rnd.NextDouble() * 0.7f + (ring - n) * 0.08f);
                    Decor("Pine", DecorArt.Pine(rnd.Next(2)), p, w, ObjectOrder(p.y), rnd.NextDouble() < 0.5);
                }
            }

            // 풀꽃: 공터 가장자리 쪽 바닥에 드문드문 (바닥 그림 위, 그림자처럼 낮은 층)
            foreach (var t in board.Tiles.Values)
            {
                if (t.Kind != TileKind.Floor || t.Coord.Length < board.Radius - 1) continue;
                if (rnd.NextDouble() > 0.28) continue;
                var p = _layout.ToWorld(t.Coord) + new Vector3((float)(rnd.NextDouble() - 0.5) * cw * 0.6f, (float)(rnd.NextDouble() - 0.5) * Size * 0.5f, 0);
                Decor("Flowers", DecorArt.Flowers(rnd.Next(3)), p, cw * 0.45f, TileBase + 1600);
            }
        }

        // ───────────────────────── 타일 ─────────────────────────

        private void SyncTiles()
        {
            if (Board.Radius != GroundRadius) PaintGround();
            var top = HexMesh.GetRounded(Size, (float)_layout.YScale, 0.05f, 0.28f);
            _gridTop = top;
            // 이음새 없는 바닥: 살짝 크게 그려서 칸끼리 겹치게
            if (_seamlessTop == null) _seamlessTop = HexMesh.Get(Size * 1.02f, (float)_layout.YScale, 0f);
            float thick = Size * 0.2f;
            foreach (var t in Board.Tiles.Values)
            {
                if (_tiles.ContainsKey(t.Coord)) continue;
                var go = new GameObject($"Tile {t.Coord}");
                go.transform.SetParent(transform, false);
                var pos = _layout.ToWorld(t.Coord);
                go.transform.localPosition = pos;
                int order = TileBase + Mathf.RoundToInt(-pos.y * 10f) * 3 + 600;

                var sideGo = new GameObject("Side");
                sideGo.transform.SetParent(go.transform, false);
                sideGo.transform.localPosition = new Vector3(0, -thick, 0);
                var sideFilter = sideGo.AddComponent<MeshFilter>();
                sideFilter.sharedMesh = top;
                var side = sideGo.AddComponent<MeshRenderer>();
                side.sharedMaterial = _material;
                side.sortingOrder = order;

                var topFilter = go.AddComponent<MeshFilter>();
                topFilter.sharedMesh = top;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.sortingOrder = order + 1;

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(go.transform, false);
                var tm = labelGo.AddComponent<TextMesh>();
                tm.font = _font;
                tm.fontSize = 48;
                tm.characterSize = Size * 0.085f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.37f, 0.29f, 0.24f);
                var lr = labelGo.GetComponent<MeshRenderer>();
                lr.sharedMaterial = _font.material;
                lr.sortingOrder = LabelOrder;

                _tiles[t.Coord] = new TileGo { Go = go, Top = mr, Side = side, TopFilter = topFilter, SideFilter = sideFilter, Label = tm };
            }
        }

        /// <summary>색·글자·가구를 현재 상태로 다시 그린다. distanceMap 이 있으면 칸마다 거리 숫자.</summary>
        public void Refresh(DistanceMap distanceMap, int highlightFurnitureId)
        {
            SyncTiles();
            foreach (var kv in _tiles)
            {
                var tile = Board.Get(kv.Key);
                var tg = kv.Value;
                var mesh = ShowGrid ? _gridTop : _seamlessTop;
                if (tg.TopFilter.sharedMesh != mesh) { tg.TopFilter.sharedMesh = mesh; tg.SideFilter.sharedMesh = mesh; }
                var c = ColorFor(tile, highlightFurnitureId);
                if (ShowGrid && MarkOccupied && tile.Furniture != null)
                    c = Color.Lerp(c, OccupiedTint, 0.65f);
                // 평소엔 칸을 숨기고 바닥 그림만. 문 발판과 러그 칸만 남긴다. 놓기·거리 모드에선 바닥 칸을 반투명하게.
                bool isRug = tile.Furniture != null && tile.Furniture.Def.Id == FurnitureCatalog.BigRug.Id;
                bool walk = tile.Kind == TileKind.Floor || tile.Kind == TileKind.Door;
                bool show = ShowGrid && walk;
                tg.Top.enabled = show;
                tg.Side.enabled = show;
                if (ShowGrid && tile.Kind == TileKind.Floor) c.a = isRug || tile.Furniture != null ? 0.8f : 0.55f;
                // 놓기 모드: 이 칸에 고른 가구를 놓을 수 있으면 밝은 연두, 안 되면 빨강 (벽 가구는 벽 표시로 대신)
                if (ShowGrid && PlaceDef != null && !PlaceDef.WallMounted && tile.Kind == TileKind.Floor)
                {
                    bool ok = _world.CanPlace(PlaceDef, _boardId, tile.Coord, PlaceMirrored) == PlaceResult.Ok;
                    c = ok ? PlaceableTint : BlockedTint;
                    c.a = ok ? 0.78f : 0.62f;
                }
                _mpb.SetColor("_Color", c);
                tg.Top.SetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", Darken(c, 0.78f));
                tg.Side.SetPropertyBlock(_mpb);

                string text = "";
                if (tile.Kind == TileKind.Door)
                    text = _boardId == BoardId.Indoor ? "문\n<size=34>마당으로</size>" : "문\n<size=34>집으로</size>";
                if (distanceMap != null)
                {
                    int d = distanceMap.Get(new Cell(_boardId, tile.Coord));
                    if (d != DistanceMap.Unreachable) text = string.IsNullOrEmpty(text) ? d.ToString() : text + " " + d;
                }
                tg.Label.text = text;
            }
            SyncProps();
        }

        // ───────────────────────── 가구 소품 ─────────────────────────

        public Vector3 FurnitureAnchor(FurnitureInstance f)
        {
            Vector3 sum = Vector3.zero;
            foreach (var cell in f.Cells) sum += _layout.ToWorld(cell.Offset);
            return sum / Mathf.Max(1, f.Cells.Count);
        }

        private float FurnitureWidth(FurnitureInstance f)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var cell in f.Cells)
            {
                float x = _layout.ToWorld(cell.Offset).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
            return Mathf.Max(CellW * 0.95f, max - min + CellW * 0.85f) * PropScale(f.Def);
        }

        /// <summary>가구별 그림 크기 보정 (풋프린트 폭만으로는 너무 크거나 작아 보이는 것).</summary>
        private static float PropScale(FurnitureDef def)
        {
            switch (def.Id)
            {
                case "cat_wheel": return 0.62f;
                case "cat_tower_5": return 1.05f;
                case "bed": return 0.9f;
                case "bookshelf": return 0.85f;
                case "litter_box":
                case "food_bowl": return 0.9f;
                default: return 1f;
            }
        }

        private void SyncProps()
        {
            var alive = new HashSet<int>();
            foreach (var f in _world.Furniture)
            {
                if (f.Board != _boardId) continue;
                int variant = VariantOf != null ? VariantOf(f)
                    : RewardTable.IsLitterBox(f.Def) && _eco != null ? (int)_eco.StageOfLitter(f.Id) : 0;
                var sprite = CuteArt.Furniture(f.Def, variant);
                if (sprite == null) continue;
                alive.Add(f.Id);
                if (!_props.TryGetValue(f.Id, out var sr) || sr == null)
                {
                    var go = new GameObject(f.ToString());
                    go.transform.SetParent(_propRoot, false);
                    sr = go.AddComponent<SpriteRenderer>();
                    sr.sharedMaterial = _material;
                    _props[f.Id] = sr;
                }
                sr.sprite = sprite;
                if (f.Def.WallMounted)
                {
                    sr.flipX = false;
                    if (WallPose(f.Def, new List<HexCoord>(f.WallCells()), out var pv, out float ang, out float ww))
                    {
                        sr.transform.localPosition = pv;
                        sr.transform.localRotation = Quaternion.Euler(0, 0, ang);
                        sr.transform.localScale = new Vector3(ww / CuteArt.WidthOf(sprite), ww / CuteArt.WidthOf(sprite), 1);
                    }
                    sr.sortingOrder = TileBase - 30; // 벽 위 (창문 위, 바닥 위 모든 것 아래)
                    continue;
                }
                sr.flipX = f.Mirrored;
                var anchor = FurnitureAnchor(f) + new Vector3(0, -Size * 0.42f * (float)_layout.YScale, 0);
                sr.transform.localPosition = anchor;
                float w = FurnitureWidth(f) / CuteArt.WidthOf(sprite);
                sr.transform.localScale = new Vector3(w, w, 1);
                sr.sortingOrder = ObjectOrder(anchor.y);
                SyncPatch(f);
            }
            var dead = new List<int>();
            foreach (var kv in _props) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead)
            {
                if (_props[id] != null) Destroy(_props[id].gameObject);
                _props.Remove(id);
            }
            var deadPatch = new List<int>();
            foreach (var kv in _patches) if (!alive.Contains(kv.Key)) deadPatch.Add(kv.Key);
            foreach (var id in deadPatch)
            {
                if (_patches[id] != null) Destroy(_patches[id].gameObject);
                _patches.Remove(id);
            }
        }

        /// <summary>가구 밑 발판: 마당은 흙 자리, 실내는 부드러운 그림자. 바닥에 놓인 느낌을 준다.</summary>
        private void SyncPatch(FurnitureInstance f)
        {
            if (f.Def.WallMounted) return;
            bool rug = f.Def.Id == FurnitureCatalog.BigRug.Id;
            bool indoor = _boardId == BoardId.Indoor;
            if (!_patches.TryGetValue(f.Id, out var sr) || sr == null)
            {
                var go = new GameObject("Patch " + f.Id);
                go.transform.SetParent(_propRoot, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = _material;
                sr.sprite = rug ? DecorArt.Rug() : indoor ? DecorArt.SoftBlob() : DecorArt.DirtPatch();
                sr.sortingOrder = TileBase + (rug ? 1640 : 1650); // 바닥 그림 위, 시간 색감 아래
                sr.color = rug ? Color.white : indoor ? new Color(0.35f, 0.2f, 0.1f, 0.28f) : new Color(1, 1, 1, 0.9f);
                _patches[f.Id] = sr;
            }
            float w = FurnitureWidth(f) / Mathf.Max(0.3f, PropScale(f.Def));
            var center = FurnitureAnchor(f);
            float h = Size * 1.5f * (float)_layout.YScale;
            float rows = 1f;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var cell in f.Cells) { float y = _layout.ToWorld(cell.Offset).y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            rows += (maxY - minY) / Mathf.Max(0.01f, h);
            if (rug)
            {
                sr.transform.localPosition = center;
                float rw = CellW * 3.3f;
                sr.transform.localScale = new Vector3(rw, rw * (Size * 1.5f * (float)_layout.YScale * 3.4f) / (rw * 0.5f), 1);
                return;
            }
            sr.transform.localPosition = center + new Vector3(0, -Size * 0.12f, 0);
            float sx = w * (indoor ? 1.0f : 1.1f);
            float sy = Mathf.Min(sx * 0.7f, h * rows * (indoor ? 0.95f : 1.1f));
            sr.transform.localScale = new Vector3(sx, sy, 1);
        }

        // ───────────────────────── 벽 가구 ─────────────────────────

        /// <summary>
        /// 벽 가구 그림을 어디에 어떻게 둘지: 걸린 벽 칸들의 가운데 x, 그 x 의 벽 밑동 높이 + 그림별 띄움.
        /// 가로로 긴 것(캣워크)은 사선 벽의 기울기를 따라 기울인다.
        /// </summary>
        private bool WallPose(FurnitureDef def, List<HexCoord> wallCells, out Vector3 pivot, out float angleDeg, out float width)
        {
            pivot = Vector3.zero; angleDeg = 0f; width = 1f;
            if (Ground == null || wallCells == null || wallCells.Count == 0) return false;
            var spec = WallArt.SpecOf(def.Id);
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var c in wallCells) { float x = _layout.ToWorld(c).x; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            float cx = (minX + maxX) * 0.5f;
            float baseY = Ground.WallBaseAt(cx);
            if (float.IsNaN(baseY)) return false;
            if (spec.FollowSlope && maxX - minX > 0.01f)
            {
                float y0 = Ground.WallBaseAt(minX), y1 = Ground.WallBaseAt(maxX);
                if (!float.IsNaN(y0) && !float.IsNaN(y1)) angleDeg = Mathf.Atan2(y1 - y0, maxX - minX) * Mathf.Rad2Deg;
            }
            width = spec.Width * Size;
            pivot = new Vector3(cx, baseY + spec.Lift * Size, 0);
            return true;
        }

        /// <summary>벽 가구에서 slot 번째 고양이가 앉는 자리 (월드 기준 = 이 보드의 로컬).</summary>
        public bool TryWallSpot(FurnitureInstance f, int slot, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (f == null || !f.Def.WallMounted) return false;
            if (!WallPose(f.Def, new List<HexCoord>(f.WallCells()), out var pv, out float ang, out float w)) return false;
            var spec = WallArt.SpecOf(f.Def.Id);
            var s = spec.Spots[Mathf.Clamp(slot, 0, spec.Spots.Length - 1)];
            var local = new Vector3((s.x - 0.5f) * w, s.y * w, 0);
            pos = pv + Quaternion.Euler(0, 0, ang) * local;
            return true;
        }

        /// <summary>벽 가구를 고를 때 화면을 누른 곳에서 가장 가까운 벽 자리 (벽 면 가운데 기준).</summary>
        public HexCoord? NearestWallSlot(Vector2 world)
        {
            if (Ground == null) return null;
            HexCoord? best = null;
            float bestD = float.MaxValue;
            foreach (var c in _world.WallSlots(_boardId))
            {
                float x = _layout.ToWorld(c).x;
                float by = Ground.WallBaseAt(x);
                if (float.IsNaN(by)) continue;
                // 벽 면은 세로로 길다: 밑동~꼭대기 구간까지의 거리
                float y = Mathf.Clamp(world.y, by, by + Ground.WallHeight);
                float dx = world.x - x, dy = world.y - y;
                float d = dx * dx + dy * dy * 0.5f;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private readonly List<SpriteRenderer> _wallMarks = new List<SpriteRenderer>();

        /// <summary>벽 가구를 놓는 중이면 벽 자리마다 걸 수 있는지(초록)/없는지(빨강) 옅은 기둥으로 보여준다.</summary>
        public void ShowWallSlots(FurnitureDef def)
        {
            int n = 0;
            if (def != null && def.WallMounted && Ground != null)
            {
                foreach (var c in _world.WallSlots(_boardId))
                {
                    float x = _layout.ToWorld(c).x;
                    float by = Ground.WallBaseAt(x);
                    if (float.IsNaN(by)) continue;
                    if (n >= _wallMarks.Count) _wallMarks.Add(NewFx("Wall Slot", CuteArt.WhitePixel(), TileBase - 32));
                    var m = _wallMarks[n++];
                    m.gameObject.SetActive(true);
                    bool ok = _world.CanPlace(def, _boardId, c, false) == PlaceResult.Ok;
                    m.color = ok ? new Color(0.45f, 0.8f, 0.4f, 0.28f) : new Color(0.92f, 0.35f, 0.3f, 0.14f);
                    m.transform.localPosition = new Vector3(x, by + Ground.WallHeight * 0.5f, 0);
                    m.transform.localScale = new Vector3(CellW * 0.6f, Ground.WallHeight * 0.8f, 1);
                }
            }
            for (int i = n; i < _wallMarks.Count; i++) _wallMarks[i].gameObject.SetActive(false);
        }

        // ───────────────────────── 놓기 미리보기 ─────────────────────────

        /// <summary>
        /// 고른 가구를 origin 에 놓으면 어떻게 되는지 보여준다: 차지할 칸 전부(놓을 수 있으면 초록, 안 되면 빨강),
        /// 고양이가 들어가는 입구 칸의 발바닥, 반투명 가구 그림.
        /// </summary>
        public void ShowPreview(FurnitureDef def, HexCoord origin, bool mirrored, bool valid)
        {
            if (_previewMesh == null) _previewMesh = HexMesh.GetRounded(Size, (float)_layout.YScale, 0.1f, 0.28f);
            var cells = _world.ResolveCells(def, _boardId, origin, mirrored) ?? new List<FootprintCell>();
            var col = valid ? PreviewOk : PreviewBad;
            int n = 0, paws = 0;
            foreach (var fc in cells)
            {
                if (!Board.Contains(fc.Offset)) continue;
                if (def.WallMounted && fc.Role == CellRole.Blocked) continue; // 벽 칸은 벽 위 표시로 대신
                if (n >= _previewCells.Count)
                {
                    var go = new GameObject("Preview Cell");
                    go.transform.SetParent(_fxRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = _previewMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _material;
                    mr.sortingOrder = TileBase + 1900;
                    _previewCells.Add(mr);
                }
                var r = _previewCells[n++];
                r.gameObject.SetActive(true);
                r.transform.localPosition = _layout.ToWorld(fc.Offset);
                _mpb.SetColor("_Color", col);
                r.SetPropertyBlock(_mpb);

                if (fc.Role == CellRole.Entry)
                {
                    if (paws >= _previewPaws.Count) _previewPaws.Add(NewFx("Preview Paw", CuteArt.PawPrint(), TileBase + 1950));
                    var p = _previewPaws[paws++];
                    p.gameObject.SetActive(true);
                    if (p.sprite == null) p.sprite = CuteArt.PawPrint();
                    p.color = new Color(1, 1, 1, 0.9f);
                    float s = Size * 0.4f / CuteArt.WidthOf(p.sprite);
                    p.transform.localScale = new Vector3(s, s * 0.8f, 1);
                    p.transform.localPosition = _layout.ToWorld(fc.Offset);
                }
            }
            for (int i = n; i < _previewCells.Count; i++) _previewCells[i].gameObject.SetActive(false);
            for (int i = paws; i < _previewPaws.Count; i++) _previewPaws[i].gameObject.SetActive(false);

            // 반투명 가구 그림 (제일 위에)
            var sprite = CuteArt.Furniture(def, 0);
            if (_ghost == null) _ghost = NewFx("Preview Ghost", sprite, LabelOrder - 100);
            _ghost.gameObject.SetActive(sprite != null);
            if (sprite != null && def.WallMounted)
            {
                var wc = new List<HexCoord>();
                foreach (var fc in cells) if (fc.Role == CellRole.Blocked) wc.Add(fc.Offset);
                bool posed = WallPose(def, wc, out var pv, out float ang, out float ww);
                _ghost.gameObject.SetActive(posed);
                _ghost.sprite = sprite;
                _ghost.flipX = false;
                _ghost.transform.localPosition = pv;
                _ghost.transform.localRotation = Quaternion.Euler(0, 0, ang);
                _ghost.transform.localScale = new Vector3(ww / CuteArt.WidthOf(sprite), ww / CuteArt.WidthOf(sprite), 1);
                _ghost.color = valid ? new Color(1, 1, 1, 0.8f) : new Color(1f, 0.6f, 0.6f, 0.65f);
            }
            else if (sprite != null)
            {
                _ghost.transform.localRotation = Quaternion.identity;
                _ghost.sprite = sprite;
                _ghost.flipX = mirrored;
                var coords = new List<HexCoord>();
                foreach (var fc in cells) coords.Add(fc.Offset);
                var anchor = AnchorOf(coords) + new Vector3(0, -Size * 0.42f * (float)_layout.YScale, 0);
                float w = WidthOf(coords, def) / CuteArt.WidthOf(sprite);
                _ghost.transform.localPosition = anchor;
                _ghost.transform.localScale = new Vector3(w, w, 1);
                _ghost.color = valid ? new Color(1, 1, 1, 0.75f) : new Color(1f, 0.7f, 0.7f, 0.6f);
            }
        }

        public void HidePreview()
        {
            foreach (var r in _previewCells) if (r != null) r.gameObject.SetActive(false);
            foreach (var p in _previewPaws) if (p != null) p.gameObject.SetActive(false);
            if (_ghost != null) _ghost.gameObject.SetActive(false);
        }

        private Vector3 AnchorOf(List<HexCoord> coords)
        {
            Vector3 sum = Vector3.zero;
            foreach (var c in coords) sum += _layout.ToWorld(c);
            return sum / Mathf.Max(1, coords.Count);
        }

        private float WidthOf(List<HexCoord> coords, FurnitureDef def)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var c in coords)
            {
                float x = _layout.ToWorld(c).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
            return Mathf.Max(CellW * 0.95f, max - min + CellW * 0.85f) * PropScale(def);
        }

        /// <summary>하트가 떠 있는 위치 (가구 위쪽).</summary>
        public Vector3 HeartPosition(FurnitureInstance f)
        {
            if (f.Def.WallMounted && TryWallSpot(f, f.Def.Capacity - 1, out var top)) return top + new Vector3(0, Size * 0.9f, 0);
            float w = FurnitureWidth(f);
            float h = f.Def.Interaction == InteractionType.Climb && f.Def.Size > 1 ? w * 0.95f : w * 0.7f;
            return FurnitureAnchor(f) + new Vector3(0, h, 0);
        }

        // ───────────────────────── 하트·드롭 (매 프레임) ─────────────────────────

        private void LateUpdate()
        {
            if (_eco == null) return;
            float t = Time.time;
            UpdateRustle(t);

            var aliveHearts = new HashSet<int>();
            foreach (var h in _eco.Hearts)
            {
                if (h.Board != _boardId) continue;
                var f = _world.GetFurniture(h.FurnitureId);
                if (f == null) continue;
                aliveHearts.Add(h.FurnitureId);
                if (!_hearts.TryGetValue(h.FurnitureId, out var sr) || sr == null)
                {
                    sr = NewFx("Heart", CuteArt.Item(ItemKind.Heart), HeartOrder);
                    _hearts[h.FurnitureId] = sr;
                }
                float bob = Mathf.Sin(t * 3f + h.FurnitureId) * Size * 0.06f;
                // 사라지기 20초 전부터 깜빡
                double left = h.ExpiresAt - _eco.Sim.SimTime;
                float alpha = left < 20 ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(t * 4f)) : 1f;
                sr.color = new Color(1, 1, 1, alpha);
                float pop = Mathf.Clamp01((float)(_eco.Sim.SimTime - h.CreatedAt) * 4f);
                float s = Size * 0.7f * (0.6f + 0.4f * EaseOutBack(pop)) / CuteArt.WidthOf(sr.sprite);
                sr.transform.localScale = new Vector3(s, s, 1);
                sr.transform.localPosition = HeartPosition(f) + new Vector3(0, bob, 0);
            }
            RemoveDead(_hearts, aliveHearts);

            var aliveDrops = new HashSet<int>();
            foreach (var d in _eco.Drops)
            {
                if (d.Cell.Board != _boardId) continue;
                aliveDrops.Add(d.Id);
                if (!_drops.TryGetValue(d.Id, out var sr) || sr == null)
                {
                    sr = NewFx("Drop", CuteArt.Item(d.Kind), 0);
                    _drops[d.Id] = sr;
                }
                var p = _layout.ToWorld(d.Cell.Coord) + new Vector3(0, -Size * 0.05f, 0);
                float pop = Mathf.Clamp01((float)(_eco.Sim.SimTime - d.CreatedAt) * 3f);
                float s = Size * 0.55f * EaseOutBack(pop) / CuteArt.WidthOf(sr.sprite);
                sr.transform.localPosition = p + new Vector3(0, Mathf.Sin(t * 2f + d.Id) * Size * 0.02f, 0);
                sr.transform.localScale = new Vector3(s, s, 1);
                sr.sortingOrder = ObjectOrder(p.y) - 2;
            }
            RemoveDead(_drops, aliveDrops);

            SyncPawPrints();
        }

        private void SyncPawPrints()
        {
            int n = 0;
            if (Strays != null)
            {
                double now = _eco.Sim.SimTime;
                foreach (var p in Strays.PawPrints)
                {
                    if (p.Cell.Board != _boardId) continue;
                    if (n >= _paws.Count) _paws.Add(NewFx("Paw", CuteArt.PawPrint(), TileBase + 2000));
                    var sr = _paws[n++];
                    sr.gameObject.SetActive(true);
                    if (sr.sprite == null) sr.sprite = CuteArt.PawPrint();
                    float age = (float)((now - p.CreatedAt) / Strays.Tuning.PawPrintLife);
                    sr.color = new Color(1, 1, 1, Mathf.Clamp01(1f - age) * 0.9f);
                    // 걸어간 방향으로 돌리고, 왼발·오른발을 번갈아 조금 어긋나게
                    var dir = _layout.ToWorld(HexCoord.Zero.Neighbor(p.Heading)) - _layout.ToWorld(HexCoord.Zero);
                    float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                    var side = new Vector3(-dir.y, dir.x, 0).normalized * Size * 0.12f * ((n % 2) == 0 ? 1 : -1);
                    sr.transform.localPosition = _layout.ToWorld(p.Cell.Coord) + side;
                    sr.transform.localRotation = Quaternion.Euler(0, 0, ang);
                    float s = Size * 0.42f / CuteArt.WidthOf(sr.sprite);
                    sr.transform.localScale = new Vector3(s, s * 0.8f, 1);
                }
            }
            for (int i = n; i < _paws.Count; i++) if (_paws[i] != null) _paws[i].gameObject.SetActive(false);
        }

        private SpriteRenderer NewFx(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_fxRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _material;
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        private static void RemoveDead(Dictionary<int, SpriteRenderer> map, HashSet<int> alive)
        {
            var dead = new List<int>();
            foreach (var kv in map) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead)
            {
                if (map[id] != null) Destroy(map[id].gameObject);
                map.Remove(id);
            }
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
        }

        // ───────────────────────── 탭 판정 ─────────────────────────

        /// <summary>탭한 월드 좌표 근처의 하트. 화면에서 보이는 위치로 판정한다.</summary>
        public bool TryHitHeart(Vector2 world, out int furnitureId)
        {
            furnitureId = -1;
            float best = Size * 0.6f;
            foreach (var kv in _hearts)
            {
                if (kv.Value == null) continue;
                float d = Vector2.Distance(world, kv.Value.transform.position);
                if (d < best) { best = d; furnitureId = kv.Key; }
            }
            return furnitureId > 0;
        }

        public bool TryHitDrop(Vector2 world, out int dropId)
        {
            dropId = -1;
            float best = Size * 0.55f;
            foreach (var kv in _drops)
            {
                if (kv.Value == null) continue;
                float d = Vector2.Distance(world, kv.Value.transform.position);
                if (d < best) { best = d; dropId = kv.Key; }
            }
            return dropId > 0;
        }

        /// <summary>탭한 위치의 가구 (소품 그림 범위 포함).</summary>
        public FurnitureInstance HitFurniture(Vector2 world, HexCoord tapped)
        {
            var tile = Board.Get(tapped);
            if (tile?.Furniture != null) return tile.Furniture;
            // 키 큰 가구는 위쪽으로 그림이 솟아 있으니 소품 그림 영역도 본다
            foreach (var kv in _props)
            {
                if (kv.Value == null) continue;
                var b = kv.Value.bounds;
                if (b.Contains(new Vector3(world.x, world.y, b.center.z))) return _world.GetFurniture(kv.Key);
            }
            return null;
        }

        // ───────────────────────── 색 ─────────────────────────

        private static readonly Color IndoorFloorA = SdfCanvas.Col("#D9AA7C");
        private static readonly Color IndoorFloorB = SdfCanvas.Col("#D0A072");
        private static readonly Color OutdoorFloorA = SdfCanvas.Col("#A9BF8A");
        private static readonly Color OutdoorFloorB = SdfCanvas.Col("#A5B986");
        private static readonly Color Wall = SdfCanvas.Col("#F1E6D3");
        private static readonly Color EdgeBush = SdfCanvas.Col("#7E9A6E");
        private static readonly Color Door = SdfCanvas.Col("#EBA683");
        private static readonly Color RugTint = SdfCanvas.Col("#EDB8A8");

        private Color ColorFor(Tile t, int highlightId)
        {
            Color c;
            // 칸 무늬를 숨길 땐 체크 무늬도 없애서 한 장의 바닥처럼
            bool alt = ShowGrid && ((t.Coord.Q - t.Coord.R) % 3 + 3) % 3 == 0;
            switch (t.Kind)
            {
                case TileKind.Wall: c = Wall; break;
                case TileKind.Edge: c = EdgeBush; break;
                case TileKind.Door: c = Door; break;
                default: c = _boardId == BoardId.Indoor ? (alt ? IndoorFloorB : IndoorFloorA) : (alt ? OutdoorFloorB : OutdoorFloorA); break;
            }
            if (t.Furniture != null)
            {
                if (t.Furniture.Def.Id == FurnitureCatalog.BigRug.Id) c = Color.Lerp(c, RugTint, alt ? 0.75f : 0.6f);
                else if (!ShowGrid) { /* 평소에는 가구 칸도 바닥과 같은 색 */ }
                else if (t.FurnitureRole == CellRole.Entry) c = Color.Lerp(c, Color.white, 0.18f);
                else c = Darken(c, 0.93f);
                if (t.Furniture.Id == highlightId) c = Color.Lerp(c, Color.white, 0.4f);
            }
            c.a = 1f;
            return c;
        }

        private static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    

        // ───────────────────────── 기척 신호: 수풀 바스락 ─────────────────────────

        private struct BushState { public Transform T; public Vector3 BaseScale, BasePos; public float Until, Seed; }
        private readonly List<BushState> _bushes = new List<BushState>();
        private struct Leaf { public SpriteRenderer R; public Vector3 Vel; public float Born, Spin; }
        private readonly List<Leaf> _leaves = new List<Leaf>();
        private static Sprite _leafSprite;

        /// <summary>길냥이가 오기 직전: 그 자리 근처 덤불이 흔들리고 잎사귀가 흩날린다.</summary>
        public void Rustle(Vector3 world, float seconds)
        {
            float r = Size * 2.2f;
            var local = transform.InverseTransformPoint(world);
            for (int i = 0; i < _bushes.Count; i++)
            {
                var b = _bushes[i];
                if (b.T == null || (b.BasePos - local).sqrMagnitude > r * r) continue;
                b.Until = Time.time + seconds;
                b.Seed = Random.value * 10f;
                _bushes[i] = b;
                for (int k = 0; k < 3; k++) SpawnLeaf(b.T.position + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0f, 0.4f), 0) * Size, b.T.GetComponent<SpriteRenderer>().sortingOrder + 1);
            }
        }

        private void SpawnLeaf(Vector3 at, int order)
        {
            if (_leafSprite == null || _leafSprite.texture == null)
            {
                var c = new SdfCanvas(48);
                c.Fill(SdfCanvas.Ellipse(0.5f, 0.5f, 0.42f, 0.2f), SdfCanvas.Col("#7FAE5B"), SdfCanvas.Col("#4E7A3A"), 0.05f);
                c.Fill(SdfCanvas.Capsule(0.15f, 0.5f, 0.85f, 0.5f, 0.02f), SdfCanvas.Col("#5E8C45"));
                _leafSprite = c.ToSprite("leaf", 1f, new Vector2(0.5f, 0.5f));
            }
            var go = new GameObject("Leaf");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _leafSprite;
            sr.sharedMaterial = _material;
            sr.sortingOrder = order;
            float s = Size * 0.18f;
            go.transform.localScale = new Vector3(s, s, 1);
            _leaves.Add(new Leaf { R = sr, Vel = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.8f, 1.4f), 0) * Size, Born = Time.time, Spin = Random.Range(-360f, 360f) });
        }

        private void UpdateRustle(float t)
        {
            for (int i = 0; i < _bushes.Count; i++)
            {
                var b = _bushes[i];
                if (b.T == null || b.Until < 0f) continue;
                float left = b.Until - t;
                if (left <= 0f)
                {
                    b.T.localRotation = Quaternion.identity;
                    b.T.localScale = b.BaseScale;
                    b.T.localPosition = b.BasePos;
                    b.Until = -1f;
                    _bushes[i] = b;
                    continue;
                }
                float k = Mathf.Clamp01(left / 0.6f);
                float w = Mathf.Sin(t * 26f + b.Seed) * 7f * k;
                b.T.localRotation = Quaternion.Euler(0, 0, w);
                b.T.localScale = new Vector3(b.BaseScale.x * (1f + 0.04f * k * Mathf.Sin(t * 19f + b.Seed)), b.BaseScale.y * (1f - 0.04f * k * Mathf.Sin(t * 19f + b.Seed)), 1f);
                b.T.localPosition = b.BasePos + new Vector3(Mathf.Sin(t * 31f + b.Seed) * Size * 0.03f * k, 0, 0);
            }
            for (int i = _leaves.Count - 1; i >= 0; i--)
            {
                var l = _leaves[i];
                float age = t - l.Born;
                if (l.R == null || age > 1.2f)
                {
                    if (l.R != null) Destroy(l.R.gameObject);
                    _leaves.RemoveAt(i);
                    continue;
                }
                l.Vel += new Vector3(0, -Size * 2.2f, 0) * Time.deltaTime;
                l.R.transform.position += l.Vel * Time.deltaTime;
                l.R.transform.rotation = Quaternion.Euler(0, 0, l.Spin * age);
                l.R.color = new Color(1, 1, 1, 1f - Mathf.Clamp01((age - 0.7f) / 0.5f));
                _leaves[i] = l;
            }
        }
}
}
