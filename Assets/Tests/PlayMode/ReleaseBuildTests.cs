using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-10: the store (release) build showed the "Unlock all (test)" switch. A release build has no testing
    /// tools: no switch, no full list of test maps, and a switch left on in old settings opens nothing.
    /// </summary>
    public class ReleaseBuildTests
    {
        private GameSession _session;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Gui.DevToolsOverride = null;
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        private static string[] MenuTexts() =>
            Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled).Select(t => t.text).ToArray();

        [UnityTest]
        public IEnumerator AReleaseBuildHasNoTestingTools()
        {
            Gui.DevToolsOverride = false;
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            foreach (var coop in new[] { false, true })
            {
                typeof(GameSession).GetProperty("WithAi")?.SetValue(_session, coop);
                _session.GoTo(SessionState.StageSelect);
                for (var i = 0; i < 4; i++) yield return null;
                var texts = MenuTexts();
                Assert.IsFalse(texts.Any(t => t.Contains("Unlock all")), "no test switch");
                Assert.IsFalse(texts.Any(t => t == "All stages" || t == "All"), "no full list of test maps");
            }
            // A switch left on from a development build opens nothing in a release build.
            var settings = new SettingsData { unlockAllForTesting = true };
            Assert.IsFalse(Campaign.Unlocked(1, settings, false), "the second mission stays locked");
            Assert.IsFalse(Campaign.TestUnlock(settings));

            // A development build still has the switch.
            Gui.DevToolsOverride = true;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            _session.GoTo(SessionState.StageSelect);
            for (var i = 0; i < 4; i++) yield return null;
            Assert.IsTrue(MenuTexts().Any(t => t.Contains("Unlock all")), "development builds keep the switch");
            Assert.IsTrue(Campaign.Unlocked(1, settings, false));
        }
    }
}
