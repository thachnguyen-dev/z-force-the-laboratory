using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>Dev tool: reports the owner's pack models (size, polygons, parts) and renders previews to logs/pack-preview.</summary>
    public static class PackInspector
    {
        public const string Folder = "Assets/Zombie/Art/Pack";

        [MenuItem("Zombie/Developer (Claude)/Inspect Pack Models")]
        public static void Inspect()
        {
            var report = new StringBuilder();
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "pack-preview");
            Directory.CreateDirectory(outDir);
            foreach (var path in Directory.GetFiles(Folder, "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (!importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = Object.Instantiate(model);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                var tris = 0;
                var parts = 0;
                var info = new StringBuilder();
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    bounds.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    parts++;
                    var m = mf.sharedMesh;
                    tris += m.triangles.Length / 3;
                    info.Append($" [{r.name}: {m.vertexCount}v {m.triangles.Length / 3}t sub{m.subMeshCount} uv{m.uv.Length > 0} col{m.colors.Length > 0} mats {r.sharedMaterials.Length}]");
                }
                var pieces = PackParts.Split(go.GetComponentInChildren<MeshFilter>().sharedMesh);
                var ps = new StringBuilder();
                for (var i = 0; i < Mathf.Min(8, pieces.Count); i++) ps.Append($" ({pieces[i].Area:F3} c{pieces[i].Bounds.center:F2} s{pieces[i].Bounds.size:F2})");
                report.AppendLine($"{Path.GetFileNameWithoutExtension(path)}: size {bounds.size:F2} tris {tris} pieces {pieces.Count}:{ps}");
                Render(go, bounds, Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png"));
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            Debug.Log("[ZOMBIE-PACK]\n" + report);
        }

        [MenuItem("Zombie/Developer (Claude)/Preview Coloured Pack")]
        public static void PreviewBaked()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "pack-preview");
            Directory.CreateDirectory(outDir);
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            foreach (var path in Directory.GetFiles(PackBuilder.OutFolder, "*.asset"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                // One image per model for the review (owner, 2026-10-07): the colour variants share its painting.
                if (System.Text.RegularExpressions.Regex.IsMatch(id, "_v[0-9]+$")) continue;
                var go = new GameObject(id);
                // At real size (2026-10-04), so the wear details (cracks, rust) look as they will in the game.
                var baseId = System.Text.RegularExpressions.Regex.Replace(id, "_v[0-9]+$", "");
                var size = Art.PackProps.Get(baseId)?.Size ?? 1f;
                go.AddComponent<MeshFilter>().sharedMesh = Art.PackArt.Model(id);
                go.transform.localScale = Vector3.one * size;
                go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Resources/" + Art.ArtMaterials.StylizedResource + ".mat");
                var b = go.GetComponent<MeshRenderer>().bounds;
                RenderGame(go, b, Path.Combine(outDir, id + "-coloured.png"));
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Batch: every code-built prop and decoration (the library pieces that aren't the owner's pack models), one
        /// in-game camera image each, to logs/pack-preview/lib-{id}.png (owner, 2026-10-07: review in the game view).
        /// </summary>
        public static void PreviewLibrary()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "pack-preview");
            Directory.CreateDirectory(outDir);
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Resources/" + Art.ArtMaterials.StylizedResource + ".mat");
            foreach (var piece in Art.ArtLibrary.Pieces.Values)
            {
                if (piece.Model != null || piece.Build == null) continue;
                var kit = new Art.MeshKit();
                piece.Build(kit, 1);
                var mesh = kit.ToMesh(piece.Id);
                if (mesh.vertexCount == 0) continue;
                var go = new GameObject(piece.Id);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                RenderGame(go, go.GetComponent<MeshRenderer>().bounds, Path.Combine(outDir, "lib-" + piece.Id + ".png"));
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        [MenuItem("Zombie/Developer (Claude)/Preview Held Weapons")]
        public static void PreviewHeld()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "pack-preview");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Resources/" + Art.ArtMaterials.StylizedResource + ".mat");
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            foreach (var (id, size) in new[] { ("pistol", 0.7f), ("knife", 0.8f), ("ar_gun", 1.4f), ("shotgun", 1.3f), ("sniper_gun", 1.6f), ("smg_gun", 1f) })
            {
                var root = new GameObject("Held " + id);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                var gun = new GameObject("gun");
                gun.transform.SetParent(root.transform, false);
                gun.AddComponent<MeshFilter>().sharedMesh = Art.PackArt.Model(id);
                gun.AddComponent<MeshRenderer>().sharedMaterial = mat;
                gun.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                gun.transform.localScale = new Vector3(size, size * Survivor.Chunky, size * Survivor.Chunky);
                gun.transform.localPosition = new Vector3(0.4f, 0.85f, 0.15f + size * 0.3f);
                if (id != "knife") Art.MuzzleFlash.Attach(root.transform).Fire(new Vector3(0.4f, 0.85f + size * 0.1f, 0.15f + size * 0.8f), 0.8f);
                root.transform.rotation = Quaternion.Euler(0f, 40f, 0f);
                RenderGame(root, new Bounds(new Vector3(0f, 1f, 0.5f), Vector3.one * 2.4f), Path.Combine(outDir, "held-" + id + ".png"));
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>From the game's camera: tilted 60°, looking north.</summary>
        private static void RenderGame(GameObject go, Bounds b, string file)
        {
            var camGo = new GameObject("Preview camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.3f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.36f, 0.25f);
            camGo.transform.rotation = Quaternion.Euler(CameraRig.Tilt, 0f, 0f);
            camGo.transform.position = b.center - camGo.transform.forward * 50f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var rt = new RenderTexture(384, 384, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(384, 384, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 384, 384), 0, 0);
            t.Apply();
            File.WriteAllBytes(file, t.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
        }

        private static void Render(GameObject go, Bounds b, string file)
        {
            var camGo = new GameObject("Preview camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
            camGo.transform.rotation = Quaternion.Euler(30f, -35f, 0f);
            camGo.transform.position = b.center - camGo.transform.forward * 50f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var rt = new RenderTexture(512, 512, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(512, 512, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            t.Apply();
            File.WriteAllBytes(file, t.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
        }
    }
}
