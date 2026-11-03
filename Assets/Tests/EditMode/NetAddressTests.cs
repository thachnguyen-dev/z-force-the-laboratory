using System.Linq;
using NUnit.Framework;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-07: the address shown for "Host on this network" is the one a friend on the same Wi-Fi reaches.</summary>
    public class NetAddressTests
    {
        private static bool Home(string a) => a.StartsWith("192.168.") || a.StartsWith("10.") ||
            (a.StartsWith("172.") && int.TryParse(a.Split('.')[1], out var b) && b >= 16 && b <= 31);

        [Test]
        public void TypedAddressesAreCleaned()
        {
            // Owner, 2026-10-07: the phone keyboard put a space after every dot, and joining failed.
            Assert.AreEqual("192.168.68.127", NetSession.CleanAddress("192. 168. 68. 127"));
            Assert.AreEqual("192.168.68.127:7777", NetSession.CleanAddress(" 192,168,68,127 : 7777 \t"));
            Assert.IsTrue(NetSession.IsAddress("192. 168. 68. 127"));
            Assert.IsTrue(NetSession.IsAddress("10.0.0.5:7777"));
            Assert.IsFalse(NetSession.IsAddress("MQ8PMD"), "a join code isn't an address");
            Assert.IsFalse(NetSession.IsAddress("192.168.68"));
            Assert.IsFalse(NetSession.IsAddress("192.168.68.300"));
            Assert.AreEqual("MQ8PMD", NetSession.CleanAddress(" MQ8 PMD ").ToUpperInvariant());
        }

        [Test]
        public void AHomeNetworkAddressComesFirst()
        {
            var list = NetSession.LocalAddresses();
            Assert.IsTrue(list.All(a => a.Split('.').Length == 4), "IPv4 addresses");
            if (list.Any(Home)) Assert.IsTrue(Home(list[0]), $"a home-network address first: {string.Join(", ", list)}");
        }
    }
}
