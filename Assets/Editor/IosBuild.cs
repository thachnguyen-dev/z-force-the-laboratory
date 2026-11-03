using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Owner, 2026-10-11: the iOS release. Unity on Windows exports the Xcode project (build/ios, tools/build-ios.sh);
    /// the owner builds, signs and uploads it on a Mac (docs/ios-mac-steps.md). iPhone only (owner), the same bundle id
    /// as Android, the same version as the Android release. After the export the Info.plist gets what Apple and the ad
    /// networks ask for: the tracking-permission text, the SKAdNetwork ids, no non-exempt encryption.
    /// </summary>
    public static class IosBuild
    {
        public const string BundleId = "com.thachnguyen.zombieproto";
        /// <summary>Unity 6 builds for iOS 15 and later (LevelPlay 9 needs 13).</summary>
        public const string MinimumIos = "15.0";
        public const string TrackingText =
            "This lets us show ads that suit you better. Z-Force works either way, and you can change it in Settings.";

        public static void Export()
        {
            ZombieSetup.ApplyIdentity();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.bundleVersion = ZombieSetup.ReleaseVersion;
            PlayerSettings.iOS.buildNumber = ZombieSetup.ReleaseVersionCode.ToString();
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.targetOSVersionString = MinimumIos;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            var team = System.Environment.GetEnvironmentVariable("ZOMBIE_APPLE_TEAM");
            if (!string.IsNullOrEmpty(team)) PlayerSettings.iOS.appleDeveloperTeamID = team;
            PlayerSettings.iOS.hideHomeButton = false;
            PlayerSettings.iOS.requiresPersistentWiFi = false;
            PlayerSettings.iOS.statusBarStyle = iOSStatusBarStyle.LightContent;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
            // Landscape both ways, as on Android.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            var path = Path.GetFullPath("../build/ios");
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
            var stamp = GameVersion.Write();
            Debug.Log($"[ZOMBIE-BUILD] game version {stamp}");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ZombieSetup.ScenePath },
                locationPathName = path,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            var ok = report.summary.result == BuildResult.Succeeded;
            var podfile = Path.Combine(path, "Podfile");
            if (ok && File.Exists(podfile) && !File.ReadAllText(podfile).Contains("post_install"))
                // Owner, 2026-10-11: Xcode warned that the pods target iOS 13 (below what it supports); every pod builds for
                // the game's iOS 15.
                File.AppendAllText(podfile, "\npost_install do |installer|\n  installer.pods_project.targets.each do |target|\n" +
                    "    target.build_configurations.each do |config|\n      config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'\n" +
                    "    end\n  end\nend\n");
            Debug.Log($"[ZOMBIE-BUILD] {report.summary.result}: {path} (Xcode project{(File.Exists(Path.Combine(path, "Podfile")) ? ", Podfile written" : ", NO Podfile")})");
            if (!ok) EditorApplication.Exit(1);
        }

        /// <summary>The Info.plist additions (Apple\'s tracking text, the ad networks, export compliance).</summary>
        public class InfoPlist : IPostprocessBuildWithReport
        {
            public int callbackOrder => 100;

            public void OnPostprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.iOS) return;
#if UNITY_IOS
                var file = Path.Combine(report.summary.outputPath, "Info.plist");
                var plist = new PlistDocument();
                plist.ReadFromFile(file);
                var root = plist.root;
                root.SetString("NSUserTrackingUsageDescription", TrackingText);
                // The game uses only standard HTTPS (Apple\'s export compliance: no non-exempt encryption).
                root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
                root.SetString("CFBundleDisplayName", "Z-Force");
                var items = root.values.ContainsKey("SKAdNetworkItems") ? root["SKAdNetworkItems"].AsArray() : root.CreateArray("SKAdNetworkItems");
                var have = items.values.Select(v => v.AsDict()["SKAdNetworkIdentifier"]?.AsString()).Where(s => s != null).ToList();
                var source = Path.Combine(Application.dataPath, "Zombie/Code/Editor/IosSkAdNetworks.txt");
                var added = 0;
                foreach (var line in File.ReadAllLines(source))
                {
                    var id = line.Trim();
                    if (id.Length == 0 || id.StartsWith("#") || have.Contains(id)) continue;
                    items.AddDict().SetString("SKAdNetworkIdentifier", id);
                    have.Add(id);
                    added++;
                }
                plist.WriteToFile(file);
                Debug.Log($"[ZOMBIE-BUILD] Info.plist: tracking text, export compliance, {have.Count} SKAdNetwork ids ({added} added)");
#endif
            }
        }
    }
}
