using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-07: the teammate's movement looked jittery. Over a simulated bad connection (20 updates a second,
    /// each 80-200 ms late at random, one in ten lost) the played-back teammate moves at an even speed and never backwards;
    /// shown the moment each update arrives (as before), it doesn't.
    /// </summary>
    public class RemoteMotionTests
    {
        private const float Speed = 5f;

        private struct Packet
        {
            public float Arrive, Sent;
            public Vector3 P;
        }

        private static List<Packet> Network(int seed)
        {
            var rng = new System.Random(seed);
            var packets = new List<Packet>();
            for (var sent = 0f; sent < 10f; sent += 0.05f)
            {
                if (rng.NextDouble() < 0.1) continue;
                packets.Add(new Packet { Sent = sent, Arrive = sent + 0.08f + (float)rng.NextDouble() * 0.12f, P = new Vector3(sent * Speed, 0f, 0f) });
            }
            packets.Sort((a, b) => a.Arrive.CompareTo(b.Arrive));
            return packets;
        }

        /// <summary>The displayed speed each frame (60 a second) after the first second.</summary>
        private static List<float> Play(List<Packet> packets, bool smooth)
        {
            var motion = new RemoteMotion();
            var shown = Vector3.zero;
            var latest = Vector3.zero;
            var latestSent = -1f;
            var next = 0;
            var speeds = new List<float>();
            var last = Vector3.zero;
            for (var now = 0f; now < 10f; now += 1f / 60f)
            {
                while (next < packets.Count && packets[next].Arrive <= now)
                {
                    var p = packets[next++];
                    motion.Add(p.Sent, p.Arrive, p.P, 0f);
                    if (p.Sent > latestSent)
                    {
                        latestSent = p.Sent;
                        latest = p.P;
                    }
                }
                if (smooth) motion.Get(now, out shown, out _);
                else shown = latest;
                if (now > 1f) speeds.Add((shown.x - last.x) * 60f);
                last = shown;
            }
            return speeds;
        }

        private static float ShareEven(List<float> speeds)
        {
            var even = 0;
            foreach (var v in speeds) if (v > Speed * 0.7f && v < Speed * 1.3f) even++;
            return even / (float)speeds.Count;
        }

        [Test]
        public void TheTeammateMovesEvenlyOverABadConnection()
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var smooth = Play(Network(seed), true);
                Assert.Greater(ShareEven(smooth), 0.95f, $"even speed (seed {seed})");
                Assert.IsTrue(smooth.TrueForAll(v => v > -0.01f), "never backwards");
                var raw = Play(Network(seed), false);
                Assert.Less(ShareEven(raw), 0.4f, "shown on arrival (as before), it jumps and stops");
            }
        }

        [Test]
        public void ItCarriesOnBrieflyWhenUpdatesStopThenWaits()
        {
            var motion = new RemoteMotion();
            for (var t = 0f; t <= 1f; t += 0.05f) motion.Add(t, t + 0.1f, new Vector3(t * Speed, 0f, 0f), 0f);
            motion.Get(1.1f, out var at, out _);
            motion.Get(3f, out var later, out _);
            Assert.LessOrEqual(later.x, Speed * (1f + RemoteMotion.MaxExtrapolate) + 0.01f, "waits after a moment");
            Assert.GreaterOrEqual(later.x, at.x, "doesn't go back");
        }

        [Test]
        public void ATeleportIsShownAtOnce()
        {
            var motion = new RemoteMotion();
            for (var t = 0f; t <= 0.5f; t += 0.05f) motion.Add(t, t + 0.1f, new Vector3(t, 0f, 0f), 0f);
            motion.Add(0.55f, 0.65f, new Vector3(50f, 0f, 0f), 0f);
            motion.Get(0.9f, out var p, out _);
            Assert.AreEqual(50f, p.x, 0.01f, "a respawn far away isn't slid across the map");
        }
    }
}
