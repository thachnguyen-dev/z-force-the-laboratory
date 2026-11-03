using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Online hosting through Unity Relay gives a join code (owner, 2026-10-07: the project is now linked to Unity Gaming
    /// Services). Explicit: it needs the internet. Run: -testFilter ZombieGame.Tests.OnlineTests.
    /// </summary>
    [Explicit, Category("Online")]
    public class OnlineTests
    {
        /// <summary>
        /// Owner, 2026-10-08 (ping 100-180 over a join code): how far each Relay region is from this computer. Logs every
        /// region's round trip; Relay picks the fastest itself when hosting.
        /// </summary>
        [UnityTest]
        public IEnumerator MeasureTheRelayRegions()
        {
            var init = Unity.Services.Core.UnityServices.InitializeAsync();
            while (!init.IsCompleted) yield return null;
            if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
            {
                var sign = Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                while (!sign.IsCompleted) yield return null;
            }
            var qos = Unity.Services.Qos.QosService.Instance.GetSortedQosResultsAsync("relay", null);
            while (!qos.IsCompleted) yield return null;
            Assert.IsFalse(qos.IsFaulted, qos.Exception?.ToString());
            foreach (var r in qos.Result) Debug.Log($"[ZOMBIE-QOS] {r.Region}: {r.AverageLatencyMs} ms, loss {r.PacketLossPercent:0.#} %");
            Assert.Greater(qos.Result.Count, 0, "regions measured");
        }

        [UnityTest]
        public IEnumerator HostingOnlineGivesAJoinCode()
        {
            Assert.IsTrue(NetSession.OnlineAvailable, "the project is linked");
            var net = NetSession.Ensure();
            net.HostOnline();
            var until = Time.realtimeSinceStartup + 30f;
            while (string.IsNullOrEmpty(net.JoinCode) && Time.realtimeSinceStartup < until && (net.Busy || net.Status == "")) yield return null;
            Debug.Log($"[ZOMBIE-NET] join code '{net.JoinCode}', status '{net.Status}'");
            Assert.IsFalse(string.IsNullOrEmpty(net.JoinCode), $"a join code (status: {net.Status})");
            net.Leave();
            yield return null;
            Object.Destroy(net.gameObject);
        }
    }
}
