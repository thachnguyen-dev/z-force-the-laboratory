using System.IO;
using UnityEditor;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Owner, 2026-10-08: the phone and the laptop ran different builds, and every update between them failed to read.
    /// Each build carries the fingerprint of its game code (Resources/BuildStamp.txt), so a friend on a different build is
    /// told so instead (NetSession.GameVersion).
    /// </summary>
    public static class GameVersion
    {
        public const string StampPath = "Assets/Zombie/Resources/BuildStamp.txt";

        public static string Write()
        {
            var stamp = ZombieGame.NetSession.CodeFingerprint(Path.GetFullPath("Assets/Zombie/Code/Runtime"));
            File.WriteAllText(StampPath, stamp);
            AssetDatabase.ImportAsset(StampPath);
            return stamp;
        }
    }
}
