using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// A walk and run cycle that follows how legs really move (owner, 2026-10-07: "the movement animation is the primary
    /// animation for all objects"), shared by the survivors, the zombies and the animals, scaled to each one's legs:
    /// - Step rate: from the leg length and the speed by dynamic similarity (the Froude scaling biomechanics uses to
    ///   compare legged animals of any size): human data, a 1.4 m/s walk at 1.8 steps/s and a 5 m/s run at 2.9 steps/s
    ///   on 0.9 m legs, as dimensionless step rate = 0.415 + 0.277 × dimensionless speed, times a style factor.
    /// - Planted feet: while a foot is on the ground it stays where it is: the leg swings back exactly as fast as the
    ///   body moves forward (sin of the thigh angle linear in time), the foot flat. The contact time shrinks rather than
    ///   the swing growing past its limit.
    /// - Walk to run: the contact fraction goes from 0.62 (walking: both feet down for a moment) to 0.32 (running:
    ///   a moment with both feet off the ground) round the walk-run speed of these legs.
    /// - The swing: the knee bends and the foot lifts and comes through, toes pointing down at push-off and up just
    ///   before the heel lands.
    /// - The hips ride on the leg that's on the ground (their height is that leg's own geometry), and rise in the
    ///   airborne moment of a run as a body thrown by its last step would.
    /// Phase: 0 = the first leg's contact, 0.5 = the other's. A leg's angles are degrees, forward positive.
    /// </summary>
    public class Gait
    {
        private const float G = 9.81f;

        /// <summary>The cycle, 0..1 (the first leg touching down at 0).</summary>
        public float Phase;
        /// <summary>Fraction of the cycle a foot is on the ground.</summary>
        public float Stance = 0.62f;
        /// <summary>Sine of the thigh's swing either side of straight down while a foot is on the ground.</summary>
        public float SinSwing;
        /// <summary>0 standing … 1 moving: everything scales by it, so starting and stopping blend.</summary>
        public float Weight;
        /// <summary>0 walking … 1 running.</summary>
        public float Run;
        /// <summary>Seconds per cycle (two steps).</summary>
        public float CycleTime = 1f;
        /// <summary>How far one step carries the body (m), and steps per second: for the record.</summary>
        public float StepLength, StepsPerSecond;
        /// <summary>The legs' thigh and shin (m), set by <see cref="Advance"/>.</summary>
        public float Thigh = 0.4f, Shin = 0.36f;

        /// <summary>
        /// Moves the cycle on by <paramref name="dt"/> at <paramref name="speed"/> (m/s) for legs of
        /// <paramref name="thigh"/> + <paramref name="shin"/> (m, hip joint to ankle), the thigh swinging at most
        /// <paramref name="maxSwingDeg"/>; <paramref name="cadence"/> scales the step rate (above 1 quicker, shorter steps).
        /// </summary>
        public void Advance(float speed, float dt, float thigh, float shin, float maxSwingDeg, float cadence)
        {
            Thigh = Mathf.Max(0.02f, thigh);
            Shin = Mathf.Max(0.02f, shin);
            var leg = Thigh + Shin;
            Weight = Mathf.MoveTowards(Weight, speed > 0.15f ? 1f : 0f, dt * 5f);
            var v = Mathf.Max(speed, 0.3f);
            var vhat = v / Mathf.Sqrt(G * leg);
            // Walk-run: people change gait at a Froude number of about 0.5 (dimensionless speed about 0.7).
            Run = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, vhat));
            StepsPerSecond = (0.415f + 0.277f * vhat) * cadence * Mathf.Sqrt(G / leg);
            CycleTime = 2f / StepsPerSecond;
            StepLength = v / StepsPerSecond;
            var s = Mathf.Lerp(0.62f, 0.32f, Run);
            var maxSin = Mathf.Sin(maxSwingDeg * Mathf.Deg2Rad);
            // Planted: the foot travels back 2 L sin A while it is down, as far as the body goes in that time.
            if (v * s * CycleTime / (2f * leg) > maxSin) s = 2f * leg * maxSin / (v * CycleTime);
            Stance = Mathf.Clamp(s, 0.2f, 0.7f);
            SinSwing = Mathf.Min(maxSin, v * Stance * CycleTime / (2f * leg));
            if (speed > 0.05f) Phase = Mathf.Repeat(Phase + dt / CycleTime, 1f);
        }

        public struct LegPose
        {
            /// <summary>The thigh's swing (forward positive), the knee's bend, the foot's pitch (toes up positive).</summary>
            public float Thigh, Knee, Foot;
            public bool Planted;
            /// <summary>0..1 through the airborne part of a step (running), for the hips' rise.</summary>
            public float Flight;
            /// <summary>1 - how far the knee's bend carries the foot back along the walk (0 walking forward; 2 backward).</summary>
            public float KneeAway;
        }

        /// <summary>
        /// One leg <paramref name="offset"/> of a cycle after the first (0.5 for the other leg of a pair);
        /// <paramref name="sinSwing"/> overrides the swing (four legs of different lengths keep their own feet planted).
        /// <paramref name="centre"/> (m): how far ahead of the hip joint, along the walk, the feet land and push off
        /// round (under the body's weight when the upper body leans; owner, 2026-10-07: "both legs positioned too far
        /// backward"). <paramref name="drive"/> (degrees): how far past its landing angle the thigh comes forward in
        /// the swing (the knee driving forward, as in a run).
        /// </summary>
        /// <param name="kneeAlong">
        /// How much the knee's bend carries the foot back against the walk: 1 walking forward (the default), 0 sideways,
        /// -1 backward (owner, 2026-10-08: walking backward the feet were 31-35 cm behind the body: the knee, bending as
        /// knees do, carries the foot the way the walk goes, and the drive pushed the thigh that way too). Below 1 the
        /// swing's thigh is solved so the foot keeps its track under the body, without the drive.
        /// </param>
        public LegPose Leg(float offset, float kneeStance, float kneeSwing, float sinSwing = -1f, float thigh = -1f, float shin = -1f,
            float centre = 0f, float drive = 0f, float kneeAlong = 1f)
        {
            var lt = thigh > 0f ? thigh : Thigh;
            var ls = shin > 0f ? shin : Shin;
            var u = Mathf.Repeat(Phase + offset, 1f);
            var sinA = sinSwing >= 0f ? sinSwing : SinSwing;
            var a = Mathf.Asin(Mathf.Clamp(sinA, -1f, 1f)) * Mathf.Rad2Deg;
            var p = new LegPose();
            if (u < Stance)
            {
                // On the ground: back at a steady rate; the knee gives a little under the weight; the foot flat, then
                // rolling onto the toes as the step ends.
                var k = u / Stance;
                p.Knee = kneeStance * Mathf.Sin(Mathf.PI * k);
                // The foot's place along the ground, ahead of the hip: from the reach at touch-down back at the body's
                // speed. The thigh angle that puts it there with the knee as bent as it is (a few Newton steps).
                var x = (lt + ls) * sinA * (1f - 2f * k) + centre * Weight;
                p.Thigh = Solve(x, p.Knee * Mathf.Deg2Rad * kneeAlong, lt, ls);
                p.Foot = -22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, k));
                p.Planted = true;
            }
            else
            {
                // In the air: the leg comes through, eased; the knee folds early and opens to reach; the toes go from
                // pointing down (push-off) to up just before the heel lands.
                var k = (u - Stance) / (1f - Stance);
                var e = k * k * (3f - 2f * k);
                // From where the foot left the ground to where it will land (both round the centre), the knee driving
                // forward on the way.
                var reach = lt + ls;
                var c = centre * Weight;
                var back = Mathf.Asin(Mathf.Clamp((-reach * sinA + c) / reach, -1f, 1f)) * Mathf.Rad2Deg;
                var front = Mathf.Asin(Mathf.Clamp((reach * sinA + c) / reach, -1f, 1f)) * Mathf.Rad2Deg;
                p.Thigh = Mathf.Lerp(back, front, e) + drive * Mathf.Sin(Mathf.PI * Mathf.Pow(k, 1.3f));
                p.Knee = kneeSwing * Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.75f));
                if (kneeAlong < 1f)
                {
                    // The foot along its track from where it left the ground to where it lands, the knee bent as it is.
                    var x = Mathf.Lerp(-reach * sinA + c, reach * sinA + c, e);
                    var solved = Solve(x, p.Knee * Mathf.Deg2Rad * kneeAlong, lt, ls);
                    p.Thigh = Mathf.Lerp(solved, p.Thigh, Mathf.Clamp01(kneeAlong));
                }
                p.Foot = Mathf.Lerp(-22f, 8f, Mathf.SmoothStep(0f, 1f, k));
                // The airborne part of the step this leg pushed off from (running only).
                var air = 0.5f - Stance;
                if (air > 0f && u - Stance < air) p.Flight = (u - Stance) / air;
            }
            p.Thigh *= Weight;
            p.Knee *= Weight;
            p.Foot *= Weight;
            p.KneeAway = 1f - kneeAlong;
            return p;
        }

        /// <summary>
        /// The thigh angle (degrees) that puts the foot <paramref name="x"/> m ahead of the hip joint along the walk, the
        /// knee bent so it carries the foot back by <paramref name="kn"/> (radians; negative carries it ahead).
        /// </summary>
        private static float Solve(float x, float kn, float lt, float ls)
        {
            var th = Mathf.Asin(Mathf.Clamp(x / (lt + ls), -1f, 1f)) + kn * ls / (lt + ls);
            for (var it = 0; it < 4; it++)
            {
                var f = lt * Mathf.Sin(th) + ls * Mathf.Sin(th - kn) - x;
                var d = lt * Mathf.Cos(th) + ls * Mathf.Cos(th - kn);
                if (Mathf.Abs(d) < 1e-4f) break;
                th -= f / d;
            }
            return th * Mathf.Rad2Deg;
        }

        /// <summary>
        /// How far the hips sit below standing (m, positive = lower) with this leg holding them: the leg's own
        /// geometry, thigh <paramref name="thigh"/> m and shin <paramref name="shin"/> m long.
        /// </summary>
        public static float Drop(LegPose p, float thigh, float shin)
        {
            var t = p.Thigh * Mathf.Deg2Rad;
            var s = (p.Thigh - p.Knee * (1f - p.KneeAway)) * Mathf.Deg2Rad;
            return thigh + shin - (thigh * Mathf.Cos(t) + shin * Mathf.Cos(s));
        }

        /// <summary>
        /// The hips' height offset (m) for a pair of legs: on the leg that's down (the straighter one if both are);
        /// in a run's airborne moment, the arc of a body thrown by its last step.
        /// </summary>
        public float HipsOffset(LegPose a, LegPose b, float thigh, float shin)
        {
            if (a.Planted || b.Planted)
            {
                var drop = float.MaxValue;
                if (a.Planted) drop = Mathf.Min(drop, Drop(a, thigh, shin));
                if (b.Planted) drop = Mathf.Min(drop, Drop(b, thigh, shin));
                return -drop * Weight;
            }
            // Airborne: from the height it left the ground at, up and back down (g t² / 8 at the top).
            var flight = Mathf.Max(a.Flight, b.Flight);
            var air = Mathf.Max(0f, 0.5f - Stance) * CycleTime;
            var edge = Drop(new LegPose { Thigh = -Mathf.Asin(SinSwing) * Mathf.Rad2Deg }, thigh, shin);
            return (-edge + G * air * air / 8f * 4f * flight * (1f - flight)) * Weight;
        }
    }
}
