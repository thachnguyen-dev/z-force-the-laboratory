using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZombieGame.Art;
using ModelData = ZombieGame.Art.CharacterModelLibrary.ModelData;
using HoldData = ZombieGame.Art.CharacterModelLibrary.HoldData;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Bakes the owner's delivered characters for the game (owner, 2026-10-07: "Connect them"): each model exactly as
    /// the editor tools show and verify it (HoldPose.Spawn: scaled, skinning repaired at import, arms fitted, coloured
    /// and shaded), as a mesh on its own skeleton (Resources/Characters/{id}.asset), and the library
    /// (CharacterModelLibrary) with every skeleton and, for the playable characters, the approved firing poses read from
    /// the hold solver (the same locked poses the verification passes).
    /// </summary>
    public static partial class HoldPose
    {
        private const string BakeFolder = "Assets/Zombie/Resources/Characters";

        /// <summary>The playable characters: model, the game's character, its branch camouflage.</summary>
        private static readonly (string model, string character, string camo)[] Playable =
        {
            // Character 03 was removed from the roster (owner, 2026-10-03); its model is the only one left for the
            // Protagonist, in the Army camouflage the design gives the Protagonist (owner, 2026-10-07: it looked the same
            // as character 01's tan Air Force pattern in Space Force tan).
            ("chr_03", "protagonist", "camo_army"),
            ("chr_01", "character_01", "camo_airforce"),
            ("chr_girl", "character_02", "camo_marines"),
            ("chr_04", "character_04", "camo_navy"),
        };

        /// <summary>The rival soldiers: the baked id (also their character id), the delivered model, the camouflage.</summary>
        public static readonly (string id, string model, string camo)[] Rivals =
        {
            ("rival0_a", "chr_04", "camo_rival_red"), ("rival0_b", "chr_01", "camo_rival_red"),
            ("rival1_a", "chr_04", "camo_rival_orange"), ("rival1_b", "chr_01", "camo_rival_orange"),
        };

        /// <summary>The zombies: the baked id, the delivered model, whether it's the red-skinned fast variant.</summary>
        private static readonly (string id, string model, bool fast)[] Zombies =
        {
            ("zmb_1", "zmb_1", false), ("zmb_1_fast", "zmb_1", true),
            ("zmb_2", "zmb_2", false), ("zmb_2_fast", "zmb_2", true),
            ("boss_1", "boss_1", false), ("boss_bear", "boss_bear", false),
            ("zmb_dog", "zmb_dog", false), ("zmb_rat", "zmb_rat", false), ("boss_tiger", "boss_tiger", false),
        };

        [MenuItem("Zombie/Developer (Claude)/Bake Characters For The Game")]
        public static void BakeCharacters()
        {
            Directory.CreateDirectory(BakeFolder);
            var log = new StringBuilder();
            var models = new List<ModelData>();
            foreach (var (model, character, camo) in Playable)
            {
                var data = BakeModel(model, model, false, camo, log);
                if (data == null) continue;
                data.character = character;
                data.holds = BakeHolds(model, camo, data, log).ToArray();
                models.Add(data);
            }
            foreach (var (id, model, fast) in Zombies)
            {
                var data = BakeModel(id, model, fast, "", log);
                if (data != null) models.Add(data);
            }
            // Owner, 2026-10-11: the Vaccine stages' rival teams had no model (a placeholder capsule): soldiers in the two
            // masked models, in each team's own camouflage, with the firing poses.
            foreach (var (id, model, camo) in Rivals)
            {
                var data = BakeModel(id, model, false, camo, log);
                if (data == null) continue;
                data.character = id;
                data.holds = BakeHolds(model, camo, data, log).ToArray();
                models.Add(data);
            }
            // Owner, 2026-10-09: the rescue missions' scientist in the owner's model (no backpack, no gun poses: they
            // don't fight).
            {
                var data = BakeModel("npc_scientist", "npc_scientist", false, "", log);
                if (data != null)
                {
                    data.character = "hostage";
                    models.Add(data);
                }
            }
            var lib = ScriptableObject.CreateInstance<CharacterModelLibrary>();
            lib.models = models.ToArray();
            var path = BakeFolder + "/Library.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(lib, path);
            AssetDatabase.SaveAssets();
            CharacterModelLibrary.Reload();
            Debug.Log("[ZOMBIE-BAKE]\n" + log);
        }

        /// <summary>The rig's bones for a model: the named rig bones first (in the rig's order), then any others it has (fingers).</summary>
        private static List<Transform> RigOrder(Transform[] delivered, bool quad)
        {
            var names = quad ? System.Enum.GetNames(typeof(CharacterRig.Quad)) : System.Enum.GetNames(typeof(CharacterRig.Bone));
            var order = new List<Transform>();
            foreach (var n in names)
            {
                var b = delivered.FirstOrDefault(t => t != null && t.name == CharacterRig.Delivered(n));
                if (b == null) return null;
                order.Add(b);
            }
            foreach (var b in delivered)
                if (b != null && !order.Contains(b)) order.Add(b);
            return order;
        }

        private static ModelData BakeModel(string id, string model, bool fast, string camo, StringBuilder log)
        {
            FastVariant = fast;
            var camoTex = string.IsNullOrEmpty(camo) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + camo + ".png");
            var (go, smr) = Spawn(camoTex, new StringBuilder(), false, false, -1f, model);
            FastVariant = false;
            try
            {
                var quad = IsAnimal(model);
                var order = RigOrder(smr.bones, quad);
                if (order == null)
                {
                    log.AppendLine($"  {id}: SKIPPED, its bones don't match the rig");
                    return null;
                }
                // The model's space: the character at the origin, unscaled metres (Spawn scaled it to the game).
                var joints = order.Select(b => b.position - go.transform.position).ToArray();
                // Four-legged models face +z like everything else: the head ahead of the hips.
                var flip = quad && joints[(int)CharacterRig.Quad.Head].z < joints[0].z;
                Vector3 F(Vector3 p) => flip ? new Vector3(-p.x, p.y, -p.z) : p;
                joints = joints.Select(F).ToArray();
                var parents = order.Select(b =>
                {
                    var p = b.parent;
                    while (p != null && !order.Contains(p)) p = p.parent;
                    return p == null ? -1 : order.IndexOf(p);
                }).ToArray();
                var index = new Dictionary<int, int>();
                for (var i = 0; i < smr.bones.Length; i++) index[i] = smr.bones[i] != null ? order.IndexOf(smr.bones[i]) : 0;

                var src = smr.sharedMesh;
                var rest = new Mesh();
                smr.BakeMesh(rest, true);
                var verts = rest.vertices.Select(v => F(smr.transform.TransformPoint(v) - go.transform.position)).ToArray();
                var normals = rest.normals.Select(n => F(smr.transform.TransformDirection(n).normalized)).ToArray();
                Object.DestroyImmediate(rest);
                // Weights on the rig's bones (a delivered bone outside the rig rides its nearest rig ancestor).
                var weights = src.boneWeights.Select(w =>
                {
                    var sum = new Dictionary<int, float>();
                    void Add(int bi, float wt)
                    {
                        if (wt <= 0f) return;
                        var r = index.TryGetValue(bi, out var x) && x >= 0 ? x : 0;
                        sum[r] = (sum.TryGetValue(r, out var s) ? s : 0f) + wt;
                    }
                    Add(w.boneIndex0, w.weight0);
                    Add(w.boneIndex1, w.weight1);
                    Add(w.boneIndex2, w.weight2);
                    Add(w.boneIndex3, w.weight3);
                    var top = sum.OrderByDescending(k => k.Value).Take(4).ToList();
                    var total = top.Sum(k => k.Value);
                    var bw = new BoneWeight();
                    if (top.Count > 0) { bw.boneIndex0 = top[0].Key; bw.weight0 = top[0].Value / total; }
                    if (top.Count > 1) { bw.boneIndex1 = top[1].Key; bw.weight1 = top[1].Value / total; }
                    if (top.Count > 2) { bw.boneIndex2 = top[2].Key; bw.weight2 = top[2].Value / total; }
                    if (top.Count > 3) { bw.boneIndex3 = top[3].Key; bw.weight3 = top[3].Value / total; }
                    return bw;
                }).ToArray();

                var mesh = new Mesh { name = id, indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.vertices = verts;
                mesh.normals = normals;
                mesh.colors = src.colors;
                foreach (var ch in new[] { 1, 2, 3 })
                {
                    var uv = new List<Vector4>();
                    src.GetUVs(ch, uv);
                    if (uv.Count == verts.Length) mesh.SetUVs(ch, uv);
                }
                mesh.triangles = src.triangles;
                mesh.boneWeights = weights;
                mesh.bindposes = joints.Select(j => Matrix4x4.Translate(-j)).ToArray();
                mesh.RecalculateBounds();
                var path = $"{BakeFolder}/{id}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var height = verts.Max(v => v.y);
                log.AppendLine($"  {id}: {verts.Length} vertices, {mesh.triangles.Length / 3} triangles, {order.Count} bones, height {height:F2} m{(flip ? ", turned to face +z" : "")}");
                return new ModelData
                {
                    id = id, character = "", quadruped = quad, camo = camo, height = height,
                    bones = order.Select(b => b.name).ToArray(), parents = parents, joints = joints,
                };
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The firing poses of a playable character, read from the hold solver after it posed the delivered rig
        /// (locked poses: exactly what the verification passes): each bone's rotation in the model's space relative to
        /// its rest, the body's turn, the gun relative to the right hand. The SMG has no pose of its own yet: it takes
        /// the rifle's, its grip where the rifle's grip is.
        /// </summary>
        private static List<HoldData> BakeHolds(string model, string camo, ModelData data, StringBuilder log)
        {
            var holds = new List<HoldData>();
            foreach (var hold in new[] { Hold.Rifle, Hold.Shotgun, Hold.Sniper, Hold.Pistol })
            {
                var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
                try
                {
                    var order = RigOrder(smr.bones, false);
                    var restRot = order.Select(b => b.rotation).ToArray();
                    var (gunId, size, name) = Held(hold);
                    var gun = Gun(gunId, size);
                    LastNote = "";
                    Pose(smr.bones, gun, hold);
                    Blade(go, gun);
                    var bodyRot = go.transform.rotation;
                    var inv = Quaternion.Inverse(bodyRot);
                    var rotations = order.Select((b, i) => inv * b.rotation * Quaternion.Inverse(restRot[i])).ToArray();
                    var hand = order[(int)CharacterRig.Bone.HandR];
                    var handRot = inv * hand.rotation * Quaternion.Inverse(restRot[(int)CharacterRig.Bone.HandR]);
                    var handPos = inv * (hand.position - go.transform.position);
                    var gunPos = inv * (gun.transform.position - go.transform.position);
                    var gunRot = inv * gun.transform.rotation;
                    var h = new HoldData
                    {
                        weapon = gunId,
                        bodyTurn = bodyRot.eulerAngles.y,
                        rotations = rotations,
                        gunPosition = Quaternion.Inverse(handRot) * (gunPos - handPos),
                        gunRotation = Quaternion.Inverse(handRot) * gunRot,
                    };
                    holds.Add(h);
                    log.AppendLine($"    {model} {name}: body turned {Mathf.DeltaAngle(0f, h.bodyTurn):0}°; {LastNote}");
                    if (hold == Hold.Rifle)
                    {
                        // The SMG on the rifle's pose: its grip (the same point the game's hands use, Survivor.HeldGrips)
                        // where the rifle's grip is, pointing the same way.
                        Vector3 Grip(string id, float s) => Vector3.Scale(new Vector3(s, s * Survivor.Chunky, s * Survivor.Chunky),
                            new Vector3(-0.2f, PackArt.MuzzleOf(id).y - 0.12f, 0f));
                        var shift = h.gunRotation * (Grip("ar_gun", Survivor.RifleSize) - Grip("smg_gun", Survivor.SmgSize));
                        holds.Add(new HoldData { weapon = "smg_gun", bodyTurn = h.bodyTurn, rotations = rotations, gunPosition = h.gunPosition + shift, gunRotation = h.gunRotation });
                    }
                    Object.DestroyImmediate(gun);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            return holds;
        }
    }
}
