using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-09: zombies never appear inside the area a player's camera shows.</summary>
    public class SpawnViewTests
    {
        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 41);
            yield return new WaitForSeconds(0.7f);
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheViewMatchesTheCamera()
        {
            // Points just inside the real camera's picture are in view; a point well outside is not.
            var cam = Camera.main;
            var p = Player.transform.position;
            foreach (var v in new[] { new Vector2(0.02f, 0.5f), new Vector2(0.98f, 0.5f), new Vector2(0.5f, 0.02f), new Vector2(0.5f, 0.98f) })
            {
                var ray = cam.ViewportPointToRay(new Vector3(v.x, v.y, 0f));
                new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var d);
                var ground = ray.GetPoint(d);
                Assert.IsTrue(SpawnView.Seen(ground), $"the screen edge at {v} is in view");
            }
            Assert.IsFalse(SpawnView.Seen(p + new Vector3(30f, 0f, 0f)));
            Assert.IsFalse(SpawnView.Seen(p + new Vector3(0f, 0f, 16f)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator NothingAppearsOnScreen()
        {
            var p = Player.transform.position;
            var population = _session.Stage.Population;
            Assert.IsFalse(_session.Stage.Placing);
            // Added in view: it waits dormant, never real.
            var before = Zombie.Alive.Count;
            population.Add(ZombieType.Basic, p + new Vector3(6f, 0f, 0f));
            yield return null;
            yield return null;
            Assert.AreEqual(before, Zombie.Alive.Count, "not real in view");
            Assert.AreEqual(1, Enumerable.Count(population.Records), "dormant");
            // The player walks away: once it's out of view it wakes.
            var cc = Player.GetComponent<CharacterController>();
            cc.enabled = false;
            Player.transform.position = p + new Vector3(-16f, 0f, 0f); // 22 m away: out of view, within the 26 m wake-up range
            cc.enabled = true;
            for (var i = 0; i < 30 && Zombie.Alive.Count == before; i++) yield return null;
            Assert.Greater(Zombie.Alive.Count, before, "wakes out of view (within reach)");
        }
    }
}
