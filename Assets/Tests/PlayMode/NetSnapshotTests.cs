using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-07 (Co-op over a join code was very laggy): the snapshot is sent unreliably, so it must always fit
    /// one unreliable message, however many zombies there are; the packed zombie entries keep their values.
    /// </summary>
    public class NetSnapshotTests
    {
        private GameSession _session;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ASnapshotFitsOneUnreliableMessageEvenWhenCrowded()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("m1_gate_a"), _session.Tuning.characters[0], 41, withAi: true);
            yield return null;
            var p = _session.LocalSurvivor.transform.position;
            for (var i = 0; i < 120; i++) _session.Stage.SpawnZombie(ZombieType.Basic, p + Quaternion.Euler(0f, i * 13f, 0f) * Vector3.forward * (4f + i * 0.2f));
            yield return null;
            Assert.Greater(Zombie.Alive.Count, 60, "a crowd");
            var host = new GameObject("Host").AddComponent<NetHost>();
            using (var w = new FastBufferWriter(Wire.SnapshotLimit, Allocator.Temp, 1 << 16))
            {
                host.WriteSnapshot(w, p);
                Assert.LessOrEqual(w.Length, Wire.SnapshotLimit, "fits one unreliable message");
            }
            Object.Destroy(host.gameObject);
        }

        [UnityTest]
        public IEnumerator OnTheHostTheFriendIsDrawnSmoothlyButStaysWhereTheyReallyAre()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("m7_collection_a"), _session.Tuning.characters[0], 41, withAi: true);
            yield return null;
            // The AI teammate stands in for the online friend: updates every 50 ms, arriving unevenly.
            var friend = _session.AiSurvivor;
            friend.InputSource = null;
            var start = friend.transform.position;
            var rng = new System.Random(5);
            var sent = 0f;
            // What is drawn, read where the camera reads it: at the end of each frame.
            var probe = friend.gameObject.AddComponent<DrawnProbe>();
            probe.Watch = friend.Visual;
            probe.Real = friend.transform;
            var t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 2.5f)
            {
                var now = Time.unscaledTime - t0;
                // Send whatever is due, but let them through in uneven bursts.
                while (sent <= now && rng.NextDouble() < 0.6)
                {
                    var p = start + new Vector3(sent * 3f, 0f, 0f);
                    friend.transform.position = p;
                    friend.AddRemoteSample(t0 + sent, p, 90f);
                    sent += 0.05f;
                }
                probe.Recording = now > 0.6f;
                yield return null;
            }
            var shown = probe.Speeds;
            Assert.Greater(probe.AverageLag, 0.05f, "drawn a moment behind, on average");
            var even = shown.FindAll(v => v > 3f * 0.6f && v < 3f * 1.4f).Count / (float)shown.Count;
            Assert.Greater(even, 0.85f, "at an even speed");
            Assert.IsTrue(shown.TrueForAll(v => v > -0.01f), "never backwards");
            // The shadow and the flashlight go with what is drawn.
            var blob = friend.transform.Find("Blob shadow");
            var away = new Vector3(-TileAssembler.ToSun.x, 0f, -TileAssembler.ToSun.z).normalized;
            var offset = blob.position - friend.Visual.position;
            Assert.Less(Vector3.Cross(new Vector3(offset.x, 0f, offset.z), away).magnitude, 0.05f, "the shadow lies by the drawn body");
        }

        [Test]
        public void APackedZombieKeepsItsValues()
        {
            using var w = new FastBufferWriter(64, Allocator.Temp);
            Wire.WriteZombie(w, 123456, 3, new Vector3(271.37f, 0f, 3.21f), 271f, 1234.5f, 0.5f);
            Assert.AreEqual(Wire.ZombieEntry, w.Length, "the entry size the budget uses");
            using var r = new FastBufferReader(w, Allocator.Temp);
            Wire.ReadZombie(r, out var id, out var type, out var pos, out var yaw, out var hp, out var slam);
            Assert.AreEqual(123456, id);
            Assert.AreEqual(3, type);
            Assert.AreEqual(271.37f, pos.x, 0.011f);
            Assert.AreEqual(3.21f, pos.z, 0.011f);
            Assert.AreEqual(271f, yaw, 1.5f);
            Assert.AreEqual(1234.5f, hp, 1.5f);
            Assert.AreEqual(0.5f, slam, 0.01f);
        }
    }

    /// <summary>Test helper: the watched transform's speed along x, read after everything else each frame.</summary>
    [DefaultExecutionOrder(10000)]
    public class DrawnProbe : MonoBehaviour
    {
        public Transform Watch;
        public Transform Real;
        public bool Recording;
        private float _lagSum;
        private int _lagFrames;
        /// <summary>How far behind the real position the watched one was, on average while recording (along x).</summary>
        public float AverageLag => _lagFrames > 0 ? _lagSum / _lagFrames : 0f;
        public readonly System.Collections.Generic.List<float> Speeds = new System.Collections.Generic.List<float>();
        private Vector3 _last;
        private bool _any;

        private void LateUpdate()
        {
            if (Watch == null) return;
            var p = Watch.position;
            if (_any && Recording) Speeds.Add((p.x - _last.x) / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
            if (Recording && Real != null)
            {
                _lagSum += Real.position.x - p.x;
                _lagFrames++;
            }
            _last = p;
            _any = true;
        }
    }
}
