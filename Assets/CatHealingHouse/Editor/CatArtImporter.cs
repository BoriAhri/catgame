using System.Collections.Generic;
using System.IO;
using CatHealingHouse.View.Art;
using UnityEditor;
using UnityEngine;

namespace CatHealingHouse.EditorTools
{
    /// <summary>
    /// AI로 뽑아 잘라 둔 고양이 부위 그림(폴더 하나 = 품종 하나)을 스프라이트로 읽어서 그림 보관함(ArtLibrary)에 등록한다.
    /// 폴더 안: 부위 이름.png (Head, Body, LegFrontNear …) + parts.json (캔버스 크기, 부위별 피벗) + Empty.png (안 쓰는 부위용 빈 그림).
    /// 피벗은 게임 고양이의 관절 위치(CatParts.Joint)에 맞춰져 있어서 관절 값은 따로 넣지 않는다.
    /// </summary>
    public static class CatArtImporter
    {
        public const string Root = "Assets/CatHealingHouse/Art/Cats";
        public const string LibraryPath = "Assets/CatHealingHouse/Data/ArtLibrary.asset";

        /// <summary>그림이 머리에 귀·볼터치를 이미 담고 있어서 따로 그리지 않는 부위 (빈 그림을 넣어 코드 그림이 나오지 않게).</summary>
        static readonly CatPart[] EmptyParts = { CatPart.EarL, CatPart.EarR, CatPart.EyesOpen, CatPart.Blush, CatPart.TailTip };

        [System.Serializable] class PartMeta { public int w, h; public float[] pivot; }

        /// <summary>folder 의 그림을 skinIds 에 모두 등록한다 (예: cheese, stray_cheese 가 같은 그림).</summary>
        public static string Import(string look, params string[] skinIds)
        {
            string folder = Root + "/" + look;
            string json = File.ReadAllText(folder + "/parts.json");
            int canvas = ParseCanvas(json);
            var metas = ParseParts(json);
            var parts = new List<ArtLibrary.CatPartArt>();

            foreach (var kv in metas)
            {
                if (!System.Enum.TryParse(kv.Key, out CatPart part)) continue;
                var sp = ImportSprite(folder + "/" + kv.Key + ".png", canvas, new Vector2(kv.Value[0], kv.Value[1]));
                if (sp == null) continue;
                string ol = folder + "/" + kv.Key + "_Outline.png";
                var outline = File.Exists(ol) ? ImportSprite(ol, canvas, new Vector2(kv.Value[0], kv.Value[1])) : null;
                parts.Add(new ArtLibrary.CatPartArt { part = part, sprite = sp, outline = outline, joint = Joints.TryGetValue(kv.Key, out var jv) ? jv : Vector2.zero });
            }
            var empty = ImportSprite(folder + "/Empty.png", canvas, new Vector2(0.5f, 0.5f));
            foreach (var p in EmptyParts) parts.Add(new ArtLibrary.CatPartArt { part = p, sprite = empty });

            Sprite portrait = File.Exists(folder + "/Portrait.png") ? ImportSprite(folder + "/Portrait.png", 512, new Vector2(0.5f, 0.5f)) : null;
            // 자세별 한 장짜리 몸 그림: poses.txt 한 줄 = 이름 피벗x 피벗y 머리x 머리y (머리 없으면 -)
            var poses = new List<ArtLibrary.PoseArt>();
            if (File.Exists(folder + "/poses.txt"))
                foreach (var line in File.ReadAllLines(folder + "/poses.txt"))
                {
                    var f = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 5 || !System.Enum.TryParse(f[0], out ArtLibrary.CatPoseArt kind)) continue;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var piv = new Vector2(float.Parse(f[1], inv), float.Parse(f[2], inv));
                    var sp = ImportSprite(folder + "/Pose" + f[0] + ".png", canvas, piv);
                    if (sp == null) continue;
                    var pa = new ArtLibrary.PoseArt { pose = kind, sprite = sp, outline = ImportSprite(folder + "/Pose" + f[0] + "_Outline.png", canvas, piv) };
                    if (f[3] != "-") { pa.hasHead = true; pa.head = new Vector2(float.Parse(f[3], inv), float.Parse(f[4], inv)); }
                    poses.Add(pa);
                }
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            foreach (var id in skinIds)
            {
                var skin = lib.catSkins.Find(s => s != null && s.skinId == id);
                if (skin == null) { skin = new ArtLibrary.CatSkin { skinId = id }; lib.catSkins.Add(skin); }
                skin.parts = parts.ToArray();
                skin.portrait = portrait;
                skin.poses = poses.ToArray();
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"{look}: 부위 {parts.Count}개, 자세 {poses.Count}개 → {string.Join(", ", skinIds)}";
        }

        static Sprite ImportSprite(string path, int canvas, Vector2 pivot)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return null;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = canvas;   // 캔버스 폭 = 1 (게임 고양이 규칙)
            var st = new TextureImporterSettings();
            ti.ReadTextureSettings(st);
            st.spriteAlignment = (int)SpriteAlignment.Custom;
            st.spritePivot = pivot;
            st.spriteMeshType = SpriteMeshType.Tight;
            ti.SetTextureSettings(st);
            ti.mipmapEnabled = true;           // 멀리서 작게 볼 때 지글거리지 않게
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // JsonUtility 는 사전을 못 읽어서 parts.json 을 간단히 직접 읽는다
        static int ParseCanvas(string json)
        {
            int i = json.IndexOf("\"canvas\"");
            int c = json.IndexOf(':', i) + 1;
            int e = json.IndexOfAny(new[] { ',', '}' }, c);
            return int.Parse(json.Substring(c, e - c).Trim());
        }

        static readonly Dictionary<string, Vector2> Joints = new Dictionary<string, Vector2>();

        static Dictionary<string, float[]> ParseParts(string json)
        {
            var result = new Dictionary<string, float[]>();
            Joints.Clear();
            string lastPart = null;
            int at = json.IndexOf('{', json.IndexOf("\"parts\"")); // parts 의 { 다음부터
            while (true)
            {
                int q = json.IndexOf('"', at + 1);
                if (q < 0) break;
                int q2 = json.IndexOf('"', q + 1);
                string key = json.Substring(q + 1, q2 - q - 1);
                at = q2;
                if (key == "w" || key == "h" || key == "pivot") continue;
                if (key == "joint")
                {
                    // 관절 덮어쓰기 (앞 부위의 것)
                    int j0 = json.IndexOf('[', q2), j1 = json.IndexOf(']', j0);
                    var jn = json.Substring(j0 + 1, j1 - j0 - 1).Split(',');
                    if (lastPart != null) Joints[lastPart] = new Vector2(float.Parse(jn[0].Trim(), System.Globalization.CultureInfo.InvariantCulture), float.Parse(jn[1].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                    at = j1;
                    continue;
                }
                lastPart = key;
                int pv = json.IndexOf("\"pivot\"", q2);
                if (pv < 0) break;
                int b0 = json.IndexOf('[', pv), b1 = json.IndexOf(']', b0);
                var nums = json.Substring(b0 + 1, b1 - b0 - 1).Split(',');
                result[key] = new[] { float.Parse(nums[0].Trim(), System.Globalization.CultureInfo.InvariantCulture), float.Parse(nums[1].Trim(), System.Globalization.CultureInfo.InvariantCulture) };
                at = b1;
            }
            return result;
        }
    }
}
