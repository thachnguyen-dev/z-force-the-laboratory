using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-09: a character pushed by zombies against the surrounding fence was knocked outside it and couldn't
    /// get back. Nothing may carry a character through a fence or a wall.
    /// </summary>
    public class FenceTests
    {
        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

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

        private static void Put(Survivor s, Vector3 at)
        {
            var cc = s.GetComponent<CharacterController>();
            cc.enabled = false;
            s.transform.position = at;
            cc.enabled = true;
        }

        [UnityTest]
        public IEnumerator AZombieCrowdCantPushAPlayerThroughTheEndlessFenceAtAnyFrameRate()
        {
            // Owner, 2026-10-11: still through the fence; the crowd's push out of overlaps is long on any frame rate.
            foreach (var fps in new[] { 60, 20, 8 })
            {
                Time.captureFramerate = fps;
                try
                {
                    var run = AZombieCrowdCantPushAPlayerThroughTheEndlessFence();
                    while (run.MoveNext()) yield return run.Current;
                }
                finally
                {
                    Time.captureFramerate = 0;
                }
                yield return TearDown();
            }
        }

        [UnityTest]
        public IEnumerator AZombieCrowdCantPushAPlayerThroughTheEndlessFenceAtFourFramesASecond()
        {
            // A slow phone: a quarter of a second a frame, so a boss's knockback carries the player far in one frame.
            Time.captureFramerate = 4;
            try
            {
                var run = AZombieCrowdCantPushAPlayerThroughTheEndlessFence();
                while (run.MoveNext()) yield return run.Current;
            }
            finally
            {
                Time.captureFramerate = 0;
            }
        }

        [UnityTest]
        public IEnumerator AZombieCrowdCantPushAPlayerThroughTheEndlessFence()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(Endless.StageId), _session.Tuning.characters[0], 41);
            for (var i = 0; i < 5; i++) yield return null;
            foreach (var z in Zombie.Alive.ToArray()) Object.Destroy(z.gameObject);
            var size = _session.Stage.Layout.Size;
            // Against the north fence line (2 m in from the map's edge), halfway along.
            var line = size.y - 2f;
            var at = new Vector3(size.x * 0.5f, 0f, line - 0.6f);
            Put(Player, at);
            yield return null;
            // A crowd pressing in from the inside, bosses that knock hard among them.
            var types = new[] { ZombieType.Tank, ZombieType.Boss, ZombieType.Basic, ZombieType.Basic, ZombieType.Fast, ZombieType.Tank, ZombieType.Basic, ZombieType.Bear };
            for (var i = 0; i < types.Length; i++)
            {
                var a = Mathf.Lerp(-70f, 70f, i / (types.Length - 1f)) * Mathf.Deg2Rad;
                _session.Stage.SpawnZombie(types[i], at + new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)) * 1.2f);
            }
            var worst = 0f;
            for (var t = 0f; t < 8f && !Player.IsDead; t += Time.deltaTime)
            {
                typeof(Survivor).GetProperty("Hp").SetValue(Player, Player.MaxHp); // kept alive to keep being pushed
                worst = Mathf.Max(worst, Player.transform.position.z);
                yield return null;
            }
            Assert.Less(worst, line, $"stays inside the fence line at z {line:0.0} (got to {worst:0.00})");
        }

        [UnityTest]
        public IEnumerator AHugeKnockbackCantCarryACharacterThroughTheFence()
        {
            // Through the controller, as in play: a 60 m/s shove straight at the fence on a slow frame (15 m in one frame).
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(Endless.StageId), _session.Tuning.characters[0], 41);
            for (var i = 0; i < 5; i++) yield return null;
            foreach (var z in Zombie.Alive.ToArray()) Object.Destroy(z.gameObject);
            var size = _session.Stage.Layout.Size;
            var line = size.y - 2f;
            Put(Player, new Vector3(size.x * 0.5f, 0f, line - 0.6f));
            for (var i = 0; i < 3; i++) yield return null;
            Time.captureFramerate = 4;
            try
            {
                Player.AddKnockback(new Vector3(0f, 0f, 60f));
                var worst = 0f;
                for (var i = 0; i < 12; i++)
                {
                    yield return null;
                    worst = Mathf.Max(worst, Player.transform.position.z);
                }
                Assert.Less(worst, line, $"stays inside the fence line at z {line:0.0} (got to {worst:0.00})");
            }
            finally
            {
                Time.captureFramerate = 0;
            }
        }
    }
}
