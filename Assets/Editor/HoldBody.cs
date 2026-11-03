using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ZombieGame.Art;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Full-body weapon holds (owner, 2026-10-06: "rotate the entire character body to make the weapon hold physically
    /// work"; no part of an arm may disappear into the body, still or animated). The whole rig takes part: the upper body
    /// turns and leans into the gun (spine and chest), the collarbones roll forward, the head keeps facing the target,
    /// and the arms are solved for each body pose. A hold is accepted only after it is verified on the real skinned mesh:
    /// no vertex of either arm or hand inside the body, and the gun clear of the body, still and under test motions.
    /// Owner, 2026-10-06 (option 1): while a long gun is held, aiming turns the whole character (the feet step round),
    /// not the waist; on top of the hold an animation adds walking, lean, recoil and at most ±5° of sway.
    /// </summary>
    public static partial class HoldPose
    {
        /// <summary>The body's share of a hold: upper-body turn (support shoulder forward), forward lean, shoulder roll.</summary>
        private struct BodyPose
        {
            public float Twist, Lean, Roll;
            public override string ToString() => $"torso turned {Twist:0}°, leaning {Lean:0}°, shoulders rolled forward {Roll:0}°";
        }

        private static Transform Bone(Transform[] bones, string name) => bones.FirstOrDefault(b => b != null && b.name == name);

        /// <summary>
        /// Poses the body (bones in their rest pose): the turn and the lean are shared by the spine and the chest, the
        /// neck turns the head back to the target, and each collarbone rolls its shoulder forward.
        /// </summary>
        private static void ApplyBody(Transform[] bones, BodyPose b)
        {
            var spine = Bone(bones, "Spine");
            var chest = Bone(bones, "Chest");
            var neck = Bone(bones, "Neck");
            var chestRest = chest.rotation;
            var half = Quaternion.AngleAxis(b.Twist * 0.5f, Vector3.up) * Quaternion.AngleAxis(b.Lean * 0.5f, Vector3.right);
            spine.rotation = half * spine.rotation;
            chest.rotation = half * chest.rotation;
            neck.rotation = Quaternion.Inverse(half * half) * neck.rotation;
            // Turning about the vertical: the left collarbone brings its shoulder forward with +, the right with -.
            var sl = Bone(bones, "Shoulder.L");
            var sr = Bone(bones, "Shoulder.R");
            if (sl != null) sl.rotation = Quaternion.AngleAxis(b.Roll, Vector3.up) * sl.rotation;
            if (sr != null) sr.rotation = Quaternion.AngleAxis(-b.Roll, Vector3.up) * sr.rotation;
            _waist = spine.position;
            _upperRot = chest.rotation * Quaternion.Inverse(chestRest);
        }

        /// <summary>How deep (metres) an arm vertex may sit inside the body: skin contact, not a limb going in.</summary>
        private const float DepthLimit = 0.005f;
        /// <summary>The deepest arm vertex found by the last verification (metres).</summary>
        private static float MaxDepth;

        /// <summary>Bakes the posed body (the T-pose's body part only, no arms) into <see cref="_bodyCol"/>.</summary>
        private static void BuildBodyCollider(SkinnedMeshRenderer smr, int[] part)
        {
            DropBodyCollider();
            var posed = new Mesh();
            smr.BakeMesh(posed, true);
            var tris = posed.triangles;
            var keep = new List<int>();
            for (var t = 0; t < tris.Length; t += 3)
                if (part[tris[t]] == 0 && part[tris[t + 1]] == 0 && part[tris[t + 2]] == 0) keep.AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(posed.vertices);
            m.SetTriangles(keep, 0);
            Object.DestroyImmediate(posed);
            var holder = new GameObject("body-search") { layer = BodyLayer };
            holder.transform.SetParent(smr.transform, false);
            _bodyCol = holder.AddComponent<MeshCollider>();
            _bodyCol.sharedMesh = m;
            Physics.SyncTransforms();
        }

        private static void DropBodyCollider()
        {
            if (_bodyCol == null) return;
            var m = _bodyCol.sharedMesh;
            Object.DestroyImmediate(_bodyCol.gameObject);
            Object.DestroyImmediate(m);
            _bodyCol = null;
        }

        /// <summary>Diagnosis: on failure, leave the most natural failing candidate posed instead of the rest pose.</summary>
        public static bool KeepFailed;

        /// <summary>Batch diagnosis: characters 03 and 04, rifle and pistol, the best failing candidate rendered from the side and front.</summary>
        public static void DiagnoseFailed()
        {
            KeepFailed = true;
            foreach (var model in new[] { "chr_03" })
            foreach (var hold in new[] { Hold.Rifle, Hold.Shotgun })
            {
                var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
                var (id, size, name) = Held(hold);
                var gun = Gun(id, size);
                Pose(smr.bones, gun, hold);
                Diag.Clear();
                // The in-game camera only (owner, 2026-10-07).
                go.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                gun.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                RenderGame(System.IO.Path.Combine(OutDir, $"diag-{model}-{name}-FAILED-game.png"));
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
            KeepFailed = false;
        }

        /// <summary>What a hold verification found.</summary>
        public static string LastVerify = "";

        /// <summary>
        /// The long-gun pose (owner, 2026-10-06: firing poses only): the firing pose from the reference "Chibi
        /// characters" (ShinobiZor, Sketchfab, the aiming figure).
        /// </summary>
        public enum HoldMode { Fire }
        public static HoldMode Mode = HoldMode.Fire;

        /// <summary>
        /// Character 02's firing poses, locked (owner, 2026-10-06): the body pose and the exact placement the search
        /// found, re-applied as they are, so no later change to the solver can alter them.
        /// </summary>
        private static readonly Dictionary<string, (BodyPose body, (Vector3 d, float yaw, float pitch, int part, float x) at)> Locked =
            new Dictionary<string, (BodyPose, (Vector3, float, float, int, float))>
            {
                ["chr_girl/rifle/fire"] = (new BodyPose { Twist = 40f, Lean = 15f, Roll = 20f }, (new Vector3(-0.010f, -0.035f, 0.353f), -7.5f, 2.5f, 0, 0.110f)),
                ["chr_girl/shotgun/fire"] = (new BodyPose { Twist = 50f, Lean = 10f, Roll = 20f }, (new Vector3(0.000f, 0.000f, 0.408f), -3.75f, -1f, 0, 0.230f)),
                ["chr_girl/sniper/fire"] = (new BodyPose { Twist = 40f, Lean = 15f, Roll = 20f }, (new Vector3(-0.020f, 0.038f, 0.395f), 0f, -1f, 1, 0.080f)),
                // Character 04 (2026-10-07), verified clean.
                ["chr_04/shotgun/fire"] = (new BodyPose { Twist = 45f, Lean = 25f, Roll = 20f }, (new Vector3(0.030f, -0.080f, 0.420f), -10f, -4.5f, 0, 0.230f)),   // 2026-10-07: higher, clear of the legs when running
                ["chr_04/rifle/fire"] = (new BodyPose { Twist = 45f, Lean = 25f, Roll = 20f }, (new Vector3(0.030f, -0.080f, 0.500f), -10f, -4.5f, 0, 0.110f)),
                // chr_01 (2026-10-07, after its sleeve was re-weighted): the left armpit up to 15 mm into the chest, accepted
                // by the owner 2026-10-07 (see AcceptedPadding).
                ["chr_01/rifle/fire"] = (new BodyPose { Twist = 55f, Lean = 20f, Roll = 10f }, (new Vector3(0.090f, -0.080f, 0.340f), -10f, -4.5f, 0, 0.110f)),
                ["chr_01/shotgun/fire"] = (new BodyPose { Twist = 55f, Lean = 20f, Roll = 10f }, (new Vector3(0.130f, -0.080f, 0.340f), -10f, -4.5f, 0, 0.230f)),
                ["chr_01/sniper/fire"] = (new BodyPose { Twist = 55f, Lean = 20f, Roll = 10f }, (new Vector3(0.130f, -0.080f, 0.340f), -10f, -4.5f, 0, 0.140f)),
                // Owner-approved 2026-10-07: the upper arms rest on the padded vest (see AcceptedPadding).
                ["chr_03/rifle/fire"] = (new BodyPose { Twist = 50f, Lean = 20f, Roll = 20f }, (new Vector3(0.050f, -0.050f, 0.640f), -10f, -4.5f, 0, 0.200f)),
                ["chr_03/shotgun/fire"] = (new BodyPose { Twist = 45f, Lean = 20f, Roll = 10f }, (new Vector3(0.050f, -0.020f, 0.600f), 0f, -4.5f, 0, 0.300f)),
                ["chr_03/sniper/fire"] = (new BodyPose { Twist = 40f, Lean = 20f, Roll = 20f }, (new Vector3(0.030f, -0.050f, 0.600f), -10f, -4.5f, 1, 0.030f)),
                ["chr_04/sniper/fire"] = (new BodyPose { Twist = 45f, Lean = 25f, Roll = 20f }, (new Vector3(0.030f, -0.080f, 0.500f), -10f, -4.5f, 0, 0.140f)),
            };

        /// <summary>
        /// The firing pose (reference: ShinobiZor's aiming figure): the gun raised level at chin height, pointing at the target, the stock under the
        /// face; the firing hand on the grip under the chin, the support hand forward on the front of the gun, both
        /// forearms roughly level in front of the chest, elbows down; the body turned side-on (30-40°), leaning in.
        /// </summary>
        public static readonly HoldTarget FireTarget = new HoldTarget
        {
            Name = "fire", D = new Vector3(-0.05f, 0.06f, 0.32f), Span = new Vector3(0.12f, 0.06f, 0.14f),
            Yaw = 0f, YawSpan = 10f, Pitch = 0f, PitchSpan = 6f,
            Twists = new[] { 20f, 30f, 40f, 50f, 60f }, Leans = new[] { 10f, 15f, 20f, 25f, 30f }, Rolls = new[] { 10f, 20f },
        };

        /// <summary>
        /// The firing pose for characters other than character 02 (whose poses are locked): the same reference shape, but
        /// the gun may sit from belly to chin height and further forward, for bodies whose head, hood or chest leave no
        /// room close in at chin height (characters 03 and 04). The cost still pulls it towards the reference's height.
        /// </summary>
        public static readonly HoldTarget WideFireTarget = new HoldTarget
        {
            Name = "fire", D = new Vector3(-0.05f, -0.08f, 0.38f), Span = new Vector3(0.14f, 0.26f, 0.2f),
            Yaw = 0f, YawSpan = 10f, Pitch = 0f, PitchSpan = 6f,
            Twists = new[] { 20f, 30f, 40f, 50f, 60f }, Leans = new[] { 10f, 15f, 20f, 25f, 30f }, Rolls = new[] { 10f, 20f },
        };

        private static HoldTarget FireTargetFor(string model) => model == "chr_girl" ? FireTarget : WideFireTarget;

        /// <summary>
        /// The full-body long-gun hold: tries body poses, solves the gun and the arms for each (coarsely), then solves
        /// the most natural ones finely and keeps the first that passes <see cref="Verify"/>.
        /// </summary>
        private static bool FullBodyHold(Transform[] bones, HandRig right, HandRig left, GameObject gun, LongGunSpec g, out string note)
        {
            var smr = bones[0].root.GetComponentInChildren<SkinnedMeshRenderer>();
            var rest = bones.Select(b => b != null ? b.localRotation : Quaternion.identity).ToArray();
            void Reset()
            {
                for (var i = 0; i < bones.Length; i++)
                    if (bones[i] != null) bones[i].localRotation = rest[i];
                _upperRot = Quaternion.identity;
            }
            var key = smr.gameObject.name + "/" + g.Name;
            // A locked firing pose: its stored body pose and placement.
            if (Locked.TryGetValue(key + "/fire", out var lk))
            {
                var lparts = ArmParts(smr);
                ApplyBody(bones, lk.body);
                BuildBodyCollider(smr, lparts.part);
                FixedPlacement = lk.at;
                var placed = UnderarmLong(right, left, gun, g, true, out var lnote);
                FixedPlacement = null;
                DropBodyCollider();
                var (lok, lrep) = Verify(smr, gun, g, lparts);
                LastVerify = $"    locked: {(lok ? "PASSES" : "fails")}: {lrep}\n";
                note = $"{lnote}; {lk.body}; locked";
                return placed && lok;
            }
            Target = FireTargetFor(smr.gameObject.name);
            var parts = ArmParts(smr);
            var tried = new List<(float score, BodyPose body)>();
            // The body poses: the reference's, or (no reference) square-ish with a small turn, a slight lean, shoulders a
            // little forward.
            var twists = Target?.Twists ?? new[] { 0f, 10f, 20f, 30f };
            var leans = Target?.Leans ?? new[] { 0f, 5f, 10f };
            var rolls = Target?.Rolls ?? new[] { 0f, 10f, 20f };
            foreach (var twist in twists)
            foreach (var lean in leans)
            foreach (var roll in rolls)
            {
                Reset();
                var body = new BodyPose { Twist = twist, Lean = lean, Roll = roll };
                ApplyBody(bones, body);
                BuildBodyCollider(smr, parts.part);
                var ok = UnderarmLong(right, left, gun, g, false, out var why, true);
                if (!ok && twist == twists[0] && lean == leans[0] && roll == rolls[0]) Debug.Log($"[ZOMBIE-WHY] {Mode} {g.Name} {body}: {why}");
                if (ok)
                    tried.Add((LastHoldScore + twist * 0.3f + lean * 0.4f + roll * 0.2f, body));
            }
            DropBodyCollider();
            var log = new StringBuilder();
            foreach (var (_, body) in tried.OrderBy(t => t.score))
            {
                Reset();
                ApplyBody(bones, body);
                BuildBodyCollider(smr, parts.part);
                var solved = UnderarmLong(right, left, gun, g, true, out var hold);
                DropBodyCollider();
                if (!solved) continue;
                var (ok, report) = Verify(smr, gun, g, parts);
                log.AppendLine($"    {body}: {(ok ? "PASSES" : "fails")}: {report}");
                if (!ok) continue;
                note = $"{hold}; {body}";
                LastVerify = log.ToString();
                return true;
            }
            DropBodyCollider();
            Reset();
            if (KeepFailed && tried.Count > 0)
            {
                // Diagnosis: leave the most natural failing candidate posed, to look at.
                ApplyBody(bones, tried.OrderBy(t => t.score).First().body);
                BuildBodyCollider(smr, parts.part);
                UnderarmLong(right, left, gun, g, true, out _);
                DropBodyCollider();
            }
            note = $"{g.Name}: no clean full-body hold ({tried.Count} of {twists.Length * leans.Length * rolls.Length} body poses gave a reachable placement)";
            LastVerify = log.ToString();
            return false;
        }

/// <summary>
        /// The pistol hold on the whole body (owner, 2026-10-07: both hands, both arms extended as far forward as
        /// possible): the isosceles, the arms reaching 97 % of their length if that passes <see cref="Verify"/>, less only
        /// as far as needed; the torso square to the target, small leans and shoulder rolls (rolled shoulders reach
        /// further forward). Keeps the first that passes.
        /// </summary>
        private static void FullBodyPistol(Transform[] bones, HandRig right, HandRig left, GameObject gun)
        {
            var smr = bones[0].root.GetComponentInChildren<SkinnedMeshRenderer>();
            var rest = bones.Select(b => b != null ? b.localRotation : Quaternion.identity).ToArray();
            var parts = ArmParts(smr);
            var log = new StringBuilder();
            foreach (var extension in new[] { 0.97f, 0.95f, 0.93f })
            foreach (var roll in new[] { 20f, 10f, 0f })
            foreach (var lean in new[] { 0f, 5f, 10f, 15f })
            {
                for (var i = 0; i < bones.Length; i++)
                    if (bones[i] != null) bones[i].localRotation = rest[i];
                _upperRot = Quaternion.identity;
                var body = new BodyPose { Twist = 0f, Lean = lean, Roll = roll };
                ApplyBody(bones, body);
                PistolArms(right, left, gun, extension);
                var (ok, report) = Verify(smr, gun, null, parts);
                log.AppendLine($"    {body}, arms {extension * 100f:0} %: {(ok ? "PASSES" : "fails")}: {report}");
                if (!ok) continue;
                LastNote = $"pistol: two hands, arms {extension * 100f:0} % extended; {body}";
                LastVerify = log.ToString();
                return;
            }
            LastNote = "pistol: no clean full-body hold";
            LastVerify = log.ToString();
        }

        /// <summary>Each vertex's part from the T-pose shape: 0 body, 1 upper arm, 2 forearm, 3 hand, 4 the shoulder seam; and its side.</summary>
        /// <summary>Diagnosis: for upper-arm vertices found too deep, how far along the upper arm they are (0 the shoulder joint, 1 the elbow).</summary>
        public static float DeepAlongMin = 1f, DeepAlongMax = 0f;
        private static float[] _along;

        /// <summary>
        /// Owner, 2026-10-07: character 04's hood drapes over its shoulders; the hood covering the top of the upper arm is
        /// clothing, not the arm going into the body. Per vertex: part of such a hood (skinned mainly to the head or neck).
        /// </summary>
        private static bool[] _hood = new bool[0];
        private static readonly HashSet<string> HoodModels = new HashSet<string> { "chr_04" };

        private static (int[] part, int[] side) ArmParts(SkinnedMeshRenderer smr)
        {
            var bones = smr.bones;
            {
                var hw = smr.sharedMesh.boneWeights;
                var hoodModel = HoodModels.Contains(smr.gameObject.name);
                _hood = hw.Select(bw => hoodModel && bones[bw.boneIndex0] != null && (bones[bw.boneIndex0].name == "Head" || bones[bw.boneIndex0].name == "Neck")).ToArray();
            }
            var sh = Mathf.Abs(Bone(bones, "UpperArm.R").position.x);
            var el = Mathf.Abs(Bone(bones, "LowerArm.R").position.x);
            var wr = Mathf.Abs(Bone(bones, "Hand.R").position.x);
            var seam = sh + (el - sh) * 0.3f;
            var m = new Mesh();
            smr.BakeMesh(m, true);
            var v = m.vertices.Select(p => smr.transform.TransformPoint(p)).ToArray();
            Object.DestroyImmediate(m);
            _along = v.Select(p => (Mathf.Abs(p.x) - sh) / (el - sh)).ToArray();
            // Between the shoulder joints is the body, except skin that belongs to an arm bone (a bulky shoulder cap
            // skinned to the upper arm moves with the arm): that counts as the shoulder seam, neither body nor checked.
            var w = smr.sharedMesh.boneWeights;
            var armBone = smr.bones.Select(b => b != null && (b.name.StartsWith("UpperArm") || b.name.StartsWith("LowerArm") || b.name.StartsWith("Hand") || b.name.StartsWith("Thumb") || b.name.StartsWith("Index") || b.name.StartsWith("Middle") || b.name.StartsWith("Ring") || b.name.StartsWith("Little"))).ToArray();
            var part = v.Select((p, i) =>
            {
                var x = Mathf.Abs(p.x);
                if (x <= sh) return armBone[w[i].boneIndex0] ? 4 : 0;
                // Beyond the shoulder joints only skin on an arm bone is arm (character 04's swords and character 03's
                // belly reach out there too: they are body).
                if (!armBone[w[i].boneIndex0]) return 0;
                return x < seam ? 4 : x < el ? 1 : x < wr ? 2 : 3;
            }).ToArray();
            return (part, v.Select(p => p.x >= 0f ? 1 : -1).ToArray());
        }

        /// <summary>
        /// Verifies a posed hold on the real skinned mesh: how many vertices of each arm part (upper arm past the seam,
        /// forearm, hand) are inside the body, and how much of the gun is inside the body; still, then under test motions
        /// an animation would add (spine twist ±20°, lean ±10°, a ±25° walking leg swing, walk with twist), the gun held in
        /// the right hand. Passes only with none anywhere.
        /// </summary>
        /// <summary>
        /// Owner, 2026-10-07: character 03's upper arms may rest against its padded vest (rifle and shotgun firing), and
        /// chr_01's left armpit cloth may press into its chest (rifle, shotgun and sniper firing, up to 2 cm): only
        /// upper-arm vertices, no deeper than this; forearms, hands and the gun still may not go in at all.
        /// </summary>
        private static readonly Dictionary<string, float> AcceptedPadding = new Dictionary<string, float> { ["chr_03"] = 0.03f, ["chr_01"] = 0.02f };

        private static (bool ok, string report) Verify(SkinnedMeshRenderer smr, GameObject gun, LongGunSpec g, (int[] part, int[] side) parts)
        {
            var padding = AcceptedPadding.TryGetValue(smr.gameObject.name, out var pad) ? pad : 0f;
            var onlyUpper = true;
            var bones = smr.bones;
            var gt = gun.transform;
            var parent = gt.parent;
            gt.SetParent(Bone(bones, "Hand.R"), true);
            var spine = Bone(bones, "Spine");
            var legL = Bone(bones, "UpperLeg.L");
            var legR = Bone(bones, "UpperLeg.R");
            var s0 = spine.localRotation;
            var l0 = legL.localRotation;
            var r0 = legR.localRotation;
            var hitsBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var gunCol = gun.GetComponent<Collider>();
            if (gunCol != null) gunCol.enabled = false;
            var worst = 0;
            MaxDepth = 0f;
            var lines = new List<string>();
            foreach (var (name, twist, lean, swing) in new[] { ("still", 0f, 0f, 0f), ("sway -5°", -5f, 0f, 0f), ("sway +5°", 5f, 0f, 0f), ("lean +10°", 0f, 10f, 0f), ("lean -10°", 0f, -10f, 0f), ("walk ±25°", 0f, 0f, 25f), ("walk + sway 5° + lean 5°", 5f, 5f, 25f), ("run, knees driven ±40°", 5f, 0f, 40f) })
            {
                spine.localRotation = s0;
                spine.rotation = Quaternion.AngleAxis(twist, Vector3.up) * Quaternion.AngleAxis(lean, Vector3.right) * spine.rotation;
                legL.localRotation = l0;
                legR.localRotation = r0;
                legL.rotation = Quaternion.AngleAxis(swing, Vector3.right) * legL.rotation;
                legR.rotation = Quaternion.AngleAxis(-swing, Vector3.right) * legR.rotation;
                var (arms, gunIn) = Clipping(smr, gun, g, parts);
                var total = arms.Sum() + gunIn;
                if (arms[1] + arms[2] + arms[4] + arms[5] + gunIn > 0) onlyUpper = false;
                worst = Mathf.Max(worst, total);
                if (total > 0 || name == "still")
                    lines.Add($"{name}: right upper arm {arms[0]}, forearm {arms[1]}, hand {arms[2]}; left upper arm {arms[3]}, forearm {arms[4]}, hand {arms[5]}; gun points {gunIn}");
            }
            spine.localRotation = s0;
            legL.localRotation = l0;
            legR.localRotation = r0;
            if (gunCol != null) gunCol.enabled = true;
            Physics.queriesHitBackfaces = hitsBack;
            gt.SetParent(parent, true);
            if (worst > 0 && onlyUpper && MaxDepth <= padding)
                return (true, $"accepted: upper arms resting against the body (owner-approved, 2026-10-07; AcceptedPadding); deepest arm vertex {MaxDepth * 1000f:0} mm; " + string.Join(" | ", lines));
            return (worst == 0, $"deepest arm vertex {MaxDepth * 1000f:0} mm; vertices more than {DepthLimit * 1000f:0} mm inside the body: " + string.Join(" | ", lines));
        }

        /// <summary>
        /// Arm vertices inside the body (right upper arm, forearm, hand, left upper arm, forearm, hand) and points along the
        /// gun inside the body, for the current pose. Inside: the body's generalized winding number at the point is over
        /// one half (the solid angle its triangles cover, over 4π). Unlike counting ray crossings, this holds for
        /// clothing built from layered and open shells (characters 03 and 04: vests, pouches, a hood) and for the
        /// openings where the arms are cut off. Depth: the distance to the nearest body triangle.
        /// </summary>
        private static (int[] arms, int gunIn) Clipping(SkinnedMeshRenderer smr, GameObject gun, LongGunSpec g, (int[] part, int[] side) parts)
        {
            var posed = new Mesh();
            smr.BakeMesh(posed, true);
            var pv = posed.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
            var tris = posed.triangles;
            var body = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            // For the upper arm: the body without a hood draped over the shoulders (owner, 2026-10-07).
            var bodyNoHood = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            for (var t = 0; t < tris.Length; t += 3)
                if (parts.part[tris[t]] == 0 && parts.part[tris[t + 1]] == 0 && parts.part[tris[t + 2]] == 0)
                {
                    var tri = (pv[tris[t]], pv[tris[t + 1]], pv[tris[t + 2]]);
                    body.Add(tri);
                    var hood = _hood.Length == pv.Length && (_hood[tris[t]] || _hood[tris[t + 1]] || _hood[tris[t + 2]]);
                    if (!hood) bodyNoHood.Add(tri);
                }
            Object.DestroyImmediate(posed);
            float Winding(List<(Vector3 a, Vector3 b, Vector3 c)> mesh, Vector3 q)
            {
                var sum = 0.0;
                foreach (var (ta, tb, tc) in mesh)
                {
                    var x = ta - q;
                    var y = tb - q;
                    var z = tc - q;
                    float lx = x.magnitude, ly = y.magnitude, lz = z.magnitude;
                    var num = Vector3.Dot(x, Vector3.Cross(y, z));
                    var den = lx * ly * lz + Vector3.Dot(x, y) * lz + Vector3.Dot(x, z) * ly + Vector3.Dot(y, z) * lx;
                    sum += 2.0 * System.Math.Atan2(num, den);
                }
                return Mathf.Abs((float)(sum / (4.0 * System.Math.PI)));
            }
            float Nearest(List<(Vector3 a, Vector3 b, Vector3 c)> mesh, Vector3 q)
            {
                var best = float.MaxValue;
                foreach (var (ta, tb, tc) in mesh) best = Mathf.Min(best, (ClosestOnTriangle(q, ta, tb, tc) - q).sqrMagnitude);
                return Mathf.Sqrt(best);
            }
            // Inside: how deep (0 when outside).
            float Depth(Vector3 q, bool upperArm = false)
            {
                var mesh = upperArm ? bodyNoHood : body;
                return Winding(mesh, q) > 0.5f ? Nearest(mesh, q) : 0f;
            }
            var arms = new int[6];
            for (var i = 0; i < pv.Length; i++)
            {
                var p = parts.part[i];
                if (p < 1 || p > 3) continue;
                // Skin pressing against skin (a fold at the armpit, a palm on the chest) goes in a few millimetres;
                // a limb disappearing goes in centimetres. Counted: deeper than DepthLimit; the deepest is reported.
                var dd = Depth(pv[i], p == 1);
                MaxDepth = Mathf.Max(MaxDepth, dd);
                if (dd > DepthLimit)
                {
                    arms[(parts.side[i] > 0 ? 0 : 3) + p - 1]++;
                    if (p == 1)
                    {
                        DeepAlongMin = Mathf.Min(DeepAlongMin, _along[i]);
                        DeepAlongMax = Mathf.Max(DeepAlongMax, _along[i]);
                    }
                }
            }
            // Along the gun itself, butt to bore.
            var gunIn = 0;
            if (g != null)
            {
                var bore = PackArt.MuzzleOf(g.Id).y;
                for (var k = 0; k <= 20; k++)
                {
                    var x = -0.5f + k * 0.05f;
                    if (Depth(gun.transform.TransformPoint(new Vector3(x, Mathf.Lerp(g.Butt.y, bore, x + 0.5f), g.Zc))) > DepthLimit) gunIn++;
                }
            }
            return (arms, gunIn);
        }

        /// <summary>The point of triangle abc nearest to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            var d1 = Vector3.Dot(ab, ap);
            var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            var bp = p - b;
            var d3 = Vector3.Dot(ab, bp);
            var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            var d5 = Vector3.Dot(ab, cp);
            var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            var denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>
        /// Batch: the characters (owner, 2026-10-06: character 02 first, approved; then 03 and 04) with every held weapon: the
        /// full-body hold, its verification on the real mesh (still and under test motions), and renders of the ones
        /// that pass.
        /// </summary>
        /// <summary>Which long-gun poses VerifyHolds makes.</summary>
        public static HoldMode[] VerifyModes = { HoldMode.Fire };

        /// <summary>Batch: character 02 only, its firing poses and the pistol.</summary>
        public static void VerifyCharacter02()
        {
            VerifyModels = new[] { ("chr_girl", "camo_marines") };
            VerifyHolds();
        }

        /// <summary>
        /// Batch: each character's reach against its body depth: the shoulder joint, how far the arm reaches from it, and
        /// how far in front of the shoulder joint the body (not the arms) comes at chest and belly height.
        /// </summary>
        public static void MeasureReach()
        {
            var log = new StringBuilder();
            foreach (var model in new[] { "chr_01", "chr_girl", "chr_03", "chr_04" })
            {
                var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, model);
                var sh = Bone(smr.bones, "UpperArm.R").position;
                var reach = (Bone(smr.bones, "LowerArm.R").position - sh).magnitude + (Bone(smr.bones, "Hand.R").position - Bone(smr.bones, "LowerArm.R").position).magnitude;
                var parts = ArmParts(smr);
                var m = new Mesh();
                smr.BakeMesh(m, true);
                var v = m.vertices.Select(p => smr.transform.TransformPoint(p)).ToArray();
                Object.DestroyImmediate(m);
                float FrontAt(float y) => v.Where((p, i) => parts.part[i] == 0 && Mathf.Abs(p.y - y) < 0.05f).Select(p => p.z).DefaultIfEmpty(float.NaN).Max();
                log.AppendLine($"  {model}: shoulder joint at height {sh.y:F2} m, z {sh.z:F2}; arm reach {reach:F2} m; body front at shoulder height {FrontAt(sh.y) - sh.z:F2} m ahead of the shoulder joint, 15 cm lower {FrontAt(sh.y - 0.15f) - sh.z:F2} m, 30 cm lower {FrontAt(sh.y - 0.3f) - sh.z:F2} m");
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-REACH]\n" + log);
        }

/// <summary>
        /// Batch: nudges a near-miss locked pose (character 04's shotgun) by small steps of body turn, shoulder roll and
        /// grip position, verifying each on the real mesh, and reports the passing ones (the most like the original first).
        /// </summary>
        public static void NudgeChr04Shotgun()
        {
            var log = new StringBuilder();
            var camo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + "camo_navy.png");
            var baseBody = Locked["chr_04/shotgun/fire"].body;
            var baseAt = Locked["chr_04/shotgun/fire"].at;
            var passes = new List<(float cost, string line)>();
            foreach (var twist in new[] { 45f, 50f, 55f })
            foreach (var roll in new[] { 10f, 15f, 20f })
            foreach (var dx in new[] { 0f, 0.02f, 0.04f })
            foreach (var dz in new[] { 0f, 0.03f })
            {
                var (go, smr) = Spawn(camo, new StringBuilder(), false, false, -1f, "chr_04");
                var gun = Gun("shotgun", Survivor.ShotgunSize);
                var right = Rig(smr.bones, "R");
                var left = Rig(smr.bones, "L");
                var parts = ArmParts(smr);
                var body = new BodyPose { Twist = twist, Lean = baseBody.Lean, Roll = roll };
                ApplyBody(smr.bones, body);
                BuildBodyCollider(smr, parts.part);
                var at = (baseAt.d + new Vector3(dx, 0f, dz), baseAt.yaw, baseAt.pitch, baseAt.part, baseAt.x);
                FixedPlacement = at;
                var placed = UnderarmLong(right, left, gun, ShotgunSpec, true, out _);
                FixedPlacement = null;
                DropBodyCollider();
                var (ok, report) = Verify(smr, gun, ShotgunSpec, parts);
                if (placed && ok)
                    passes.Add((Mathf.Abs(twist - baseBody.Twist) * 0.1f + Mathf.Abs(roll - baseBody.Roll) * 0.1f + (dx + dz) * 20f,
                        $"twist {twist}, roll {roll}, d {at.Item1.x:F3},{at.Item1.y:F3},{at.Item1.z:F3}: {report.Substring(0, Mathf.Min(60, report.Length))}"));
                Diag.Clear();
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
            foreach (var p in passes.OrderBy(p => p.cost)) log.AppendLine("  PASSES " + p.line);
            Debug.Log($"[ZOMBIE-NUDGE] {passes.Count} of 54 pass\n" + log);
        }

/// <summary>
        /// Batch: character 04's rifle and sniper firing poses, tried directly round its clean shotgun pose (the search
        /// finds no placement it trusts): small steps of grip position, gun turn, support place and body pose, each
        /// verified on the real mesh and checked that both hands reach their grips (within 1 cm).
        /// </summary>
        public static void NudgeChr04Long() => NudgeLong("chr_04", "camo_navy", new[] { (45f, 25f, 20f), (50f, 25f, 20f) }, new[] { 0.03f }, new[] { -0.155f, -0.08f }, new[] { 0.42f, 0.5f, 0.58f });

        /// <summary>Batch: character 03's rifle, shotgun and sniper firing poses tried directly (see NudgeLong).</summary>
        /// <summary>Batch: chr_01's rifle, shotgun and sniper firing poses tried directly (see NudgeLong).</summary>
        public static void NudgeChr01Long()
        {
            Supports = null;
            Supports2 = null;
            NudgeLong("chr_01", "camo_airforce", new[] { (55f, 20f, 10f), (60f, 20f, 10f), (55f, 15f, 20f), (60f, 25f, 20f) }, new[] { 0.09f, 0.13f }, new[] { -0.08f, -0.03f }, new[] { 0.34f, 0.4f });
        }

        public static void NudgeChr03Long()
        {
            // The support hand further along the barrel keeps the left arm off the chest.
            Supports = null;
            Supports2 = null;
            NudgeLong("chr_03", "camo_army", new[] { (50f, 20f, 20f), (50f, 20f, 10f), (50f, 15f, 15f), (45f, 20f, 10f) }, new[] { 0.05f, 0.08f }, new[] { -0.05f, -0.02f }, new[] { 0.6f, 0.64f });
        }

        /// <summary>
        /// Tries firing poses directly over a small grid (body pose, grip offset from the right shoulder, gun turn,
        /// support place), each verified on the real mesh and checked that both hands reach their grips (within 1 cm);
        /// for when the search finds no placement it trusts. Logs the passing ones, the most like the reference first.
        /// </summary>
        private static (int, float)[] Supports, Supports2;

        private static void NudgeLong(string model, string camoName, (float, float, float)[] bodies, float[] xs, float[] ys, float[] zs)
        {
            var log = new StringBuilder();
            var camo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + camoName + ".png");
            foreach (var (spec, size, supports) in new[]
            {
                (RifleSpec, Survivor.RifleSize, Supports ?? new[] { (0, 0.11f), (0, 0.2f) }),
                (ShotgunSpec, Survivor.ShotgunSize, Supports2 ?? new[] { (0, 0.23f), (0, 0.3f) }),
                (SniperSpec, Survivor.SniperSize, new[] { (1, 0.03f), (0, 0.14f) }),
            })
            {
                var passes = new List<(float cost, string line)>();
                var tried = 0;
                var reachedCount = 0;
                var worstWrist = 0f;
                var bestFail = (deep: float.MaxValue, line: "");
                foreach (var (twist, lean, roll) in bodies)
                foreach (var dx in xs)
                foreach (var dz in zs)
                foreach (var dy in ys)
                foreach (var yaw in new[] { -10f, 0f })
                foreach (var (part, sx) in supports)
                {
                    tried++;
                    var (go, smr) = Spawn(camo, new StringBuilder(), false, false, -1f, model);
                    var gun = Gun(spec.Id, size);
                    var right = Rig(smr.bones, "R");
                    var left = Rig(smr.bones, "L");
                    var parts = ArmParts(smr);
                    var body = new BodyPose { Twist = twist, Lean = lean, Roll = roll };
                    ApplyBody(smr.bones, body);
                    BuildBodyCollider(smr, parts.part);
                    Diag.Clear();
                    FixedPlacement = (new Vector3(dx, dy, dz), yaw, -4.5f, part, sx);
                    UnderarmLong(right, left, gun, spec, true, out _);
                    FixedPlacement = null;
                    DropBodyCollider();
                    // Both hands on their grips: the solver's "wrist N cm from its target" for each arm.
                    var gaps = System.Text.RegularExpressions.Regex.Matches(Diag.ToString(), @"wrist ([0-9.]+) cm from its target")
                        .Cast<System.Text.RegularExpressions.Match>().Select(m => float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                    var reached = gaps.All(g => g <= 1f);
                    if (gaps.Count > 0) worstWrist = Mathf.Max(worstWrist, gaps.Max());
                    Diag.Clear();
                    if (reached)
                    {
                        reachedCount++;
                        var (ok, report) = Verify(smr, gun, spec, parts);
                        if (!ok && MaxDepth < bestFail.deep)
                            bestFail = (MaxDepth, $"twist {twist}, d ({dx:F2}, {dy:F2}, {dz:F2}), yaw {yaw}, support {part}/{sx}: {report.Substring(0, Mathf.Min(260, report.Length))}");
                        if (ok)
                            passes.Add((Mathf.Abs(dy - 0.06f) * 10f + Mathf.Abs(yaw) * 0.1f + Mathf.Abs(dz - 0.4f) * 5f,
                                $"twist {twist}, lean {lean}, roll {roll}, d ({dx:F3}, {dy:F3}, {dz:F3}), yaw {yaw}, support part {part} at {sx}: {report.Substring(0, Mathf.Min(40, report.Length))}"));
                    }
                    Object.DestroyImmediate(gun);
                    Object.DestroyImmediate(go);
                }
                log.AppendLine($"  {spec.Name}: {passes.Count} of {tried} pass; hands reached the grips in {reachedCount}; largest wrist gap {worstWrist:F1} cm");
                if (passes.Count == 0 && bestFail.line != "") log.AppendLine("    least-bad: " + bestFail.line);
                foreach (var p in passes.OrderBy(p => p.cost).Take(3)) log.AppendLine("    PASSES " + p.line);
            }
            Debug.Log("[ZOMBIE-NUDGE]\n" + log);
        }

        /// <summary>
        /// Batch: the animals' separate mesh pieces (eyes, teeth, claws, a nose are usually their own pieces): each
        /// piece's size, its main bone and where it sits, to colour them though the model has one material region.
        /// </summary>
        public static void AnimalPieces()
        {
            var log = new StringBuilder();
            foreach (var model in new[] { "zmb_dog", "zmb_rat", "boss_tiger" })
            {
                var smr = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CharacterInspector.Folder + $"/{model}.fbx").GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = smr.sharedMesh;
                var pieces = PackParts.Split(mesh);
                var v = mesh.vertices;
                var w = mesh.boneWeights;
                var t = mesh.triangles;
                var top = v.Max(p => p.y);
                log.AppendLine($"{model}: {pieces.Count} pieces, {t.Length / 3} triangles");
                foreach (var part in pieces.OrderByDescending(p => p.Triangles.Count).Take(40))
                {
                    var idx = part.Triangles.SelectMany(k => new[] { t[k], t[k + 1], t[k + 2] }).Distinct().ToList();
                    var bone = idx.GroupBy(i => smr.bones[w[i].boneIndex0].name).OrderByDescending(g => g.Count()).First().Key;
                    var c = idx.Aggregate(Vector3.zero, (s, i) => s + v[i]) / idx.Count;
                    var b = new Bounds(v[idx[0]], Vector3.zero);
                    foreach (var i in idx) b.Encapsulate(v[i]);
                    log.AppendLine($"  {part.Triangles.Count} tris, bone {bone}, centre ({c.x / top:F2}, {c.y / top:F2}, {c.z / top:F2}), size {b.size.magnitude / top:F2}");
                }
            }
            Debug.Log("[ZOMBIE-PIECES]\n" + log);
            // Diagnostic (not for review): each animal with its separate pieces in distinct colours, from the front side.
            var palette = new[] { Color.red, Color.yellow, Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), Color.green, Color.blue, Color.white };
            foreach (var model in new[] { "zmb_dog", "zmb_rat", "boss_tiger" })
            {
                var go = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CharacterInspector.Folder + $"/{model}.fbx"));
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = Object.Instantiate(smr.sharedMesh);
                var pieces = PackParts.Split(mesh).OrderByDescending(p => p.Triangles.Count).ToList();
                var t = mesh.triangles;
                var col = Enumerable.Repeat(new Color(0.5f, 0.5f, 0.5f), mesh.vertexCount).ToArray();
                for (var k = 1; k < pieces.Count; k++)
                    foreach (var tri in pieces[k].Triangles)
                        for (var e = 0; e < 3; e++) col[t[tri + e]] = palette[(k - 1) % palette.Length];
                mesh.colors = col;
                smr.sharedMesh = mesh;
                smr.sharedMaterial = new Material(ArtMaterials.Stylized);
                var b = smr.bounds;
                go.transform.position -= b.center;
                foreach (var yaw in new[] { 30f, 150f })
                    Render(System.IO.Path.Combine(OutDir, $"diag-{model}-pieces-{yaw:0}.png"), Vector3.zero, Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.2f, yaw, 15f);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Batch: a skinning check in the game camera: zombie_2 with its arms down, a walking stride, the head turned and
        /// the body twisted (every part must follow its bone), and chr_01 holding the rifle (no floating glove).
        /// </summary>
        public static void SkinCheck()
        {
            {
                var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, "zmb_2");
                var b = smr.bones;
                Bone(b, "UpperArm.L").rotation = Quaternion.AngleAxis(60f, Vector3.forward) * Bone(b, "UpperArm.L").rotation;
                Bone(b, "UpperArm.R").rotation = Quaternion.AngleAxis(-60f, Vector3.forward) * Bone(b, "UpperArm.R").rotation;
                Bone(b, "LowerArm.L").rotation = Quaternion.AngleAxis(-40f, Vector3.right) * Bone(b, "LowerArm.L").rotation;
                Bone(b, "UpperLeg.L").rotation = Quaternion.AngleAxis(30f, Vector3.right) * Bone(b, "UpperLeg.L").rotation;
                Bone(b, "UpperLeg.R").rotation = Quaternion.AngleAxis(-30f, Vector3.right) * Bone(b, "UpperLeg.R").rotation;
                Bone(b, "Head").rotation = Quaternion.AngleAxis(25f, Vector3.up) * Bone(b, "Head").rotation;
                Bone(b, "Chest").rotation = Quaternion.AngleAxis(20f, Vector3.up) * Bone(b, "Chest").rotation;
                go.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                RenderGame(System.IO.Path.Combine(OutDir, "zmb_2-skin-game.png"));
                Object.DestroyImmediate(go);
            }
            {
                var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, "chr_01");
                var (id, size, name) = Held(Hold.Rifle);
                var gun = Gun(id, size);
                Pose(smr.bones, gun, Hold.Rifle);
                go.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                gun.transform.RotateAround(Vector3.zero, Vector3.up, 45f);
                RenderGame(System.IO.Path.Combine(OutDir, "chr_01-skin-game.png"));
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Batch: finds parts of a posed character that end up away from the body (a stray piece): the posed vertices
        /// more than 25 cm from every bone, with their region and the bones they are skinned to.
        /// </summary>
        public static void FindStray()
        {
            var (go, smr) = Spawn(null, new StringBuilder(), false, false, -1f, "chr_01");
            var (id, size, name) = Held(Hold.Rifle);
            var gun = Gun(id, size);
            Pose(smr.bones, gun, Hold.Rifle);
            var m = new Mesh();
            smr.BakeMesh(m, true);
            var pv = m.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
            var w = smr.sharedMesh.boneWeights;
            var region = new string[pv.Length];
            for (var sm = 0; sm < smr.sharedMesh.subMeshCount; sm++)
                foreach (var i in smr.sharedMesh.GetTriangles(sm)) region[i] = Region(smr.sharedMaterials[Mathf.Min(sm, smr.sharedMaterials.Length - 1)]);
            var log = new StringBuilder();
            var counts = new Dictionary<string, int>();
            for (var i = 0; i < pv.Length; i++)
            {
                var near = smr.bones.Where(b => b != null).Min(b => (b.position - pv[i]).magnitude);
                if (near < 0.25f) continue;
                var bw = w[i];
                var key = $"{region[i]}: {smr.bones[bw.boneIndex0].name} {bw.weight0:F2}, {smr.bones[bw.boneIndex1].name} {bw.weight1:F2}; at {pv[i]:F2}";
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(12)) log.AppendLine($"  {kv.Value} x {kv.Key}");
            // In the delivered model: vertices above the waist skinned only to Hips, by region.
            var src = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CharacterInspector.Folder + "/chr_01.fbx").GetComponentInChildren<SkinnedMeshRenderer>();
            var sm2 = src.sharedMesh;
            var sv = sm2.vertices;
            var sw = sm2.boneWeights;
            var top = sv.Max(v => v.y);
            for (var s = 0; s < sm2.subMeshCount; s++)
            {
                var idx = sm2.GetTriangles(s).Distinct().Where(i => src.bones[sw[i].boneIndex0].name == "Hips" && sw[i].weight0 > 0.9f && sv[i].y > top * 0.55f).ToList();
                if (idx.Count > 0)
                    log.AppendLine($"  region '{Region(src.sharedMaterials[s])}': {idx.Count} vertices above the waist skinned only to Hips, at height {idx.Min(i => sv[i].y) / top:P0}-{idx.Max(i => sv[i].y) / top:P0} of the model, x {idx.Min(i => sv[i].x):F2}..{idx.Max(i => sv[i].x):F2}");
                // Inside the shoulders' width but skinned mainly to an arm bone (they swing into the chest).
                var shoulderX = Mathf.Abs(src.transform.InverseTransformPoint(src.bones.First(b => b.name == "UpperArm.L").position).x);
                var inner = sm2.GetTriangles(s).Distinct().Where(i => Mathf.Abs(sv[i].x) < shoulderX)
                    .Select(i => src.bones[sw[i].boneIndex0].name).Where(n => n.StartsWith("UpperArm") || n.StartsWith("LowerArm") || n.StartsWith("Hand")).GroupBy(n => n).ToList();
                foreach (var g in inner) log.AppendLine($"  region '{Region(src.sharedMaterials[s])}': {g.Count()} vertices inside the shoulders skinned mainly to {g.Key}");
            }
            Debug.Log("[ZOMBIE-STRAY]\n" + log);
            Object.DestroyImmediate(gun);
            Object.DestroyImmediate(go);
        }

        /// <summary>Batch: character 01 only, the firing poses and the pistol.</summary>
        public static void VerifyCharacter01()
        {
            VerifyModels = new[] { ("chr_01", "camo_airforce") };
            VerifyModes = new[] { HoldMode.Fire };
            VerifyHolds();
        }

        /// <summary>Batch: character 03 only (arms lengthened), the firing poses and the pistol.</summary>
        public static void VerifyCharacter03()
        {
            VerifyModels = new[] { ("chr_03", "camo_army") };
            VerifyModes = new[] { HoldMode.Fire };
            VerifyHolds();
        }

        /// <summary>Batch: character 04 only, the firing poses and the pistol.</summary>
        public static void VerifyCharacter04()
        {
            VerifyModels = new[] { ("chr_04", "camo_navy") };
            VerifyModes = new[] { HoldMode.Fire };
            VerifyHolds();
        }

        /// <summary>Batch: characters 03 and 04, the firing poses and the pistol.</summary>
        public static void VerifyOthers()
        {
            VerifyModels = new[] { ("chr_03", "camo_army"), ("chr_04", "camo_navy") };
            VerifyModes = new[] { HoldMode.Fire };
            VerifyHolds();
        }

        /// <summary>Batch: character 02, only the firing poses.</summary>
        public static void VerifyNewModes02()
        {
            VerifyModels = new[] { ("chr_girl", "camo_marines") };
            VerifyModes = new[] { HoldMode.Fire };
            VerifyHolds();
        }

        /// <summary>Which characters VerifyHolds runs.</summary>
        public static (string model, string camo)[] VerifyModels = { ("chr_01", "camo_airforce"), ("chr_girl", "camo_marines"), ("chr_03", "camo_army"), ("chr_04", "camo_navy") };

        public static void VerifyHolds()
        {
            System.Array.Clear(LimbHits, 0, LimbHits.Length);
            var all = new StringBuilder();
            foreach (var (model, camoName) in VerifyModels)
            foreach (var hold in new[] { Hold.Rifle, Hold.Shotgun, Hold.Sniper, Hold.Pistol })
            foreach (var mode in hold == Hold.Pistol ? new[] { HoldMode.Fire } : VerifyModes)
            {
                Mode = mode;
                var camo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.Folder + camoName + ".png");
                var (go, smr) = Spawn(camo, new StringBuilder(), false, false, -1f, model);
                var (id, size, name) = Held(hold);
                var gun = Gun(id, size);
                LastNote = "";
                LastVerify = "";
                Pose(smr.bones, gun, hold);
                Diag.Clear();
                all.AppendLine($"  {model} {name} [{(hold == Hold.Pistol ? "isosceles" : mode.ToString())}]: {LastNote}");
                if (hold != Hold.Pistol && !LastNote.Contains("no clean")) all.AppendLine($"    placement: d ({LastPlacement.d.x:F3}, {LastPlacement.d.y:F3}, {LastPlacement.d.z:F3}), yaw {LastPlacement.yaw:F2}, pitch {LastPlacement.pitch:F2}, support part {LastPlacement.part} at x {LastPlacement.x:F3}");
                all.Append(LastVerify);
                if (!LastNote.Contains("no clean"))
                {
                    var file = model == "chr_girl" ? "chr_girl" : model;
                    // Firing turns the whole body so the gun points at the target.
                    Blade(go, gun);
                    // One gameplay-camera view per weapon (owner, 2026-10-07), the gun pointing down and to the left on
                    // screen.
                    foreach (var (facing, label) in new[] { (45f, "game") })
                    {
                        go.transform.RotateAround(Vector3.zero, Vector3.up, facing);
                        gun.transform.RotateAround(Vector3.zero, Vector3.up, facing);
                        RenderGame(System.IO.Path.Combine(OutDir, $"{file}-game-{name}{(hold == Hold.Pistol ? "" : "-fire")}-{label}.png"));
                        go.transform.RotateAround(Vector3.zero, Vector3.up, -facing);
                        gun.transform.RotateAround(Vector3.zero, Vector3.up, -facing);
                    }
                }
                Object.DestroyImmediate(gun);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ZOMBIE-VERIFY]\n" + all + $"  too-deep upper-arm vertices lie {DeepAlongMin * 100f:0}-{DeepAlongMax * 100f:0} % of the way from the shoulder joint to the elbow\n" + "  arm samples in the body (upper arm 40/60/80/100 %, forearm 10/30/50/70/90 %, hand wrist/mid/knuckles): " + string.Join(" ", LimbHits));
        }
    }
}
