using UnityEngine;
using ZombieGame.Art;
using Bone = ZombieGame.Art.CharacterRig.Bone;
using Quad = ZombieGame.Art.CharacterRig.Quad;

namespace ZombieGame
{
    /// <summary>
    /// Code-made animation for the owner's zombie models (owner, 2026-10-07: connected, with movement animations),
    /// posed every frame from the zombie's state, so local, remote and pooled zombies behave alike. The legs walk on
    /// <see cref="Gait"/> (feet planted, step rate and length from the legs' own size and the speed).
    /// - On two legs (zombies, boss_1, the Bear): a shamble: slower steps than a person's, one leg dragging (its knee
    ///   lifts less), the body bent forward and swaying with the steps, the head lolling, the arms reaching forward.
    ///   An attack raises the arms and brings them down as the hit lands.
    /// - On four legs (Dog, Rat, Tiger): a trot (diagonal legs together), each leg's swing fitted to its own length so
    ///   all four feet stay planted; the back rolling, the head bobbing, the tail swaying. An attack opens the jaw and
    ///   drives the head forward as the bite lands.
    /// Dead: limp (Zombie tips the body over).
    /// </summary>
    public class ZombiePuppet : MonoBehaviour
    {
        private Zombie _zombie;
        private Transform _root;
        private Transform[] _b;
        private CharacterModelLibrary.ModelData _m;
        private Vector3 _hipsRest;
        private Vector3 _lastPos;
        private Vector3 _velocity;
        private float _seed;
        private float _scale;
        private Vector3 _armRestL, _armRestR, _foreRestL, _foreRestR;
        private readonly Gait _gait = new Gait();

        /// <summary>The walk's numbers, for the record and the tests.</summary>
        public Gait Gait => _gait;

        /// <summary>Zombies step a little slower than people (a shamble); animals at their natural rate. PLACEHOLDER.</summary>
        public const float ShambleCadence = 0.9f, AnimalCadence = 1f;

        public void Init(Zombie zombie, Transform root, Transform[] bones, CharacterModelLibrary.ModelData model)
        {
            _zombie = zombie;
            _root = root;
            _b = bones;
            _m = model;
            _hipsRest = bones[0].localPosition;
            _lastPos = zombie.transform.position;
            _seed = Random.value * 10f;
            _gait.Phase = Random.value;
            _scale = root.localScale.y;
            _bear = model.id == "boss_bear";
            if (_bear) _bearPitch = BearPitch(model);
            if (!model.quadruped)
            {
                Vector3 J(Bone b) => model.joints[(int)b];
                _armRestL = (J(Bone.LowerArmL) - J(Bone.UpperArmL)).normalized;
                _armRestR = (J(Bone.LowerArmR) - J(Bone.UpperArmR)).normalized;
                _foreRestL = (J(Bone.HandL) - J(Bone.LowerArmL)).normalized;
                _foreRestR = (J(Bone.HandR) - J(Bone.LowerArmR)).normalized;
            }
        }

        private void Set(int bone, Quaternion modelRotation) => _b[bone].rotation = _root.rotation * modelRotation;

        /// <summary>A bone's length to its child joint, in metres in the game (the model is scaled to its type's height).</summary>
        private float Length(int child) => _b[child].localPosition.magnitude * _scale;

        private void LateUpdate()
        {
            if (_b == null || _zombie == null) return;
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var pos = _zombie.transform.position;
            var v = (pos - _lastPos) / dt;
            v.y = 0f;
            _lastPos = pos;
            if (v.sqrMagnitude > 400f) v = Vector3.zero;
            _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-10f * dt));
            var speed = _zombie.IsDead ? 0f : _velocity.magnitude;
            // The direction it walks in its own frame (forwards, mostly; knocked back, backwards).
            var local = Quaternion.Inverse(_zombie.transform.rotation) * _velocity;
            var dir = speed > 0.2f ? local.normalized : Vector3.forward;
            var axis = Vector3.Cross(Vector3.up, dir).normalized;
            for (var i = 0; i < _b.Length; i++) _b[i].localRotation = Quaternion.identity;
            _b[0].localPosition = _hipsRest;
            if (_m.quadruped) PoseFourLegs(speed, axis, dt);
            else if (_bear) PoseBear(speed, axis, dt);
            else PoseTwoLegs(speed, axis, dt);
        }

        // ------------------------------------------------------------------ two legs

        private void PoseTwoLegs(float speed, Vector3 axis, float dt)
        {
            var t = Time.time + _seed;
            var dead = _zombie.IsDead;
            var attack = _zombie.AttackProgress;
            var thigh = Length((int)Bone.LowerLegR);
            var shin = Length((int)Bone.FootR);
            _gait.Advance(speed, dt, thigh, shin, 30f, ShambleCadence);
            var w = _gait.Weight;
            var run = _gait.Run;
            // The dragging leg (the left) lifts its knee less and scuffs its toes.
            var drive = Mathf.Lerp(6f, 18f, run);
            var right = _gait.Leg(0f, Mathf.Lerp(8f, 16f, run), Mathf.Lerp(45f, 70f, run), -1f, -1f, -1f, 0f, drive);
            var left = _gait.Leg(0.5f, Mathf.Lerp(8f, 16f, run), Mathf.Lerp(26f, 45f, run), -1f, -1f, -1f, 0f, drive * 0.5f);
            left.Foot *= 0.4f;
            _b[0].localPosition = _hipsRest + Vector3.up * (_gait.HipsOffset(right, left, thigh, shin) / _scale);
            var cycle = _gait.Phase * Mathf.PI * 2f;
            var pelvisYaw = Mathf.Sin(cycle) * 6f * w;
            // A lurch: the hips drop further onto the dragging side.
            var pelvisRoll = (Mathf.Sin(cycle * 2f) * 2.5f + Mathf.Sin(cycle) * 3f) * w + Mathf.Sin(t * 0.9f) * 1.5f;
            Set((int)Bone.Hips, Quaternion.AngleAxis(pelvisYaw, Vector3.up) * Quaternion.AngleAxis(pelvisRoll, Vector3.forward));
            LegBones((int)Bone.UpperLegR, (int)Bone.LowerLegR, (int)Bone.FootR, right, axis);
            LegBones((int)Bone.UpperLegL, (int)Bone.LowerLegL, (int)Bone.FootL, left, axis);
            // Bent forward, swaying against the hips, lurching into the attack.
            var lunge = attack < 0.7f ? -8f * attack / 0.7f : -8f + 26f * (attack - 0.7f) / 0.3f;
            var bend = Quaternion.AngleAxis(dead ? 0f : 12f + 5f * w + lunge, Vector3.right);
            var roll = Quaternion.AngleAxis(Mathf.Sin(t * 1.3f) * 3f - pelvisRoll * 0.6f, Vector3.forward) * Quaternion.AngleAxis(-pelvisYaw * 0.7f, Vector3.up);
            Set((int)Bone.Spine, roll * bend);
            Set((int)Bone.Chest, roll * bend);
            Set((int)Bone.Neck, roll * bend);
            // The head lolls to one side and nods with each step.
            var nod = Mathf.Cos(cycle * 2f) * 4f * w;
            Set((int)Bone.Head, Quaternion.AngleAxis(14f + Mathf.Sin(t * 0.7f) * 6f, Vector3.forward) * Quaternion.AngleAxis(nod, Vector3.right) * bend);
            Set((int)Bone.ShoulderL, roll * bend);
            Set((int)Bone.ShoulderR, roll * bend);

            // Arms: reaching forward (slightly apart, drifting, bobbing with the steps); an attack raises them and
            // brings them down.
            Vector3 Reach(float side, float drift)
            {
                if (dead) return new Vector3(side * 0.25f, -1f, 0.15f).normalized;
                var forward = new Vector3(side * 0.18f, -0.12f + drift + Mathf.Cos(cycle * 2f) * 0.04f * w, 1f);
                if (attack <= 0f) return forward.normalized;
                var up = new Vector3(side * 0.15f, 1.1f, 0.5f);
                var down = new Vector3(side * 0.12f, -0.55f, 1f);
                return (attack < 0.7f ? Vector3.Slerp(forward.normalized, up.normalized, attack / 0.7f)
                    : Vector3.Slerp(up.normalized, down.normalized, (attack - 0.7f) / 0.3f)).normalized;
            }
            Arm(Bone.UpperArmL, Bone.LowerArmL, Bone.HandL, _armRestL, _foreRestL, Reach(-1f, Mathf.Sin(t * 1.7f) * 0.08f), -1f);
            Arm(Bone.UpperArmR, Bone.LowerArmR, Bone.HandR, _armRestR, _foreRestR, Reach(1f, Mathf.Sin(t * 1.5f + 1f) * 0.08f), 1f);
        }

        // ------------------------------------------------------------------ the Bear: on all fours

        private bool _bear;
        /// <summary>How far the Bear's body tips forward so its hands reach the ground (degrees).</summary>
        private float _bearPitch;

        /// <summary>
        /// The forward tilt that brings the Bear's shoulders down to the height its arms reach (it walks on all fours,
        /// owner 2026-10-07: "not upright like a human"), from its own skeleton: the arms nearly straight.
        /// </summary>
        private static float BearPitch(CharacterModelLibrary.ModelData m)
        {
            Vector3 J(Bone b) => m.joints[(int)b];
            var hips = J(Bone.Hips);
            var shoulder = (J(Bone.UpperArmL) + J(Bone.UpperArmR)) * 0.5f - hips;
            var arm = (J(Bone.LowerArmR) - J(Bone.UpperArmR)).magnitude + (J(Bone.HandR) - J(Bone.LowerArmR)).magnitude;
            var ground = (J(Bone.FootL).y + J(Bone.FootR).y) * 0.5f;
            var want = ground + arm * 0.92f;
            var best = 60f;
            var bestErr = float.MaxValue;
            for (var p = 30f; p <= 85f; p += 1f)
            {
                var r = Quaternion.AngleAxis(p, Vector3.right) * shoulder;
                var err = Mathf.Abs(hips.y + r.y - want);
                if (err < bestErr) { bestErr = err; best = p; }
            }
            return best;
        }

        /// <summary>
        /// The Bear walks like a bear (owner, 2026-10-07): the body tipped forward onto its arms, the arms working as
        /// forelegs in a walk with the hind legs (each foot planted, diagonal pairs), the head up looking ahead. Its
        /// attack: rearing up on its hind legs, paws raised, and slamming them down as the hit lands.
        /// </summary>
        private void PoseBear(float speed, Vector3 axis, float dt)
        {
            var t = Time.time + _seed;
            var dead = _zombie.IsDead;
            var attack = _zombie.AttackProgress;
            var thigh = Length((int)Bone.LowerLegR);
            var shin = Length((int)Bone.FootR);
            var arm = Length((int)Bone.LowerArmR) + Length((int)Bone.HandR);
            _gait.Advance(speed, dt, thigh, shin, 30f, AnimalCadence);
            var w = _gait.Weight;
            var v = Mathf.Max(speed, 0.3f);
            var maxSin = Mathf.Sin(30f * Mathf.Deg2Rad);
            var armSin = Mathf.Min(maxSin, v * _gait.Stance * _gait.CycleTime / (2f * Mathf.Max(0.05f, arm)));
            // Rearing: up onto the hind legs through the windup, slamming down as it lands.
            var rear = attack <= 0f ? 0f : attack < 0.7f ? Mathf.SmoothStep(0f, 1f, attack / 0.7f) : 1f - Mathf.SmoothStep(0f, 1f, (attack - 0.7f) / 0.3f);
            var pitch = dead ? 0f : Mathf.Lerp(_bearPitch, 10f, rear);
            var cycle = _gait.Phase * Mathf.PI * 2f;
            // A heavy walk: the back rolls and the head swings with the steps.
            var roll = Quaternion.AngleAxis(Mathf.Sin(cycle) * 4f * w, Vector3.forward);
            var tip = Quaternion.AngleAxis(pitch, Vector3.right);
            var hindR = _gait.Leg(0f, 8f, 45f);
            var hindL = _gait.Leg(0.5f, 8f, 45f);
            _b[0].localPosition = _hipsRest + Vector3.up * (_gait.HipsOffset(hindR, hindL, thigh, shin) / _scale);
            Set((int)Bone.Hips, roll * Quaternion.AngleAxis(pitch * 0.25f, Vector3.right));
            Set((int)Bone.Spine, roll * tip);
            Set((int)Bone.Chest, Quaternion.Inverse(roll) * tip);
            Set((int)Bone.ShoulderL, Quaternion.Inverse(roll) * tip);
            Set((int)Bone.ShoulderR, Quaternion.Inverse(roll) * tip);
            // The neck and head lift back up to look ahead, swinging a little from side to side.
            var look = Quaternion.AngleAxis(Mathf.Sin(cycle) * 6f * w + Mathf.Sin(t * 0.5f) * 5f * (1f - w), Vector3.up);
            Set((int)Bone.Neck, look * Quaternion.AngleAxis(pitch * 0.45f, Vector3.right));
            Set((int)Bone.Head, look * Quaternion.AngleAxis(pitch * 0.1f - 8f * rear, Vector3.right));
            LegBones((int)Bone.UpperLegR, (int)Bone.LowerLegR, (int)Bone.FootR, hindR, axis);
            LegBones((int)Bone.UpperLegL, (int)Bone.LowerLegL, (int)Bone.FootL, hindL, axis);
            // The forelegs: left with the right hind leg, right with the left (a bear's walk); reared up, the paws up
            // and forward, coming down with the slam.
            var foreL = _gait.Leg(0f, 0f, 35f, armSin, Length((int)Bone.LowerArmL), Length((int)Bone.HandL));
            var foreR = _gait.Leg(0.5f, 0f, 35f, armSin, Length((int)Bone.LowerArmR), Length((int)Bone.HandR));
            Foreleg(Bone.UpperArmL, Bone.LowerArmL, Bone.HandL, _armRestL, _foreRestL, foreL, axis, rear, -1f, dead);
            Foreleg(Bone.UpperArmR, Bone.LowerArmR, Bone.HandR, _armRestR, _foreRestR, foreR, axis, rear, 1f, dead);
        }

        private void Foreleg(Bone upper, Bone lower, Bone hand, Vector3 armRest, Vector3 foreRest, Gait.LegPose p, Vector3 axis,
            float rear, float side, bool dead)
        {
            // Walking: straight down to the ground, swinging with the gait; the elbow giving as the leg comes through.
            var down = new Vector3(side * 0.12f, -1f, 0f).normalized;
            var walk = Quaternion.AngleAxis(-p.Thigh, axis) * down;
            var raised = new Vector3(side * 0.35f, 0.9f, 0.6f).normalized;
            var dir = dead ? new Vector3(side * 0.4f, -1f, 0.3f).normalized : Vector3.Slerp(walk, raised, rear).normalized;
            var upperRot = Quaternion.FromToRotation(armRest, dir);
            Set((int)upper, upperRot);
            var foreDir = Quaternion.AngleAxis(p.Knee * (1f - rear), Vector3.right) * dir;
            var lowerRot = Quaternion.FromToRotation(upperRot * foreRest, foreDir) * upperRot;
            Set((int)lower, lowerRot);
            Set((int)hand, lowerRot);
        }

        private void LegBones(int upper, int lower, int foot, Gait.LegPose p, Vector3 axis)
        {
            var thigh = Quaternion.AngleAxis(-p.Thigh, axis);
            Set(upper, thigh);
            Set(lower, thigh * Quaternion.AngleAxis(p.Knee, Vector3.right));
            Set(foot, Quaternion.AngleAxis(-p.Foot, Vector3.right));
        }

        /// <summary>An arm pointing along <paramref name="dir"/> (model space), the elbow a little bent.</summary>
        private void Arm(Bone upper, Bone lower, Bone hand, Vector3 armRest, Vector3 foreRest, Vector3 dir, float side)
        {
            var upperRot = Quaternion.FromToRotation(armRest, dir);
            Set((int)upper, upperRot);
            // The forearm bends slightly inwards and up from the upper arm.
            var foreDir = Quaternion.AngleAxis(-side * 12f, Vector3.up) * Quaternion.AngleAxis(-10f, Vector3.Cross(dir, Vector3.up).normalized * side) * dir;
            var lowerRot = Quaternion.FromToRotation(upperRot * foreRest, foreDir) * upperRot;
            Set((int)lower, lowerRot);
            Set((int)hand, lowerRot);
        }

        // ------------------------------------------------------------------ four legs

        private void PoseFourLegs(float speed, Vector3 axis, float dt)
        {
            var t = Time.time + _seed;
            var dead = _zombie.IsDead;
            var attack = _zombie.AttackProgress;
            // Leg lengths (hip or shoulder joint to the foot), each leg keeping its own foot planted.
            var frontThigh = Length((int)Quad.FrontLowerLegR);
            var frontShin = Length((int)Quad.FrontFootR);
            var front = frontThigh + frontShin;
            var hindThigh = Length((int)Quad.HindLowerLegR);
            var hindShin = Length((int)Quad.HindAnkleR) + Length((int)Quad.HindFootR);
            var hind = hindThigh + hindShin;
            _gait.Advance(speed, dt, hindThigh, hindShin, 35f, AnimalCadence);
            var w = _gait.Weight;
            var v = Mathf.Max(speed, 0.3f);
            var maxSin = Mathf.Sin(35f * Mathf.Deg2Rad);
            float Swing(float leg) => Mathf.Min(maxSin, v * _gait.Stance * _gait.CycleTime / (2f * Mathf.Max(0.05f, leg)));
            // The trot: left front with right hind, right front with left hind.
            var reachF = Mathf.Lerp(6f, 16f, _gait.Run);
            var fl = _gait.Leg(0f, 6f, 55f, Swing(front), frontThigh, frontShin, 0f, reachF);
            var hr = _gait.Leg(0f, 8f, 50f, Swing(hind), -1f, -1f, 0f, reachF * 0.6f);
            var fr = _gait.Leg(0.5f, 6f, 55f, Swing(front), frontThigh, frontShin, 0f, reachF);
            var hl = _gait.Leg(0.5f, 8f, 50f, Swing(hind), -1f, -1f, 0f, reachF * 0.6f);
            _b[0].localPosition = _hipsRest + Vector3.up * (_gait.HipsOffset(hr, hl, hindThigh, hindShin) / _scale);
            var cycle = _gait.Phase * Mathf.PI * 2f;
            var roll = Quaternion.AngleAxis(Mathf.Sin(cycle) * 4f * w, Vector3.forward);
            var breathe = Quaternion.AngleAxis(Mathf.Sin(t * 2.3f) * 1.5f, Vector3.right);
            Set((int)Quad.Hips, roll);
            Set((int)Quad.Spine, roll * breathe);
            // The attack: the front of the body dips and drives forward with the bite.
            var bite = attack < 0.7f ? attack / 0.7f : 1f - (attack - 0.7f) / 0.3f;
            var chest = Quaternion.AngleAxis(6f * bite, Vector3.right) * Quaternion.Inverse(roll) * breathe;
            Set((int)Quad.Chest, chest);
            var nod = Quaternion.AngleAxis(Mathf.Cos(cycle * 2f) * 4f * w + 12f * bite, Vector3.right);
            var look = Quaternion.AngleAxis(Mathf.Sin(t * 0.6f) * 8f * (1f - w), Vector3.up);
            Set((int)Quad.Neck, look * nod * chest);
            var head = look * Quaternion.AngleAxis(-10f * bite, Vector3.right) * nod * chest;
            Set((int)Quad.Head, head);
            // The jaw hangs a little open, gapes on the attack.
            Set((int)Quad.Jaw, Quaternion.AngleAxis((dead ? 20f : 4f + Mathf.Sin(t * 3f) * 2f) + 30f * bite, Vector3.right) * head);
            Set((int)Quad.FrontShoulderL, chest);
            Set((int)Quad.FrontShoulderR, chest);
            FrontLeg(Quad.FrontUpperLegL, Quad.FrontLowerLegL, Quad.FrontFootL, fl, axis);
            FrontLeg(Quad.FrontUpperLegR, Quad.FrontLowerLegR, Quad.FrontFootR, fr, axis);
            HindLeg(Quad.HindUpperLegL, Quad.HindLowerLegL, Quad.HindAnkleL, Quad.HindFootL, hl, axis);
            HindLeg(Quad.HindUpperLegR, Quad.HindLowerLegR, Quad.HindAnkleR, Quad.HindFootR, hr, axis);
            // The tail swings side to side, each segment a little later and further.
            var tail = Quaternion.identity;
            for (var i = 0; i < 4; i++)
            {
                var swing = dead ? 0f : Mathf.Sin(t * (3f + 3f * w) - i * 0.6f) * (8f + 6f * i) * (0.5f + 0.5f * w);
                tail = Quaternion.AngleAxis(swing, Vector3.up) * Quaternion.AngleAxis(dead ? 0f : -4f, Vector3.right) * tail;
                Set((int)Quad.Tail1 + i, tail * roll);
            }
        }

        /// <summary>A foreleg: the wrist folds back as the leg comes through; the paw flat while down.</summary>
        private void FrontLeg(Quad upper, Quad lower, Quad foot, Gait.LegPose p, Vector3 axis)
        {
            var swing = Quaternion.AngleAxis(-p.Thigh, axis);
            Set((int)upper, swing);
            Set((int)lower, swing * Quaternion.AngleAxis(p.Knee, Vector3.right));
            Set((int)foot, Quaternion.AngleAxis(-p.Foot, Vector3.right));
        }

        /// <summary>A hind leg: the knee bends forward and the hock back as it comes through; the paw flat while down.</summary>
        private void HindLeg(Quad upper, Quad lower, Quad ankle, Quad foot, Gait.LegPose p, Vector3 axis)
        {
            var swing = Quaternion.AngleAxis(-p.Thigh, axis);
            Set((int)upper, swing);
            var knee = swing * Quaternion.AngleAxis(-p.Knee * 0.6f, Vector3.right);
            Set((int)lower, knee);
            Set((int)ankle, knee * Quaternion.AngleAxis(p.Knee * 1.1f, Vector3.right));
            Set((int)foot, Quaternion.AngleAxis(-p.Foot, Vector3.right));
        }
    }
}
