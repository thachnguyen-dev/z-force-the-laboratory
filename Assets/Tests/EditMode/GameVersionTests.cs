using System.IO;
using NUnit.Framework;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-08: two different builds can't play together; each carries its code's fingerprint.</summary>
    public class GameVersionTests
    {
        [Test]
        public void TheFingerprintFollowsTheCode()
        {
            var runtime = Path.GetFullPath("Assets/Zombie/Code/Runtime");
            var a = NetSession.CodeFingerprint(runtime);
            Assert.AreEqual(12, a.Length, "short hex");
            Assert.AreEqual(a, NetSession.CodeFingerprint(runtime), "the same code, the same fingerprint");
            Assert.AreEqual(a, NetSession.GameVersion, "the editor's version is its code's");
            // A copy with one change differs.
            var temp = Path.Combine(Path.GetTempPath(), "zombie-version-test");
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            foreach (var f in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(temp, Path.GetFileName(f)), true);
            var b = NetSession.CodeFingerprint(temp);
            File.AppendAllText(Path.Combine(temp, "Wire.Extra.cs"), "// one more line\n");
            Assert.AreNotEqual(b, NetSession.CodeFingerprint(temp), "changed code, another fingerprint");
            Directory.Delete(temp, true);
        }
    }
}
