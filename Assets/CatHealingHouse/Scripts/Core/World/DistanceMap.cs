using System.Collections.Generic;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.World
{
    /// <summary>
    /// 가구 하나까지의 거리표. 가구의 사용 칸을 0으로 두고 BFS 로 퍼뜨린다 (문 너머 보드까지).
    /// 고양이는 매 칸마다 숫자가 더 작은 이웃으로 한 걸음 가기만 하면 도착한다. 실시간 길찾기 없음.
    /// </summary>
    public sealed class DistanceMap
    {
        public const int Unreachable = int.MaxValue;

        public readonly int FurnitureId;
        public readonly int BuiltAtVersion;
        private readonly Dictionary<Cell, int> _dist;

        private DistanceMap(int furnitureId, int version, Dictionary<Cell, int> dist)
        {
            FurnitureId = furnitureId;
            BuiltAtVersion = version;
            _dist = dist;
        }

        public int Count => _dist.Count;

        public int Get(Cell c) => _dist.TryGetValue(c, out var d) ? d : Unreachable;

        public static DistanceMap Build(HouseWorld world, FurnitureInstance target)
        {
            var dist = new Dictionary<Cell, int>();
            var queue = new Queue<Cell>();
            foreach (var u in target.UsableCells())
            {
                var c = new Cell(target.Board, u);
                if (dist.ContainsKey(c)) continue;
                dist[c] = 0;
                queue.Enqueue(c);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int next = dist[cur] + 1;
                foreach (var n in world.Neighbors(cur))
                {
                    if (dist.ContainsKey(n) || !world.IsTransit(n)) continue;
                    dist[n] = next;
                    queue.Enqueue(n);
                }
            }
            return new DistanceMap(target.Id, world.Version, dist);
        }

        /// <summary>
        /// from 에서 한 걸음 나아갈 칸. 지금 칸보다 숫자가 작은 이웃 중 가장 작은 곳.
        /// 동점이면 tieBreak(0~1 난수)로 고른다. 이미 도착했거나 길이 없으면 false.
        /// throughDoor=false 면 문을 넘지 않는다 (영입 전 길냥이).
        /// </summary>
        public bool TryStep(HouseWorld world, Cell from, out Cell step, double tieBreak = 0, bool throughDoor = true)
        {
            step = from;
            int here = Get(from);
            if (here == 0) return false;

            int best = here;
            var candidates = new List<Cell>(2);
            foreach (var n in world.Neighbors(from, throughDoor))
            {
                int d = Get(n);
                if (d == Unreachable) continue;
                if (d < best) { best = d; candidates.Clear(); candidates.Add(n); }
                else if (d == best && best != here) candidates.Add(n);
            }
            if (candidates.Count == 0) return false;
            int idx = (int)(tieBreak * candidates.Count);
            if (idx >= candidates.Count) idx = candidates.Count - 1;
            step = candidates[idx];
            return true;
        }
    }

    /// <summary>가구별 거리맵을 들고 있다가, 배치·철거·확장으로 세계가 바뀌면 필요할 때 다시 굽는다.</summary>
    public sealed class DistanceMapCache
    {
        private readonly HouseWorld _world;
        private readonly Dictionary<int, DistanceMap> _maps = new Dictionary<int, DistanceMap>();

        public DistanceMapCache(HouseWorld world) { _world = world; }

        public DistanceMap Get(int furnitureId)
        {
            var f = _world.GetFurniture(furnitureId);
            if (f == null) { _maps.Remove(furnitureId); return null; }
            if (_maps.TryGetValue(furnitureId, out var m) && m.BuiltAtVersion == _world.Version) return m;
            m = DistanceMap.Build(_world, f);
            _maps[furnitureId] = m;
            return m;
        }

        public void Clear() => _maps.Clear();
    }
}
