using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    /// <summary>
    /// The walk (owner, 2026-10-07: "the movement animation is the primary animation for all objects"): feet stay
    /// planted while down, steps come at a natural rate for the legs' size, and the table of what every object does is
    /// written to the log ([ZOMBIE-GAIT]).
    /// </summary>
    public class GaitTests
    {
        /// <summary>
        /// Walks a body at <paramref name="speed"/> for 4 s at 60 frames a second and measures how far the right foot
        /// slides along the ground while it is down (the hip moves on; the foot is the hip plus the leg's reach).
        /// </summary>
        private static (float slip, Gait g) Walk(float speed, float thigh, float shin, float maxSwing, float cadence)
        {
            var g = new Gait();
            const float dt = 1f / 60f;
            var body = 0f;
            var slip = 0f;
            float? plantedAt = null;
            for (var i = 0; i < 240; i++)
            {
                g.Advance(speed, dt, thigh, shin, maxSwing, cadence);
                body += speed * dt;
                if (i < 60) continue;   // let it get going
                var p = g.Leg(0f, 10f, 60f);
                var t = p.Thigh * Mathf.Deg2Rad;
                var s = (p.Thigh - p.Knee) * Mathf.Deg2Rad;
                var foot = body + thigh * Mathf.Sin(t) + shin * Mathf.Sin(s);
                if (p.Planted)
                {
                    if (plantedAt == null) plantedAt = foot;
                    slip = Mathf.Max(slip, Mathf.Abs(foot - plantedAt.Value));
                }
                else plantedAt = null;
            }
            return (slip, g);
        }

        [Test]
        public void FeetStayPlantedAndStepsComeAtANaturalRate()
        {
            var table = new StringBuilder("[ZOMBIE-GAIT] who, speed m/s: steps per second, frames per step at 60 fps, step length m, foot down %, thigh swing °, foot slide cm\n");
            void Row(string who, float speed, float thigh, float shin, float maxSwing, float cadence, float maxSlideCm)
            {
                var (slip, g) = Walk(speed, thigh, shin, maxSwing, cadence);
                var swing = Mathf.Asin(g.SinSwing) * Mathf.Rad2Deg;
                table.AppendLine($"  {who}, {speed:0.0}: {g.StepsPerSecond:0.0} steps/s, {60f / g.StepsPerSecond:0.0} frames/step, {g.StepLength:0.00} m, {g.Stance * 100f:0} %, {swing:0}°, slide {slip * 100f:0.0} cm");
                Assert.Less(slip * 100f, maxSlideCm, $"{who} at {speed} m/s: the foot slides while down");
                Assert.That(g.StepsPerSecond, Is.InRange(0.8f, 12f), who);
            }
            // Leg lengths from the baked models (thigh, shin in m; zombies at their type's height).
            Row("survivor (chr_04) walking", 1.5f, 0.41f, 0.35f, 25f, CharacterPuppet.LegCadence, 2f);
            Row("survivor (chr_04) running", 5f, 0.41f, 0.35f, 25f, CharacterPuppet.LegCadence, 2f);
            Row("Character 01 running (+20 %)", 6f, 0.32f, 0.39f, 25f, CharacterPuppet.LegCadence, 2f);
            Row("Basic zombie wandering", 1f, 0.36f, 0.29f, 30f, ZombiePuppet.ShambleCadence, 2f);
            Row("Basic zombie chasing", 2.7f, 0.36f, 0.29f, 30f, ZombiePuppet.ShambleCadence, 2f);
            Row("Fast zombie chasing", 4.8f, 0.36f, 0.29f, 30f, ZombiePuppet.ShambleCadence, 2f);
            Row("Boss (boss_1) chasing", 2.4f, 0.45f, 0.45f, 30f, ZombiePuppet.ShambleCadence, 2f);
            Row("Dog chasing (hind leg)", 4.4f, 0.25f, 0.25f, 35f, ZombiePuppet.AnimalCadence, 2f);
            Row("Rat chasing (hind leg)", 5.2f, 0.1f, 0.09f, 35f, ZombiePuppet.AnimalCadence, 2f);
            Row("Tiger chasing (hind leg)", 3.4f, 0.31f, 0.31f, 35f, ZombiePuppet.AnimalCadence, 2f);
            Debug.Log(table.ToString());
        }

        [Test]
        public void StandingStillTheLegsAreStraightAndTheHipsLevel()
        {
            var g = new Gait();
            for (var i = 0; i < 120; i++) g.Advance(0f, 1f / 60f, 0.41f, 0.35f, 25f, 1f);
            var r = g.Leg(0f, 10f, 60f);
            var l = g.Leg(0.5f, 10f, 60f);
            Assert.AreEqual(0f, r.Thigh, 0.01f);
            Assert.AreEqual(0f, l.Knee, 0.01f);
            Assert.AreEqual(0f, g.HipsOffset(r, l, 0.41f, 0.35f), 0.001f);
        }
    }
}
