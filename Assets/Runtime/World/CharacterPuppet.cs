using UnityEngine;
using ZombieGame.Art;
using Bone = ZombieGame.Art.CharacterRig.Bone;

namespace ZombieGame
{
    /// <summary>
    /// Code-made posing for the owner's character models. Everything is posed every frame from the character's state,
    /// so no animation files are needed and local, remote and AI characters behave alike.
    ///
    /// Facing (owner, 2026-10-06): the survivor's transform is the upper body: it faces the walking direction, and turns
    /// to the aim while firing (Survivor). The legs face the way they walk and only walk forwards or backwards (no
    /// sideways steps): when walking and aim differ by more than about 100°, the legs walk backwards instead. So the
    /// upper body never twists more than about 100° from the hips (the owner's limit: 180°), spread over spine, chest
    /// and head.
    ///
    /// Weapon holds (owner, 2026-10-06: standard, natural poses, not aimed exactly; after real-world references):
    /// - Long guns: stock tucked into the right shoulder, right hand on the pistol grip, left hand on the fore-end.
    ///   "Low ready" (muzzle about 40° down, a little inwards) until firing, then shouldered level.
    /// - Pistol: "compressed ready" close to the chest, muzzle down; firing, the two-handed isosceles: both arms out,
    ///   the support hand wrapped around the shooting hand.
    /// - Knife: forward grip, held close to the body with the point towards the enemy; the free hand up as a guard
    ///   in front of the ribs and face. A stab thrusts it forward.
    /// - Grenade: in the right hand at the chest; wound back overhead while aiming, then thrown.
    /// The hands always land on the weapon: where an oversized (Brawl Stars) gun's fore-end is beyond the chibi arm's
    /// reach, the left hand takes the furthest point of the fore-end it can reach.
    /// </summary>
    public class CharacterPuppet : MonoBehaviour
    {
        /// <summary>What drives the pose (taken from the Survivor in play; set directly for editor previews).</summary>
        public struct State
        {
            /// <summary>Velocity in the upper body's space (x right, z forward), m/s.</summary>
            public Vector3 Move;
            public bool Dead, Engaged, Reloading, Switching, Healing, GrenadeAiming;
            public string HeldId;
        }

        private Survivor _survivor;
        private Transform _space;
        private Transform[] _b;
        private Transform _root;
        private Transform _held;
        private Vector3 _hipsRest;
        private float _upperLen, _lowerLen;
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private float _aimBlend;
        private float _kick, _stab, _hurt, _throw;
        private bool _wasCharging;
        private float _reloadT;
        public State Preview;
        private CharacterModelLibrary.ModelData _model;

        public void Init(Survivor survivor, Transform modelRoot, Transform[] bones, CharacterModelLibrary.ModelData model = null)
        {
            _survivor = survivor;
            _model = model;
            // The drawn body: on the Host the online friend's body is drawn at their smooth playback.
            Setup(survivor.Visual, modelRoot, bones);
        }

        /// <summary>The approved firing pose for the weapon in hand, if the model has one.</summary>
        private CharacterModelLibrary.HoldData FiringHold(string heldId)
        {
            if (_model == null || _model.holds == null || heldId == null) return null;
            foreach (var h in _model.holds)
                if (h.weapon == heldId && h.rotations != null && h.rotations.Length == _b.Length) return h;
            return null;
        }

        /// <summary>The bones parent first, so each is posed after the one it hangs from.</summary>
        private int[] _order;

        private int[] Order()
        {
            if (_order != null) return _order;
            var order = new System.Collections.Generic.List<int>();
            var parents = _model != null && _model.parents != null && _model.parents.Length == _b.Length ? _model.parents : null;
            var done = new bool[_b.Length];
            while (order.Count < _b.Length)
                for (var i = 0; i < _b.Length; i++)
                {
                    if (done[i]) continue;
                    var p = parents != null ? parents[i] : (i < CharacterRig.Parent.Length ? CharacterRig.Parent[i] : 0);
                    if (p >= 0 && !done[p]) continue;
                    done[i] = true;
                    order.Add(i);
                }
            return _order = order.ToArray();
        }

        /// <summary>Is <paramref name="bone"/> <paramref name="ancestor"/> or below it?</summary>
        private bool Under(int bone, Bone ancestor)
        {
            for (var t = _b[bone]; t != null && t != _root; t = t.parent)
                if (t == _b[(int)ancestor]) return true;
            return false;
        }

        /// <summary>
        /// A gun held in its approved firing pose (owner, 2026-10-06/07: firing poses only; option 1: aiming turns the
        /// whole character, the waist sways at most ±5°). Every bone takes the pose the bake read from the verified hold;
        /// the walk adds what the verification checked: the legs swinging up to 25°, the upper body swaying up to ±5°
        /// and leaning a few degrees (recoil, a flinch). The gun rides the right hand.
        /// </summary>
        private void FiringPose(State s, CharacterModelLibrary.HoldData hold, float dt)
        {
            _root.localRotation = Quaternion.Euler(0f, hold.bodyTurn, 0f);
            // The legs and hips walk (the thigh within the 25° the verification checks); the upper body keeps the pose,
            // turning against the hips' twist by at most the ±5° sway the verification checks.
            var pelvis = PoseLegs(s.Move, s.Dead, dt, 25f, UpperCentre(hold), GunThighLimit);
            var breathe = Mathf.Sin(Time.time * 2.1f) * (1f - _gait.Weight);
            var sway = Quaternion.AngleAxis(Mathf.Clamp(-pelvis, -5f, 5f), Vector3.up);
            var lean = Quaternion.AngleAxis(Mathf.Clamp(-3f * _kick - 6f * _hurt + breathe, -8f, 8f), Vector3.right);
            var upper = sway * lean;
            foreach (var i in Order())
            {
                // The hips and legs are the gait's; everything above the hips keeps the firing pose.
                if (i == (int)Bone.Hips || !Under(i, Bone.Spine)) continue;
                Set((Bone)i, upper * hold.rotations[i]);
            }
            if (_held != null)
            {
                var hand = _b[(int)Bone.HandR];
                _held.SetPositionAndRotation(hand.position + hand.rotation * hold.gunPosition, hand.rotation * hold.gunRotation);
            }
        }

        private void Setup(Transform space, Transform modelRoot, Transform[] bones)
        {
            _space = space;
            _root = modelRoot;
            _b = bones;
            _hipsRest = bones[(int)Bone.Hips].localPosition;
            _upperLen = bones[(int)Bone.LowerArmR].localPosition.magnitude;
            _lowerLen = bones[(int)Bone.HandR].localPosition.magnitude;
            _lastPos = space.position;
        }

        public void Fired(float strength) => _kick = Mathf.Max(_kick, strength);
        public void Stabbed() => _stab = 1f;
        public void Hurt() => _hurt = 1f;

        private State Read()
        {
            if (_survivor == null) return Preview;
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var pos = _space.position;
            var v = (pos - _lastPos) / dt;
            v.y = 0f;
            _lastPos = pos;
            // Teleports and respawns aren't walking.
            if (v.sqrMagnitude > 400f) v = Vector3.zero;
            _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-12f * dt));
            _held = _survivor.HeldTransform;
            var gun = _survivor.ActiveFirearm;
            return new State
            {
                Move = _space.InverseTransformDirection(_velocity),
                Dead = _survivor.IsDead,
                Engaged = _survivor.FacingAim,
                Reloading = gun != null && gun.Reloading && _survivor.Selected != Slot.Knife,
                Switching = _survivor.Switching,
                Healing = _survivor.Healing,
                GrenadeAiming = _survivor.GrenadeAiming,
                HeldId = _held != null ? _survivor.HeldId : null,
            };
        }

        private void LateUpdate()
        {
            if (_b == null || _space == null) return;
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var s = Read();
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 9f);
            _hurt = Mathf.MoveTowards(_hurt, 0f, dt * 5f);
            _stab = Mathf.MoveTowards(_stab, 0f, dt / 0.32f);
            _throw = Mathf.MoveTowards(_throw, 0f, dt / 0.35f);
            if (_wasCharging && !s.GrenadeAiming && !s.Dead) _throw = 1f;
            _wasCharging = s.GrenadeAiming;
            // Raise the weapon fast when firing starts, lower it more slowly afterwards.
            var wantAim = s.Engaged && !s.Reloading && !s.Switching ? 1f : 0f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, wantAim, dt / (wantAim > _aimBlend ? 0.12f : 0.35f));
            Pose(s, dt);
        }

        // ------------------------------------------------------------------ pose

        private Quaternion ModelRot => _root.rotation;

        /// <summary>Sets a bone's rotation in model space (its parent must already be posed).</summary>
        private void Set(Bone b, Quaternion modelRotation) => _b[(int)b].rotation = ModelRot * modelRotation;

        private void Pose(State s, float dt)
        {
            for (var i = 0; i < _b.Length; i++) _b[i].localRotation = Quaternion.identity;
            _b[(int)Bone.Hips].localPosition = _hipsRest;
            var firing = !s.Dead && _held != null ? FiringHold(s.HeldId) : null;
            if (firing != null)
            {
                FiringPose(s, firing, dt);
                return;
            }
            _root.localRotation = Quaternion.identity;
            var pelvis = PoseLegs(s.Move, s.Dead, dt, 32f, Vector3.zero, 90f);
            var twist = 0f;
            var breathe = Mathf.Sin(Time.time * 2.1f) * (1f - _gait.Weight);
            // Leaning into a run; flinch, kick, breathing; the spine takes 40 % of the hips' turn, the chest turns
            // against their step-by-step twist.
            var lean = Quaternion.AngleAxis(6f * _gait.Run * _gait.Weight, Vector3.right);
            var flinch = Quaternion.AngleAxis(-12f * _hurt - 4f * _kick + breathe * 1.5f, Vector3.right);
            var counter = Quaternion.AngleAxis(-pelvis * 0.8f, Vector3.up);
            Set(Bone.Spine, Quaternion.AngleAxis(_legYaw * 0.4f, Vector3.up) * lean);
            Set(Bone.Chest, counter * flinch * lean);
            Set(Bone.ShoulderL, counter * flinch * lean);
            Set(Bone.ShoulderR, counter * flinch * lean);
            Set(Bone.Neck, flinch * lean);
            Set(Bone.Head, Quaternion.AngleAxis(_hurt * 8f, Vector3.right) * lean);

            if (s.Dead || _held == null || s.HeldId == null)
            {
                Rest(Bone.UpperArmR, Bone.LowerArmR, 1f, s.Dead ? 0.6f : 1f, twist);
                Rest(Bone.UpperArmL, Bone.LowerArmL, -1f, s.Dead ? 0.6f : 1f, twist);
                return;
            }
            Hold(s);
        }

        private readonly Gait _gait = new Gait();

        /// <summary>How far the hips and legs are turned towards the walking direction (degrees, within ±35).</summary>
        private float _legYaw;

        /// <summary>The walk's numbers, for the record and the tests.</summary>
        public Gait Gait => _gait;

        /// <summary>Step rate relative to people's (chibi legs take quick little steps, after Brawl Stars). PLACEHOLDER.</summary>
        public const float LegCadence = 1.15f;

        /// <summary>
        /// The legs and hips (Gait): the legs turn up to 35° towards the walking direction and step that way (sideways
        /// or backwards for the rest of the angle); the feet stay planted while down; the hips ride on the leg that's
        /// down, twisting towards the leg that's stepping and dropping on its side. Returns the hips' twist (degrees) so
        /// the upper body can turn against it.
        /// </summary>
        /// <summary>
        /// With a gun in its firing pose, the thighs swing at most this far either way (degrees): the running frame the
        /// hold verification checks (HoldBody.Verify), so the arms never touch the legs.
        /// </summary>
        public const float GunThighLimit = 40f;

        private float PoseLegs(Vector3 move, bool dead, float dt, float maxSwing, Vector3 upperCentre, float thighLimit)
        {
            move.y = 0f;
            // The walk in the model's space (the root may be turned for a firing pose).
            var local = Quaternion.Inverse(_root.localRotation) * move;
            var speed = dead ? 0f : local.magnitude;
            var want = 0f;
            if (speed > 0.2f)
            {
                var a = Vector3.SignedAngle(Vector3.forward, local, Vector3.up);
                want = Mathf.Clamp(Mathf.Abs(a) <= 110f ? a : a > 0f ? a - 180f : a + 180f, -35f, 35f);
            }
            _legYaw = Mathf.MoveTowardsAngle(_legYaw, want, 360f * dt);
            var yaw = Quaternion.AngleAxis(_legYaw, Vector3.up);
            // The direction the legs step in, in their own frame; the swing turns about the axis across it.
            var dir = speed > 0.2f ? Quaternion.Inverse(yaw) * local.normalized : Vector3.forward;
            var axis = Vector3.Cross(Vector3.up, dir).normalized;
            var thigh = _b[(int)Bone.LowerLegR].localPosition.magnitude;
            var shin = _b[(int)Bone.FootR].localPosition.magnitude;
            _gait.Advance(speed, dt, thigh, shin, maxSwing, LegCadence);
            var run = _gait.Run;
            // The feet land and push off round a point under the body's weight: halfway between the hip joint and the
            // upper body (a pose leaning forward puts it ahead of the hips), along the walk.
            var centre = Vector3.Dot(Quaternion.Inverse(yaw) * upperCentre, dir) * 0.5f;
            var drive = Mathf.Lerp(10f, 30f, run);
            // The knee bends the same way whichever way the legs step: walking backward it carries the foot along the walk.
            var kneeAlong = Mathf.Clamp(dir.z, -1f, 1f);
            var right = _gait.Leg(0f, Mathf.Lerp(6f, 16f, run), Mathf.Lerp(48f, 75f, run), -1f, -1f, -1f, centre, drive, kneeAlong);
            var left = _gait.Leg(0.5f, Mathf.Lerp(6f, 16f, run), Mathf.Lerp(48f, 75f, run), -1f, -1f, -1f, centre, drive, kneeAlong);
            right.Thigh = Mathf.Clamp(right.Thigh, -thighLimit, thighLimit);
            left.Thigh = Mathf.Clamp(left.Thigh, -thighLimit, thighLimit);
            _b[(int)Bone.Hips].localPosition = _hipsRest + Vector3.up * _gait.HipsOffset(right, left, thigh, shin);
            // The hips twist towards the leg that's stepping forward and drop on its side.
            var cycle = _gait.Phase * Mathf.PI * 2f;
            var pelvisYaw = Mathf.Sin(cycle) * Mathf.Lerp(4f, 7f, run) * _gait.Weight;
            var pelvisRoll = Mathf.Sin(cycle * 2f) * 2.5f * _gait.Weight;
            Set(Bone.Hips, yaw * Quaternion.AngleAxis(pelvisYaw, Vector3.up) * Quaternion.AngleAxis(pelvisRoll, Vector3.forward));
            LegBones(Bone.UpperLegR, Bone.LowerLegR, Bone.FootR, right, yaw, axis);
            LegBones(Bone.UpperLegL, Bone.LowerLegL, Bone.FootL, left, yaw, axis);
            return pelvisYaw;
        }

        private readonly System.Collections.Generic.Dictionary<CharacterModelLibrary.HoldData, Vector3> _centres =
            new System.Collections.Generic.Dictionary<CharacterModelLibrary.HoldData, Vector3>();

        /// <summary>
        /// Where a firing pose puts the upper body (the middle of the chest, neck and head joints) relative to the hip
        /// joint, in the model's space: the pose's bone rotations applied down the spine.
        /// </summary>
        private Vector3 UpperCentre(CharacterModelLibrary.HoldData hold)
        {
            if (_centres.TryGetValue(hold, out var c)) return c;
            var j = _model.joints;
            var r = hold.rotations;
            var spine = j[(int)Bone.Hips] + (j[(int)Bone.Spine] - j[(int)Bone.Hips]);
            var chest = spine + r[(int)Bone.Spine] * (j[(int)Bone.Chest] - j[(int)Bone.Spine]);
            var neck = chest + r[(int)Bone.Chest] * (j[(int)Bone.Neck] - j[(int)Bone.Chest]);
            var head = neck + r[(int)Bone.Neck] * (j[(int)Bone.Head] - j[(int)Bone.Neck]);
            c = (chest + neck + head) / 3f - j[(int)Bone.Hips];
            c.y = 0f;
            // As far ahead as it leans compared with standing straight.
            var restC = (j[(int)Bone.Chest] + j[(int)Bone.Neck] + j[(int)Bone.Head]) / 3f - j[(int)Bone.Hips];
            restC.y = 0f;
            c -= restC;
            _centres[hold] = c;
            return c;
        }

        /// <summary>
        /// The hands closed (owner, 2026-10-07: the knife hand a fist, not open): every finger bent as the firing
        /// pose's grip bends it round the pistol (the bake read it from the verified hold), relative to its hand.
        /// </summary>
        private void Fists()
        {
            var grip = FiringHold("pistol");
            if (grip == null || _model == null || _model.parents == null) return;
            for (var i = CharacterRig.BoneCount; i < _b.Length; i++)
            {
                var name = _model.bones[i];
                if (!(name.StartsWith("Thumb") || name.StartsWith("Index") || name.StartsWith("Middle") || name.StartsWith("Ring") || name.StartsWith("Little"))) continue;
                var p = _model.parents[i];
                if (p < 0) continue;
                _b[i].localRotation = Quaternion.Inverse(grip.rotations[p]) * grip.rotations[i];
            }
        }

        /// <summary>
        /// Owner, 2026-10-08: the legs straight and close together, the feet parallel, stepping straight. The owner's
        /// models stand with their legs spread (feet up to twice as far apart as the hip joints, each leg leaning out
        /// 6-15°), and every pose turns the bones from that stance. So each leg is first straightened: the thigh and the
        /// shin point at a spot under the body with the feet this share of the hip joints' spacing apart. PLACEHOLDER.
        /// </summary>
        public const float FeetSpan = 0.8f;

        /// <summary>How much a straightened leg's thigh, shin and foot are turned from the model's stance, per leg.</summary>
        private struct LegFix
        {
            public Quaternion Thigh, Shin, Foot;
        }

        private LegFix[] _legFix;

        /// <summary>The fixed legs (left, right), worked out once per model.</summary>
        private LegFix Fix(Bone upper)
        {
            if (_legFix == null) _legFix = new[] { MakeFix(Bone.UpperLegL), MakeFix(Bone.UpperLegR) };
            return _legFix[upper == Bone.UpperLegL ? 0 : 1];
        }

        /// <summary>A boot of the model as it stands: where its toes point (degrees from straight ahead, + towards +x) and how wide it is across that (m).</summary>
        public struct BootShape
        {
            public float ToeYaw, Width;
        }

        private static readonly System.Collections.Generic.Dictionary<string, BootShape[]> Boots = new System.Collections.Generic.Dictionary<string, BootShape[]>();

        /// <summary>
        /// The model's boots (left, right): the vertices mostly on each foot bone; the toes point from the back half of the
        /// boot to its front half (the chibi boots are nearly as wide as they are long, so their outline can't tell).
        /// </summary>
        public static BootShape[] BootsOf(CharacterModelLibrary.ModelData model)
        {
            var boots = new BootShape[2];
            if (model == null) return boots;
            if (Boots.TryGetValue(model.id, out var known)) return known;
            var mesh = CharacterRig.LoadMesh(model.id);
            if (mesh != null && mesh.isReadable)
            {
                var v = mesh.vertices;
                var w = mesh.boneWeights;
                for (var side = 0; side < 2; side++)
                {
                    var bone = side == 0 ? (int)Bone.FootL : (int)Bone.FootR;
                    var pts = new System.Collections.Generic.List<Vector3>();
                    for (var i = 0; i < v.Length && i < w.Length; i++)
                        if (w[i].boneIndex0 == bone && w[i].weight0 > 0.5f) pts.Add(v[i]);
                    if (pts.Count < 12) continue;
                    var mz = 0f;
                    foreach (var p in pts) mz += p.z;
                    mz /= pts.Count;
                    Vector3 front = Vector3.zero, back = Vector3.zero;
                    int nf = 0, nb = 0;
                    foreach (var p in pts)
                    {
                        if (p.z > mz) { front += p; nf++; }
                        else { back += p; nb++; }
                    }
                    if (nf == 0 || nb == 0) continue;
                    var d = front / nf - back / nb;
                    var yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -30f, 30f);
                    // The width across the toes' direction.
                    var across = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var p in pts)
                    {
                        var a = Vector3.Dot(p, across);
                        lo = Mathf.Min(lo, a);
                        hi = Mathf.Max(hi, a);
                    }
                    boots[side] = new BootShape { ToeYaw = yaw, Width = hi - lo };
                }
            }
            Boots[model.id] = boots;
            return boots;
        }

        /// <summary>The gap kept between the boots (m). PLACEHOLDER.</summary>
        public const float BootGap = 0.03f;

        /// <summary>How far apart the feet are put (centre to centre, m): FeetSpan of the hip joints' spacing, never so close the boots touch.</summary>
        public static float FeetApart(CharacterModelLibrary.ModelData model)
        {
            if (model == null || model.joints == null || model.joints.Length <= (int)Bone.UpperLegR) return 0f;
            var hips = Mathf.Abs(model.joints[(int)Bone.UpperLegR].x - model.joints[(int)Bone.UpperLegL].x);
            var boots = BootsOf(model);
            var widest = Mathf.Max(boots[0].Width, boots[1].Width);
            return Mathf.Max(hips * FeetSpan, widest + BootGap);
        }

        private LegFix MakeFix(Bone upper)
        {
            var fix = new LegFix { Thigh = Quaternion.identity, Shin = Quaternion.identity, Foot = Quaternion.identity };
            if (_model == null || _model.joints == null || _model.joints.Length <= (int)upper + 2) return fix;
            var j = _model.joints;
            Vector3 hip = j[(int)upper], knee = j[(int)upper + 1], foot = j[(int)upper + 2];
            var leg = (knee - hip).magnitude + (foot - knee).magnitude;
            var side = hip.x - j[(int)Bone.Hips].x;
            // The foot under the body, half the feet's spacing from the middle.
            var dx = Mathf.Sign(side) * FeetApart(_model) * 0.5f - side;
            var down = new Vector3(dx, -Mathf.Sqrt(Mathf.Max(0.0001f, leg * leg - dx * dx)), 0f).normalized;
            fix.Thigh = Quaternion.FromToRotation(knee - hip, down);
            fix.Shin = Quaternion.FromToRotation(foot - knee, down);
            fix.Foot = Quaternion.AngleAxis(-BootsOf(_model)[upper == Bone.UpperLegL ? 0 : 1].ToeYaw, Vector3.up);
            return fix;
        }

        private void LegBones(Bone upper, Bone lower, Bone foot, Gait.LegPose p, Quaternion yaw, Vector3 axis)
        {
            var fix = Fix(upper);
            var swing = yaw * Quaternion.AngleAxis(-p.Thigh, axis);
            // The straightened leg swings in the walking direction; the knee hinges about the leg's own side axis.
            Set(upper, swing * fix.Thigh);
            Set(lower, swing * Quaternion.AngleAxis(p.Knee, Vector3.right) * fix.Shin);
            // The foot level with the ground while down, its toes straight ahead.
            Set(foot, yaw * Quaternion.AngleAxis(-p.Foot, Vector3.right) * fix.Foot);
        }

        /// <summary>A bone of the puppet (for the tests).</summary>
        public Transform BoneOf(Bone b) => _b != null && (int)b < _b.Length ? _b[(int)b] : null;

        /// <summary>Arms hanging at the sides: side = +1 right, -1 left; drop 0..1 (1 = straight down).</summary>
        private void Rest(Bone upper, Bone lower, float side, float drop, float twist)
        {
            var turn = Quaternion.AngleAxis(twist, Vector3.up);
            var down = turn * Quaternion.AngleAxis(-side * 75f * drop, Vector3.forward);
            Set(upper, down);
            Set(lower, turn * Quaternion.AngleAxis(-side * 10f, Vector3.forward) * Quaternion.AngleAxis(-15f * side, Vector3.up) * Quaternion.AngleAxis(-side * 75f * drop, Vector3.forward));
        }

        // ------------------------------------------------------------------ holds

        private Vector3 ToSpace(Vector3 world) => _space.InverseTransformPoint(world);
        private Vector3 FromSpace(Vector3 local) => _space.TransformPoint(local);

        private enum Kind { LongGun, Pistol, Knife, Grenade }

        private static Kind KindOf(string id)
        {
            switch (id)
            {
                case "pistol": return Kind.Pistol;
                case "knife": return Kind.Knife;
                case "grenade": return Kind.Grenade;
                default: return Kind.LongGun;
            }
        }

        /// <summary>Where the right and left hands go on each weapon, as fractions of its length from the back end.</summary>
        private static (float right, float left) Grips(string id)
        {
            switch (id)
            {
                case "shotgun": return (0.3f, 0.66f);      // the pump
                case "smg_gun": return (0.42f, 0.7f);      // a compact gun: grip near the middle
                case "sniper_gun": return (0.3f, 0.56f);
                case "pistol": return (0.2f, 0.2f);
                case "knife": return (0.18f, 0.18f);       // the handle
                case "grenade": return (0.5f, 0.5f);
                default: return (0.32f, 0.62f);            // assault rifle
            }
        }

        private void Hold(State s)
        {
            var id = s.HeldId;
            var kind = KindOf(id);
            var mesh = PackArt.Model(id);
            if (mesh == null) return;
            var bounds = mesh.bounds;
            var bore = PackArt.MuzzleOf(id).y;
            var scale = _held.localScale;
            var len = bounds.size.x;
            Vector3 OnGun(float t, float drop) => new Vector3(bounds.min.x + t * len, bore - drop, 0f);
            var (gr, gl) = Grips(id);
            var shoulderR = ToSpace(_b[(int)Bone.UpperArmR].position);
            var shoulderL = ToSpace(_b[(int)Bone.UpperArmL].position);
            var chest = ToSpace(_b[(int)Bone.Chest].position);
            var aim = _aimBlend;

            Vector3 dir;          // where the weapon points (upper-body space)
            Vector3 anchorLocal;  // the point of the weapon placed at...
            Vector3 anchorAt;     // ...this spot
            Vector3 rightPole = new Vector3(1f, -1f, -0.4f), leftPole = new Vector3(-0.4f, -1f, -0.2f);
            Vector3? leftHand = null;
            switch (kind)
            {
                case Kind.Pistol:
                {
                    // Compressed ready (close to the chest, muzzle down) → isosceles (both arms out, level).
                    var ready = new Vector3(0.06f, chest.y - 0.05f, 0.24f);
                    var fire = new Vector3(0.03f, shoulderR.y - 0.1f, 0.36f);
                    anchorAt = Vector3.Lerp(ready, fire, aim);
                    dir = Quaternion.Euler(Mathf.Lerp(45f, 0f, aim), 0f, 0f) * Vector3.forward;
                    anchorLocal = OnGun(gr, 0.2f);
                    rightPole = new Vector3(1f, -1f, -0.3f);
                    leftPole = new Vector3(-1f, -1f, -0.3f);
                    break;
                }
                case Kind.Knife:
                {
                    // A slash (owner, 2026-10-07: slashing, not stabbing; without deforming the character): the knife
                    // arm, extended to 85 % of its reach, sweeps level from out on the right towards the front, the blade
                    // pointing outwards along the swing. The sweep stops short of the front of the body (15° right of
                    // straight ahead), so the hand never cuts across a deep chest. Ready: held out to the front right.
                    var armReach = _upperLen + _lowerLen;
                    float swing;
                    if (_stab > 0f)
                    {
                        var st = 1f - _stab;
                        swing = Mathf.Lerp(65f, 15f, st * st * (3f - 2f * st));
                    }
                    else swing = 35f;
                    var radial = Quaternion.AngleAxis(swing, Vector3.up) * Vector3.forward;
                    anchorAt = shoulderR + radial * (armReach * 0.85f) + Vector3.down * 0.12f;
                    dir = Quaternion.AngleAxis(swing, Vector3.up) * Quaternion.Euler(8f, 0f, 0f) * Vector3.forward;
                    anchorLocal = OnGun(gr, 0f);
                    rightPole = new Vector3(1f, -1f, -0.4f);
                    // The free hand out to the side and front, clear of the body.
                    leftHand = shoulderL + new Vector3(-0.22f, -0.3f, 0.22f) * (armReach / 0.5f);
                    leftPole = new Vector3(-1f, -1f, 0f);
                    break;
                }
                case Kind.Grenade:
                {
                    var held = new Vector3(0.2f, chest.y, 0.24f);
                    var windUp = new Vector3(0.32f, shoulderR.y + 0.35f, -0.18f);
                    var release = new Vector3(0.18f, shoulderR.y + 0.2f, 0.45f);
                    anchorAt = s.GrenadeAiming ? windUp : _throw > 0f ? Vector3.Lerp(release, windUp, _throw) : held;
                    dir = Vector3.forward;
                    anchorLocal = OnGun(gr, 0f);
                    leftHand = new Vector3(-0.15f, chest.y + 0.05f, 0.25f);
                    break;
                }
                default:
                {
                    // The stock in the right shoulder pocket; low ready → shouldered.
                    anchorAt = shoulderR + new Vector3(-0.07f, -0.07f, 0.06f);
                    var pitch = Mathf.Lerp(40f, 3f, aim);
                    var yaw = Mathf.Lerp(-10f, -3f, aim);
                    if (s.Reloading) pitch = 25f;
                    dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                    anchorLocal = new Vector3(bounds.min.x, bore, 0f);
                    break;
                }
            }
            // Recoil: back along the barrel and the muzzle up a touch.
            if (kind == Kind.LongGun || kind == Kind.Pistol)
            {
                anchorAt -= dir * (0.08f * _kick);
                dir = Quaternion.AngleAxis(-6f * _kick, Vector3.Cross(dir, Vector3.up).normalized * -1f) * dir;
            }
            var up = s.Reloading && kind == Kind.LongGun ? Quaternion.AngleAxis(-25f, dir) * Vector3.up : Vector3.up;
            var rot = Quaternion.LookRotation(dir, up) * Quaternion.Euler(0f, -90f, 0f);
            var pos = anchorAt - rot * Vector3.Scale(scale, anchorLocal);
            _held.localPosition = pos;
            _held.localRotation = rot;
            Vector3 P(Vector3 local) => pos + rot * Vector3.Scale(scale, local);

            var right = P(OnGun(gr, kind == Kind.LongGun ? 0.12f : kind == Kind.Pistol ? 0.2f : 0f));
            Vector3 left;
            if (leftHand.HasValue) left = leftHand.Value;
            else if (kind == Kind.Pistol) left = right + new Vector3(-0.05f, -0.03f, -0.01f);
            else
            {
                // The fore-end, or as far along it as the left arm reaches.
                var reach = (_upperLen + _lowerLen) * 0.97f;
                var t = gl;
                left = P(OnGun(t, 0.07f));
                while (t > gr + 0.05f && (left - shoulderL).magnitude > reach)
                {
                    t -= 0.02f;
                    left = P(OnGun(t, 0.07f));
                }
                if (s.Reloading)
                {
                    // Working the magazine: the left hand dips under the gun and comes back.
                    _reloadT += Time.deltaTime / 0.7f;
                    var dip = Mathf.Abs(Mathf.Sin(_reloadT * Mathf.PI));
                    left = Vector3.Lerp(left, P(OnGun(gr + 0.12f, 0.3f)), dip);
                }
            }
            if (!s.Reloading) _reloadT = 0f;
            if (s.Healing)
            {
                left = new Vector3(-0.08f, chest.y + 0.05f, 0.2f);
                leftPole = new Vector3(-1f, -1f, 0f);
            }
            Ik(Bone.UpperArmR, Bone.LowerArmR, Bone.HandR, right, rightPole);
            Ik(Bone.UpperArmL, Bone.LowerArmL, Bone.HandL, left, leftPole);
            // The knife (and a grenade) held in a closed hand.
            if (kind == Kind.Knife || kind == Kind.Grenade) Fists();
        }

        /// <summary>
        /// Two-bone IK, positions in the upper body's space: the hand reaches <paramref name="target"/> (or as far as the
        /// arm goes), the elbow bends towards <paramref name="pole"/> (a direction from the shoulder).
        /// </summary>
        private void Ik(Bone upper, Bone lower, Bone hand, Vector3 target, Vector3 pole)
        {
            // Work in model space.
            var toModel = Quaternion.Inverse(ModelRot) * _space.rotation;
            var t = _root.InverseTransformPoint(FromSpace(target));
            pole = toModel * pole;
            var shoulder = _root.InverseTransformPoint(_b[(int)upper].position);
            var d = t - shoulder;
            var reach = _upperLen + _lowerLen;
            var dist = Mathf.Clamp(d.magnitude, 0.05f, reach * 0.999f);
            var dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            var cos = (_upperLen * _upperLen + dist * dist - _lowerLen * _lowerLen) / (2f * _upperLen * dist);
            var angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
            var normal = Vector3.Cross(dir, pole);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(dir, Vector3.up);
            normal.Normalize();
            var a = Quaternion.AngleAxis(-angle, normal) * dir;
            var b = Quaternion.AngleAxis(angle, normal) * dir;
            var elbowDir = Vector3.Dot(a, pole) >= Vector3.Dot(b, pole) ? a : b;
            var elbow = shoulder + elbowDir * _upperLen;
            var hand3 = shoulder + dir * dist;
            var restDir = _b[(int)lower].localPosition.normalized;
            var parentRot = Quaternion.Inverse(ModelRot) * _b[(int)upper].parent.rotation;
            var upperRot = Quaternion.FromToRotation(parentRot * restDir, elbowDir) * parentRot;
            Set(upper, upperRot);
            var lowerRest = _b[(int)hand].localPosition.normalized;
            var lowerRot = Quaternion.FromToRotation(upperRot * lowerRest, (hand3 - elbow).normalized) * upperRot;
            Set(lower, lowerRot);
            Set(hand, lowerRot);
        }
    }
}
