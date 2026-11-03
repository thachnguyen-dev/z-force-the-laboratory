using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Owner, 2026-10-12: Android backs up app data to the phone's Google account and restores it on a reinstall, so the
    /// saved "Remove Ads + Co-op owned" answer could come back for another account. The small PlayerPrefs store (that
    /// answer and the ad-consent choice) is left out of backups; Google Play itself gives a real purchase back. The
    /// settings and progress (settings.json) are still backed up.
    /// </summary>
    public class AndroidBackupRules : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        private const string FullBackup = "zforce_backup_rules";
        private const string Extraction = "zforce_data_extraction_rules";

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var launcher = Path.GetFullPath(Path.Combine(unityLibraryPath, "..", "launcher"));
            var main = Directory.Exists(launcher) ? Path.Combine(launcher, "src", "main") : Path.Combine(unityLibraryPath, "src", "main");
            var prefs = $"{Application.identifier}.v2.playerprefs.xml";
            var xml = Path.Combine(main, "res", "xml");
            Directory.CreateDirectory(xml);
            // Android 11 and older.
            File.WriteAllText(Path.Combine(xml, FullBackup + ".xml"),
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<full-backup-content>
    <exclude domain=""sharedpref"" path=""{prefs}"" />
</full-backup-content>
");
            // Android 12 and newer (cloud backup and device-to-device transfer).
            File.WriteAllText(Path.Combine(xml, Extraction + ".xml"),
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<data-extraction-rules>
    <cloud-backup>
        <exclude domain=""sharedpref"" path=""{prefs}"" />
    </cloud-backup>
    <device-transfer>
        <exclude domain=""sharedpref"" path=""{prefs}"" />
    </device-transfer>
</data-extraction-rules>
");
            var manifest = Path.Combine(main, "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogError("[ZOMBIE-BUILD] no AndroidManifest.xml at " + manifest);
                return;
            }
            var text = File.ReadAllText(manifest);
            if (!text.Contains("xmlns:tools=")) text = text.Replace("<manifest ", "<manifest xmlns:tools=\"http://schemas.android.com/tools\" ");
            text = Regex.Replace(text, @"\s(android:fullBackupContent|android:dataExtractionRules)=""[^""]*""", "");
            var attrs = $" android:fullBackupContent=\"@xml/{FullBackup}\" android:dataExtractionRules=\"@xml/{Extraction}\" tools:replace=\"android:fullBackupContent,android:dataExtractionRules\"";
            text = Regex.Replace(text, @"<application(?=[\s>])", "<application" + attrs, RegexOptions.None, System.TimeSpan.FromSeconds(1));
            File.WriteAllText(manifest, text);
            Debug.Log($"[ZOMBIE-BUILD] backups leave out {prefs} ({manifest})");
        }
    }
}
