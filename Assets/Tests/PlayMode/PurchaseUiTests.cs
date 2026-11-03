using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-14: on iPhone, Restore purchase stays after the purchase (a reviewer whose test account already owns
    /// it must still find it); only Buy goes away.
    /// </summary>
    public class PurchaseUiTests
    {
        private const string OwnedKey = "zforce.removeAdsCoop";
        private GameSession _session;
        private int _ownedBefore;

        [SetUp]
        public void SetUp() => _ownedBefore = PlayerPrefs.GetInt(OwnedKey, 0);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Monetization.RestoreShownOverride = null;
            PlayerPrefs.SetInt(OwnedKey, _ownedBefore);
            if (_session == null) yield break;
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        private IEnumerator OpenOffer(bool owned, bool iphone)
        {
            PlayerPrefs.SetInt(OwnedKey, owned ? 1 : 0);
            Monetization.RestoreShownOverride = iphone;
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            var menus = typeof(GameSession).GetField("_menus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(_session);
            menus.GetType().GetMethod("ShowOffer").Invoke(menus, null);
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static bool HasButton(string startsWith)
        {
            foreach (var b in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
                if (b.name.StartsWith("Button " + startsWith)) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator OnIphoneRestoreStaysAfterThePurchase()
        {
            yield return OpenOffer(owned: true, iphone: true);
            Assert.IsTrue(HasButton("Restore purchase"), "Restore after the purchase");
            Assert.IsFalse(HasButton("Buy"), "no Buy once owned");
        }

        [UnityTest]
        public IEnumerator OnIphoneBuyAndRestoreBeforeThePurchase()
        {
            yield return OpenOffer(owned: false, iphone: true);
            Assert.IsTrue(HasButton("Restore purchase"));
            Assert.IsTrue(HasButton("Buy"));
        }

        [UnityTest]
        public IEnumerator OnAndroidNoRestore()
        {
            yield return OpenOffer(owned: true, iphone: false);
            Assert.IsFalse(HasButton("Restore purchase"));
        }
    }
}
