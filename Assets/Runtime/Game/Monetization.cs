using System;
using System.Collections.Generic;
using Unity.Services.Core;
using Unity.Services.LevelPlay;
using UnityEngine;
using UnityEngine.Purchasing;

namespace ZombieGame
{
    /// <summary>
    /// Owner, 2026-10-08: ads and the one purchase.
    /// <list type="bullet">
    /// <item>A full-screen ad (LevelPlay interstitial) when pausing in Solo, at most once every <see cref="PauseAdCooldown"/>.</item>
    /// <item>One purchase, "Remove ads + Co-op" ($3.99, a non-consumable Google Play product): no more ads, and Co-op opens
    /// (Co-op is locked without it).</item>
    /// </list>
    /// Both run on Android only: the Windows version has no store, so it shows no ads and Co-op stays open there.
    /// </summary>
    public static class Monetization
    {
        /// <summary>The owner's LevelPlay app and its pause ad unit (Android).</summary>
        public const string AndroidAppKey = "2894cd0d5";
        public const string AndroidPauseAdUnit = "6hf8lkms248r4u9e";
        /// <summary>
        /// Owner, 2026-10-11 (the iOS release): LevelPlay gives an iOS app its own key and ad unit (the owner's iOS app).
        /// </summary>
        public const string IosAppKey = "28a2ad9b5";
        public const string IosPauseAdUnit = "arwbarvgg7do86o8";

        public static string AppKey => Application.platform == RuntimePlatform.IPhonePlayer ? IosAppKey : AndroidAppKey;
        public static string PauseAdUnit => Application.platform == RuntimePlatform.IPhonePlayer ? IosPauseAdUnit : AndroidPauseAdUnit;

        /// <summary>The Google Play product id (a one-time, non-consumable product; the price, $3.99, is set in Play Console).</summary>
        public const string ProductId = "remove_ads_coop";
        /// <summary>Shown until the store tells the local price.</summary>
        public const string PriceFallback = "$3.99";

        /// <summary>The pause ad shows at most once in this long (s; owner, 2026-10-08). PLACEHOLDER.</summary>
        public const float PauseAdCooldown = 180f;
        /// <summary>A failed ad load is tried again after this long (s).</summary>
        private const float AdRetry = 30f;

        private const string OwnedKey = "zforce.removeAdsCoop";

        /// <summary>Ads and the store exist here: Google Play on Android, the App Store on iPhone (owner, 2026-10-11).</summary>
        public static bool StoreSupported => Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;

        /// <summary>The purchase is owned (remembered on the device, confirmed by the store each start).</summary>
        public static bool Owned => PlayerPrefs.GetInt(OwnedKey, 0) == 1;

        /// <summary>
        /// Co-op is open: owned, or no store here (Windows, the editor), or a development build with "Unlock all (test)"
        /// on.
        /// </summary>
        public static bool CoopUnlocked
        {
            get
            {
                if (!StoreSupported || Owned) return true;
                var settings = GameSession.Instance != null ? GameSession.Instance.Settings : null;
                return Gui.DevTools && settings != null && settings.unlockAllForTesting;
            }
        }

        /// <summary>The price as the store shows it (the player's currency), or <see cref="PriceFallback"/>.</summary>
        public static string Price { get; private set; } = PriceFallback;

        /// <summary>The store's last word for the player ("Purchase cancelled", ...), or null.</summary>
        public static string Message { get; private set; }

        /// <summary>A purchase or restore is under way.</summary>
        public static bool Busy { get; private set; }

        /// <summary>Raised when ownership changes (menus rebuild).</summary>
        public static event Action Changed;

        private static bool _started;
        private static StoreController _store;
        private static bool _storeReady;

        /// <summary>Starts the store and (when not owned) the ads. Called once at start; does nothing off Android.</summary>
        public static void Start()
        {
            if (_started || !StoreSupported) return;
            _started = true;
            StartStore();
            if (!Owned) StartAds();
        }

        /// <summary>Per frame (the ad retry timer).</summary>
        public static void Tick()
        {
            // iOS: the ads start once the player has answered Apple's tracking question.
            if (_waitingForTracking && Tracking.Answered)
            {
                _waitingForTracking = false;
                StartAds();
            }
            if (_retryAt > 0f && Time.realtimeSinceStartup >= _retryAt)
            {
                _retryAt = 0f;
                _interstitial?.LoadAd();
            }
        }

        // ---------------------------------------------------------------- the store

        private static async void StartStore()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
                _store = UnityIAPServices.StoreController();
                _store.OnStoreConnected += () => _storeReady = true;
                _store.OnStoreDisconnected += _ => _storeReady = false;
                _store.OnProductsFetched += products =>
                {
                    foreach (var p in products)
                        if (p.definition.id == ProductId && !string.IsNullOrEmpty(p.metadata?.localizedPriceString)) Price = p.metadata.localizedPriceString;
                    _store.FetchPurchases();
                };
                _store.OnProductsFetchFailed += f =>
                {
                    Debug.LogWarning($"[ZOMBIE-STORE] products not fetched: {f.FailureReason}");
                    // Owner, 2026-10-12: what the account owns is asked anyway (a stale saved answer never stays).
                    _store.FetchPurchases();
                };
                _store.OnPurchasesFetched += OnPurchasesFetched;
                _store.OnPurchasesFetchFailed += f =>
                {
                    Debug.LogWarning($"[ZOMBIE-STORE] purchases not fetched: {f.FailureReason}");
                    EndBusy(null);
                };
                _store.OnPurchasePending += order =>
                {
                    if (Has(order)) SetOwned(true);
                    _store.ConfirmPurchase(order);
                };
                _store.OnPurchaseConfirmed += order =>
                {
                    if (Has(order)) SetOwned(true);
                    EndBusy(Has(order) ? "Thank you! Ads removed, Co-op unlocked." : null);
                };
                _store.OnPurchaseFailed += failed =>
                {
                    // Already bought on this account: the store says so; ask it again.
                    if (failed.FailureReason == PurchaseFailureReason.DuplicateTransaction) _store.FetchPurchases();
                    EndBusy(failed.FailureReason == PurchaseFailureReason.UserCancelled ? "Purchase cancelled." : $"Purchase failed ({failed.FailureReason}).");
                };
                _store.OnPurchaseDeferred += _ => EndBusy("Purchase pending: it unlocks when the payment completes.");
                await _store.Connect();
                _store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(ProductId, ProductType.NonConsumable) });
                // Owner, 2026-10-12: asked at every start, whether or not the product's details load; Google Play's answer
                // replaces the one saved on the phone.
                _store.FetchPurchases();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZOMBIE-STORE] " + e.Message);
            }
        }

        private static bool Has(Order order)
        {
            if (order?.CartOrdered == null) return false;
            foreach (var item in order.CartOrdered.Items())
            {
                if (item == null) continue;
                // Known by the product (when its details loaded) or by the store's own id for it.
                if (item.Product?.definition?.id == ProductId || item.Product?.definition?.storeSpecificId == ProductId) return true;
                if (item.CatalogListingId == ProductId) return true;
            }
            return false;
        }

        /// <summary>The store's list of what this account owns: the purchase is owned exactly when it's on it (a refund takes it away).</summary>
        private static void OnPurchasesFetched(Orders orders)
        {
            var owned = false;
            foreach (var o in orders.ConfirmedOrders) owned |= Has(o);
            foreach (var o in orders.PendingOrders)
            {
                if (!Has(o)) continue;
                owned = true;
                _store.ConfirmPurchase(o);
            }
            Debug.Log($"[ZOMBIE-STORE] the store says: {(owned ? "owned" : "not owned")} ({orders.ConfirmedOrders.Count} confirmed, {orders.PendingOrders.Count} pending)");
            SetOwned(owned);
            // Owner, 2026-10-09: no Restore button; the store is asked at every start (a new phone or a reinstall gets it back).
            EndBusy(null);
        }

        /// <summary>Buys "Remove ads + Co-op".</summary>
        public static void Buy()
        {
            if (Busy) return;
            if (!StoreSupported || _store == null || !_storeReady)
            {
                Message = "The store isn't available right now.";
                Changed?.Invoke();
                return;
            }
            var product = _store.GetProductById(ProductId);
            if (product == null || !product.availableToPurchase)
            {
                Message = "This purchase isn't available right now.";
                Changed?.Invoke();
                return;
            }
            Busy = true;
            Message = null;
            Changed?.Invoke();
            _store.PurchaseProduct(product);
        }

        /// <summary>
        /// Owner, 2026-10-11: Apple requires a "Restore Purchases" button for a permanent purchase (App Review 3.1.1),
        /// so iPhone has one (Android doesn't: owner, 2026-10-09). Asks the App Store for this account's purchases.
        /// </summary>
        public static bool RestoreShown => RestoreShownOverride ?? Application.platform == RuntimePlatform.IPhonePlayer;

        /// <summary>The store screenshots only: show the iPhone's Restore button (App Store review picture).</summary>
        public static bool? RestoreShownOverride;

        public static void Restore()
        {
            if (Busy || _store == null) return;
            Busy = true;
            Message = null;
            Changed?.Invoke();
            _store.RestoreTransactions((ok, error) =>
            {
                if (!ok)
                {
                    EndBusy("Couldn't restore: " + (string.IsNullOrEmpty(error) ? "try again later." : error));
                    return;
                }
                // The restored purchases arrive through the usual list (OnPurchasesFetched).
                _store.FetchPurchases();
                EndBusy(Owned ? "Purchase restored." : "Checking your purchases…");
            });
        }

        private static void EndBusy(string message)
        {
            Busy = false;
            if (message != null) Message = message;
            Changed?.Invoke();
        }

        private static void SetOwned(bool owned)
        {
            if (Owned == owned) return;
            PlayerPrefs.SetInt(OwnedKey, owned ? 1 : 0);
            PlayerPrefs.Save();
            if (!owned) StartAds();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- the pause ad

        private static bool _adsStarted;
        private static LevelPlayInterstitialAd _interstitial;
        private static float _lastAdAt = float.NegativeInfinity;
        private static float _retryAt;

        private static void StartAds()
        {
            if (_adsStarted) return;
            // Owner, 2026-10-10: in the consent countries no ad starts before the player has chosen (AdConsent).
            if (!AdConsent.Ready)
            {
                AdConsent.Changed -= StartAdsOnce;
                AdConsent.Changed += StartAdsOnce;
                return;
            }
            // Owner, 2026-10-11: on iPhone Apple's tracking question comes first (App Tracking Transparency).
            if (!Tracking.Answered)
            {
                if (!_waitingForTracking) Tracking.Ask();
                _waitingForTracking = true;
                return;
            }
            if (string.IsNullOrEmpty(AppKey))
            {
                Debug.LogWarning("[ZOMBIE-ADS] no LevelPlay app key for this platform: no ads");
                return;
            }
            _adsStarted = true;
            AdConsent.Apply();
            LevelPlay.OnInitSuccess += _ =>
            {
                _interstitial = new LevelPlayInterstitialAd(PauseAdUnit);
                _interstitial.OnAdLoadFailed += e =>
                {
                    Debug.LogWarning($"[ZOMBIE-ADS] no ad loaded: {e}");
                    _retryAt = Time.realtimeSinceStartup + AdRetry;
                };
                // The next ad loads as soon as one is closed or fails to show.
                _interstitial.OnAdClosed += _ => _interstitial.LoadAd();
                _interstitial.OnAdDisplayFailed += (_, e) =>
                {
                    Debug.LogWarning($"[ZOMBIE-ADS] ad not shown: {e}");
                    _interstitial.LoadAd();
                };
                _interstitial.LoadAd();
            };
            LevelPlay.OnInitFailed += e => Debug.LogWarning($"[ZOMBIE-ADS] LevelPlay didn't start: {e}");
            LevelPlay.Init(AppKey);
        }

        private static bool _waitingForTracking;

        private static void StartAdsOnce()
        {
            AdConsent.Changed -= StartAdsOnce;
            if (!Owned) StartAds();
        }

        /// <summary>
        /// Pausing in Solo: shows the full-screen ad when one is ready, not owned, and none was shown in the last
        /// <see cref="PauseAdCooldown"/>. True when it shows.
        /// </summary>
        public static bool TryShowPauseAd()
        {
            if (!StoreSupported || Owned || _interstitial == null) return false;
            if (Time.realtimeSinceStartup - _lastAdAt < PauseAdCooldown) return false;
            if (!_interstitial.IsAdReady()) return false;
            _lastAdAt = Time.realtimeSinceStartup;
            _interstitial.ShowAd();
            return true;
        }
    }
}
