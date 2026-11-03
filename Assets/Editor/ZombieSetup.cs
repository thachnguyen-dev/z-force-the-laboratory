using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Headless project setup and builds:
    /// <c>Unity.exe -batchmode -quit -projectPath src -executeMethod ZombieGame.EditorTools.ZombieSetup.Configure</c>
    /// </summary>
    public static class ZombieSetup
    {
        public const string ScenePath = "Assets/Zombie/Scenes/Main.unity";
        private const string TuningPath = "Assets/Zombie/Resources/" + GameTuning.ResourceName + ".asset";

        // ⚠ Development identity, following the owner's other projects. Not a store identity.
        private const string CompanyName = "Thach Nguyen";
        // Owner, 2026-10-08: the official name. The bundle id stays, so the phone updates the installed game.
        private const string ProductName = "Z-Force: The Laboratory";
        /// <summary>Owner, 2026-10-08: the game icon (delivered 512 x 512).</summary>
        private const string IconPath = "Assets/Zombie/Art/UI/Icon.png";
        private const string BundleId = "com.thachnguyen.zombieproto";

        [MenuItem("Zombie/Developer (Claude)/Configure Project and Rebuild Scene")]
        public static void Configure()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            // Keep running when the window loses focus: an online host that alt-tabs must not freeze the other player.
            PlayerSettings.runInBackground = true;
            SetNavMeshAgentRadius(0.3f);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);

            // Two thumbs on the screen: landscape only.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;

            NameLayers();
            BuildBaseMaterial();
            BuildTuning();
            TileSetup.BuildTiles();
            TileSetup.EnsureTestStageMap();
            StageFiles.EnsureIndex();
            BuildScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[ZOMBIE-SETUP] Done. Scene: {ScenePath}. Tuning: {TuningPath}.");
        }

        private static void NameLayers()
        {
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = manager.FindProperty("layers");
            void Name(int index, string name) => layers.GetArrayElementAtIndex(index).stringValue = name;
            Name(Layers.Obstacle, "Obstacle");
            Name(Layers.Survivor, "Survivor");
            Name(Layers.Zombie, "Zombie");
            Name(Layers.Barrel, "Barrel");
            Name(Layers.ShootThrough, "ShootThrough");
            manager.ApplyModifiedProperties();
        }

        /// <summary>The one material every greybox colour is made from; in Resources so it and its URP shader ship.</summary>
        private static void BuildBaseMaterial()
        {
            const string path = "Assets/Zombie/Resources/" + Primitives.BaseMaterialResource + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Simple Lit shader not found");
            Directory.CreateDirectory("Assets/Zombie/Resources");
            AssetDatabase.CreateAsset(new Material(shader) { name = Primitives.BaseMaterialResource, color = Color.white }, path);
        }

        /// <summary>Creates the tuning asset once. Afterwards it's edited in the inspector and never overwritten.</summary>
        private static void BuildTuning()
        {
            if (AssetDatabase.LoadAssetAtPath<GameTuning>(TuningPath) != null) return;
            Directory.CreateDirectory("Assets/Zombie/Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameTuning>(), TuningPath);
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            // Owner, 2026-09-30: ellipse shadows under characters; 2026-10-07: buildings and props cast real shadows (each
            // stage turns them on by day, StageAtmosphere).
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = TileAssembler.SunRotation;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Owner, 2026-10-03: brighter, cartoon look.
            // 2026-10-04: neutral, so the four ground colours (dirt, grass, roads, floors) read true.
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            new GameObject("Game Session").AddComponent<GameSession>();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// The NavMesh agent radius (project settings). 0.3 keeps 1-cell gaps open for zombies, the AI teammate and the
        /// hostage, as they are for players (2026-10-04: some loot behind 1-cell squeezes counted as unreachable).
        /// </summary>
        private static void SetNavMeshAgentRadius(float radius)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var settings = so.FindProperty("m_Settings");
            if (settings == null || settings.arraySize == 0) return;
            settings.GetArrayElementAtIndex(0).FindPropertyRelative("agentRadius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Builds ----

        /// <summary>A debug-signed development APK at <c>build/zombie-proto.apk</c>, for testing on a phone.</summary>
        public static void BuildApk()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ResolveAndroidLibraries();
            Build("../build/zombie-proto.apk", BuildTarget.Android, BuildOptions.Development);
        }

        /// <summary>
        /// The ads SDK's Android libraries (owner, 2026-10-08: LevelPlay ads; Assets/LevelPlay/Editor/*Dependencies.xml)
        /// fetched by the Mobile Dependency Resolver before the build (a batch build never opens its dialogs).
        /// </summary>
        private static void ResolveAndroidLibraries()
        {
            var resolver = System.Type.GetType("GooglePlayServices.PlayServicesResolver, Google.JarResolver");
            var resolve = resolver?.GetMethod("ResolveSync", new[] { typeof(bool) });
            if (resolve == null)
            {
                Debug.LogError("[ZOMBIE-BUILD] Mobile Dependency Resolver not found: the ads can't be built in.");
                EditorApplication.Exit(1);
                return;
            }
            var ok = (bool)resolve.Invoke(null, new object[] { true });
            Debug.Log($"[ZOMBIE-BUILD] Android libraries resolved: {ok}");
            if (!ok)
            {
                Debug.LogError("[ZOMBIE-BUILD] The ads' Android libraries didn't resolve.");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The released version (Google Play): the name players see, and the code that must rise with every upload.</summary>
        public const string ReleaseVersion = "1.0.7";
        public const int ReleaseVersionCode = 10;

        /// <summary>
        /// Owner, 2026-10-09: the signed release app bundle for Google Play at <c>build/release/zforce-{version}.aab</c>
        /// (tools/build-aab.sh). Signed with the upload key from outside the repo (paths and passwords come from the
        /// environment, never stored in the project); no development features; Android 15+ target (API 36); the native
        /// debug symbols beside it for Play's crash reports.
        /// </summary>
        public static void BuildAab()
        {
            string Env(string name)
            {
                var v = System.Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(v))
                {
                    Debug.LogError($"[ZOMBIE-BUILD] {name} is not set (run tools/build-aab.sh).");
                    EditorApplication.Exit(1);
                }
                return v;
            }
            var keystore = Env("ZOMBIE_KEYSTORE");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = Env("ZOMBIE_KEYSTORE_PASS");
            PlayerSettings.Android.keyaliasName = Env("ZOMBIE_KEY_ALIAS");
            PlayerSettings.Android.keyaliasPass = Env("ZOMBIE_KEY_PASS");
            PlayerSettings.bundleVersion = ReleaseVersion;
            PlayerSettings.Android.bundleVersionCode = ReleaseVersionCode;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            EditorUserBuildSettings.buildAppBundle = true;
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            ResolveAndroidLibraries();
            try
            {
                Build($"../build/release/zforce-{ReleaseVersion}.aab", BuildTarget.Android, BuildOptions.None);
            }
            finally
            {
                // Nothing secret stays in the project: back to the debug signing the test builds use.
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.useCustomKeystore = false;
                EditorUserBuildSettings.buildAppBundle = false;
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>A Windows x64 development build at <c>build/windows/Zombie.exe</c>.</summary>
        public static void BuildWindows() => Build("../build/windows/Zombie.exe", BuildTarget.StandaloneWindows64, BuildOptions.Development);

        /// <summary>A Windows release build at <c>build/store-shots/ZForce.exe</c> (no "Development Build" mark): the store's screenshots.</summary>
        public static void BuildWindowsRelease() => Build("../build/store-shots/ZForce.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);

        /// <summary>The game's name and icon (every platform: the default icon, no per-platform overrides).</summary>
        public static void ApplyIdentity()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            if (AssetImporter.GetAtPath(IconPath) is TextureImporter importer &&
                (importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency || importer.mipmapEnabled))
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning("[ZOMBIE-BUILD] No icon at " + IconPath);
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
        }

        private static void Build(string relativePath, BuildTarget target, BuildOptions options)
        {
            var path = Path.GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Zombie/Resources/" + Primitives.BaseMaterialResource + ".mat");
            if (baseMaterial == null || !File.Exists(ScenePath))
            {
                Debug.LogError("[ZOMBIE-BUILD] Project not configured; run ZombieSetup.Configure first.");
                EditorApplication.Exit(1);
                return;
            }
            ApplyIdentity();
            var stamp = GameVersion.Write();
            Debug.Log($"[ZOMBIE-BUILD] game version {stamp}");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = options,
            });
            var ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[ZOMBIE-BUILD] {report.summary.result}: {path} ({(File.Exists(path) ? new FileInfo(path).Length / (1024 * 1024) : 0)} MB on disk)");
            if (!ok) EditorApplication.Exit(1);
        }
    }
}
