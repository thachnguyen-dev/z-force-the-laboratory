using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Dev tool for the owner's character models (2026-10-05): imports a delivered FBX into Art/Characters, reports what
    /// it holds (size, pivot, polygons, skeleton, weights, materials, textures, animations, whether Unity's Humanoid rig
    /// accepts it) and renders previews to logs/char-preview.
    /// </summary>
    public static class CharacterInspector
    {
        public const string Folder = "Assets/Zombie/Art/Characters";
        public const string Delivery = "D:/Game/assets for zombie";

        /// <summary>Batch: -executeMethod ...CharacterInspector.InspectAll (every FBX in Art/Characters).</summary>
        public static void InspectAll()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            foreach (var path in Directory.GetFiles(Folder, "*.fbx")) Inspect(path.Replace('\\', '/'), outDir);
        }

        /// <summary>Batch: the mesh's shape slice by slice (heights, widths, arm and leg spans) to place the joints.</summary>
        public static void Slices()
        {
            foreach (var path in Directory.GetFiles(Folder, "*.fbx"))
            {
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var mf = inst.GetComponentInChildren<MeshFilter>();
                var mesh = mf.sharedMesh;
                // In model space as the game places it (the FBX root may carry an axis turn and a scale).
                var toModel = mf.transform.localToWorldMatrix;
                Object.DestroyImmediate(inst);
                var v = mesh.vertices.Select(p => toModel.MultiplyPoint3x4(p)).ToArray();
                var s = new StringBuilder($"[ZOMBIE-CHAR-SLICES] {path}\n");
                for (var y = 0f; y < 1f; y += 0.02f)
                {
                    var inSlice = v.Where(p => p.y >= y && p.y < y + 0.02f).ToArray();
                    if (inSlice.Length == 0) continue;
                    // Centre column (|x| < 0.16) and the outer parts separately, so arms show apart from the body.
                    var centre = inSlice.Where(p => Mathf.Abs(p.x) < 0.16f).ToArray();
                    var left = inSlice.Where(p => p.x >= 0.16f).ToArray();
                    s.Append($"y {y:F2}: n {inSlice.Length} x [{inSlice.Min(p => p.x):F3},{inSlice.Max(p => p.x):F3}] z [{inSlice.Min(p => p.z):F3},{inSlice.Max(p => p.z):F3}]");
                    if (centre.Length > 0) s.Append($" centre z [{centre.Min(p => p.z):F3},{centre.Max(p => p.z):F3}]");
                    if (left.Length > 0) s.Append($" +x part x [{left.Min(p => p.x):F3},{left.Max(p => p.x):F3}]");
                    // Gap between the legs: is there an empty band around x = 0?
                    var nearMid = inSlice.Count(p => Mathf.Abs(p.x) < 0.015f);
                    s.Append($" mid {nearMid}\n");
                }
                Debug.Log(s.ToString());
            }
        }

        // ------------------------------------------------------------------ validation against the rig spec (docs 4.1, 4.2)

        /// <summary>The 49 bones of the rig spec (docs/3-assets-art-direction.md 4.2) and their parents (null = root).</summary>
        public static readonly (string bone, string parent)[] SpecBones = BuildSpec();

        private static (string, string)[] BuildSpec()
        {
            var list = new List<(string, string)>
            {
                ("Hips", null), ("Spine", "Hips"), ("Chest", "Spine"), ("Neck", "Chest"), ("Head", "Neck"),
            };
            foreach (var s in new[] { "L", "R" })
            {
                // Owner, 2026-10-06: shoulder (collarbone) bones kept: they keep the shoulders round when the arms reach for a gun.
                list.Add(($"Shoulder.{s}", "Chest"));
                list.Add(($"UpperArm.{s}", $"Shoulder.{s}"));
                list.Add(($"LowerArm.{s}", $"UpperArm.{s}"));
                list.Add(($"Hand.{s}", $"LowerArm.{s}"));
            }
            foreach (var s in new[] { "L", "R" })
            {
                list.Add(($"UpperLeg.{s}", "Hips"));
                list.Add(($"LowerLeg.{s}", $"UpperLeg.{s}"));
                list.Add(($"Foot.{s}", $"LowerLeg.{s}"));
            }
            foreach (var s in new[] { "L", "R" })
                foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    list.Add(($"{f}1.{s}", $"Hand.{s}"));
                    list.Add(($"{f}2.{s}", $"{f}1.{s}"));
                    list.Add(($"{f}3.{s}", $"{f}2.{s}"));
                }
            return list.ToArray();
        }

        /// <summary>Batch: checks every FBX in Art/Characters against the character requirements; report per file.</summary>
        public static void ValidateAll()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            foreach (var path in Directory.GetFiles(Folder, "*.fbx"))
            {
                var p = path.Replace('\\', '/');
                var report = Validate(p, outDir);
                File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(p) + "-validation.txt"), report);
                Debug.Log("[ZOMBIE-CHAR-CHECK]\n" + report);
            }
        }

        public static string Validate(string path, string outDir)
        {
            var r = new StringBuilder($"== {path}\n");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = Object.Instantiate(model);
            go.transform.position = Vector3.zero;
            var all = go.GetComponentsInChildren<Transform>(true);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            r.AppendLine($"objects: {smrs.Length} skinned mesh(es), {mfs.Length} static mesh(es), {all.Length} transforms");
            // Every transform name and its parent.
            r.AppendLine("transforms (name <- parent):");
            foreach (var t in all) r.AppendLine($"  {t.name} <- {(t.parent ? t.parent.name : "-")}  at {t.position:F3}");
            // Bones against the spec.
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var missing = new List<string>();
            var wrongParent = new List<string>();
            foreach (var (bone, parent) in SpecBones)
            {
                if (!byName.TryGetValue(bone, out var t)) { missing.Add(bone); continue; }
                if (parent == null) continue;
                var actual = t.parent ? t.parent.name : "-";
                if (actual != parent) wrongParent.Add($"{bone}: parent is '{actual}', should be '{parent}'");
            }
            r.AppendLine($"spec bones present: {SpecBones.Length - missing.Count}/{SpecBones.Length}");
            if (missing.Count > 0) r.AppendLine($"  MISSING: {string.Join(", ", missing)}");
            foreach (var w in wrongParent) r.AppendLine($"  PARENT: {w}");
            var specNames = new HashSet<string>(SpecBones.Select(b => b.bone));
            foreach (var smr in smrs)
            {
                var m = smr.sharedMesh;
                r.AppendLine($"skinned mesh '{smr.name}': {m.vertexCount} verts, {m.triangles.Length / 3} tris, submeshes {m.subMeshCount}, uv0 {m.uv.Length > 0}, colours {m.colors.Length > 0}, blendshapes {m.blendShapeCount}, root bone {(smr.rootBone ? smr.rootBone.name : "none")}");
                var extra = smr.bones.Where(b => b != null && !specNames.Contains(b.name)).Select(b => b.name).ToList();
                r.AppendLine($"  skin bones {smr.bones.Length}; not in the spec: {(extra.Count == 0 ? "none" : string.Join(", ", extra))}");
                // Weights.
                var bpv = m.GetBonesPerVertex();
                var weights = m.GetAllBoneWeights();
                var perBone = new int[smr.bones.Length];
                int unweighted = 0, maxInf = 0, badSum = 0, idx = 0;
                for (var v = 0; v < bpv.Length; v++)
                {
                    int n = bpv[v];
                    maxInf = Mathf.Max(maxInf, n);
                    if (n == 0) unweighted++;
                    var sum = 0f;
                    for (var k = 0; k < n; k++)
                    {
                        var w = weights[idx + k];
                        sum += w.weight;
                        if (w.weight > 0.05f) perBone[w.boneIndex]++;
                    }
                    if (n > 0 && Mathf.Abs(sum - 1f) > 0.02f) badSum++;
                    idx += n;
                }
                r.AppendLine($"  weights: unweighted verts {unweighted}, max bones per vertex {maxInf}, sums off by >2% {badSum}");
                var zero = new List<string>();
                for (var i = 0; i < smr.bones.Length; i++)
                    if (smr.bones[i] != null && perBone[i] == 0) zero.Add(smr.bones[i].name);
                r.AppendLine($"  bones with no vertices (weight > 5%): {(zero.Count == 0 ? "none" : string.Join(", ", zero))}");
                r.AppendLine($"  vertices per bone: {string.Join(", ", Enumerable.Range(0, smr.bones.Length).Where(i => smr.bones[i] != null).Select(i => smr.bones[i].name + " " + perBone[i]))}");
                // Materials (colour regions) with their triangle counts.
                for (var s = 0; s < m.subMeshCount; s++)
                {
                    var mat = s < smr.sharedMaterials.Length ? smr.sharedMaterials[s] : null;
                    var colour = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat != null && mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;
                    r.AppendLine($"  material {s}: '{(mat ? mat.name : "none")}' {m.GetTriangles(s).Length / 3} tris, colour {colour}, texture {(mat && mat.mainTexture ? mat.mainTexture.name : "none")}");
                }
            }
            // Size, pivot, facing, pose.
            var bounds = new Bounds();
            var first = true;
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = rend.bounds; first = false; }
                else bounds.Encapsulate(rend.bounds);
            }
            r.AppendLine($"bounds: size {bounds.size:F3} centre {bounds.center:F3} min {bounds.min:F3}");
            if (byName.TryGetValue("Hand.L", out var hl) && byName.TryGetValue("Hand.R", out var hr) && byName.TryGetValue("UpperArm.L", out var ul))
                r.AppendLine($"pose: Hand.L {hl.position:F3}, Hand.R {hr.position:F3}, UpperArm.L {ul.position:F3} (T-pose: hands level with the shoulders, out to the sides; character's left = +x when facing +z)");
            if (byName.TryGetValue("Foot.L", out var fl) && byName.TryGetValue("Head", out var hd))
                r.AppendLine($"Foot.L {fl.position:F3}, Head {hd.position:F3}");
            var name = Path.GetFileNameWithoutExtension(path);
            Render(go, bounds, Path.Combine(outDir, name + "-front.png"), 0f);
            // The skinned character alone, each material region in its own colour.
            if (smrs.Length > 0)
            {
                foreach (var rend in go.GetComponentsInChildren<Renderer>()) rend.enabled = rend is SkinnedMeshRenderer;
                var regionColours = new[] { Color.red, new Color(0.2f, 0.8f, 0.2f), new Color(0.2f, 0.4f, 1f), Color.yellow, Color.magenta, Color.cyan, new Color(1f, 0.5f, 0f), Color.white };
                foreach (var smr in smrs)
                {
                    var mats = new Material[smr.sharedMesh.subMeshCount];
                    for (var i = 0; i < mats.Length; i++)
                    {
                        mats[i] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        mats[i].SetColor("_BaseColor", regionColours[i % regionColours.Length]);
                    }
                    smr.sharedMaterials = mats;
                }
                // The real vertex extent (renderer bounds can be loose).
                var baked = new Mesh();
                smrs[0].BakeMesh(baked, true);
                var bv = baked.vertices.Select(v => smrs[0].transform.TransformPoint(v)).ToArray();
                r.AppendLine($"vertices: x {bv.Min(v => v.x):F3}..{bv.Max(v => v.x):F3}, y {bv.Min(v => v.y):F3}..{bv.Max(v => v.y):F3}, z {bv.Min(v => v.z):F3}..{bv.Max(v => v.z):F3}");
                Object.DestroyImmediate(baked);
                var only = smrs[0].bounds;
                foreach (var smr in smrs) only.Encapsulate(smr.bounds);
                r.AppendLine($"skinned character alone: size {only.size:F3} centre {only.center:F3} min {only.min:F3}");
                r.AppendLine("region colours in the -regions renders: " + string.Join(", ", Enumerable.Range(0, smrs[0].sharedMesh.subMeshCount).Select(i => $"{smrs[0].sharedMaterials[i].name.Replace(" (Instance)", "")}... material {i} = {new[] { "red", "green", "blue", "yellow", "magenta", "cyan", "orange", "white" }[i % 8]}")));
                Render(go, only, Path.Combine(outDir, name + "-regions-front.png"), 0f);
                Render(go, only, Path.Combine(outDir, name + "-regions-side.png"), 90f);
                Render(go, only, Path.Combine(outDir, name + "-regions-back.png"), 180f);
            }
            Object.DestroyImmediate(go);
            return r.ToString();
        }

        // ------------------------------------------------------------------ shoulder bones study (owner, 2026-10-06)

        /// <summary>
        /// Batch: does a character need its shoulder (clavicle) bones to hold a weapon without ugly deformation? Poses the
        /// delivered rig, with its own skin weights, in the shouldered rifle hold and the two-handed pistol hold, twice:
        /// shoulders left still (exactly what merging their weights into the chest would give) and shoulders turned
        /// forward with the arms (as real collarbones do), and renders both from several sides to logs/char-preview.
        /// </summary>
        public static void ShoulderStudy()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            foreach (var path in Directory.GetFiles(Folder, "*.fbx"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                foreach (var hold in new[] { "rifle", "pistol" })
                    foreach (var useShoulders in new[] { false, true })
                    {
                        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (smr == null) { Object.DestroyImmediate(go); continue; }
                        foreach (var rend in go.GetComponentsInChildren<Renderer>()) rend.enabled = rend == smr;
                        smr.updateWhenOffscreen = true;
                        var mats = new Material[smr.sharedMesh.subMeshCount];
                        for (var i = 0; i < mats.Length; i++)
                        {
                            mats[i] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            mats[i].SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.6f));
                        }
                        smr.sharedMaterials = mats;
                        // Game size: 2.2 m tall.
                        go.transform.localScale = Vector3.one * (2.2f / 0.998f);
                        Transform B(string n) => smr.bones.FirstOrDefault(b => b != null && (b.name == n || b.name.ToLowerInvariant() == n.ToLowerInvariant()));
                        var chest = B("chest");
                        var sR = B("shoulder.R");
                        var sL = B("shoulder.L");
                        var uR = B("upper_arm.R") ?? B("UpperArm.R");
                        var uL = B("upper_arm.L") ?? B("UpperArm.L");
                        var fR = B("forearm.R") ?? B("LowerArm.R");
                        var fL = B("forearm.L") ?? B("LowerArm.L");
                        var hR = B("hand.R") ?? B("Hand.R");
                        var hL = B("hand.L") ?? B("Hand.L");
                        // The character's right is -x here (it faces +z with its left hand at -x... check by the hands).
                        var rightIsPlusX = hR.position.x > hL.position.x;
                        var side = rightIsPlusX ? 1f : -1f;
                        var rs = uR.position;
                        var ls = uL.position;
                        Vector3 rightHand, leftHand;
                        GameObject gun;
                        if (hold == "rifle")
                        {
                            // Shouldered: stock in the right shoulder pocket, barrel level, forward.
                            gun = Weapon("ar_gun", 1.4f);
                            var stock = rs + new Vector3(-0.07f * side, -0.07f, 0.06f);
                            Place(gun, "ar_gun", stock, Quaternion.Euler(3f, -3f * side, 0f) * Vector3.forward, 0f);
                            rightHand = GunPoint(gun, "ar_gun", 0.32f, 0.12f);
                            leftHand = GunPoint(gun, "ar_gun", 0.62f, 0.07f);
                        }
                        else
                        {
                            // Isosceles: both arms out, the gun on the centre line.
                            gun = Weapon("pistol", 0.7f);
                            var grip = new Vector3(0.03f * side, rs.y - 0.1f, rs.z + 0.36f);
                            Place(gun, "pistol", grip, Vector3.forward, 0.2f, 0.2f);
                            rightHand = grip;
                            leftHand = grip + new Vector3(-0.05f * side, -0.03f, -0.01f);
                        }
                        if (useShoulders)
                        {
                            // Collarbones turn the shoulder joint forward (and slightly in and up) towards the hands.
                            Protract(sR, uR, rightHand, 0.35f);
                            Protract(sL, uL, leftHand, 0.35f);
                        }
                        TwoBone(uR, fR, hR, rightHand, new Vector3(side, -1f, -0.4f));
                        TwoBone(uL, fL, hL, leftHand, new Vector3(-side, -1f, -0.2f));
                        smr.sharedMesh.RecalculateBounds();
                        var bounds = new Bounds(new Vector3(0f, 1.15f, 0.25f), new Vector3(2f, 2.3f, 2f));
                        var tag = $"{name}-{hold}-{(useShoulders ? "with-shoulders" : "no-shoulders")}";
                        Render(go, bounds, Path.Combine(outDir, tag + "-front.png"), 25f);
                        Render(go, bounds, Path.Combine(outDir, tag + "-side.png"), 90f);
                        // Close-up of the shoulders from above and in front.
                        var close = new Bounds(new Vector3(0f, rs.y, rs.z + 0.15f), new Vector3(0.9f, 0.9f, 0.9f));
                        Render(go, close, Path.Combine(outDir, tag + "-closeup.png"), 20f);
                        RenderGameView(go, new Bounds(new Vector3(0f, 1.1f, 0.2f), new Vector3(2.2f, 2.2f, 2.2f)), Path.Combine(outDir, tag + "-game.png"));
                        Object.DestroyImmediate(gun);
                        Object.DestroyImmediate(go);
                    }
            }
        }

        private static GameObject Weapon(string id, float size)
        {
            var gun = new GameObject(id);
            gun.AddComponent<MeshFilter>().sharedMesh = ZombieGame.Art.PackArt.Model(id);
            gun.AddComponent<MeshRenderer>().sharedMaterial = ZombieGame.Art.ArtMaterials.Stylized;
            gun.transform.localScale = new Vector3(size, size * Survivor.Chunky, size * Survivor.Chunky);
            return gun;
        }

        /// <summary>Puts the weapon's back end (or the point t along it, drop below the bore) at <paramref name="at"/>, pointing along <paramref name="dir"/>.</summary>
        private static void Place(GameObject gun, string id, Vector3 at, Vector3 dir, float t, float drop = 0f)
        {
            var mesh = ZombieGame.Art.PackArt.Model(id);
            var bore = ZombieGame.Art.PackArt.MuzzleOf(id).y;
            var local = new Vector3(mesh.bounds.min.x + t * mesh.bounds.size.x, bore - drop, 0f);
            var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            gun.transform.rotation = rot;
            gun.transform.position = at - rot * Vector3.Scale(gun.transform.localScale, local);
        }

        private static Vector3 GunPoint(GameObject gun, string id, float t, float drop)
        {
            var mesh = ZombieGame.Art.PackArt.Model(id);
            var bore = ZombieGame.Art.PackArt.MuzzleOf(id).y;
            return gun.transform.TransformPoint(new Vector3(mesh.bounds.min.x + t * mesh.bounds.size.x, bore - drop, 0f));
        }

        /// <summary>Turns a bone so its child points at <paramref name="target"/> (works whatever the bone's rest rotation).</summary>
        private static void Aim(Transform bone, Transform child, Vector3 target)
        {
            var current = child.position - bone.position;
            var wanted = target - bone.position;
            if (current.sqrMagnitude < 1e-8f || wanted.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(current, wanted) * bone.rotation;
        }

        private static void TwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            var a = (lower.position - upper.position).magnitude;
            var b = (hand.position - lower.position).magnitude;
            var s = upper.position;
            var d = target - s;
            var dist = Mathf.Clamp(d.magnitude, 0.01f, (a + b) * 0.999f);
            var dir = d.normalized;
            var angle = Mathf.Acos(Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f)) * Mathf.Rad2Deg;
            var normal = Vector3.Cross(dir, pole).normalized;
            var e1 = Quaternion.AngleAxis(-angle, normal) * dir;
            var e2 = Quaternion.AngleAxis(angle, normal) * dir;
            var elbow = s + (Vector3.Dot(e1, pole) >= Vector3.Dot(e2, pole) ? e1 : e2) * a;
            Aim(upper, lower, elbow);
            Aim(lower, hand, s + dir * dist);
        }

        /// <summary>The collarbone turns part of the way from its rest direction towards the hand's target.</summary>
        private static void Protract(Transform shoulder, Transform upperArm, Vector3 target, float share)
        {
            if (shoulder == null) return;
            var current = upperArm.position - shoulder.position;
            var towards = (target - shoulder.position).normalized * current.magnitude;
            var turn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, towards), share);
            shoulder.rotation = turn * shoulder.rotation;
        }

        private static void RenderGameView(GameObject go, Bounds b, string file)
        {
            var camGo = new GameObject("Preview camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = b.extents.y * 1.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.36f, 0.25f);
            // The game camera looks north at 60°; the character faces the camera (south) here.
            camGo.transform.rotation = Quaternion.Euler(CameraRig.Tilt, 180f, 0f);
            camGo.transform.position = b.center - camGo.transform.forward * 50f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(55f, 150f, 0f);
            Shoot(cam, file, 384);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
        }

        /// <summary>Batch: the model's connected pieces (is gear separate from the body?) and the back surface by height.</summary>
        public static void Pieces()
        {
            foreach (var path in Directory.GetFiles(Folder, "*.fbx"))
            {
                var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var mf = inst.GetComponentInChildren<MeshFilter>();
                var toModel = mf.transform.localToWorldMatrix;
                var temp = Object.Instantiate(mf.sharedMesh);
                temp.vertices = mf.sharedMesh.vertices.Select(p => toModel.MultiplyPoint3x4(p)).ToArray();
                Object.DestroyImmediate(inst);
                var parts = PackParts.Split(temp);
                var s = new StringBuilder($"[ZOMBIE-CHAR-PIECES] {path}: {parts.Count} pieces\n");
                for (var i = 0; i < Mathf.Min(30, parts.Count); i++)
                    s.Append($"  {i}: tris {parts[i].Triangles.Count} area {parts[i].Area:F4} centre {parts[i].Bounds.center:F3} size {parts[i].Bounds.size:F3}\n");
                var v = temp.vertices;
                for (var y = 0.3f; y < 0.62f; y += 0.03f)
                {
                    var band = v.Where(p => p.y >= y && p.y < y + 0.03f && Mathf.Abs(p.x) < 0.12f).ToArray();
                    if (band.Length > 0) s.Append($"  back y {y:F2}: min z {band.Min(p => p.z):F3} (|x|<0.12)\n");
                }
                Debug.Log(s.ToString());
            }
        }

        public static void Inspect(string path, string outDir)
        {
            var r = new StringBuilder();
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            r.AppendLine($"== {path}");
            r.AppendLine($"import: animationType {importer.animationType}, fileScale {importer.fileScale}, useFileUnits {importer.useFileUnits}, globalScale {importer.globalScale}, bakeAxis {importer.bakeAxisConversion}");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = Object.Instantiate(model);
            go.transform.position = Vector3.zero;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = rend.bounds; first = false; }
                else bounds.Encapsulate(rend.bounds);
            }
            r.AppendLine($"bounds: size {bounds.size:F3} centre {bounds.center:F3} min {bounds.min:F3} (pivot at feet if min.y≈0 and centre x,z≈0)");
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = smr.sharedMesh;
                var maxPerVertex = 0;
                var bpv = m.GetBonesPerVertex();
                for (var i = 0; i < bpv.Length; i++) maxPerVertex = Mathf.Max(maxPerVertex, bpv[i]);
                var used = new HashSet<int>();
                foreach (var w in m.GetAllBoneWeights()) if (w.weight > 0.001f) used.Add(w.boneIndex);
                r.AppendLine($"skinned mesh '{smr.name}': {m.vertexCount} verts, {m.triangles.Length / 3} tris, submeshes {m.subMeshCount}, uv0 {m.uv.Length > 0}, uv1 {m.uv2.Length > 0}, colours {m.colors.Length > 0}, normals {m.normals.Length > 0}, tangents {m.tangents.Length > 0}, blendshapes {m.blendShapeCount}");
                r.AppendLine($"  bones {smr.bones.Length} (weighted {used.Count}), max weights per vertex {maxPerVertex}, root bone {(smr.rootBone ? smr.rootBone.name : "none")}");
                r.AppendLine($"  bones: {string.Join(", ", smr.bones.Select(b => b.name))}");
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat == null) { r.AppendLine("  material: (none)"); continue; }
                    var tex = mat.mainTexture;
                    r.AppendLine($"  material '{mat.name}' shader {mat.shader.name} colour {(mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear)} texture {(tex ? tex.name + " " + tex.width + "x" + tex.height : "none")}");
                    foreach (var prop in mat.GetTexturePropertyNames())
                    {
                        var t = mat.GetTexture(prop);
                        if (t != null) r.AppendLine($"    {prop}: {t.name} {t.width}x{t.height}");
                    }
                }
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                r.AppendLine($"static mesh '{mf.name}': {mf.sharedMesh.vertexCount} verts, {mf.sharedMesh.triangles.Length / 3} tris");
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            r.AppendLine($"embedded: {string.Join(", ", assets.Where(a => a != null && !(a is Transform) && !(a is GameObject)).Select(a => a.GetType().Name + " " + a.name))}");
            foreach (var clip in assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
                r.AppendLine($"animation '{clip.name}': {clip.length:F2} s, {clip.frameRate} fps, legacy {clip.legacy}");
            // Hierarchy with positions (bind pose).
            r.AppendLine("hierarchy:");
            void Walk(Transform t, int depth)
            {
                r.AppendLine($"{new string(' ', 2 + depth * 2)}{t.name} pos {t.position:F3} rot {t.rotation.eulerAngles:F0}");
                foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(go.transform, 0);
            var name = Path.GetFileNameWithoutExtension(path);
            Render(go, bounds, Path.Combine(outDir, name + "-front.png"), 0f);
            Render(go, bounds, Path.Combine(outDir, name + "-side.png"), 90f);
            Render(go, bounds, Path.Combine(outDir, name + "-back.png"), 180f);
            RenderGame(go, bounds, Path.Combine(outDir, name + "-game.png"));
            Object.DestroyImmediate(go);
            // Humanoid: does Unity's auto-mapping accept the skeleton?
            var human = HumanCheck(path);
            r.AppendLine(human);
            File.WriteAllText(Path.Combine(outDir, name + "-report.txt"), r.ToString());
            Debug.Log("[ZOMBIE-CHAR]\n" + r);
        }

        /// <summary>Re-imports a copy as Humanoid and reports the avatar Unity builds.</summary>
        private static string HumanCheck(string path)
        {
            var copy = Path.Combine(Path.GetDirectoryName(path), "_humancheck_" + Path.GetFileName(path)).Replace('\\', '/');
            AssetDatabase.CopyAsset(path, copy);
            var imp = (ModelImporter)AssetImporter.GetAtPath(copy);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(copy).OfType<Avatar>().FirstOrDefault();
            var s = new StringBuilder();
            s.Append($"humanoid auto-map: avatar {(avatar ? "built" : "none")}, valid {(avatar && avatar.isValid)}, human {(avatar && avatar.isHuman)}");
            if (avatar && avatar.isHuman)
            {
                var mapped = avatar.humanDescription.human.Select(h => $"{h.humanName}={h.boneName}");
                s.Append($"\n  mapped: {string.Join(", ", mapped)}");
            }
            AssetDatabase.DeleteAsset(copy);
            return s.ToString();
        }

        private static void Render(GameObject go, Bounds b, string file, float yaw)
        {
            var camGo = new GameObject("Preview camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = b.extents.y * 1.15f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f);
            // Yaw 0 looks at the model's front if it faces +Z (the camera stands on +Z looking back at it).
            camGo.transform.rotation = Quaternion.Euler(5f, 180f + yaw, 0f);
            camGo.transform.position = b.center - camGo.transform.forward * 50f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 200f;
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(40f, 150f + yaw, 0f);
            Shoot(cam, file, 512);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
        }

        private static void RenderGame(GameObject go, Bounds b, string file)
        {
            var camGo = new GameObject("Preview camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = b.extents.y * 1.3f;
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
            Shoot(cam, file, 384);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(light.gameObject);
        }

        private static void Shoot(Camera cam, string file, int size)
        {
            var rt = new RenderTexture(size, size, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            t.Apply();
            File.WriteAllBytes(file, t.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(t);
            Object.DestroyImmediate(rt);
        }
    }
}
