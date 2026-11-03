using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-10: ad consent: asked in the EU, the EEA, the UK and Switzerland before any ad; elsewhere not.</summary>
    public class AdConsentTests
    {
        private const string Key = "zforce.adConsent";
        private int _saved;
        private bool _had;

        [SetUp]
        public void SetUp()
        {
            _had = PlayerPrefs.HasKey(Key);
            _saved = PlayerPrefs.GetInt(Key, 0);
            PlayerPrefs.DeleteKey(Key);
            AdConsent.AdsHereOverride = true;
        }

        [TearDown]
        public void TearDown()
        {
            AdConsent.AdsHereOverride = null;
            AdConsent.CountryOverride = null;
            if (_had) PlayerPrefs.SetInt(Key, _saved);
            else PlayerPrefs.DeleteKey(Key);
        }

        [Test]
        public void TheConsentCountries()
        {
            foreach (var c in new[] { "DE", "FR", "it", "PL", "IE", "NO", "IS", "LI", "GB", "CH" }) Assert.IsTrue(AdConsent.InConsentRegion(c), c);
            foreach (var c in new[] { "VN", "US", "JP", "BR", "IN", "TR", "UA" }) Assert.IsFalse(AdConsent.InConsentRegion(c), c);
            Assert.IsTrue(AdConsent.InConsentRegion(""), "unknown: asked, to be safe");
        }

        [Test]
        public void InEuropeTheAdsWaitForTheChoice()
        {
            AdConsent.CountryOverride = "DE";
            Assert.IsTrue(AdConsent.MustAsk);
            Assert.IsFalse(AdConsent.Ready, "no ad before the choice");
            Assert.IsFalse(AdConsent.Personalised, "nothing personalised before a yes");
            AdConsent.Choose(false);
            Assert.IsFalse(AdConsent.MustAsk);
            Assert.IsTrue(AdConsent.Ready);
            Assert.IsFalse(AdConsent.Personalised);
            AdConsent.Choose(true);
            Assert.IsTrue(AdConsent.Personalised, "changed in Settings");
        }

        [Test]
        public void ElsewhereNobodyIsAskedAndTheyCanSayNo()
        {
            AdConsent.CountryOverride = "VN";
            Assert.IsFalse(AdConsent.MustAsk);
            Assert.IsTrue(AdConsent.Ready);
            Assert.IsTrue(AdConsent.Personalised, "personalised unless switched off");
            AdConsent.Choose(false);
            Assert.IsFalse(AdConsent.Personalised, "switched off in Settings");
        }

        [Test]
        public void NoAdsNoQuestion()
        {
            AdConsent.AdsHereOverride = false;
            AdConsent.CountryOverride = "DE";
            Assert.IsFalse(AdConsent.MustAsk, "no store or ads here (the PC): nothing to ask");
        }
    }
}
