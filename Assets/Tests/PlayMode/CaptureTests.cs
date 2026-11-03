using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Not a test: in-game camera captures for the owner's review (2026-10-07: review in the game view only). Explicit,
    /// so the suite never runs it; run with graphics: -runTests -testPlatform PlayMode -testFilter ZombieGame.Tests.CaptureTests.
    /// Each playable character in a real stage, walking and firing its long gun with zombies of every type around it,
    /// captured from the game's own camera to logs/char-preview/ingame-*.png.
    /// </summary>
    [Explicit, Category("Capture")]
    public class CaptureTests
    {
        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        [UnityTest]
        public IEnumerator CaptureTheGameView()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var tuning = new GameTuning();
            for (var c = 0; c < tuning.characters.Length; c++)
            {
                var session = new GameObject("Game Session").AddComponent<GameSession>();
                yield return null;
                session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), session.Tuning.characters[c], 31);
                for (var i = 0; i < 3; i++) yield return null;
                var player = session.LocalSurvivor;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
                // A long gun to hold: a different one for each character.
                var gunId = new[] { WeaponId.AR, WeaponId.Shotgun, WeaponId.Rifle, WeaponId.SMG }[c % 4];
                var gunDef = session.Tuning.Firearm(gunId);
                LootItem.SpawnLongGun(player.transform.position, gunDef, 30);
                var input = new Scripted { Value = new CharacterInput { Pickup = true, GrenadeDistance = -1f } };
                player.InputSource = input;
                for (var i = 0; i < 5; i++) yield return null;
                input.Value.Pickup = false;
                player.Select(Slot.LongGun);
                yield return new WaitForSeconds(0.8f);
                // Zombies of every type in a ring ahead.
                var p = player.transform.position;
                var types = (ZombieType[])System.Enum.GetValues(typeof(ZombieType));
                for (var k = 0; k < types.Length; k++)
                {
                    var a = (k / (float)types.Length) * Mathf.PI * 2f;
                    session.Stage.SpawnZombie(types[k], p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f);
                }
                // Walk and fire for a moment, then capture.
                input.Value = new CharacterInput { Move = new Vector2(0.6f, 0.3f), Aim = new Vector2(1f, 0.4f), FireHeld = true, GrenadeDistance = -1f };
                yield return new WaitForSeconds(0.6f);
                Capture(Path.Combine(outDir, $"ingame-{session.Tuning.characters[c].id}.png"));
                // The movement: eight frames, a tenth of a second apart, of the first character walking and firing
                // while the zombies close in.
                if (c == 0)
                    for (var f = 0; f < 8; f++)
                    {
                        yield return new WaitForSeconds(0.1f);
                        Capture(Path.Combine(outDir, $"ingame-walk-{f}.png"));
                    }
                // The pistol (one hand, arm extended), firing.
                player.Select(Slot.Pistol);
                yield return new WaitForSeconds(0.8f);
                Capture(Path.Combine(outDir, $"ingame-pistol-{session.Tuning.characters[c].id}.png"));
                // The knife's slash: a few frames through one swing.
                if (c == 0)
                {
                    input.Value.FireHeld = false;
                    player.Select(Slot.Knife);
                    yield return new WaitForSeconds(0.8f);
                    input.Value.FireHeld = true;
                    for (var f = 0; f < 4; f++)
                    {
                        Capture(Path.Combine(outDir, $"ingame-knife-{f}.png"));
                        yield return new WaitForSeconds(0.05f);
                    }
                }
                player.InputSource = null;
                session.GoTo(SessionState.MainMenu);
                yield return null;
                foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
                Object.Destroy(session.gameObject);
                yield return null;
            }
        }

        /// <summary>
        /// Owner, 2026-10-08 ("UI next"): the new front-end screens, at a phone's shape (2400 x 1080), to
        /// logs/char-preview/ui-*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheMenus()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            yield return null;
            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.name == "Menus");
            var cam = new GameObject("UI capture camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            const int w = 2400, h = 1080;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            foreach (var (state, file) in new[] { (SessionState.MainMenu, "ui-main.png"), (SessionState.CoopSelect, "ui-coop.png"), (SessionState.StageSelect, "ui-missions.png"), (SessionState.CharacterSelect, "ui-characters.png"), (SessionState.Victory, "ui-victory.png"), (SessionState.MainMenu, "ui-offer.png"), (SessionState.MainMenu, "ui-consent.png") })
            {
                session.GoTo(state);
                // The purchase offer (the editor has no store, so Co-op is open there; opened by hand for the picture).
                var menus = typeof(GameSession).GetField("_menus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(session);
                menus.GetType().GetField("_offer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(menus, file == "ui-offer.png");
                // The ad consent as a player in Germany sees it on first launch.
                AdConsent.AdsHereOverride = file == "ui-consent.png" ? true : (bool?)null;
                AdConsent.CountryOverride = file == "ui-consent.png" ? "DE" : null;
                if (file == "ui-consent.png") PlayerPrefs.DeleteKey("zforce.adConsent");
                for (var i = 0; i < 4; i++) yield return null;
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t.Apply();
                File.WriteAllBytes(Path.Combine(outDir, file), t.EncodeToPNG());
                RenderTexture.active = null;
                Object.Destroy(t);
            }
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(cam.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-08: the helicopter coming down with its rotor spinning, then blue blood and the Tiger's red
        /// charge warning on the ground; to logs/char-preview/ingame-heli.png and ingame-blood.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheHelicopterAndTheBlood()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            session.StartStage(StageLibrary.Load("m1_gate_a"), session.Tuning.characters[0], 31, intro: true);
            yield return new WaitForSeconds(Helicopter.LandTime * 0.55f);
            Capture(Path.Combine(outDir, "ingame-heli.png"));
            yield return new WaitForSeconds(Helicopter.LandTime * 0.5f);
            var player = session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            // Three zombies shot a few times each, beside the player; a Tiger about to charge.
            var p = player.transform.position + new Vector3(4f, 0f, -3f);
            var shot = new System.Collections.Generic.List<Zombie>();
            for (var i = 0; i < 3; i++)
            {
                var z = session.Stage.SpawnZombie(ZombieType.Basic, p + new Vector3(i * 2.2f, 0f, 0f));
                if (z == null) continue;
                z.enabled = false;
                shot.Add(z);
            }
            var tiger = session.Stage.SpawnZombie(ZombieType.Tiger, player.transform.position + new Vector3(-9f, 0f, -4f));
            tiger?.JoinChase(player, 30f);
            var until = Time.time + 4f;
            while (GameObject.Find("Charge warning") == null && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.5f);
            // Shot just now: the green spray in the air.
            foreach (var z in shot) for (var k = 0; k < 2; k++) z.TakeDamage(1f, player.transform.position, false);
            yield return new WaitForSeconds(0.14f);
            Capture(Path.Combine(outDir, "ingame-blood.png"));
            session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            Object.Destroy(sun.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-07: buildings and props cast shadows. The game view by day beside a building, to
        /// logs/char-preview/ingame-shadows.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheShadows()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            // The game scene's sun (this test scene has none).
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            session.StartStage(StageLibrary.Load("m1_gate_a"), session.Tuning.characters[0], 31);
            for (var i = 0; i < 3; i++) yield return null;
            var player = session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            // Just south of the building nearest the start, where the camera sees its front.
            var building = Building.All.OrderBy(b => (b.Def.Bounds.center - new Vector2(player.transform.position.x, player.transform.position.z)).sqrMagnitude).First();
            var b0 = building.Def.Bounds;
            var c = player.GetComponent<CharacterController>();
            c.enabled = false;
            player.transform.position = new Vector3(b0.center.x + 2f, 0f, b0.yMin - 5f);
            c.enabled = true;
            for (var i = 0; i < 2; i++)
            {
                var a = i * Mathf.PI;
                session.Stage.SpawnZombie(ZombieType.Basic, player.transform.position + new Vector3(Mathf.Cos(a), 0f, -1f) * 4f);
            }
            yield return new WaitForSeconds(1f);
            Capture(Path.Combine(outDir, "ingame-shadows.png"));
            // Close up on the player and a zombie: their ellipse shadows.
            Capture(Path.Combine(outDir, "ingame-shadows-close.png"), player.transform.position + new Vector3(0f, 0f, -2f), 3.5f);
            // The phone's pipeline (Mobile: forward, no SSAO, one shadow cascade).
            var level = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(0, true);
            for (var i = 0; i < 3; i++) yield return null;
            Capture(Path.Combine(outDir, "ingame-shadows-phone.png"));
            QualitySettings.SetQualityLevel(level, true);
            yield return null;
            session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            Object.Destroy(sun.gameObject);
            yield return null;
        }

        /// <summary>
        /// The walk, close up from the game camera's angle: the first character running then walking, and a Basic
        /// zombie, the Dog and the Tiger coming at it; 16 frames a 30th of a second apart each, to
        /// logs/char-preview/walk-{who}-{frame}.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheWalk()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), session.Tuning.characters[0], 31);
            for (var i = 0; i < 3; i++) yield return null;
            var player = session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            LootItem.SpawnLongGun(player.transform.position, session.Tuning.Firearm(WeaponId.AR), 30);
            var input = new Scripted { Value = new CharacterInput { Pickup = true, GrenadeDistance = -1f } };
            player.InputSource = input;
            for (var i = 0; i < 5; i++) yield return null;
            player.Select(Slot.LongGun);
            yield return new WaitForSeconds(0.8f);
            IEnumerator Strip(string who, Transform follow, int frames, float zoom = 1f)
            {
                // Exactly a 30th of a second of game time per captured frame, however slowly the capture renders.
                Time.captureFramerate = 30;
                for (var f = 0; f < frames; f++)
                {
                    yield return null;
                    // Keep the zombies coming (a noise where the player is).
                    Noise.Emit(player.transform.position, 30f);
                    Capture(Path.Combine(outDir, $"walk-{who}-{f:00}.png"), follow.position, 2.4f * zoom);
                    // Claude's own check of the legs (not for review): the same moment from the side.
                    Capture(Path.Combine(outDir, $"side-{who}-{f:00}.png"), follow.position, 1.6f * zoom, Quaternion.Euler(5f, 0f, 0f));
                }
                Time.captureFramerate = 0;
            }
            // Running across the screen, aiming where it runs; then walking slowly (the stick half pushed).
            input.Value = new CharacterInput { Move = new Vector2(1f, 0f), Aim = new Vector2(1f, 0f), GrenadeDistance = -1f };
            yield return new WaitForSeconds(0.6f);
            yield return Strip("survivor-run", player.transform, 24);
            input.Value.Move = new Vector2(-0.3f, 0f);
            input.Value.Aim = new Vector2(-1f, 0f);
            yield return new WaitForSeconds(0.8f);
            yield return Strip("survivor-walk", player.transform, 24);
            input.Value.Move = Vector2.zero;
            // Zombies coming at the player.
            foreach (var (type, name) in new[] { (ZombieType.Basic, "zombie"), (ZombieType.Dog, "dog"), (ZombieType.Tiger, "tiger"), (ZombieType.Bear, "bear") })
            {
                var z = session.Stage.SpawnZombie(type, player.transform.position + new Vector3(-9f, 0f, 0f));
                for (var i = 0; i < 9; i++)
                {
                    Noise.Emit(player.transform.position, 30f);
                    yield return new WaitForSeconds(0.1f);
                }
                if (z != null && !z.IsDead) yield return Strip(name, z.transform, 24, type == ZombieType.Bear ? 1.8f : 1f);
                if (z != null) Object.Destroy(z.gameObject);
                yield return null;
            }
            player.InputSource = null;
            session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-08 (the legs closer together, the feet nearly parallel): each of the four characters walking
        /// forward (towards the camera) then backward, a long gun held, seen from the front and from the game camera, to
        /// logs/char-preview/legs-{character}-{fwd|back}-{front|game}-{frame}.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheLegs()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            foreach (var character in session.Tuning.characters)
            {
                session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), character, 31);
                for (var i = 0; i < 3; i++) yield return null;
                var player = session.LocalSurvivor;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
                player.TryPickup(LootItem.SpawnLongGun(player.transform.position, session.Tuning.Firearm(WeaponId.AR), 30));
                var input = new Scripted { Value = new CharacterInput { Pickup = true, GrenadeDistance = -1f } };
                player.InputSource = input;
                for (var i = 0; i < 5; i++) yield return null;
                player.Select(Slot.LongGun);
                yield return new WaitForSeconds(0.5f);
                foreach (var (way, move, aim) in new[] { ("fwd", new Vector2(0f, -1f), new Vector2(0f, -1f)), ("back", new Vector2(0f, 1f), new Vector2(0f, -1f)) })
                {
                    input.Value = new CharacterInput { Move = move, Aim = aim, GrenadeDistance = -1f };
                    yield return new WaitForSeconds(0.6f);
                    Time.captureFramerate = 30;
                    for (var f = 0; f < 8; f++)
                    {
                        yield return null;
                        yield return null;
                        Capture(Path.Combine(outDir, $"legs-{character.id}-{way}-front-{f}.png"), player.transform.position, 1.3f, Quaternion.Euler(8f, 180f, 0f));
                        if (f % 4 == 0) Capture(Path.Combine(outDir, $"legs-{character.id}-{way}-game-{f}.png"), player.transform.position, 1.6f);
                    }
                    Time.captureFramerate = 0;
                }
                player.InputSource = null;
                session.GoTo(SessionState.MainMenu);
                yield return null;
            }
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-08: an invisible car on the first rescue mission (an overturned one lost every face). The overturned
        /// and tipped cars there, from the game camera, to logs/char-preview/car-{x}-{z}.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheOverturnedCars()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var layout = StageLibrary.Load("m2_scientist_a");
            session.StartStage(layout, session.Tuning.characters[0], 31);
            for (var i = 0; i < 3; i++) yield return null;
            foreach (var p in layout.Map.props.Where(p => (p.id == "car" || p.id == "van") && Mathf.Abs(p.roll) > 45f).Take(4))
            {
                Capture(Path.Combine(outDir, $"car-{p.x:0}-{p.z:0}.png"), new Vector3(p.x, 0f, p.z), 4f);
                yield return null;
            }
            session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-09: the scientist in the owner's model, and the bigger Tiger next to a player, from the game
        /// camera, to logs/char-preview/scientist.png and tiger-size.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTheScientistAndTheTiger()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            session.StartStage(StageLibrary.Load("test_rescue"), session.Tuning.characters[0], 31);
            for (var i = 0; i < 10; i++) yield return null;
            var scientist = session.Hostage;
            Capture(Path.Combine(outDir, "scientist.png"), scientist.transform.position, 1.6f);
            Capture(Path.Combine(outDir, "scientist-side.png"), scientist.transform.position, 1.4f, Quaternion.Euler(8f, 180f, 0f));
            session.GoTo(SessionState.MainMenu);
            yield return null;
            session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), session.Tuning.characters[0], 31);
            for (var i = 0; i < 3; i++) yield return null;
            var player = session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            var tiger = session.Stage.SpawnZombie(ZombieType.Tiger, player.transform.position + new Vector3(3f, 0f, 0f));
            for (var i = 0; i < 4; i++) yield return null;
            if (tiger != null) tiger.enabled = false;
            Capture(Path.Combine(outDir, "tiger-size.png"), player.transform.position + new Vector3(1.5f, 0f, 0f), 2.6f);
            session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-09: the intro's pictures, rendered from the game's own maps and models (the main story: the
        /// facility, the lab, the virus, the animals, the staff, the sealed town), to Resources/UI/Intro/{n}.png (1600 x 900).
        /// </summary>
        [UnityTest]
        public IEnumerator RenderTheIntroPictures()
        {
            var outDir = Path.Combine(Application.dataPath, "Zombie", "Resources", "UI", "Intro");
            Directory.CreateDirectory(outDir);
            // The game's own scene: its sun and ambient light exactly as players see them.
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            yield return null;
            var session = GameSession.Instance;
            void Freeze()
            {
                foreach (var z in Zombie.Alive.ToArray())
                {
                    var agent = z.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent != null) agent.enabled = false;
                    z.enabled = false;
                }
            }
            IEnumerator Stage(string id)
            {
                session.GoTo(SessionState.MainMenu);
                yield return null;
                session.StartStage(StageLibrary.Load(id), session.Tuning.characters[0], 77);
                for (var i = 0; i < 6; i++) yield return null;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
                session.Stage.Population?.ClearDormant();
                // Nobody of the team in the pictures: they come at the end.
                foreach (var sv in Survivor.All.ToArray()) if (sv.IsHuman || (!sv.IsHostage && !sv.IsRival)) sv.gameObject.SetActive(false);
                yield return null;
            }
            void Shot(string name, Vector3 at, float size, Quaternion rotation, float back = 40f)
            {
                var cam = Camera.main;
                var rig = cam.GetComponent<CameraRig>();
                if (rig != null) rig.enabled = false;
                cam.orthographicSize = size;
                cam.transform.rotation = rotation;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = back + 200f;
                cam.transform.position = at - cam.transform.forward * back;
                // Clean pictures: no loot, chests or their rings.
                foreach (var item in LootItem.All.ToArray()) item.gameObject.SetActive(false);
                foreach (var chest in Chest.All.ToArray()) chest.gameObject.SetActive(false);
                Capture(Path.Combine(outDir, name), 1600, 900);
            }
            Zombie Put(ZombieType type, Vector3 at, float yaw)
            {
                var z = session.Stage.SpawnZombie(type, at);
                if (z != null) z.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                return z;
            }

            // 1. A remote town, its research and conservation facility.
            yield return Stage("m2_scientist_a");
            var size = session.Stage.Layout.Size;
            Shot("1.png", new Vector3(size.x * 0.5f, 0f, size.y * 0.45f), 20f, Quaternion.Euler(50f, 20f, 0f), 120f);

            // 2. In truth a weapons laboratory: a scientist at work.
            var scientist = session.Hostage;
            // Out in the open next to the building (no roof in the way).
            var cc = scientist.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var outside = session.Stage.Layout.PlayerSpawn + new Vector3(6f, 0f, 8f);
            if (UnityEngine.AI.NavMesh.SamplePosition(outside, out var hit, 6f, NavAreas.WalkMask)) outside = hit.position;
            scientist.transform.position = outside;
            // Facing the camera (which looks along 200°).
            scientist.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            for (var i = 0; i < 3; i++) yield return null;
            var sp = scientist.transform.position;
            Shot("2.png", sp + Vector3.up * 0.9f, 2.2f, Quaternion.Euler(18f, 200f, 0f), 6f);

            // 3. The virus: it takes the brain (a scientist face to face with one it turned).
            var turned = Put(ZombieType.Basic, sp + scientist.transform.right * 1.6f, 0f);
            for (var i = 0; i < 3; i++) yield return null;
            Freeze();
            if (turned != null) turned.transform.LookAt(new Vector3(sp.x, turned.transform.position.y, sp.z));
            scientist.transform.LookAt(new Vector3(turned.transform.position.x, sp.y, turned.transform.position.z));
            yield return null;
            Shot("3.png", (sp + turned.transform.position) * 0.5f + Vector3.up * 1f, 2.6f, Quaternion.Euler(16f, 160f, 0f), 6f);

            // 4. Rats, then the reserve's big animals.
            yield return Stage("m4_reserve_a");
            var c = session.Stage.Layout.PlayerSpawn + new Vector3(0f, 0f, 24f);
            var rng = new System.Random(5);
            for (var i = 0; i < 7; i++) Put(ZombieType.Rat, c + new Vector3((float)rng.NextDouble() * 6f - 3f, 0f, (float)rng.NextDouble() * 2.5f - 3.5f), 180f + (float)rng.NextDouble() * 40f - 20f);
            Put(ZombieType.Dog, c + new Vector3(-3.2f, 0f, 0.5f), 165f);
            Put(ZombieType.Dog, c + new Vector3(3.4f, 0f, 0.8f), 195f);
            Put(ZombieType.Tiger, c + new Vector3(0f, 0f, 2.6f), 180f);
            for (var i = 0; i < 3; i++) yield return null;
            Freeze();
            Shot("4.png", c + new Vector3(0f, 0.8f, 0f), 4.2f, Quaternion.Euler(28f, 0f, 0f), 30f);

            // 5. The scientists and the security forces turn too.
            yield return Stage("m1_gate_a");
            c = session.Stage.Layout.PlayerSpawn + new Vector3(0f, 0f, 24f);
            Put(ZombieType.Basic, c + new Vector3(-2.2f, 0f, -1f), 170f);
            Put(ZombieType.Fast, c + new Vector3(2f, 0f, -1.4f), 190f);
            Put(ZombieType.Basic, c + new Vector3(-0.6f, 0f, -2.2f), 185f);
            Put(ZombieType.Tank, c + new Vector3(3.6f, 0f, 1.2f), 200f);
            Put(ZombieType.Boss, c + new Vector3(-1f, 0f, 2.6f), 180f);
            for (var i = 0; i < 3; i++) yield return null;
            Freeze();
            Shot("5.png", c + new Vector3(0f, 1.2f, 0f), 5f, Quaternion.Euler(24f, 0f, 0f), 30f);

            // 6. The government seals the town (the checkpoint, the helicopter waiting).
            var heli = Helicopter.Current;
            var hp = heli != null ? heli.transform.position : session.Stage.Layout.PlayerSpawn;
            Shot("6.png", hp, 11f, Quaternion.Euler(50f, 30f, 0f), 80f);

            session.GoTo(SessionState.MainMenu);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-09 (Google Play): the feature graphic, 1024 x 500, the cartoon backdrop with the logo on the left
        /// and the team on the right, to docs/store/feature-graphic.png.
        /// </summary>
        [UnityTest]
        public IEnumerator RenderTheFeatureGraphic()
        {
            var store = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "docs", "store");
            // Google Play's feature graphic, and the App Store's header artwork in both its sizes (owner, 2026-10-11).
            foreach (var (file, fw, fh) in new[] { ("feature-graphic.png", 1024, 500), ("app-store-header-3840x1646.png", 3840, 1646), ("app-store-header-5244x2950.png", 5244, 2950) })
            {
                var run = RenderBanner(Path.Combine(store, file), fw, fh);
                while (run.MoveNext()) yield return run.Current;
            }
        }

        private IEnumerator RenderBanner(string outFile, int w, int h)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            var cam = new GameObject("Feature camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            var canvasGo = new GameObject("Feature", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Laid out on a 1024-wide plan: wide pictures keep its height (500), taller ones its width.
            var wide = (float)w / h >= 1024f / 500f;
            scaler.referenceResolution = new Vector2(1024f, 500f);
            scaler.matchWidthOrHeight = wide ? 1f : 0f;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var root = (RectTransform)canvasGo.transform;
            var back = UiKit.Node(root, "Back").gameObject.AddComponent<UnityEngine.UI.RawImage>();
            back.texture = UiKit.Backdrop();
            UiKit.Fill(back.rectTransform);
            var team = UiKit.Node(root, "Team").gameObject.AddComponent<UnityEngine.UI.Image>();
            team.sprite = Resources.Load<Sprite>("UI/Portraits/team");
            team.preserveAspect = true;
            var trt = team.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(1f, 0.5f);
            trt.anchoredPosition = new Vector2(-40f, -4f);
            trt.sizeDelta = new Vector2(440f, 470f);
            var title = UiKit.Title(root, "Z-Force", 118, UiKit.Green);
            var rt1 = title.rectTransform;
            rt1.anchorMin = rt1.anchorMax = rt1.pivot = new Vector2(0f, 0.5f);
            rt1.anchoredPosition = new Vector2(46f, 52f);
            rt1.sizeDelta = new Vector2(520f, 150f);
            var sub = UiKit.Title(root, "The Laboratory", 46, UiKit.Ink);
            var rt2 = sub.rectTransform;
            rt2.anchorMin = rt2.anchorMax = rt2.pivot = new Vector2(0f, 0.5f);
            rt2.anchoredPosition = new Vector2(52f, -48f);
            rt2.sizeDelta = new Vector2(520f, 64f);
            var line = UiKit.Label(root, "Rescue the scientists. Recover the vaccine.", 22, UiKit.Yellow, TextAnchor.MiddleCenter);
            var rt3 = line.rectTransform;
            rt3.anchorMin = rt3.anchorMax = rt3.pivot = new Vector2(0f, 0.5f);
            rt3.anchoredPosition = new Vector2(52f, -112f);
            rt3.sizeDelta = new Vector2(520f, 40f);
            for (var i = 0; i < 3; i++) yield return null;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            File.WriteAllBytes(outFile, t.EncodeToPNG());
            RenderTexture.active = null;
            Object.Destroy(t);
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(canvasGo);
            Object.Destroy(cam.gameObject);
            yield return null;
        }

        /// <summary>Owner, 2026-10-09: each card of the introduction once its line is typed, to logs/char-preview/intro-{n}.png (2400 x 1080).</summary>
        [UnityTest]
        public IEnumerator CaptureTheIntro()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            var session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            yield return null;
            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.name == "Menus");
            var cam = new GameObject("UI capture camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            const int w = 2400, h = 1080;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            session.GoTo(SessionState.Intro);
            for (var i = 0; i < 3; i++) yield return null;
            var intro = Object.FindAnyObjectByType<IntroPlayer>();
            Assert.IsNotNull(intro);
            var next = typeof(IntroPlayer).GetMethod("Next", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (var card = 0; card < IntroPlayer.Cards.Length; card++)
            {
                yield return new WaitForSeconds(0.6f);
                next.Invoke(intro, null); // the whole line at once
                yield return new WaitForSeconds(1.2f);
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"intro-{card + 1}.png"), t.EncodeToPNG());
                RenderTexture.active = null;
                Object.Destroy(t);
                next.Invoke(intro, null); // the next card
            }
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(SessionState.MainMenu, session.State, "the last tap leaves the intro");
            Assert.IsTrue(session.Settings.introSeen);
            session.Settings.introSeen = false;
            cam.targetTexture = null;
            Object.Destroy(cam.gameObject);
            Object.Destroy(rt);
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(session.gameObject);
            yield return null;
        }

        /// <summary>Owner, 2026-10-11: the Vaccine stage's rival team, from the game camera, to logs/char-preview/rivals.png.</summary>
        [UnityTest]
        public IEnumerator CaptureTheRivals()
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "char-preview");
            Directory.CreateDirectory(outDir);
            // The game's own scene and light, by day (the rivals' colours as players see them).
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            yield return null;
            var session = GameSession.Instance;
            session.StartStage(StageLibrary.Load("m7_collection_a"), session.Tuning.characters[0], 31);
            for (var i = 0; i < 10; i++) yield return null;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            var cam = Camera.main;
            var rig = cam.GetComponent<CameraRig>();
            if (rig != null) rig.enabled = false;
            foreach (var team in new[] { Survivor.RivalTeam, Survivor.RivalTeam2 })
            {
                var squad = Survivor.All.Where(s => s.Team == team).ToArray();
                if (squad.Length == 0) continue;
                // The team side by side, out in the open.
                var at = squad[0].transform.position;
                for (var i = 0; i < squad.Length; i++)
                {
                    var cc = squad[i].GetComponent<CharacterController>();
                    cc.enabled = false;
                    squad[i].transform.position = at + new Vector3((i - 1) * 1.6f, 0f, 0f);
                    squad[i].transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    cc.enabled = true;
                    squad[i].InputSource = null;
                }
                for (var i = 0; i < 4; i++) yield return null;
                Debug.Log($"[ZOMBIE-RIVAL] team {team}: {string.Join(", ", squad.Select(s => s.name + (s.HasModel ? " model" : " NO MODEL")))}");
                Capture(Path.Combine(outDir, $"rivals-{team}.png"), at, 2.4f);
                Capture(Path.Combine(outDir, $"rivals-{team}-side.png"), at, 1.6f, Quaternion.Euler(8f, 180f, 0f));
            }
            session.GoTo(SessionState.MainMenu);
            yield return null;
        }

        /// <summary>
        /// Owner, 2026-10-12 (the inventory as a grid of pictures): each inventory item's 3D model on its own, in the Main
        /// scene's light, a three-quarter view from the game camera's angle, on a transparent background, 256 x 256, to
        /// Resources/UI/Icons/{model}.png.
        /// </summary>
        [UnityTest]
        public IEnumerator RenderTheItemIcons()
        {
            var outDir = Path.Combine(Application.dataPath, "Zombie", "Resources", "UI", "Icons");
            Directory.CreateDirectory(outDir);
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            yield return null;
            const int px = 256;
            var cam = new GameObject("Icon camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.transform.rotation = Quaternion.Euler(CameraRig.Tilt, 0f, 0f);
            var ids = new[] { "ar_magazine", "shotgun_magazine", "sniper_magazine", "smg_magazine", "medical", "grenade", "c4", "syringe" };
            var spot = new Vector3(5000f, 0f, 5000f);
            foreach (var id in ids)
            {
                var holder = new GameObject("Icon " + id);
                holder.transform.position = spot;
                var model = Art.PackArt.Show(holder.transform, id, 1f, Vector3.zero, 35f, false);
                if (model == null)
                {
                    Debug.LogWarning("[ZOMBIE-ICON] no model " + id);
                    Object.Destroy(holder);
                    continue;
                }
                yield return null;
                var b = model.GetComponent<Renderer>().bounds;
                // Fit the model in the frame from the camera's view, with a small margin.
                var corners = new System.Collections.Generic.List<Vector3>();
                for (var i = 0; i < 8; i++)
                    corners.Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                cam.transform.position = b.center - cam.transform.forward * 20f;
                float half = 0f;
                var mid = Vector2.zero;
                var local = corners.Select(c => cam.transform.InverseTransformPoint(c)).ToList();
                var minX = local.Min(c => c.x); var maxX = local.Max(c => c.x); var minY = local.Min(c => c.y); var maxY = local.Max(c => c.y);
                mid = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                half = Mathf.Max(maxX - minX, maxY - minY) * 0.5f * 1.12f;
                cam.transform.position += cam.transform.right * mid.x + cam.transform.up * mid.y;
                cam.orthographicSize = Mathf.Max(0.01f, half);
                var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(px, px, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, px, px), 0, 0);
                t.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + ".png"), t.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.Destroy(t);
                Object.Destroy(rt);
                Object.Destroy(holder);
                yield return null;
            }
            Object.Destroy(cam.gameObject);
        }

        /// <summary>
        /// The Main scene's lighting (flat ambient 0.6 grey): the tests run in an empty scene, whose dark default ambient
        /// made every capture darker than the game.
        /// </summary>
        private static void GameLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
        }

        /// <summary>From the game camera's angle, centred on <paramref name="at"/>, <paramref name="size"/> m half-height.</summary>
        private static void Capture(string file, Vector3 at, float size, Quaternion? rotation = null)
        {
            var cam = Camera.main;
            var pos = cam.transform.position;
            var rot = cam.transform.rotation;
            var old = cam.orthographicSize;
            cam.orthographicSize = size;
            var near = cam.nearClipPlane;
            if (rotation.HasValue) cam.transform.rotation = rotation.Value;
            // From the side, up close: nothing (a fence, a wall) between the camera and the character.
            var back = rotation.HasValue ? 2.5f : 40f;
            if (rotation.HasValue) cam.nearClipPlane = 0.1f;
            cam.transform.position = at + Vector3.up * 1f - cam.transform.forward * back;
            Capture(file, 900, 600);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.orthographicSize = old;
            cam.nearClipPlane = near;
        }

        private static void Capture(string file) => Capture(file, 1600, 720);

        private static void Capture(string file, int width, int height)
        {
            var cam = Camera.main;
            // sRGB: the picture as the screen shows it (a linear target saved the colours too dark).
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(width, height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            t.Apply();
            File.WriteAllBytes(file, t.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(t);
            Object.Destroy(rt);
        }
    }
}
