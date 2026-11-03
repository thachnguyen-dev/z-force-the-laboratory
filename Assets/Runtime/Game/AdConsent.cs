using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Owner, 2026-10-10 (publishing on Google Play): consent for personalised ads. Players in the EU, the EEA, the UK and
    /// Switzerland choose on first launch (the ads wait for the answer); everyone else gets personalised ads unless they
    /// switch them off in Settings. The choice goes to LevelPlay (GDPR consent, and "do not sell" for US privacy laws)
    /// before the ads start, and again whenever it changes.
    /// </summary>
    public static class AdConsent
    {
        public enum Choice { NotAsked = 0, Personalised = 1, NotPersonalised = 2 }

        private const string Key = "zforce.adConsent";

        /// <summary>The privacy policy's web address (shown on the prompt once set). Set when it's online.</summary>
        public const string PrivacyPolicyUrl = "https://munngocthach-alt.github.io/z-foce---privacy/privacy-policy.html";

        /// <summary>
        /// The EU, the EEA (Iceland, Liechtenstein, Norway), the United Kingdom and Switzerland: where personalised ads
        /// need the player's consent first.
        /// </summary>
        public static readonly string[] ConsentCountries =
        {
            "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT", "LV", "LT", "LU", "MT",
            "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "IS", "LI", "NO", "GB", "CH",
        };

        /// <summary>The player's choice (NotAsked until they answer).</summary>
        public static Choice Current
        {
            get => (Choice)PlayerPrefs.GetInt(Key, 0);
            private set
            {
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Tests only: where the player is (a country code, "" = unknown), instead of asking the device.</summary>
        public static string CountryOverride;
        /// <summary>Tests only: behave as if the store and ads were here.</summary>
        public static bool? AdsHereOverride;

        private static string _country;

        /// <summary>The player's country (ISO code, upper case), or "" when the device can't tell.</summary>
        public static string Country
        {
            get
            {
                if (CountryOverride != null) return CountryOverride.ToUpperInvariant();
                return _country ?? (_country = DetectCountry());
            }
        }

        private static bool AdsHere => AdsHereOverride ?? Monetization.StoreSupported;

        /// <summary>
        /// The player must choose before any ad: ads exist here, they haven't bought them away, haven't answered yet, and
        /// are in a consent country (or the device can't tell where they are: asked, to be safe).
        /// </summary>
        public static bool MustAsk => AdsHere && !Monetization.Owned && Current == Choice.NotAsked && InConsentRegion(Country);

        /// <summary>True where consent is needed first; an unknown country counts as needing it.</summary>
        public static bool InConsentRegion(string country) =>
            string.IsNullOrEmpty(country) || Array.IndexOf(ConsentCountries, country.ToUpperInvariant()) >= 0;

        /// <summary>Personalised ads are allowed: the player said yes, or (outside the consent countries) didn't say no.</summary>
        public static bool Personalised => Current == Choice.Personalised || (Current == Choice.NotAsked && !InConsentRegion(Country));

        /// <summary>Ads may start (nothing left to ask).</summary>
        public static bool Ready => !MustAsk;

        /// <summary>Raised when the player chooses or changes the choice.</summary>
        public static event Action Changed;

        /// <summary>The player chooses (the first-launch prompt, or the switch in Settings).</summary>
        public static void Choose(bool personalised)
        {
            Current = personalised ? Choice.Personalised : Choice.NotPersonalised;
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Tells LevelPlay (and through it every ad network) what the player allows. Before its Init, and on changes.</summary>
        public static void Apply()
        {
            if (!AdsHere || AdsHereOverride.HasValue) return;
            var yes = Personalised;
            LevelPlay.SetConsent(yes);
            LevelPlay.SetMetaData("do_not_sell", yes ? "false" : "true");
            LevelPlay.SetMetaData("is_child_directed", "false");
        }

        /// <summary>The country from the SIM, then the mobile network, then the phone's region setting.</summary>
        private static string DetectCountry()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var telephony = activity.Call<AndroidJavaObject>("getSystemService", "phone"))
                {
                    if (telephony != null)
                    {
                        var sim = telephony.Call<string>("getSimCountryIso");
                        if (!string.IsNullOrEmpty(sim)) return sim.ToUpperInvariant();
                        var network = telephony.Call<string>("getNetworkCountryIso");
                        if (!string.IsNullOrEmpty(network)) return network.ToUpperInvariant();
                    }
                }
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (var current = locale.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    var region = current.Call<string>("getCountry");
                    if (!string.IsNullOrEmpty(region)) return region.ToUpperInvariant();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZOMBIE-ADS] country unknown: " + e.Message);
            }
#endif
#if UNITY_IOS && !UNITY_EDITOR
            // iPhone: the region the player set (iOS gives apps no SIM country).
            try
            {
                var region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
                if (!string.IsNullOrEmpty(region) && region.Length == 2) return region.ToUpperInvariant();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZOMBIE-ADS] country unknown: " + e.Message);
            }
#endif
            return "";
        }
    }
}
