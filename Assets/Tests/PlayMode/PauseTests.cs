using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-08: Solo pauses (the game and its sound stop; Resume or Leave stage), and Co-op is open where there
    /// is no store (the editor, Windows).
    /// </summary>
    public class PauseTests
    {
        private GameSession _session;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        private static void Ask(PlayerHud hud, bool on) =>
            typeof(PlayerHud).GetMethod("SetAsked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(hud, new object[] { on });

        [UnityTest]
        public IEnumerator SoloPausesAndLeavingUnfreezes()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            for (var i = 0; i < 3; i++) yield return null;
            var hud = Object.FindObjectsByType<PlayerHud>(FindObjectsSortMode.None).FirstOrDefault();
            Assert.IsNotNull(hud);
            Assert.IsTrue(PlayerHud.CanPause, "Solo can pause");

            Ask(hud, true);
            yield return null;
            Assert.IsTrue(hud.Paused);
            Assert.AreEqual(0f, Time.timeScale, "the game stops");
            Assert.IsTrue(AudioListener.pause, "the sound stops");
            Assert.AreEqual(Vector2.zero, hud.Read(_session.LocalSurvivor).Move, "no input while paused");
            var at = _session.LocalSurvivor.transform.position;
            for (var i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(at, _session.LocalSurvivor.transform.position, "nothing moves");

            // Resume.
            Ask(hud, false);
            yield return null;
            Assert.IsFalse(hud.Paused);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(AudioListener.pause);

            // Leave stage from the pause: the menu runs again.
            Ask(hud, true);
            yield return null;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            Assert.AreEqual(1f, Time.timeScale, "leaving unfreezes");
            Assert.IsFalse(AudioListener.pause);
        }

        [Test]
        public void CoopIsOpenWithoutAStore()
        {
            Assert.IsFalse(Monetization.StoreSupported, "the editor has no store");
            Assert.IsTrue(Monetization.CoopUnlocked);
            Assert.IsFalse(Monetization.TryShowPauseAd(), "no ads without a store");
        }
    }
}
