using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Bakes the owner's pack models (2026-10-04: they come without colours; Claude colours them) into coloured meshes in
    /// Resources/Pack. Each model is split into its separate pieces and every piece is coloured by its role (body, stock,
    /// strap, bolt, glass…), sometimes in bands along an axis. Geometry is kept exactly as delivered (owner, 2026-10-04: no
    /// polygon reduction until a later optimisation stage). Glowing parts get vertex
    /// alpha below 1 (the Zombie/Stylized shader's glow mask). Output: y up, centred, bottom at 0, largest side 1.
    /// </summary>
    public static class PackBuilder
    {
        public const string OutFolder = "Assets/Zombie/Resources/" + Art.PackArt.ResourceFolder;

        private delegate Color Painter(int index, PackParts.Part part, Vector3 p, Vector3 n, Bounds all);

        private static Color C(float r, float g, float b, float glow = 0f) => new Color(r, g, b, 1f - glow);

        private static readonly Color GunMetal = C(0.2f, 0.22f, 0.26f);
        private static readonly Color Polymer = C(0.11f, 0.12f, 0.13f);
        private static readonly Color Steel = C(0.62f, 0.66f, 0.7f);
        private static readonly Color Wood = C(0.52f, 0.33f, 0.18f);
        private static readonly Color Olive = C(0.36f, 0.44f, 0.26f);
        private static readonly Color Brass = C(0.86f, 0.66f, 0.3f);

        private static bool Small(PackParts.Part p) => p.Area < 0.02f;

        /// <summary>
        /// Models delivered pointing the other way (muzzle or blade at -x); turned 180° so every weapon points along +x
        /// (owner, 2026-10-04: the sniper was shown reversed; the shotgun and knife were too).
        /// </summary>
        private static readonly HashSet<string> Reversed = new HashSet<string> { "sniper_gun", "shotgun", "knife" };

        /// <summary>Guns: a gunmetal body, the stock (rear pieces) in black or wood, small parts in lighter steel.</summary>
        private static Painter Gun(Color stock, Color accent) => (i, part, p, n, all) =>
        {
            if (i == 0) return p.x < all.min.x + all.size.x * 0.28f ? stock : GunMetal;
            if (Small(part)) return Steel;
            if (part.Bounds.center.x < all.center.x - all.size.x * 0.15f) return stock;
            if (part.Bounds.center.y > all.center.y + all.size.y * 0.2f) return Polymer; // sights and scopes
            return i % 2 == 0 ? accent : GunMetal;
        };

        /// <summary>Magazines: the case in the ammo type's colour, brass rounds showing at the top.</summary>
        private static Painter Magazine(Color body) => (i, part, p, n, all) =>
            p.y > all.min.y + all.size.y * 0.86f ? Brass : i == 0 || all.size.y < 0.5f ? body : C(0.8f, 0.2f, 0.16f);

        private static readonly Dictionary<string, Painter> Recipes = new Dictionary<string, Painter>
        {
            ["fences"] = (i, part, p, n, all) =>
                part.Bounds.size.y > all.size.y * 0.75f ? C(0.24f, 0.28f, 0.34f)            // posts
                : Small(part) ? C(0.78f, 0.8f, 0.82f)                                        // bolts
                : part.Bounds.size.x > all.size.x * 0.6f ? C(0.52f, 0.58f, 0.64f)           // rails and wire
                : C(0.42f, 0.48f, 0.55f),                                                    // braces
            ["ar_gun"] = Gun(Polymer, C(0.26f, 0.28f, 0.3f)),
            ["smg_gun"] = Gun(Polymer, C(0.3f, 0.28f, 0.34f)),
            ["shotgun"] = Gun(Wood, C(0.24f, 0.24f, 0.26f)),
            ["sniper_gun"] = Gun(C(0.34f, 0.38f, 0.24f), C(0.22f, 0.24f, 0.2f)),
            ["pistol"] = (i, part, p, n, all) => i == 0 ? (p.y < all.min.y + all.size.y * 0.45f && p.x < all.center.x ? C(0.36f, 0.24f, 0.16f) : GunMetal) : Steel,
            ["knife"] = (i, part, p, n, all) => i == 0 ? Steel : i == 1 ? Polymer : GunMetal,
            ["ar_magazine"] = Magazine(C(0.22f, 0.42f, 0.78f)),
            ["smg_magazine"] = Magazine(C(0.52f, 0.3f, 0.74f)),
            ["sniper_magazine"] = Magazine(C(0.86f, 0.66f, 0.18f)),
            ["shotgun_magazine"] = (i, part, p, n, all) => i == 0 ? C(0.28f, 0.3f, 0.26f) : p.y < all.min.y + all.size.y * 0.25f ? Brass : C(0.84f, 0.2f, 0.16f),
            ["c4"] = (i, part, p, n, all) =>
                i == 0 ? C(0.78f, 0.7f, 0.48f)
                : Small(part) ? (i % 2 == 0 ? C(1f, 0.25f, 0.2f, 0.7f) : C(0.3f, 0.85f, 0.3f, 0.5f))
                : C(0.12f, 0.12f, 0.13f),
            ["chest_closed"] = (i, part, p, n, all) =>
                i <= 1 ? (i == 0 ? Olive : C(0.42f, 0.5f, 0.3f)) : Small(part) ? Steel : C(0.2f, 0.22f, 0.24f),
            ["chest_opend"] = (i, part, p, n, all) =>
                i <= 1 ? (i == 0 ? Olive : C(0.42f, 0.5f, 0.3f)) : i == 2 ? C(0.24f, 0.3f, 0.18f) : Small(part) ? Steel : C(0.2f, 0.22f, 0.24f),
            ["fuel"] = (i, part, p, n, all) =>
            {
                if (i > 0) return C(1f, 0.8f, 0.2f);
                var t = (p.y - all.min.y) / all.size.y;
                if (Mathf.Abs(t - 0.3f) < 0.035f || Mathf.Abs(t - 0.7f) < 0.035f) return C(0.55f, 0.1f, 0.08f);
                if (t > 0.44f && t < 0.56f) return C(1f, 0.8f, 0.2f);
                return C(0.82f, 0.18f, 0.14f);
            },
            ["grenade"] = (i, part, p, n, all) => i > 0 ? Steel : p.y > all.min.y + all.size.y * 0.78f ? GunMetal : Olive,
            ["medical"] = (i, part, p, n, all) => i == 0 ? C(0.94f, 0.94f, 0.92f) : i == 2 ? C(0.5f, 0.52f, 0.56f) : C(0.88f, 0.16f, 0.14f),
            ["syringe"] = (i, part, p, n, all) =>
            {
                // Along its length: plunger, the glowing green vaccine, the needle.
                var t = (p.x - all.min.x) / all.size.x;
                if (t < 0.22f) return C(0.9f, 0.9f, 0.88f);
                if (t > 0.8f) return Steel;
                return t < 0.26f || t > 0.76f ? C(0.95f, 0.95f, 0.95f) : C(0.4f, 1f, 0.45f, 0.6f);
            },
            ["zombie_incubator"] = (i, part, p, n, all) =>
            {
                if (i == 0) return p.y > all.min.y + all.size.y * 0.62f ? C(0.3f, 0.72f, 0.34f, 0.3f) : C(0.26f, 0.38f, 0.34f);
                if (i == 1) return C(0.22f, 0.24f, 0.28f);
                if (Small(part)) return C(0.78f, 0.62f, 0.32f);
                return part.Bounds.center.y > all.min.y + all.size.y * 0.7f ? C(0.32f, 0.36f, 0.4f) : C(0.48f, 0.3f, 0.2f);
            },
        };

        private delegate PackObstacles.Look AnyPainter(int index, PackParts.Part part, Vector3 p, Vector3 n, Bounds all);

        [MenuItem("Zombie/Developer (Claude)/Build Pack Models (colours)")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(OutFolder);
            foreach (var path in Directory.GetFiles(PackInspector.Folder, "*.fbx"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (!importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mf = model.GetComponentInChildren<MeshFilter>();
                if (mf == null) continue;
                // The obstacle pack (2026-10-04) has colour variants: "id", "id_v1", "id_v2"…
                var painters = new List<AnyPainter>();
                if (PackObstacles.Recipes.TryGetValue(id, out var recipe))
                    for (var v = 0; v < recipe.Variants; v++)
                    {
                        var variant = v;
                        painters.Add((i, part, p, n, all) => recipe.Paint(variant, i, part, p, n, all));
                    }
                else
                {
                    Recipes.TryGetValue(id, out var old);
                    painters.Add(old != null ? (AnyPainter)((i, part, p, n, all) => (PackObstacles.Look)old(i, part, p, n, all)) : null);
                }
                for (var v = 0; v < painters.Count; v++)
                {
                    var name = v == 0 ? id : $"{id}_v{v}";
                    var mesh = Bake(name, id, mf.sharedMesh, mf.transform.localToWorldMatrix, painters[v]);
                    var outPath = $"{OutFolder}/{name}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(outPath);
                    if (existing != null)
                    {
                        existing.Clear();
                        EditorUtility.CopySerialized(mesh, existing);
                    }
                    else AssetDatabase.CreateAsset(mesh, outPath);
                    Debug.Log($"[ZOMBIE-PACK] {name}: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles");
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The model in y-up space, centred on x/z, bottom at 0, largest side 1, coloured per piece. Every triangle is
        /// kept; vertices that end up identical (same place, normal and colour) are shared, which only saves memory.
        /// </summary>
        /// <summary>
        /// Shading baked into the colours (owner, 2026-10-07: "do not turn my 3D models into 2D by painting them as one
        /// flat block of colour"), per vertex, as a multiplier about 0.6 to 1.1: creases and hollows darker (each vertex
        /// against its neighbours), a slow and a fine tonal variation at real size (grime, sun-fading, stains), and a
        /// soft gradient darker towards the ground. Positions are in the baked space (largest side 1).
        /// </summary>
        private static float[] Tone(string id, Vector3[] v, Vector3[] normals, int[] tri, Bounds all)
        {
            var n = v.Length;
            var sum = new Vector3[n];
            var cnt = new int[n];
            var edge = 0f;
            for (var t = 0; t < tri.Length; t += 3)
                for (var e = 0; e < 3; e++)
                {
                    int a = tri[t + e], b = tri[t + (e + 1) % 3];
                    sum[a] += v[b];
                    cnt[a]++;
                    sum[b] += v[a];
                    cnt[b]++;
                    edge += (v[a] - v[b]).magnitude;
                }
            edge /= Mathf.Max(1, tri.Length);
            // Noise at real size: the model's largest side is its size in metres.
            var size = Art.PackProps.Get(id)?.Size ?? 1f;
            var tone = new float[n];
            for (var i = 0; i < n; i++)
            {
                var crease = 1f;
                if (normals != null && cnt[i] > 0)
                {
                    var hollow = Vector3.Dot(normals[i], sum[i] / cnt[i] - v[i]) / Mathf.Max(1e-5f, edge);
                    crease = 1f - 0.45f * Mathf.Clamp01(hollow * 3f);
                }
                // Small items (a magazine, a fuel can) at least as varied as a 2 m object, or they stay one fill.
                var p = v[i] * Mathf.Max(size, 2f);
                var variation = 0.8f + 0.3f * HoldPose.Noise3(p * 1.3f + new Vector3(5.1f, 0f, 2.3f)) + 0.14f * (HoldPose.Noise3(p * 7f) - 0.5f);
                var height = Mathf.Lerp(0.78f, 1.06f, Mathf.InverseLerp(all.min.y, all.max.y, v[i].y));
                tone[i] = crease * variation * height;
            }
            return tone;
        }

        /// <summary>
        /// Large faces split into smaller ones (each edge halved, up to 3 times, while an edge is longer than 15 cm at
        /// real size), so the baked shading has vertices to vary across a face: a low-poly lid or wall is otherwise one
        /// colour (owner, 2026-10-07: no flat blocks). Midpoints are shared, so pieces stay connected; children keep their
        /// parent's place in the list, so the pieces keep their order (the painters pick colours by piece). Stops at
        /// 4 times the original triangles or 24 000, to keep the props cheap.
        /// </summary>
        private static (Vector3[] v, Vector3[] n, int[] t) Subdivide(string id, Vector3[] v, Vector3[] n, int[] t)
        {
            var size = Art.PackProps.Get(id)?.Size ?? 1f;
            var limit = 0.15f / size;
            var budget = Mathf.Min(t.Length / 3 * 4, 24000);
            var verts = new List<Vector3>(v);
            var norms = n != null ? new List<Vector3>(n) : null;
            var tris = new List<int>(t);
            for (var level = 0; level < 3; level++)
            {
                var mid = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (mid.TryGetValue(key, out var m)) return m;
                    m = verts.Count;
                    verts.Add((verts[a] + verts[b]) * 0.5f);
                    norms?.Add((norms[a] + norms[b]).normalized);
                    mid[key] = m;
                    return m;
                }
                var next = new List<int>(tris.Count * 2);
                var split = false;
                for (var k = 0; k < tris.Count; k += 3)
                {
                    int a = tris[k], b = tris[k + 1], c = tris[k + 2];
                    var longest = Mathf.Max((verts[a] - verts[b]).magnitude, Mathf.Max((verts[b] - verts[c]).magnitude, (verts[c] - verts[a]).magnitude));
                    // Triangles after this one if it splits: those done, those left, and 3 more.
                    if (longest <= limit || (next.Count + tris.Count - k) / 3 + 3 > budget)
                    {
                        next.Add(a); next.Add(b); next.Add(c);
                        continue;
                    }
                    split = true;
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                }
                tris = next;
                if (!split || tris.Count / 3 >= budget) break;
            }
            return (verts.ToArray(), norms?.ToArray() ?? new Vector3[0], tris.ToArray());
        }

        /// <summary>Each separate piece a slightly different shade (±4 %), so repeated parts don't read as one fill.</summary>
        private static float PieceTint(int piece)
        {
            var h = (uint)(piece * 2654435761u);
            h ^= h >> 15;
            return 0.96f + 0.08f * ((h & 0xffff) / 65535f);
        }

        private static Mesh Bake(string name, string id, Mesh source, Matrix4x4 toModel, AnyPainter paint)
        {
            var sv = source.vertices;
            var sn = source.normals;
            var st = source.triangles;
            var v = new Vector3[sv.Length];
            if (Reversed.Contains(id)) toModel = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) * toModel;
            for (var i = 0; i < sv.Length; i++) v[i] = toModel.MultiplyPoint3x4(sv[i]);
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            var scale = 1f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            for (var i = 0; i < v.Length; i++) v[i] = (v[i] + offset) * scale;
            (v, sn, st) = Subdivide(id, v, sn.Length == sv.Length ? sn : null, st);
            sv = v;
            var normalMatrix = toModel.inverse.transpose;
            // Pieces from the transformed copy, so bounds are in the same space.
            var temp = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            temp.vertices = v;
            temp.triangles = st;
            var pieces = PackParts.Split(temp);
            Object.DestroyImmediate(temp);
            var all = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) all.Encapsulate(p);
            var tone = Tone(id, v, sn.Length == sv.Length ? sn.Select(x => normalMatrix.MultiplyVector(x).normalized).ToArray() : null, st, all);
            var outV = new List<Vector3>(st.Length);
            var outN = new List<Vector3>(st.Length);
            var outC = new List<Color>(st.Length);
            var outT = new List<int>(st.Length);
            var shared = new Dictionary<(Vector3Int, Vector3Int, Color32, Color32, int), int>();
            var outW = new List<Vector4>(st.Length);
            var outB = new List<Vector2>(st.Length);
            var anyWear = false;
            for (var pi = 0; pi < pieces.Count; pi++)
            {
                var part = pieces[pi];
                foreach (var t0 in part.Triangles)
                    for (var k = 0; k < 3; k++)
                    {
                        var vi = st[t0 + k];
                        var n = sn.Length == sv.Length ? normalMatrix.MultiplyVector(sn[vi]).normalized : Vector3.up;
                        var look = paint != null ? paint(pi, part, v[vi], n, all) : (PackObstacles.Look)new Color(0.6f, 0.62f, 0.65f, 1f);
                        var colour = look.Colour;
                        // Shaded, not one flat block (owner, 2026-10-07); glowing parts keep their colour.
                        if (colour.a >= 1f)
                        {
                            var shade = tone[vi] * PieceTint(pi);
                            // Darker parts lean slightly cool, lit parts slightly warm, as painted stylised props do.
                            var cool = Mathf.Clamp01(1f - shade) * 0.5f;
                            colour = new Color(Mathf.Clamp01(colour.r * shade * (1f - cool * 0.12f)), Mathf.Clamp01(colour.g * shade), Mathf.Clamp01(colour.b * shade * (1f + cool * 0.12f)), colour.a);
                        }
                        // Glowing parts stay clean so they keep glowing.
                        // Every surface carries at least light grime, a few cracks and chips (the shader's wear texture,
                        // per pixel): low-poly faces have too few vertices for the baked shading to vary across them.
                        var wear = colour.a < 1f ? Vector4.zero : Vector4.Max(look.Wear, new Vector4(0.75f, 0.1f, 0.22f, 0f));
                        var blood = colour.a < 1f ? 0f : look.Blood;
                        var camo = colour.a < 1f ? 0f : look.Camo;
                        // The project renders in linear space; colours here are written as sRGB.
                        var linear = new Color(Mathf.GammaToLinearSpace(colour.r), Mathf.GammaToLinearSpace(colour.g), Mathf.GammaToLinearSpace(colour.b), colour.a);
                        var key = (Vector3Int.RoundToInt(v[vi] * 20000f), Vector3Int.RoundToInt(n * 1000f), (Color32)linear, (Color32)new Color(wear.x, wear.y, wear.z, wear.w), Mathf.RoundToInt(blood * 255f) * 1000 + Mathf.RoundToInt(camo * 255f));
                        if (!shared.TryGetValue(key, out var index))
                        {
                            index = outV.Count;
                            shared[key] = index;
                            outV.Add(v[vi]);
                            outN.Add(n);
                            outC.Add(linear);
                            outW.Add(wear);
                            outB.Add(new Vector2(blood, camo));
                            anyWear |= wear != Vector4.zero || blood > 0f || camo > 0f;
                        }
                        outT.Add(index);
                    }
            }
            var mesh = new Mesh { name = name, indexFormat = outV.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(outV);
            mesh.SetNormals(outN);
            mesh.SetColors(outC);
            // Wear weights for the Stylized shader (2026-10-04): UV1 = grime, cracks, chips, rust; UV2.x = blood.
            if (anyWear)
            {
                mesh.SetUVs(1, outW);
                mesh.SetUVs(2, outB);
            }
            mesh.SetTriangles(outT, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
