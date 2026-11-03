using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using Unity.Advertisement.IosSupport;
#endif

namespace ZombieGame
{
    /// <summary>
    /// Owner, 2026-10-11 (the iOS release): Apple's App Tracking Transparency. On iPhone the system asks once whether the
    /// game may use the advertising identifier; the ads start only after the answer (a "no" still shows ads, just not
    /// personalised ones by that identifier). Elsewhere there is nothing to ask.
    /// </summary>
    public static class Tracking
    {
        /// <summary>The player has answered (or there is nothing to ask on this platform).</summary>
        public static bool Answered
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return ATTrackingStatusBinding.GetAuthorizationTrackingStatus() != ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED;
#else
                return true;
#endif
            }
        }

        /// <summary>Shows Apple's tracking question (once; the text is NSUserTrackingUsageDescription).</summary>
        public static void Ask()
        {
#if UNITY_IOS && !UNITY_EDITOR
            ATTrackingStatusBinding.RequestAuthorizationTracking();
#endif
        }
    }
}
