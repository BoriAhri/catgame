using CatHealingHouse.Hex;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>Pointy-top 육각형 메시. 쿼터뷰처럼 세로를 눌러서 만든다.</summary>
    public static class HexMesh
    {
        private static Mesh _cached;
        private static float _cachedSize, _cachedYScale, _cachedInset;

        public static Mesh Get(float size, float yScale, float inset)
        {
            if (_cached != null && Mathf.Approximately(size, _cachedSize) &&
                Mathf.Approximately(yScale, _cachedYScale) && Mathf.Approximately(inset, _cachedInset))
                return _cached;

            float s = size * (1f - inset);
            var verts = new Vector3[7];
            verts[0] = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                float ang = Mathf.Deg2Rad * (60f * i - 30f); // pointy-top: 꼭짓점이 위아래
                verts[i + 1] = new Vector3(s * Mathf.Cos(ang), s * Mathf.Sin(ang) * yScale, 0f);
            }
            var tris = new int[18];
            for (int i = 0; i < 6; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = (i + 1) % 6 + 1;
                tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Hex" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            _cached = mesh;
            _cachedSize = size; _cachedYScale = yScale; _cachedInset = inset;
            return mesh;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Mesh> _rounded = new System.Collections.Generic.Dictionary<string, Mesh>();

        /// <summary>
        /// 모서리가 둥근 육각형 (몽글몽글한 타일용). cornerRadius 는 size 에 대한 비율.
        /// 정육각형의 내각은 120도라, 각 꼭짓점에서 안쪽으로 r/sin60 만큼 들어간 곳을 중심으로 60도 호를 그린다.
        /// </summary>
        public static Mesh GetRounded(float size, float yScale, float inset, float cornerRadius, int arcSteps = 5)
        {
            string key = $"{size}_{yScale}_{inset}_{cornerRadius}_{arcSteps}";
            if (_rounded.TryGetValue(key, out var cached) && cached != null) return cached;

            float R = size * (1f - inset);
            float r = R * cornerRadius;
            float toCenter = r / Mathf.Sin(60f * Mathf.Deg2Rad);
            var verts = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            for (int i = 0; i < 6; i++)
            {
                float cornerAng = Mathf.Deg2Rad * (60f * i - 30f);
                var corner = new Vector2(Mathf.Cos(cornerAng), Mathf.Sin(cornerAng)) * R;
                var arcCenter = corner - new Vector2(Mathf.Cos(cornerAng), Mathf.Sin(cornerAng)) * toCenter;
                for (int s = 0; s <= arcSteps; s++)
                {
                    float a = cornerAng + Mathf.Deg2Rad * Mathf.Lerp(-30f, 30f, s / (float)arcSteps);
                    var p = arcCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    verts.Add(new Vector3(p.x, p.y * yScale, 0f));
                }
            }
            int ring = verts.Count - 1;
            var tris = new int[ring * 3];
            for (int i = 0; i < ring; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = (i + 1) % ring + 1;
                tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "RoundedHex" };
            mesh.SetVertices(verts);
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _rounded[key] = mesh;
            return mesh;
        }

        public static Vector3 ToWorld(this HexLayout layout, HexCoord c, float z = 0f)
        {
            layout.ToWorld(c, out var x, out var y);
            return new Vector3((float)x, (float)y, z);
        }
    }
}
