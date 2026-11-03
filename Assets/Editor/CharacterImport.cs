using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Brings delivered character models to the rig spec (docs/3-assets-art-direction.md 4.1) at import, without
    /// changing the source FBX (owner, 2026-10-06: Claude may rename character bones itself). Known alternative
    /// bone names are renamed to the spec names, and the skinned mesh object is named after the file.
    /// </summary>
    public sealed class CharacterImport : AssetPostprocessor
    {
        /// <summary>Alternative bone names (without the .L / .R side) and the spec name for each.</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "head", "Head" },
            { "Upper_Arm", "UpperArm" },
            { "ForeArm", "LowerArm" },
        };

        public override uint GetVersion() => 7;

        /// <summary>
        /// Skinning repaired at import (owner, 2026-10-07: "Can you fix it?"), the source FBX unchanged. "all": the file
        /// has every bone's weight group but all of them empty (zombie_2, twice: Blender's automatic weights failing on a
        /// generated mesh leaves the groups empty), so the whole mesh is weighted from the bones. "stray": only the
        /// vertices skinned to Hips alone that lie away from the hips (chr_01: a piece of the left glove, a strip of cloth
        /// from the left arm to the chest, and the radio, which floated or stayed behind when the body moved).
        /// </summary>
        private static readonly Dictionary<string, string> Reweight = new Dictionary<string, string>
        {
            { "zmb_2", "all" },
            { "chr_01", "stray" },
        };

        /// <summary>Each bone's segment runs to this bone (the end bones are extended along their parent's direction).</summary>
        private static readonly Dictionary<string, string> Next = new Dictionary<string, string>
        {
            { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" }, { "Neck", "Head" },
            { "Shoulder.L", "UpperArm.L" }, { "UpperArm.L", "LowerArm.L" }, { "LowerArm.L", "Hand.L" },
            { "Shoulder.R", "UpperArm.R" }, { "UpperArm.R", "LowerArm.R" }, { "LowerArm.R", "Hand.R" },
            { "UpperLeg.L", "LowerLeg.L" }, { "LowerLeg.L", "Foot.L" },
            { "UpperLeg.R", "LowerLeg.R" }, { "LowerLeg.R", "Foot.R" },
        };

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(CharacterInspector.Folder + "/")) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                var side = "";
                if (name.EndsWith(".L") || name.EndsWith(".R"))
                {
                    side = name.Substring(name.Length - 2);
                    name = name.Substring(0, name.Length - 2);
                }
                if (Aliases.TryGetValue(name, out var spec)) t.name = spec + side;
            }
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null && smr.transform != root.transform) smr.gameObject.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (smr != null && Reweight.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(assetPath), out var mode))
                Debug.Log($"[ZOMBIE-REWEIGHT] {assetPath}: {RepairWeights(smr, mode == "all")} vertices weighted from the bones");
            if (smr != null)
            {
                var glass = GlassToHead(smr);
                if (glass > 0) Debug.Log($"[ZOMBIE-REWEIGHT] {assetPath}: {glass} glass vertices bound to the head");
            }
        }

        /// <summary>
        /// Owner, 2026-10-07: Character 02's glass shook when she moved. Its vertices were skinned to Hips alone (the
        /// protagonist's half to Hips, the scientist's partly), so the visor slid against the head as the body swayed.
        /// Glass is rigid: each separate piece of a "glass" part that sits at head height (its centre above the neck) is
        /// bound wholly to the head. Glass elsewhere (a lamp at the belt) keeps its weights. Returns the vertices changed.
        /// </summary>
        private static int GlassToHead(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            var mats = smr.sharedMaterials;
            var bones = smr.bones;
            int head = -1, neck = -1;
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                if (bones[i].name == "Head") head = i;
                if (bones[i].name == "Neck") neck = i;
            }
            if (head < 0) return 0;
            var neckY = bones[neck >= 0 ? neck : head].position.y;
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var changed = 0;
            for (var sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
            {
                if (mats[sub] == null || !mats[sub].name.ToLowerInvariant().StartsWith("glass")) continue;
                var tris = mesh.GetTriangles(sub);
                // Pieces: vertices joined by triangles, and vertices at the same spot (split at seams) count as one.
                var parent = new Dictionary<int, int>();
                int Find(int v)
                {
                    if (!parent.TryGetValue(v, out var p)) { parent[v] = v; return v; }
                    if (p == v) return v;
                    var root = Find(p);
                    parent[v] = root;
                    return root;
                }
                void Join(int a, int b)
                {
                    var ra = Find(a);
                    var rb = Find(b);
                    if (ra != rb) parent[ra] = rb;
                }
                for (var t = 0; t + 2 < tris.Length; t += 3)
                {
                    Join(tris[t], tris[t + 1]);
                    Join(tris[t], tris[t + 2]);
                }
                var bySpot = new Dictionary<Vector3Int, int>();
                foreach (var v in new List<int>(parent.Keys))
                {
                    var p = verts[v] * 10000f;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                    if (bySpot.TryGetValue(key, out var other)) Join(v, other);
                    else bySpot[key] = v;
                }
                var pieces = new Dictionary<int, List<int>>();
                foreach (var v in new List<int>(parent.Keys))
                {
                    var root = Find(v);
                    if (!pieces.TryGetValue(root, out var list)) pieces[root] = list = new List<int>();
                    list.Add(v);
                }
                foreach (var piece in pieces.Values)
                {
                    var centre = Vector3.zero;
                    foreach (var v in piece) centre += smr.transform.TransformPoint(verts[v]);
                    centre /= piece.Count;
                    if (centre.y <= neckY) continue;
                    foreach (var v in piece)
                    {
                        var w = weights[v];
                        if (w.boneIndex0 == head && w.weight0 >= 0.999f) continue;
                        weights[v] = new BoneWeight { boneIndex0 = head, weight0 = 1f };
                        changed++;
                    }
                }
            }
            if (changed > 0) mesh.boneWeights = weights;
            return changed;
        }

        /// <summary>
        /// Weights vertices from the bones: each bone is a segment (to the next bone down the chain; the head, hands and
        /// feet extended along their parent), and a vertex takes the nearest bones (up to 3, by inverse distance to the
        /// fourth power, normalised). A vertex on one side never takes the other side's limbs. With <paramref name="all"/>
        /// every vertex is weighted; otherwise only those skinned to Hips alone that are nearer another bone than the hips.
        /// Returns how many vertices changed.
        /// </summary>
        private static int RepairWeights(SkinnedMeshRenderer smr, bool all)
        {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            var index = new Dictionary<string, int>();
            for (var i = 0; i < bones.Length; i++) if (bones[i] != null) index[bones[i].name] = i;
            if (!index.ContainsKey("Hips")) return 0;
            var verts = mesh.vertices;
            var world = new Vector3[verts.Length];
            for (var i = 0; i < verts.Length; i++) world[i] = smr.transform.TransformPoint(verts[i]);
            var top = float.MinValue;
            foreach (var p in world) top = Mathf.Max(top, p.y);
            var segs = new List<(int bone, Vector3 a, Vector3 b, int side)>();
            foreach (var kv in index)
            {
                var name = kv.Key;
                if (!Next.ContainsKey(name) && name != "Head" && !name.StartsWith("Hand.") && !name.StartsWith("Foot.")) continue;
                var a = bones[kv.Value].position;
                Vector3 b;
                if (Next.TryGetValue(name, out var next) && index.ContainsKey(next)) b = bones[index[next]].position;
                else if (name == "Head") b = new Vector3(a.x, top, a.z);
                else
                {
                    var parent = bones[kv.Value].parent;
                    var dir = parent != null ? (a - parent.position) : Vector3.down;
                    if (name.StartsWith("Foot.")) dir = new Vector3(0f, 0f, dir.magnitude);
                    b = a + dir * 0.6f;
                }
                var side = name.EndsWith(".L") ? (a.x < 0f ? -1 : 1) : name.EndsWith(".R") ? (a.x < 0f ? -1 : 1) : 0;
                segs.Add((kv.Value, a, b, side));
            }
            var hips = index["Hips"];
            var centre = top * 0.02f;
            var weights = mesh.boneWeights;
            var changed = 0;
            for (var i = 0; i < world.Length; i++)
            {
                var p = world[i];
                var vSide = Mathf.Abs(p.x) < centre ? 0 : (p.x < 0f ? -1 : 1);
                var near = new List<(int bone, float d)>();
                foreach (var (bone, a, b, side) in segs)
                {
                    if (side != 0 && vSide != 0 && side != vSide) continue;
                    var ab = b - a;
                    var t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                    near.Add((bone, (a + ab * t - p).magnitude));
                }
                near.Sort((x, y) => x.d.CompareTo(y.d));
                if (near.Count == 0) continue;
                if (!all)
                {
                    var w0 = weights[i];
                    // Only above the waist (the spine bone): the belt and hips keep their delivered weights.
                    var stuck = w0.boneIndex0 == hips && w0.weight0 > 0.9f && (!index.ContainsKey("Spine") || p.y > bones[index["Spine"]].position.y);
                    if (!stuck || near[0].bone == hips) continue;
                }
                var take = Mathf.Min(3, near.Count);
                var ws = new float[take];
                var sum = 0f;
                for (var k = 0; k < take; k++)
                {
                    ws[k] = 1f / Mathf.Pow(near[k].d + top * 0.01f, 4f);
                    sum += ws[k];
                }
                var bw = new BoneWeight { boneIndex0 = near[0].bone, weight0 = ws[0] / sum };
                if (take > 1) { bw.boneIndex1 = near[1].bone; bw.weight1 = ws[1] / sum; }
                if (take > 2) { bw.boneIndex2 = near[2].bone; bw.weight2 = ws[2] / sum; }
                weights[i] = bw;
                changed++;
            }
            mesh.boneWeights = weights;
            return changed;
        }

        /// <summary>Batch: re-imports every delivered character so the renames apply.</summary>
        public static void ReimportAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CharacterInspector.Folder }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }
    }
}
