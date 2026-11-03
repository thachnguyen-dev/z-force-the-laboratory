using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    public enum SessionState { MainMenu, CoopSelect, Join, OnlineLobby, StageSelect, CharacterSelect, Settings, Playing, Victory, Defeat, Intro }

    /// <summary>
    /// The whole game flow: main menu → stage selection (every stage open) → stage → Victory / Defeat →
    /// Stage Selection or Main Menu (D-188, D-189). One instance lives in the scene.
    /// </summary>
    public partial class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        /// <summary>Editor only: the Map Editor stores a stage id here to play it right away.</summary>
        public const string PlayStagePref = "zombie.playStage";

        private System.Collections.IEnumerator PlayStageSoon(string id)
        {
            // Netcode and the scene finish starting up first.
            yield return new WaitForSeconds(0.3f);
            _stageId = id;
            StartStage(StageLibrary.Load(id), Tuning.characters[0], System.Environment.TickCount);
        }

        public SessionState State { get; private set; } = SessionState.MainMenu;
        public bool Playing => State == SessionState.Playing;
        public Transform StageRoot { get; private set; }
        public StageRuntime Stage { get; private set; }
        public Survivor LocalSurvivor { get; private set; }
        public Survivor AiSurvivor { get; private set; }
        /// <summary>The menu path chose Co-op with an AI teammate.</summary>
        public bool WithAi { get; private set; }
        public SettingsData Settings { get; private set; }
        public GameTuning Tuning { get; private set; }

        private CameraRig _camera;
        private PlayerHud _hud;

        private void Awake()
        {
            Instance = this;
            // Owner, 2026-10-09: the characters (players, AI, the scientist, rivals) walk through each other.
            Physics.IgnoreLayerCollision(Layers.Survivor, Layers.Survivor, true);
            Tuning = GameTuning.Load();
            Settings = SettingsStore.Load();
            ApplySettings();
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            // Owner, 2026-10-07: the stats display popped up by itself and couldn't be closed: URP's Rendering Debugger
            // (its Display Stats panel) opens on a three-finger double tap, which twin-stick play does by accident.
            UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _camera = CameraRig.Create();
            _camera.transform.position = new Vector3(60f, 40f, 20f);
            if (Gui.DevTools) StartCoroutine(DevCapture());
            // Owner, 2026-10-09: the Google Play screenshots (a PC command line; release builds too).
            var storeShots = NetSession.CommandLineValue("-zombie-store-shots");
            if (!string.IsNullOrEmpty(storeShots) && !Application.isMobilePlatform) StartCoroutine(StoreShots(storeShots));
            // Owner, 2026-10-09: the introduction plays the first time the game starts (not in automated runs).
            if (!Settings.introSeen && !Application.isBatchMode && System.Array.FindIndex(System.Environment.GetCommandLineArgs(), a => a.StartsWith("-zombie-")) < 0)
                State = SessionState.Intro;
#if UNITY_EDITOR
            // The Map Editor's "Play this stage" button: go straight into that stage (Solo).
            var play = UnityEditor.EditorPrefs.GetString(PlayStagePref, "");
            if (!string.IsNullOrEmpty(play))
            {
                UnityEditor.EditorPrefs.DeleteKey(PlayStagePref);
                StartCoroutine(PlayStageSoon(play));
            }
#endif
        }

        // On mobile, CaptureScreenshot always writes into persistentDataPath and wants a bare file name.
        private static void Shot(string folder, string name) =>
            ScreenCapture.CaptureScreenshot(Application.isMobilePlatform ? name : System.IO.Path.Combine(folder, name));

        /// <summary>
        /// Development captures: the local character's animations, one screenshot each (2026-10-05, the owner's character
        /// models): idle with the Pistol, walking, aiming the AR, the kick of a shot, reloading, a knife stab, walking with
        /// the AR. The character faces the camera for most shots so the face and chest show.
        /// </summary>
        private System.Collections.IEnumerator CaptureAnimations(string folder)
        {
            var p = LocalSurvivor;
            var cc = p.GetComponent<CharacterController>();
            void Face(float yaw) => p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            System.Collections.IEnumerator Walk(Vector3 dir, float seconds, string shot)
            {
                for (var t = 0f; t < seconds; t += Time.deltaTime)
                {
                    cc.Move(dir * 4f * Time.deltaTime);
                    if (shot != null && t > seconds * 0.6f)
                    {
                        Shot(folder, shot);
                        shot = null;
                    }
                    yield return null;
                }
            }
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Destroy(z.gameObject);
            // No input: the capture turns the character itself.
            p.InputSource = null;
            Face(160f);
            yield return new WaitForSeconds(1f);
            Shot(folder, "anim-0-game-view.png");
            yield return null;
            yield return null;
            // Close up for the rest.
            Camera.main.orthographicSize = 2.2f;
            yield return new WaitForSeconds(0.3f);
            Shot(folder, "anim-1-idle-pistol.png");
            yield return new WaitForSeconds(0.3f);
            Face(90f);
            yield return Walk(Vector3.right, 0.8f, "anim-2-walk-pistol.png");
            p.TryPickup(LootItem.SpawnLongGun(p.transform.position, Tuning.Firearm(WeaponId.AR), 30));
            p.Select(Slot.LongGun);
            Face(160f);
            yield return new WaitForSeconds(1.2f);
            Shot(folder, "anim-3-aim-ar.png");
            yield return new WaitForSeconds(0.3f);
            p.DevFire();
            yield return null;
            Shot(folder, "anim-4-fire-ar.png");
            yield return new WaitForSeconds(0.4f);
            p.ActiveFirearm?.RequestReload();
            yield return new WaitForSeconds(0.45f);
            Shot(folder, "anim-5-reload-ar.png");
            yield return new WaitForSeconds(2.5f);
            Face(200f);
            yield return Walk(Vector3.back, 0.8f, "anim-6-walk-ar.png");
            p.Select(Slot.Knife);
            Face(160f);
            yield return new WaitForSeconds(1.2f);
            p.DevStab();
            yield return new WaitForSeconds(0.1f);
            Shot(folder, "anim-7-stab.png");
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// Development builds only: <c>-zombie-capture &lt;folder&gt;</c> plays the test stage, saves a few screenshots
        /// and quits — used to check a build renders correctly without a person at the keyboard.
        /// </summary>
        private System.Collections.IEnumerator DevCapture()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-zombie-capture");
            if (i < 0 || i + 1 >= args.Length) yield break;
            var folder = args[i + 1];
            // Mobile players can only write inside their own storage (screenshots land there directly).
            if (!System.IO.Path.IsPathRooted(folder)) folder = System.IO.Path.Combine(Application.persistentDataPath, folder);
            if (!Application.isMobilePlatform) System.IO.Directory.CreateDirectory(folder);
            if (System.Array.IndexOf(args, "-zombie-net-host") >= 0)
            {
                yield return DevNetHost(folder);
                yield break;
            }
            var join = NetSession.CommandLineValue("-zombie-net-join");
            if (join != null)
            {
                yield return DevNetJoin(folder, join);
                yield break;
            }
            yield return new WaitForSeconds(1f);
            Shot(folder, "0-menu.png");
            if (System.Array.IndexOf(args, "-zombie-capture-stages") >= 0)
            {
                GoTo(SessionState.StageSelect);
                yield return new WaitForSeconds(0.6f);
                Shot(folder, "stages-story.png");
                yield return new WaitForSeconds(0.3f);
                _allStages = true;
                yield return new WaitForSeconds(0.4f);
                Shot(folder, "stages-all.png");
                yield return new WaitForSeconds(0.4f);
                Application.Quit();
                yield break;
            }
            if (System.Array.IndexOf(args, "-zombie-capture-settings") >= 0)
            {
                State = SessionState.Settings;
                foreach (var (_, tab) in SettingsTabs)
                {
                    _tab = tab;
                    yield return new WaitForSeconds(0.5f);
                    Shot(folder, $"settings-{tab}.png");
                }
                yield return new WaitForSeconds(0.5f);
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var coop = System.Array.IndexOf(args, "-zombie-coop") >= 0;
            // -zombie-character id: play as that character (2026-10-05: checking the owner's character models).
            var characterId = NetSession.CommandLineValue("-zombie-character");
            var playAs = System.Array.Find(Tuning.characters, c => c.id == characterId) ?? Tuning.characters[0];
            StartStage(StageLibrary.Load(NetSession.CommandLineValue("-zombie-stage") ?? StageLibrary.TestStageId), playAs, System.Environment.TickCount, coop);
            yield return new WaitForSeconds(2f);
            Shot(folder, "1-spawn.png");
            // -zombie-capture-inventory: the backpack with one of everything, open (owner, 2026-10-12: the grid of pictures).
            if (System.Array.IndexOf(args, "-zombie-capture-inventory") >= 0)
            {
                var inv = LocalSurvivor.Inventory;
                foreach (var t in new[] { AmmoType.AR, AmmoType.Shotgun, AmmoType.Rifle, AmmoType.SMG }) inv.AddAmmo(t, 30);
                inv.AddHealthItem();
                inv.AddHealthItem();
                inv.AddGrenade();
                inv.C4 = true;
                inv.Vaccine = true;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Destroy(z.gameObject);
                _hud.TouchMode = true;
                _hud.OpenInventory();
                yield return new WaitForSeconds(1f);
                Shot(folder, "inventory.png");
                yield return new WaitForSeconds(0.5f);
                Application.Quit();
                yield break;
            }
            // -zombie-capture-anim: the character's animations, one shot each (walk, aim, fire, reload, knife).
            if (System.Array.IndexOf(args, "-zombie-capture-anim") >= 0)
            {
                yield return CaptureAnimations(folder);
                Application.Quit();
                yield break;
            }
            // Can every item and chest be walked to from the start? (owner, 2026-10-04: weapons on rooftops.)
            {
                var start = Stage.Layout.PlayerSpawn;
                if (UnityEngine.AI.NavMesh.SamplePosition(Stage.Layout.PlayerSpawn, out var startHit, 3f, UnityEngine.AI.NavMesh.AllAreas)) start = startHit.position;
                var path = new UnityEngine.AI.NavMeshPath();
                var bad = 0;
                var checkedCount = 0;
                var targets = new List<(string what, Vector3 at)>();
                foreach (var item in LootItem.All) targets.Add((item.Data.Kind.ToString(), item.transform.position));
                foreach (var c in Chest.All) targets.Add(("Chest", c.transform.position));
                foreach (var (what, at) in targets)
                {
                    checkedCount++;
                    var ok = UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 1.2f, UnityEngine.AI.NavMesh.AllAreas)
                             && UnityEngine.AI.NavMesh.CalculatePath(start, hit.position, UnityEngine.AI.NavMesh.AllAreas, path)
                             && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    if (ok) continue;
                    bad++;
                    if (bad <= 12) Debug.Log($"[ZOMBIE-REACH] unreachable {what} at {at:0.0}");
                }
                Debug.Log($"[ZOMBIE-REACH] {bad} of {checkedCount} items and chests can't be reached");
            }
            yield return new WaitForSeconds(0.5f);
            // Walk the player to the Town edge and fire, so zombies react.
            var player = LocalSurvivor;
            player.GetComponent<CharacterController>().enabled = false;
            player.transform.position = new Vector3(54f, 0f, 38f); // inside House 1: its roof lifts
            player.GetComponent<CharacterController>().enabled = true;
            player.Inventory.AddGrenade();
            player.TryPickup(LootItem.SpawnLongGun(player.transform.position, Tuning.Firearm(WeaponId.AR), 30));
            player.Select(Slot.LongGun);
            yield return new WaitForSeconds(3f);
            player.DevFire();
            Shot(folder, "2-town.png");
            // -zombie-capture-at x,z;x,z: more views (map art checks).
            yield return new WaitForSeconds(0.3f);
            var spots = NetSession.CommandLineValue("-zombie-capture-at");
            // -zombie-capture-chest: also look at the first equipment chest (chests are placed at random each run).
            if (System.Array.IndexOf(args, "-zombie-capture-chest") >= 0)
            {
                var firstChest = Chest.All.Find(c => c.Lifetime < 0f);
                if (firstChest != null)
                {
                    var at = firstChest.transform.position + new Vector3(1.5f, 0f, -2f);
                    spots = (string.IsNullOrEmpty(spots) ? "" : spots + ";") + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1}", at.x, at.z);
                }
            }
            if (!string.IsNullOrEmpty(spots))
            {
                var n = 3;
                foreach (var spot in spots.Split(';'))
                {
                    var xz = spot.Split(',');
                    if (xz.Length != 2 || !float.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sx) ||
                        !float.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sz)) continue;
                    player.GetComponent<CharacterController>().enabled = false;
                    player.transform.position = new Vector3(sx, 0f, sz);
                    player.GetComponent<CharacterController>().enabled = true;
                    _camera.Snap();
                    yield return new WaitForSeconds(1.2f);
                    Shot(folder, $"{n++}-at-{sx:0}-{sz:0}.png");
                    // The screenshot is taken at the end of the frame: wait before moving on.
                    yield return new WaitForSeconds(0.3f);
                }
            }
            yield return new WaitForSeconds(0.5f);
            if (coop)
            {
                // Let the teammate play for a while next to the Town, then look again.
                var start = Zombie.Alive.Count;
                for (var t = 0; t < 8; t++)
                {
                    yield return new WaitForSeconds(1f);
                    var brain = AiSurvivor != null ? AiSurvivor.GetComponent<AiTeammate>() : null;
                    Debug.Log($"[ZOMBIE-CAPTURE] t={t + 1}s ai={(AiSurvivor == null ? "gone" : AiSurvivor.IsDead ? "dead" : brain.State)} hp={(AiSurvivor != null ? AiSurvivor.Hp : 0):0} gun={AiSurvivor?.LongGunId} zombies={Zombie.Alive.Count} human={LocalSurvivor.transform.position} holds={LocalSurvivor.Selected}/{LocalSurvivor.LongGunId} mag={LocalSurvivor.Pistol.Magazine}");
                }
                Shot(folder, "3-coop.png");
                yield return new WaitForSeconds(0.5f);
            }
            // Rival team (Vaccine stages): log what it does for a while.
            if (Mission.RivalCarrier != null || System.Array.Exists(Survivor.All.ToArray(), s => s.IsRival))
            {
                for (var t = 0; t < 25; t++)
                {
                    yield return new WaitForSeconds(1f);
                    var line = new System.Text.StringBuilder($"[ZOMBIE-RIVALS] t={t + 1}s hold={Mission.RivalHoldLeft:0} state={State} ");
                    foreach (var s in Survivor.All)
                        if (s.IsRival)
                            line.Append($"{s.DisplayName}:{(s.IsDead ? "dead" : s.GetComponent<AiTeammate>()?.State)}@{s.transform.position.x:0},{s.transform.position.z:0}{(s.Inventory.Vaccine ? "+V" : "")} ");
                    Debug.Log(line.ToString());
                }
                Shot(folder, "9-rivals.png");
                yield return new WaitForSeconds(0.3f);
            }
            // Average over 3 s (one frame is too noisy to compare builds).
            var frames = 0;
            var worst = 0f;
            var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 3f)
            {
                yield return null;
                frames++;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            }
            var equipment = Chest.All.FindAll(c => c.RemoveWith == null && c.Lifetime < 0f);
            foreach (var c in equipment.GetRange(0, Mathf.Min(3, equipment.Count)))
            {
                var mr = c.GetComponentInChildren<MeshRenderer>(true);
                var mf = c.GetComponentInChildren<MeshFilter>(true);
                Debug.Log($"[ZOMBIE-CAPTURE] chest at {c.transform.position} active {c.gameObject.activeInHierarchy} renderer {(mr != null ? mr.enabled + " " + mr.gameObject.activeInHierarchy + " bounds " + mr.bounds : "none")} mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1)} material {(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "none")}");
            }
            Debug.Log($"[ZOMBIE-CAPTURE] chests {equipment.Count}{(equipment.Count > 0 ? " first at " + equipment[0].transform.position + " model " + (equipment[0].GetComponentInChildren<MeshRenderer>() != null) : "")}, long guns on the ground {LootItem.All.FindAll(i => i.Data.Kind == LootKind.LongGun).Count}");
            Debug.Log($"[ZOMBIE-CAPTURE] zombies {Zombie.Alive.Count}, loot {LootItem.All.Count}, fps avg {frames / (Time.realtimeSinceStartup - started):0.0}, worst frame {worst * 1000f:0} ms");
            Application.Quit();
        }


        /// <summary>Dev: host on localhost, start the test stage when the friend joins, log both characters, quit.</summary>
        private System.Collections.IEnumerator DevNetHost(string folder)
        {
            // Netcode registers its message types after the scene has loaded: never host during scene start-up.
            yield return new WaitForSeconds(0.5f);
            var net = NetSession.Ensure();
            net.HostDirect();
            Debug.Log($"[ZOMBIE-NET] hosting at {net.LocalAddress}: {net.Status}");
            for (var t = 0f; t < 40f && !net.PeerConnected; t += 0.25f) yield return new WaitForSeconds(0.25f);
            if (!net.PeerConnected)
            {
                Debug.Log("[ZOMBIE-NET] host: nobody joined");
                Application.Quit();
                yield break;
            }
            StartStage(StageLibrary.Load(NetSession.CommandLineValue("-zombie-stage") ?? StageLibrary.TestStageId), Tuning.characters[0], 777, online: true);
            var me = LocalSurvivor;
            me.GetComponent<CharacterController>().enabled = false;
            me.transform.position = new Vector3(54f, 0f, 38f);
            me.GetComponent<CharacterController>().enabled = true;
            var quitAt = int.TryParse(NetSession.CommandLineValue("-zombie-net-host-quit"), out var q) ? q : -1;
            for (var t = 0; t < 14; t++)
            {
                yield return new WaitForSeconds(1f);
                if (t + 1 == quitAt)
                {
                    // Simulate the Host vanishing mid-match (the friend should take over, D-172).
                    Debug.Log("[ZOMBIE-NET] host: quitting abruptly");
                    Application.Quit();
                    yield break;
                }
                Survivor friend = null;
                foreach (var s in Survivor.All) if (s.RemoteDriven) friend = s;
                Debug.Log($"[ZOMBIE-NET] host t={t + 1}s zombies={Zombie.Alive.Count} loot={LootItem.All.Count} friend=" +
                          (friend == null ? "none" : $"{friend.transform.position} hp={friend.Hp:0}/{friend.MaxHp:0} char={friend.Character.displayName} holds={friend.Selected}") +
                          $" peer={net.PeerConnected}");
            }
            Shot(folder, "net-host.png");
            yield return new WaitForSeconds(0.5f);
            net.Leave();
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>Dev: join, walk into the Town, shoot the nearest zombie, switch weapons, log what arrives, quit.</summary>
        private System.Collections.IEnumerator DevNetJoin(string folder, string address)
        {
            yield return new WaitForSeconds(1.5f);
            var net = NetSession.Ensure();
            if (int.TryParse(NetSession.CommandLineValue("-zombie-character"), out var pick)) net.ChooseCharacter(pick);
            net.JoinDirect(address);
            for (var t = 0f; t < 30f && !net.InMatch; t += 0.25f) yield return new WaitForSeconds(0.25f);
            if (!net.InMatch)
            {
                Debug.Log($"[ZOMBIE-NET] client: no match started ({net.Status})");
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            var me = LocalSurvivor;
            me.GetComponent<CharacterController>().enabled = false;
            me.transform.position = new Vector3(50f, 0f, 44f);
            me.GetComponent<CharacterController>().enabled = true;
            // Autopilot: aim at the nearest zombie in range and hold Fire.
            _hud.DevOverride = input =>
            {
                Zombie target = null;
                var best = 14f;
                foreach (var z in Zombie.Alive)
                {
                    var d = Vector3.Distance(z.transform.position, me.transform.position);
                    if (d < best) { best = d; target = z; }
                }
                if (target == null) return input;
                var to = target.transform.position - me.transform.position;
                input.Aim = new Vector2(to.x, to.z).normalized;
                input.FireHeld = true;
                return input;
            };
            var rejoin = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-zombie-net-rejoin") >= 0;
            var seconds = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-zombie-net-long") >= 0 ? 34 : 12;
            for (var t = 0; t < seconds; t++)
            {
                yield return new WaitForSeconds(1f);
                if (rejoin && t == 4)
                {
                    // D-179/D-180: drop the connection, come back, and get the same character.
                    var before = me.transform.position;
                    net.Leave();
                    Debug.Log("[ZOMBIE-NET] client: dropped the connection");
                    yield return new WaitForSeconds(2f);
                    net.JoinDirect(address);
                    for (var w = 0f; w < 15f && !net.InMatch; w += 0.25f) yield return new WaitForSeconds(0.25f);
                    me = LocalSurvivor;
                    Debug.Log($"[ZOMBIE-NET] client: rejoined={net.InMatch} me={(me != null ? me.transform.position.ToString() : "none")} (was {before})");
                    if (me == null) break;
                    var captured = me;
                    _hud.DevOverride = input => input;
                    me = captured;
                }
                if (t == 3) me.Select(Slot.Knife);
                if (t == 6) me.Select(Slot.Pistol);
                if (me == null) break;
                Debug.Log($"[ZOMBIE-NET] client t={t + 1}s snapshots={ClientSnapshots()} zombies={Zombie.Alive.Count} loot={LootItem.All.Count} " +
                          $"chests={Chest.All.Count} me={me.transform.position} hp={me.Hp:0} holds={me.Selected} id={me.NetId} role={net.Role} peer={net.PeerConnected} sim={World.Simulating} rivals={System.Array.FindAll(Survivor.All.ToArray(), s => s.Team == Survivor.RivalTeam && !s.IsDead).Length}");
            }
            Shot(folder, "net-client.png");
            yield return new WaitForSeconds(3f);
            Debug.Log($"[ZOMBIE-NET] client end: state={State} role={net.Role} status={net.Status}");
            Application.Quit();
        }

        private static int ClientSnapshots() => NetSession.Instance != null ? NetSession.Instance.ClientSide.Snapshots : 0;
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _menus?.Destroy();
        }

        public void ApplySettings()
        {
            Sfx.Volume = Settings.volume;
            if (Settings.quality >= 0 && Settings.quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(Settings.quality, true);
        }

        public void Haptic()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (Settings.haptics) Handheld.Vibrate();
#endif
        }

        // ---- Stage lifecycle ----

        /// <summary>
        /// Solo is always alone (D-183). Co-op is 1 human + 1 AI (<paramref name="withAi"/>) or two humans online
        /// (<paramref name="online"/>: this device is the Host) — D-170.
        /// </summary>
        /// <param name="intro">The players arrive in the helicopter (owner, 2026-10-08); the menu's Start does this.</param>
        public void StartStage(StageLayout layout, CharacterDef character, int seed, bool withAi = false, bool online = false, bool intro = false)
        {
            EndStageWorld();
            NetIds.Reset();
            World.Simulating = true;
            State = SessionState.Playing;
            StageRoot = new GameObject($"Stage: {layout.Name}").transform;
            Stage = StageRoot.gameObject.AddComponent<StageRuntime>();
            // Owner, 2026-10-03: co-op (an AI teammate or an online friend) is harder; Solo keeps the base difficulty.
            Stage.Coop = withAi || online;
            Stage.Build(layout, Tuning, seed);
            BeginConditions(layout);

            LocalSurvivor = SpawnSurvivor(layout.PlayerSpawn, character, seed);
            LocalSurvivor.NetId = 0;
            AttachHud(LocalSurvivor);
            if (withAi)
            {
                // Owner, 2026-10-03: the AI picks a random character other than the player's.
                AiSurvivor = SpawnAi(layout.PlayerSpawn + new Vector3(2f, 0f, 0f), AiCharacter(character, seed), seed + 1);
                AiSurvivor.NetId = 1;
            }
            if (layout.Rescue) SpawnHostage(layout.HostagePosition, character, seed + 3);
            BeginMission(layout);
            // Owner, 2026-10-08: the players arrive in the helicopter; they step out when it has landed.
            if (intro) Helicopter.Current?.BeginLanding();
            if (online && NetSession.Instance != null)
            {
                // The friend's character: their device moves it; the Host does everything else.
                // Each player picks their own character (owner, 2026-09-30: both may pick the same one).
                var friendCharacter = Tuning.characters[Mathf.Clamp(NetSession.Instance.PeerCharacter, 0, Tuning.characters.Length - 1)];
                var friend = SpawnSurvivor(layout.PlayerSpawn + new Vector3(-2f, 0f, 0f), friendCharacter, seed + 2, "Friend", FriendColor);
                friend.NetId = 1;
                friend.RemoteDriven = true;
                friend.RemoteTarget = friend.transform.position;
                var input = new RemoteInput();
                friend.InputSource = input;
                NetSession.Instance.HostSide.BeginMatch(friend, input, seed, layout.Id);
            }
            _camera.Target = LocalSurvivor.transform;
            _camera.Snap();
        }

        private static readonly Color FriendColor = new Color(0.3f, 0.85f, 0.45f);

        /// <summary>
        /// Bomb stages: the first player (the Host) starts with the one C4. Vaccine stages: the Vaccine waits on its cell
        /// (owner, 2026-10-02).
        /// </summary>
        private void BeginMission(StageLayout layout)
        {
            Mission.Reset();
            if (layout.Bomb) LocalSurvivor.Inventory.C4 = true;
            if (layout.VaccineMission) LootItem.Spawn(VaccineSpot(layout), LootKind.Vaccine);
            if (layout.VaccineMission)
                for (var i = 0; i < layout.RivalSpawns.Count; i++) SpawnRivals(layout.RivalSpawns[i], i);
        }

        /// <summary>
        /// Owner, 2026-10-11: the Vaccine lies at the exact centre of the map (the walkable spot nearest to it, clear of
        /// walls and obstacles; the map's Vaccine cell when there is nowhere).
        /// </summary>
        public static Vector3 VaccineSpot(StageLayout layout)
        {
            var middle = new Vector3(layout.Size.x * 0.5f, 0f, layout.Size.y * 0.5f);
            for (var reach = 3f; reach <= 48f; reach *= 2f)
                if (UnityEngine.AI.NavMesh.SamplePosition(middle, out var hit, reach, NavAreas.WalkMask) && hit.position.y < 0.3f)
                {
                    var spot = hit.position;
                    if (StageRuntime.ClearSpot(ref spot, 0.4f)) return spot;
                }
            return layout.VaccinePosition;
        }

        private static readonly Color RivalColor = new Color(0.85f, 0.22f, 0.3f);
        private static readonly Color RivalColor2 = new Color(0.95f, 0.55f, 0.15f);

        /// <summary>
        /// Vaccine stages (owner, 2026-10-03): a rival AI team of three that attacks zombies and players and goes for the
        /// Vaccine. Each starts with the Pistol and a random long gun with some ammo (owner's choice). The final stage
        /// (owner, 2026-10-07) has two such teams, which fight each other as well.
        /// </summary>
        private void SpawnRivals(Vector3 at, int teamIndex)
        {
            var m = Tuning.missions;
            var rng = new System.Random(at.GetHashCode());
            for (var i = 0; i < m.rivalCount; i++)
            {
                var offset = Quaternion.Euler(0f, i * 360f / m.rivalCount, 0f) * Vector3.forward * 1.5f;
                var name = teamIndex == 0 ? $"Rival {i + 1}" : $"Raider {i + 1}";
                // Owner, 2026-10-11: soldiers in their team's camouflage (CharacterBake.Rivals), alternating two models.
                var look = $"rival{Mathf.Min(teamIndex, 1)}_{(i % 2 == 0 ? "a" : "b")}";
                var rival = SpawnSurvivor(at + offset, new CharacterDef { id = look, displayName = "Rival" }, rng.Next(), name, teamIndex == 0 ? RivalColor : RivalColor2);
                rival.IsHuman = false;
                rival.Team = Survivor.RivalTeam + teamIndex;
                rival.NetId = 10 + teamIndex * 10 + i;
                var gun = Tuning.Firearm(Arsenal.LongGuns[rng.Next(Arsenal.LongGuns.Length)]);
                rival.TryPickup(LootItem.SpawnLongGun(rival.transform.position, gun, gun.magazine));
                rival.Inventory.AddAmmo(gun.ammo, gun.ammoPerBox * m.rivalAmmoBoxes);
                var brain = rival.gameObject.AddComponent<AiTeammate>();
                brain.Rival = true;
                rival.InputSource = brain;
            }
        }

        private static readonly Color HostageColor = new Color(0.95f, 0.95f, 0.85f);

        /// <summary>Owner, 2026-10-09: the scientist's HP +50 %. PLACEHOLDER.</summary>
        public const float HostageHpBonus = 0.5f;

        /// <summary>Rescue stages: the hostage waits at its cell; reaching them sets every zombie hunting (owner, 2026-09-30).</summary>
        private Survivor SpawnHostage(Vector3 position, CharacterDef character, int seed)
        {
            // The hostage is nobody's character: base stats, no bonuses.
            // Owner, 2026-10-09: the scientist has 50 % more HP (and heals when left alone: Survivor.HostageRegen).
            var hostage = SpawnSurvivor(position, new CharacterDef { id = "hostage", displayName = "Scientist", hpBonus = HostageHpBonus }, seed, "Hostage", HostageColor);
            hostage.IsHuman = false;
            hostage.IsHostage = true;
            hostage.NetId = 2;
            var brain = hostage.gameObject.AddComponent<HostageBrain>();
            hostage.InputSource = brain;
            // Owner, 2026-10-09: following raises the zombies' detection (Mission.RaiseAlert); they no longer all come at once.
            brain.FreedBy += player =>
            {
                if (_hud != null) _hud.Toast("The scientist follows you!");
            };
            return hostage;
        }

        /// <summary>Rain and darkness (owner, 2026-09-30): the rules multiply, the atmosphere shows it.</summary>
        private void BeginConditions(StageLayout layout)
        {
            StageConditions.Apply(layout, Tuning);
            StageAtmosphere.Attach(StageRoot, layout, Tuning);
            Zombie.Hunted = null;
        }

        /// <summary>The rescue stage's hostage, if there is one.</summary>
        public Survivor Hostage
        {
            get
            {
                foreach (var s in Survivor.All) if (s.IsHostage) return s;
                return null;
            }
        }

        private void AttachHud(Survivor survivor)
        {
            _hud = new GameObject("Player HUD").AddComponent<PlayerHud>();
            _hud.transform.SetParent(StageRoot, false);
            _hud.Player = survivor;
            _hud.Camera = _camera.GetComponent<Camera>();
            survivor.InputSource = _hud;
        }

        public PlayerHud Hud => _hud;

        /// <summary>
        /// Online client: build the replica of the Host's stage. Only this player's movement is simulated here
        /// (<see cref="World.Simulating"/> is off).
        /// </summary>
        public List<Survivor> StartReplicaStage(string stageName, int seed, List<NetSurvivorInfo> survivors, int localId)
        {
            EndStageWorld();
            World.Simulating = false;
            State = SessionState.Playing;
            var layout = StageLibrary.Load(stageName);
            StageRoot = new GameObject($"Stage: {stageName} (online)").transform;
            Stage = StageRoot.gameObject.AddComponent<StageRuntime>();
            Stage.Coop = true;
            Stage.BuildReplica(layout, Tuning, seed);
            BeginConditions(layout);
            Mission.Reset();
            var list = new List<Survivor>();
            foreach (var info in survivors)
            {
                var mine = info.Id == localId;
                var character = Tuning.characters[Mathf.Clamp(info.CharacterIndex, 0, Tuning.characters.Length - 1)];
                var s = SpawnSurvivor(info.Position, character, seed + info.Id, mine ? null : info.Name, info.Color);
                s.NetId = info.Id;
                s.IsHuman = info.IsHuman;
                s.Replica = true;
                s.IsHostage = info.IsHostage;
                s.Team = info.Team;
                if (mine)
                {
                    LocalSurvivor = s;
                    AttachHud(s);
                }
                list.Add(s);
            }
            if (LocalSurvivor != null)
            {
                _camera.Target = LocalSurvivor.transform;
                _camera.Snap();
            }
            // The friend arrives in the helicopter too (each device lands its own).
            Helicopter.Current?.BeginLanding();
            return list;
        }

        /// <summary>Online client: the Host decided the match (D-067, D-098).</summary>
        public void EndOnlineMatch(MatchOutcome outcome)
        {
            if (State != SessionState.Playing) return;
            State = outcome == MatchOutcome.Victory ? SessionState.Victory : SessionState.Defeat;
            if (Stage != null && Stage.Layout.Endless && Endless.Current != null)
            {
                Mission.FailReason = Endless.Current.Finish(Settings, true);
                SettingsStore.Save(Settings);
            }
        }

        private static bool OnlineHost => NetSession.Instance != null && NetSession.Instance.Role == NetRole.Host;
        private static bool OnlineClient => NetSession.Instance != null && NetSession.Instance.Role == NetRole.Client;

        public void StartTestStage() => StartStage(StageLibrary.Load(StageLibrary.TestStageId), Tuning.characters[0], System.Environment.TickCount);

        /// <summary>The AI teammate: an ordinary survivor driven by <see cref="AiTeammate"/> (D-043).</summary>
        /// <summary>A random character that isn't the player's (the player's own if it's the only one).</summary>
        private CharacterDef AiCharacter(CharacterDef player, int seed)
        {
            var others = new List<CharacterDef>();
            foreach (var c in Tuning.characters) if (c != player && c.id != player.id) others.Add(c);
            return others.Count > 0 ? others[new System.Random(seed).Next(others.Count)] : player;
        }

        private Survivor SpawnAi(Vector3 position, CharacterDef character, int seed)
        {
            var ai = SpawnSurvivor(position, character, seed, "AI teammate", new Color(0.95f, 0.55f, 0.15f));
            ai.IsHuman = false;
            var brain = ai.gameObject.AddComponent<AiTeammate>();
            brain.Leader = LocalSurvivor;
            ai.InputSource = brain;
            return ai;
        }

        private Survivor SpawnSurvivor(Vector3 position, CharacterDef character, int seed, string name = null, Color? color = null)
        {
            var go = new GameObject(name ?? character.displayName) { layer = Layers.Survivor };
            go.transform.SetParent(StageRoot, false);
            go.transform.position = position;
            var controller = go.AddComponent<CharacterController>();
            controller.radius = 0.4f;
            controller.height = 1.8f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.skinWidth = 0.05f;
            controller.stepOffset = 0.2f;
            var survivor = go.AddComponent<Survivor>();
            survivor.Init(Tuning, character, seed);
            survivor.DisplayName = name ?? character.displayName;
            survivor.BuildVisual(color ?? character.color);
            return survivor;
        }


        // ---- Host migration (D-172, Handoff 4 §7) ----

        /// <summary>Shown on the HUD after this device took over as Host.</summary>
        public string MigrationNotice { get; private set; }
        public float MigrationTime { get; private set; } = -100f;

        private System.Collections.IEnumerator MigrateToHost()
        {
            // Let Netcode finish shutting the old connection down before hosting again.
            yield return null;
            var client = NetSession.Instance.ClientSide;
            var formerHost = TakeOverWorld();
            Debug.Log($"[ZOMBIE-NET] host migration: now hosting ({Zombie.Alive.Count} zombies, {LootItem.All.Count} loot)");
            MigrationTime = Time.unscaledTime;
            MigrationNotice = "The host left — you're the host now.";
            var net = NetSession.Instance;
            net.Rehost(client.HostPlayerId, () =>
            {
                if (formerHost == null) return;
                var input = new RemoteInput();
                formerHost.InputSource = input;
                formerHost.RemoteDriven = true;
                formerHost.RemoteTarget = formerHost.transform.position;
                net.HostSide.BeginMatch(formerHost, input, client.Seed, client.StageName);
                MigrationNotice = !string.IsNullOrEmpty(net.JoinCode)
                    ? $"The host left — you're the host now. Your friend can rejoin with code {net.JoinCode}."
                    : $"The host left — you're the host now. Your friend can rejoin at {net.LocalAddress}.";
            });
        }

        /// <summary>
        /// Turns every replica into the real thing, from the state this device already holds. Returns the former Host's
        /// character (it stays where it was, defenceless and killable — D-179).
        /// </summary>
        internal Survivor TakeOverWorld()
        {
            World.Simulating = true;
            foreach (var z in Zombie.Alive.ToArray()) z.BecomeSimulated();
            foreach (var i in Incubator.All) i.BecomeSimulated();
            Stage.BecomeHost();
            Survivor formerHost = null;
            foreach (var s in Survivor.All)
            {
                s.BecomeSimulated();
                if (s != LocalSurvivor && s.IsHuman) formerHost = s;
            }
            if (formerHost != null && formerHost.InputSource == null) formerHost.InputSource = new RemoteInput();
            // Characters the old Host's AI drove get a brain here (the hostage, the Vaccine stage's rivals).
            foreach (var s in Survivor.All)
            {
                if (s.IsDead || s.InputSource != null && !(s.InputSource is RemoteInput)) continue;
                if (s.IsRival)
                {
                    var rival = s.gameObject.AddComponent<AiTeammate>();
                    rival.Rival = true;
                    s.InputSource = rival;
                }
                else if (s.IsHostage)
                {
                    var brain = s.gameObject.AddComponent<HostageBrain>();
                    if (s.IsFreed) brain.Resume(LocalSurvivor);
                    s.InputSource = brain;
                }
            }
            return formerHost;
        }

        private NetSession _watched;

        /// <summary>Online client: take over when the Host leaves mid-match (D-172), or follow it back to the lobby.</summary>
        private void WatchNet()
        {
            var net = NetSession.Instance;
            if (net == null || net == _watched) return;
            _watched = net;
            net.HostLost += midMatch =>
            {
                // D-172: mid-match, the remaining human takes over as Host — unless they're already dead (D-097).
                if (midMatch && Playing && LocalSurvivor != null && !LocalSurvivor.IsDead)
                {
                    StartCoroutine(MigrateToHost());
                    return;
                }
                _menuMessage = "The host left the game.";
                GoTo(SessionState.CoopSelect);
            };
            net.BackToLobby += () =>
            {
                if (State == SessionState.Playing) GoTo(SessionState.OnlineLobby);
            };
        }

        // ---- Spectating (owner, 2026-10-08: in Co-op a dead player watches their teammates; replaces D-097) ----

        private int _spectate;

        /// <summary>The teammate the dead local player is watching (null: alive, or nobody left to watch).</summary>
        public Survivor Spectated { get; private set; }

        /// <summary>The local player's living teammates, the human ones first (the AI teammate after).</summary>
        public List<Survivor> LivingTeammates()
        {
            var list = new List<Survivor>();
            if (LocalSurvivor == null) return list;
            foreach (var s in Survivor.All)
                if (s != LocalSurvivor && !s.IsDead && s.Team == LocalSurvivor.Team && !s.IsHostage) list.Add(s);
            list.Sort((a, b) => b.IsHuman.CompareTo(a.IsHuman));
            return list;
        }

        public void SpectateNext() => _spectate++;

        private void UpdateSpectate()
        {
            if (!Playing || LocalSurvivor == null || _camera == null) return;
            if (!LocalSurvivor.IsDead)
            {
                if (Spectated != null) _camera.Target = LocalSurvivor.transform;
                Spectated = null;
                return;
            }
            var mates = LivingTeammates();
            if (mates.Count == 0)
            {
                Spectated = null;
                return;
            }
            var next = mates[Mathf.Abs(_spectate) % mates.Count];
            if (next != Spectated)
            {
                Spectated = next;
                _camera.Target = next.Visual;
            }
        }

        private void Update()
        {
            WatchNet();
            UpdateRebinding();
            UpdateSpectate();
            // Owner, 2026-10-08: the store and the pause ad (Android only).
            Monetization.Start();
            Monetization.Tick();
            if (_menus == null) _menus = new Menus(this);
            _menus.Update();
            if (!Playing || Stage == null || !World.Simulating) return;
            var bossDead = Stage.MainBoss == null || Stage.MainBoss.IsDead;
            var humansAlive = 0;
            foreach (var s in Survivor.All) if (s.IsHuman && !s.IsDead) humansAlive++;
            Mission.Tick(Time.deltaTime);
            var layoutNow = Stage.Layout;
            var outcome = layoutNow.Endless ? (humansAlive > 0 ? MatchOutcome.Ongoing : MatchOutcome.Defeat)
                : layoutNow.Rescue ? EvaluateRescue(humansAlive)
                : layoutNow.Bomb || layoutNow.VaccineMission ? Mission.Evaluate(humansAlive)
                : MatchRules.Evaluate(bossDead, humansAlive);
            outcome = Extraction(outcome);
            if (outcome == MatchOutcome.Ongoing) return;
            // Endless (owner, 2026-10-07): the run is over when the whole team is down; the time survived is the result.
            if (layoutNow.Endless && Endless.Current != null)
            {
                Mission.FailReason = Endless.Current.Finish(Settings, WithAi || OnlineHost);
                SettingsStore.Save(Settings);
            }
            State = outcome == MatchOutcome.Victory ? SessionState.Victory : SessionState.Defeat;
            // Owner, 2026-10-03: story missions won are marked completed (they all stay open).
            var won = outcome == MatchOutcome.Victory ? Campaign.MissionOf(_stageId) : null;
            var record = WithAi || OnlineHost ? Settings.completedCoop : Settings.completedMissions;
            if (won != null && !record.Contains(won.id))
            {
                record.Add(won.id);
                SettingsStore.Save(Settings);
            }
            if (OnlineHost && NetSession.Instance.InMatch) NetSession.Instance.HostSide.EndMatch(outcome);
        }


        /// <summary>
        /// Owner, 2026-10-08: a completed mission isn't won until every living player is back at the helicopter; then it
        /// takes off, and once it has flown the stage is complete. Stages without the helicopter (test stages) and Endless
        /// end as before. Defeat is never held back.
        /// </summary>
        private MatchOutcome Extraction(MatchOutcome outcome)
        {
            var heli = Helicopter.Current;
            if (heli == null || Stage.Layout.Endless || outcome == MatchOutcome.Defeat) return outcome;
            if (heli.State == Helicopter.Phase.Gone) return MatchOutcome.Victory;
            if (heli.State == Helicopter.Phase.TakingOff || heli.State == Helicopter.Phase.Landing) return MatchOutcome.Ongoing;
            if (outcome != MatchOutcome.Victory) return MatchOutcome.Ongoing;
            heli.MakeReady();
            if (EveryoneAtTheHelicopter()) heli.BeginTakeOff();
            return MatchOutcome.Ongoing;
        }

        /// <summary>Every living player (humans and the AI teammate) and a freed hostage are at the helicopter (on its pad).</summary>
        public bool EveryoneAtTheHelicopter()
        {
            var pad = Stage.Layout.SpawnArea;
            foreach (var s in Survivor.All)
            {
                if (s.IsDead || s.Team != Survivor.PlayerTeam || (s.IsHostage && !s.IsFreed)) continue;
                if (!pad.Contains(new Vector2(s.transform.position.x, s.transform.position.z))) return false;
            }
            return true;
        }

        /// <summary>The players still to reach the helicopter (for the objective line).</summary>
        public int AwayFromTheHelicopter()
        {
            if (Stage == null) return 0;
            var pad = Stage.Layout.SpawnArea;
            var n = 0;
            foreach (var s in Survivor.All)
                if (!s.IsDead && s.Team == Survivor.PlayerTeam && !(s.IsHostage && !s.IsFreed) && !pad.Contains(new Vector2(s.transform.position.x, s.transform.position.z))) n++;
            return n;
        }

        /// <summary>Rescue: the freed hostage and a living player both inside the Spawn Area (the starting position).</summary>
        private MatchOutcome EvaluateRescue(int humansAlive)
        {
            var hostage = Hostage;
            var start = Stage.Layout.SpawnArea;
            bool Inside(Survivor s) => start.Contains(new Vector2(s.transform.position.x, s.transform.position.z));
            var back = false;
            if (hostage != null && !hostage.IsDead && hostage.IsFreed && Inside(hostage))
                foreach (var s in Survivor.All)
                    if (s.IsHuman && !s.IsDead && Inside(s)) back = true;
            return MatchRules.EvaluateRescue(back, hostage == null || hostage.IsDead, humansAlive);
        }
        private void EndStageWorld()
        {
            if (StageRoot != null)
            {
                // Deactivate first so every static registry (zombies, loot, survivors) drops the old stage now.
                StageRoot.gameObject.SetActive(false);
                Destroy(StageRoot.gameObject);
            }
            StageRoot = null;
            Stage = null;
            LocalSurvivor = null;
            AiSurvivor = null;
            _hud = null;
            World.Simulating = true;
            StageConditions.Clear();
            Zombie.Hunted = null;
            if (_camera != null) _camera.Target = null;
        }

        public void GoTo(SessionState state)
        {
            // Never leave a paused game frozen.
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (state != SessionState.Playing) EndStageWorld();
            var net = NetSession.Instance;
            if (net != null && net.Role != NetRole.None)
            {
                // Online: the main menu ends the connection; anything else keeps the friend in the lobby.
                if (state == SessionState.MainMenu) net.Leave();
                else if (net.Role == NetRole.Host && net.InMatch && state != SessionState.Playing) net.HostSide.ReturnToLobby();
            }
            State = state;
        }

        // ---- Menus (⚠ placeholder IMGUI) ----

        private Texture2D _backdrop;

        private void OnGUI()
        {
            if (State == SessionState.Playing) return;
            // Owner, 2026-10-08: the front-end is the real UI now (Menus); the rest still draws here.
            if (Menus.Handles(State)) return;
            Gui.Begin();
            var w = Gui.Width;
            var center = w * 0.5f;
            // The real UI's cartoon backdrop behind the screens still drawn here (Settings).
            if (_backdrop == null) _backdrop = UiKit.Backdrop();
            GUI.DrawTexture(new Rect(0f, 0f, w, Gui.Height), _backdrop, ScaleMode.StretchToFill);

            switch (State)
            {
                case SessionState.MainMenu:
                    Gui.Shadowed(new Rect(0f, 110f, w, 70f), "ZOMBIE", 48, TextAnchor.MiddleCenter, Color.white, true);
                    Gui.Label(new Rect(0f, 175f, w, 30f), "working title · placeholder build (M6)", 18, TextAnchor.MiddleCenter, Color.gray);
                    if (Gui.Button(new Rect(center - 160f, 270f, 320f, 64f), "Solo"))
                    {
                        WithAi = false;
                        GoTo(SessionState.StageSelect);
                    }
                    if (Gui.Button(new Rect(center - 160f, 350f, 320f, 64f), "Co-op")) GoTo(SessionState.CoopSelect);
                    if (Gui.Button(new Rect(center - 160f, 430f, 320f, 64f), "Settings")) GoTo(SessionState.Settings);
                    break;

                case SessionState.CoopSelect:
                    Gui.Shadowed(new Rect(0f, 90f, w, 60f), "Co-op", 36, TextAnchor.MiddleCenter, Color.white, true);
                    if (Gui.Button(new Rect(center - 220f, 180f, 440f, 64f), "With an AI teammate"))
                    {
                        WithAi = true;
                        GoTo(SessionState.StageSelect);
                    }
                    // Owner, 2026-10-07: join codes need Unity Gaming Services; until the project is linked the button says so.
                    GUI.enabled = NetSession.OnlineAvailable;
                    var hostOnline = Gui.Button(new Rect(center - 220f, 260f, 440f, 64f), NetSession.OnlineAvailable ? "Host online (join code)" : "Host online (join code) — not set up yet");
                    GUI.enabled = true;
                    if (hostOnline)
                    {
                        WithAi = false;
                        NetSession.Ensure().HostOnline();
                        State = SessionState.OnlineLobby;
                    }
                    // Owner, 2026-10-08: no same-network (IP) mode; Co-op online goes through join codes only.
                    if (Gui.Button(new Rect(center - 220f, 334f, 440f, 64f), "Join a friend")) State = SessionState.Join;
                    if (Gui.Button(new Rect(center - 160f, 500f, 320f, 56f), "Back")) GoTo(SessionState.MainMenu);
                    if (!string.IsNullOrEmpty(_menuMessage))
                        Gui.Label(new Rect(center - 400f, 570f, 800f, 60f), _menuMessage, 18, TextAnchor.MiddleCenter, new Color(1f, 0.7f, 0.5f));
                    break;

                case SessionState.Join:
                    Gui.Shadowed(new Rect(0f, 90f, w, 60f), "Join a friend", 36, TextAnchor.MiddleCenter, Color.white, true);
                    Gui.Label(new Rect(center - 300f, 170f, 600f, 30f), "The join code your friend sent", 18, TextAnchor.MiddleCenter, Color.gray);
                    var style = new GUIStyle(GUI.skin.textField) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
                    _joinText = GUI.TextField(new Rect(center - 220f, 210f, 440f, 64f), _joinText ?? "", 40, style);
                    // Owner, 2026-10-07: paste the code the friend sent.
                    if (Gui.Button(new Rect(center + 232f, 214f, 130f, 56f), "Paste"))
                        _joinText = (GUIUtility.systemCopyBuffer ?? "").Trim();
                    var text = (_joinText ?? "").Trim();
                    GUI.enabled = text.Length > 0;
                    if (Gui.Button(new Rect(center - 220f, 300f, 440f, 64f), "Join"))
                    {
                        NetSession.Ensure().JoinOnline(text);
                        State = SessionState.OnlineLobby;
                    }
                    GUI.enabled = true;
                    if (Gui.Button(new Rect(center - 160f, 400f, 320f, 56f), "Back")) State = SessionState.CoopSelect;
                    break;

                case SessionState.OnlineLobby:
                    DrawLobby(center);
                    break;

                case SessionState.StageSelect:
                    DrawStageSelect(center, w);
                    break;

                case SessionState.CharacterSelect:
                    DrawCharacterSelect(center);
                    break;

                case SessionState.Settings:
                    DrawSettings(center);
                    break;

                case SessionState.Victory:
                case SessionState.Defeat:
                    var victory = State == SessionState.Victory;
                    var endless = Stage != null && Stage.Layout.Endless;
                    Gui.Shadowed(new Rect(0f, 180f, w, 70f), victory ? "STAGE CLEARED" : endless ? "RUN OVER" : "DEFEAT", 48, TextAnchor.MiddleCenter,
                        victory ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.3f), true);
                    if (!victory && !string.IsNullOrEmpty(Mission.FailReason))
                        Gui.Label(new Rect(0f, 255f, w, 36f), Mission.FailReason, 22, TextAnchor.MiddleCenter, Color.white);
                    if (OnlineClient)
                    {
                        // The Host picks the next stage; the friend waits in the lobby or leaves.
                        if (Gui.Button(new Rect(center - 170f, 320f, 340f, 64f), "Wait for the next stage")) GoTo(SessionState.OnlineLobby);
                        if (Gui.Button(new Rect(center - 170f, 400f, 340f, 64f), "Leave")) GoTo(SessionState.MainMenu);
                        break;
                    }
                    // D-188 / D-189.
                    if (Gui.Button(new Rect(center - 170f, 320f, 340f, 64f), "Stage Selection")) GoTo(SessionState.StageSelect);
                    if (Gui.Button(new Rect(center - 170f, 400f, 340f, 64f), "Main Menu")) GoTo(SessionState.MainMenu);
                    break;
            }
        }

        private string _joinText;
        private string _menuMessage;

        private float _copiedUntil;

        /// <summary>Copies the text to the clipboard, beside the code or address shown at the top of the lobby.</summary>
        private void CopyButton(float center, string text)
        {
            if (Gui.Button(new Rect(center + 260f, 202f, 130f, 56f), Time.unscaledTime < _copiedUntil ? "Copied!" : "Copy"))
            {
                GUIUtility.systemCopyBuffer = text;
                _copiedUntil = Time.unscaledTime + 1.5f;
            }
        }

        private void DrawLobby(float center)
        {
            var net = NetSession.Instance;
            if (net == null || (net.Role == NetRole.None && !net.Busy))
            {
                _menuMessage = net != null ? net.Status : "";
                State = SessionState.CoopSelect;
                return;
            }
            var hosting = net.Role == NetRole.Host || (net.Role == NetRole.None && net.Busy);
            Gui.Shadowed(new Rect(0f, 90f, Gui.Width, 60f), hosting ? "Hosting a co-op game" : "Joining a co-op game", 34, TextAnchor.MiddleCenter,
                Color.white, true);
            if (!string.IsNullOrEmpty(net.JoinCode))
            {
                Gui.Label(new Rect(0f, 170f, Gui.Width, 30f), "Join code", 18, TextAnchor.MiddleCenter, Color.gray);
                Gui.Shadowed(new Rect(0f, 200f, Gui.Width, 60f), net.JoinCode, 48, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f), true);
                CopyButton(center, net.JoinCode);
            }
            else if (net.Role == NetRole.Host && !string.IsNullOrEmpty(net.LocalAddress))
            {
                Gui.Label(new Rect(0f, 170f, Gui.Width, 30f), "Your friend joins with this address", 18, TextAnchor.MiddleCenter, Color.gray);
                Gui.Shadowed(new Rect(0f, 200f, Gui.Width, 60f), net.LocalAddress, 40, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f), true);
                // Owner, 2026-10-07: copy it to send to the friend.
                CopyButton(center, net.LocalAddress);
                if (!string.IsNullOrEmpty(net.OtherAddresses))
                    Gui.Label(new Rect(0f, 256f, Gui.Width, 24f), $"If that doesn't work: {net.OtherAddresses}", 15, TextAnchor.MiddleCenter, Color.gray);
            }
            if (net.Role == NetRole.Client)
            {
                // The friend picks their own character here (the Host picks theirs before starting).
                var list = Tuning.characters;
                var pick = Mathf.Clamp(net.LocalCharacter, 0, list.Length - 1);
                if (Gui.Button(new Rect(center - 260f, 350f, 60f, 56f), "<")) net.ChooseCharacter((pick + list.Length - 1) % list.Length);
                Gui.Fill(new Rect(center - 190f, 350f, 380f, 56f), new Color(1f, 1f, 1f, 0.08f));
                Gui.Label(new Rect(center - 190f, 350f, 380f, 30f), $"Your character: {list[pick].displayName}", 20, TextAnchor.MiddleCenter, Color.white, true);
                Gui.Label(new Rect(center - 190f, 378f, 380f, 26f), list[pick].description, 15, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
                if (Gui.Button(new Rect(center + 200f, 350f, 60f, 56f), ">")) net.ChooseCharacter((pick + 1) % list.Length);
            }
            Gui.Label(new Rect(center - 400f, 280f, 800f, 60f), net.Status, 22, TextAnchor.MiddleCenter, Color.white);
            if (net.Role == NetRole.Host)
            {
                GUI.enabled = net.PeerConnected;
                if (Gui.Button(new Rect(center - 170f, 360f, 340f, 64f), "Choose the stage")) GoTo(SessionState.StageSelect);
                GUI.enabled = true;
            }
            if (Gui.Button(new Rect(center - 160f, 450f, 320f, 56f), "Leave"))
            {
                net.Leave();
                GoTo(SessionState.CoopSelect);
            }
        }

        private int _character;
        private int _storyIndex;
        private bool _allStages;

        /// <summary>
        /// Stage selection (owner, 2026-10-03): the story missions in order (Vaccine last), all open and replayable;
        /// each play takes the mission's next map. "All stages" lists every stage file (test and custom ones too).
        /// </summary>
        private void DrawStageSelect(float center, float w)
        {
            // Owner, 2026-10-03: Solo unlocks the story one mission at a time; Co-op has everything open (and is harder).
            var coop = WithAi || OnlineHost;
            var record = coop ? Settings.completedCoop : Settings.completedMissions;
            // Owner, 2026-10-07: Solo is Normal; Co-op is Hard (more zombies, more secondary bosses) and fully open,
            // without touching the Solo story's progress.
            Gui.Shadowed(new Rect(0f, 60f, w, 56f), coop ? "Choose a mission · Co-op: HARD, every mission open" : "Choose a mission · Solo: NORMAL", 30,
                TextAnchor.MiddleCenter, Color.white, true);
            // The full stage list (test and custom stages) only where nothing is locked, or in development builds.
            // Owner, 2026-10-04: a testing switch that opens every stage in Solo too.
            if (Gui.DevTools && Gui.Button(new Rect(center + 330f, 122f, 230f, 46f), Settings.unlockAllForTesting ? "Unlock all (test): ON" : "Unlock all (test): OFF"))
            {
                Settings.unlockAllForTesting = !Settings.unlockAllForTesting;
                SaveSettings();
            }
            var allOpen = Gui.DevTools;
            if (!allOpen) _allStages = false;
            if (allOpen && Gui.Button(new Rect(center - 230f, 122f, 220f, 46f), _allStages ? "Story" : "• Story •")) _allStages = false;
            if (allOpen && Gui.Button(new Rect(center + 10f, 122f, 220f, 46f), _allStages ? "• All stages •" : "All stages")) _allStages = true;
            var missions = Campaign.Missions;
            if (!_allStages && missions.Count > 0)
            {
                // The story missions, then Endless (owner, 2026-10-07); the rows shrink to fit above the Back button.
                // Owner, 2026-10-07: Co-op has no tutorial. Each row is a mission index (-1: Endless).
                var shown = Campaign.Listed(coop);
                var endlessIndex = StageLibrary.Exists(Endless.StageId) ? shown.Count : -1;
                var rows = shown.Count + (endlessIndex >= 0 ? 1 : 0);
                _storyIndex = Mathf.Clamp(_storyIndex, 0, rows - 1);
                var step = Mathf.Min(70f, 446f / rows);
                var y = 186f;
                for (var r = 0; r < rows; r++)
                {
                    var isEndless = r == endlessIndex;
                    var i = isEndless ? -1 : shown[r];
                    var title = isEndless ? "∞ Endless" : missions[i].title;
                    var tag = isEndless ? "Survive the waves · " + (coop ? "Co-op" : "Solo") : missions[i].tag;
                    var done = !isEndless && record.Contains(missions[i].id);
                    var locked = !isEndless && !Campaign.Unlocked(i, Settings, coop);
                    var row = new Rect(center - 560f, y, 470f, step - 6f);
                    Gui.Fill(row, r == _storyIndex ? new Color(0.25f, 0.45f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
                    Gui.Label(new Rect(row.x + 14f, row.y + 1f, row.width - 60f, row.height * 0.56f), title, step < 60f ? 18 : 21, TextAnchor.MiddleLeft, locked ? Color.gray : Color.white, true);
                    if (locked) Gui.Label(new Rect(row.xMax - 90f, row.y, 80f, row.height), "LOCKED", 14, TextAnchor.MiddleCenter, Color.gray, true);
                    Gui.Label(new Rect(row.x + 14f, row.y + row.height * 0.52f, row.width - 60f, row.height * 0.44f), tag, step < 60f ? 13 : 15, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
                    if (done) Gui.Label(new Rect(row.xMax - 50f, row.y, 40f, row.height), "✓", 26, TextAnchor.MiddleCenter, new Color(0.5f, 1f, 0.5f), true);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) _storyIndex = r;
                    y += step;
                }
                var panel = new Rect(center - 70f, 186f, 630f, 410f);
                if (_storyIndex == endlessIndex)
                {
                    DrawEndlessPanel(panel, coop);
                    if (Gui.Button(new Rect(center - 160f, 640f, 320f, 56f), "Back"))
                        GoTo(OnlineHost ? SessionState.OnlineLobby : WithAi ? SessionState.CoopSelect : SessionState.MainMenu);
                    return;
                }
                var missionIndex = shown[_storyIndex];
                var mission = missions[missionIndex];
                Gui.Fill(panel, new Color(0f, 0f, 0f, 0.45f));
                Gui.Label(new Rect(panel.x + 24f, panel.y + 16f, panel.width - 48f, 40f), mission.title, 28, TextAnchor.MiddleLeft, Color.white, true);
                Gui.Label(new Rect(panel.x + 24f, panel.y + 62f, panel.width - 48f, 240f), mission.story, 19, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.88f));
                var missionLocked = !Campaign.Unlocked(missionIndex, Settings, coop);
                var next = missionLocked ? null : Campaign.NextStage(mission, Settings);
                if (missionLocked)
                    Gui.Label(new Rect(panel.x + 24f, panel.yMax - 110f, panel.width - 48f, 40f), $"Complete \"{missions[missionIndex - 1].title}\" in Solo to unlock this mission.", 19, TextAnchor.MiddleCenter, new Color(1f, 0.8f, 0.5f));
                if (next != null)
                {
                    Gui.Label(new Rect(panel.x + 24f, panel.yMax - 130f, panel.width - 48f, 30f), $"Map: {StageLibrary.DisplayName(next)}", 17, TextAnchor.MiddleLeft, new Color(0.7f, 0.85f, 0.75f));
                    if (Gui.Button(new Rect(panel.x + panel.width * 0.5f - 160f, panel.yMax - 92f, 320f, 70f), record.Contains(mission.id) ? "Play again" : "Play"))
                    {
                        Settings.SetLastPlayed(mission.id, next);
                        SettingsStore.Save(Settings);
                        _stageId = next;
                        GoTo(SessionState.CharacterSelect);
                    }
                }
                if (Gui.Button(new Rect(center - 160f, 640f, 320f, 56f), "Back"))
                    GoTo(OnlineHost ? SessionState.OnlineLobby : WithAi ? SessionState.CoopSelect : SessionState.MainMenu);
                return;
            }
            var ids = new List<string>();
            foreach (var id in StageLibrary.Ids)
                if (StageLibrary.Exists(id) && !(coop && Campaign.MissionOf(id)?.id == Campaign.TutorialId)) ids.Add(id);
            for (var i = 0; i < ids.Count; i++)
            {
                var col = i % 3;
                var line = i / 3;
                if (Gui.Button(new Rect(center - 520f + col * 350f, 190f + line * 62f, 340f, 54f), StageLibrary.DisplayName(ids[i])))
                {
                    _stageId = ids[i];
                    GoTo(SessionState.CharacterSelect);
                }
            }
            if (Gui.Button(new Rect(center - 160f, 640f, 320f, 56f), "Back"))
                GoTo(OnlineHost ? SessionState.OnlineLobby : WithAi ? SessionState.CoopSelect : SessionState.MainMenu);
        }

        private string _stageId = StageLibrary.TestStageId;

        /// <summary>Endless mode (owner, 2026-10-07): its rules, the best time, and Play.</summary>
        private void DrawEndlessPanel(Rect panel, bool coop)
        {
            var t = Tuning.endless;
            Gui.Fill(panel, new Color(0f, 0f, 0f, 0.45f));
            Gui.Label(new Rect(panel.x + 24f, panel.y + 16f, panel.width - 48f, 40f), "∞ Endless", 28, TextAnchor.MiddleLeft, Color.white, true);
            var rules = $"The largest map, fenced in. You have {Mathf.RoundToInt(t.prepSeconds / 60f)} minutes to collect equipment, then the zombies " +
                        "pour in through the gaps in the fence: more of them, and tougher ones, the longer you last. Day, rain, night and " +
                        $"stormy night rain come in turn. Your backpack holds ×{t.backpackMultiplier:0.#} and every ammo pickup gives {Mathf.RoundToInt((t.ammoMultiplier - 1f) * 100f)} % more. " +
                        "How long can you survive?";
            Gui.Label(new Rect(panel.x + 24f, panel.y + 62f, panel.width - 48f, 220f), rules, 19, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.88f));
            var best = coop ? Settings.endlessBestCoop : Settings.endlessBestSolo;
            Gui.Label(new Rect(panel.x + 24f, panel.yMax - 130f, panel.width - 48f, 30f),
                best > 0f ? $"Best ({(coop ? "Co-op" : "Solo")}): {Endless.Minutes(best)}" : "No run yet", 18, TextAnchor.MiddleLeft, new Color(0.7f, 0.85f, 0.75f));
            if (Gui.Button(new Rect(panel.x + panel.width * 0.5f - 160f, panel.yMax - 92f, 320f, 70f), "Play"))
            {
                _stageId = Endless.StageId;
                GoTo(SessionState.CharacterSelect);
            }
        }

        /// <summary>Owner, 2026-09-30: pick a character before entering a stage. Only one exists for now.</summary>
        private void DrawCharacterSelect(float center)
        {
            var list = Tuning.characters;
            _character = Mathf.Clamp(_character, 0, list.Length - 1);
            Gui.Shadowed(new Rect(0f, 70f, Gui.Width, 60f), WithAi ? "Choose your character (the AI takes a different one)" : "Choose your character", 30,
                TextAnchor.MiddleCenter, Color.white, true);
            const float gap = 12f;
            var cardW = Mathf.Min(240f, (Gui.Width - 40f - gap * (list.Length - 1)) / list.Length);
            const float cardH = 290f;
            var x = center - (list.Length * cardW + (list.Length - 1) * gap) * 0.5f;
            for (var i = 0; i < list.Length; i++)
            {
                var c = list[i];
                var card = new Rect(x + i * (cardW + gap), 140f, cardW, cardH);
                Gui.Fill(card, i == _character ? new Color(0.25f, 0.4f, 0.6f, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
                Gui.Circle(new Vector2(card.center.x, card.y + 50f), 30f, c.color);
                Gui.Label(new Rect(card.x, card.y + 88f, cardW, 30f), c.displayName, 20, TextAnchor.MiddleCenter, Color.white, true);
                DrawStats(new Rect(card.x + 14f, card.y + 126f, cardW - 28f, 100f), c);
                Gui.Label(new Rect(card.x + 8f, card.y + 226f, cardW - 16f, 56f), c.description, 15, TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.5f));
                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) _character = i;
            }
            if (Gui.Button(new Rect(center - 170f, 460f, 340f, 66f), "Start")) StartStage(StageLibrary.Load(_stageId), list[_character], System.Environment.TickCount, WithAi, online: OnlineHost, intro: true);
            if (Gui.Button(new Rect(center - 160f, 544f, 320f, 54f), "Back")) GoTo(SessionState.StageSelect);
        }

        /// <summary>The three base stats (owner, 2026-10-01): HP, Attack, Speed.</summary>
        private void DrawStats(Rect r, CharacterDef c)
        {
            void Row(int i, string name, string value, bool boosted)
            {
                var y = r.y + i * 30f;
                Gui.Label(new Rect(r.x, y, r.width * 0.5f, 28f), name, 17, TextAnchor.MiddleLeft, Color.gray);
                Gui.Label(new Rect(r.x + r.width * 0.5f, y, r.width * 0.5f, 28f), value, 17, TextAnchor.MiddleRight,
                    boosted ? new Color(0.5f, 1f, 0.5f) : Color.white, boosted);
            }
            Row(0, "HP", $"{Tuning.player.maxHp * (1f + c.hpBonus):0}", c.hpBonus > 0f);
            Row(1, "Attack", $"{100f * (1f + c.attackBonus):0} %", c.attackBonus > 0f);
            Row(2, "Speed", $"{Tuning.player.moveSpeed * (1f + c.speedBonus):0.0} m/s", c.speedBonus > 0f);
        }

        /// <summary>Bumped whenever settings are saved, so the HUD re-reads the touch layout.</summary>
        public int SettingsVersion { get; private set; }

        private void SaveSettings()
        {
            SettingsStore.Save(Settings);
            SettingsVersion++;
            Controls.ReloadBindings(Settings);
            ApplySettings();
        }

        /// <summary>The introduction is over (watched or skipped): remembered, then the main menu.</summary>
        public void IntroFinished()
        {
            Settings.introSeen = true;
            SaveSettings();
            GoTo(SessionState.MainMenu);
        }

        private enum SettingsTab { General, Touch, Keys, Credits }

        /// <summary>The settings tabs on this platform: touch controls on a phone, keys on a PC.</summary>
        private static (string, SettingsTab)[] SettingsTabs => Application.isMobilePlatform
            ? new[] { ("General", SettingsTab.General), ("Touch controls", SettingsTab.Touch), ("Credits", SettingsTab.Credits) }
            : new[] { ("General", SettingsTab.General), ("PC keys", SettingsTab.Keys), ("Credits", SettingsTab.Credits) };
        private SettingsTab _tab;

        /// <summary>D-246: settings — general, the Android button layout (D-017), and PC key rebinding.</summary>
        private void DrawSettings(float center)
        {
            Gui.Shadowed(new Rect(0f, 40f, Gui.Width, 50f), "Settings", 34, TextAnchor.MiddleCenter, Color.white, true);
            // Owner, 2026-10-08: the phone has its touch controls only, the PC its keys only (no switching between them).
            var tabs = SettingsTabs;
            if (System.Array.FindIndex(tabs, t => t.Item2 == _tab) < 0) _tab = SettingsTab.General;
            for (var i = 0; i < tabs.Length; i++)
            {
                GUI.enabled = _tab != tabs[i].Item2;
                // The row of tabs centred, however many there are.
                if (Gui.Button(new Rect(center - (tabs.Length * 225f - 15f) * 0.5f + i * 225f, 100f, 210f, 48f), tabs[i].Item1))
                {
                    _tab = tabs[i].Item2;
                    _editLayout = null;
                    _waitingFor = null;
                }
                GUI.enabled = true;
            }
            switch (_tab)
            {
                case SettingsTab.General: DrawGeneral(center); break;
                case SettingsTab.Touch: DrawTouchEditor(center); return;
                case SettingsTab.Keys: DrawKeys(center); break;
                case SettingsTab.Credits: DrawCredits(center); break;
            }
            if (Gui.Button(new Rect(center - 160f, Gui.Height - 80f, 320f, 56f), "Back")) GoTo(SessionState.MainMenu);
        }

        /// <summary>
        /// Owner, 2026-10-16: the credits. Every sound is CC0 (no credit required); the three fonts are SIL Open Font License.
        /// </summary>
        private static readonly (string what, string by, string licence, string url)[] Credits =
        {
            ("Font Lilita One", "Juan Montoreano", "SIL Open Font License 1.1", "https://fonts.google.com/specimen/Lilita+One"),
            ("Font Freckle Face", "Astigmatic", "SIL Open Font License 1.1", "https://fonts.google.com/specimen/Freckle+Face"),
            ("Font Nosifer", "Typomondo", "SIL Open Font License 1.1", "https://fonts.google.com/specimen/Nosifer"),
        };

        private void DrawCredits(float center)
        {
            var x = center - 380f;
            var y = 172f;
            foreach (var (what, by, licence, url) in Credits)
            {
                Gui.Label(new Rect(x, y, 760f, 28f), $"{what}  by {by}", 20, TextAnchor.MiddleLeft, Color.white);
                // The licence and the source, tappable.
                if (GUI.Button(new Rect(x, y + 26f, 760f, 26f), GUIContent.none, GUIStyle.none)) Application.OpenURL(url);
                Gui.Label(new Rect(x, y + 26f, 760f, 26f), $"{licence}  ·  {url}", 16, TextAnchor.MiddleLeft, new Color(0.55f, 0.85f, 1f));
                y += 62f;
            }
            Gui.Label(new Rect(x, y + 4f, 760f, 26f), "Sounds: Creative Commons 0 (public domain), from freesound.org.", 16, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.7f));
            Gui.Label(new Rect(x, y + 32f, 760f, 26f), "Made with Unity.", 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));
        }

        private void DrawGeneral(float center)
        {
            var s = Settings;
            var x = center - 220f;
            var changed = false;
            if (Gui.Button(new Rect(x, 170f, 440f, 56f), $"Minimap: {(s.minimap ? "On" : "Off")}")) { s.minimap = !s.minimap; changed = true; }
            if (Gui.Button(new Rect(x, 236f, 440f, 56f), $"Haptics: {(s.haptics ? "On" : "Off")}")) { s.haptics = !s.haptics; changed = true; }
            Gui.Label(new Rect(x, 302f, 440f, 30f), $"Volume {Mathf.RoundToInt(s.volume * 100f)} %", 20, TextAnchor.MiddleCenter, Color.white);
            if (Gui.Button(new Rect(x, 334f, 210f, 52f), "-")) { s.volume = Mathf.Max(0f, s.volume - 0.1f); changed = true; }
            if (Gui.Button(new Rect(x + 230f, 334f, 210f, 52f), "+")) { s.volume = Mathf.Min(1f, s.volume + 0.1f); changed = true; }
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            if (Gui.Button(new Rect(x, 400f, 440f, 56f), $"Graphics: {(names.Length > 0 ? names[level] : "-")}"))
            {
                s.quality = (level + 1) % Mathf.Max(1, names.Length);
                changed = true;
            }
            // Owner, 2026-10-07: the stats display can be turned off (development builds only; off by default).
            if (Gui.DevTools && Gui.Button(new Rect(x, 466f, 440f, 56f), $"Stats display: {(s.statsDisplay ? "On" : "Off")}")) { s.statsDisplay = !s.statsDisplay; changed = true; }
            // Owner, 2026-10-09: the introduction again.
            if (Gui.Button(new Rect(x, 532f, 440f, 56f), "Watch the intro")) GoTo(SessionState.Intro);
            // Owner, 2026-10-10: the ad consent, changed any time (where there are ads).
            if ((Monetization.StoreSupported || Gui.DevTools) && !Monetization.Owned &&
                Gui.Button(new Rect(x + 460f, 170f, 300f, 56f), AdConsent.Personalised ? "Ads: personalised" : "Ads: not personalised"))
                AdConsent.Choose(!AdConsent.Personalised);
            // Owner, 2026-10-14: Restore purchase on iPhone is always reachable here, owned or not (once owned, Co-op opens
            // straight away and the offer with its Restore button isn't shown any more).
            if (Monetization.RestoreShown)
            {
                GUI.enabled = !Monetization.Busy;
                if (Gui.Button(new Rect(x + 460f, 236f, 300f, 56f), Monetization.Busy ? "Please wait…" : "Restore purchase")) Monetization.Restore();
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(Monetization.Message))
                    Gui.Label(new Rect(x + 460f, 296f, 300f, 60f), Monetization.Message, 16, TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.4f));
            }
            if (changed) SaveSettings();
        }

        // ---- Android button layout (D-017) ----

        private List<TouchButton> _editLayout;
        private int _grabbed = -1;
        private int _selected;
        private int _grabPointer = int.MinValue;
        private Vector2 _grabOffset;

        private static string TouchLabel(string id) => id == Controls.Pickup ? "PICK UP" : id.ToUpperInvariant();

        private void DrawTouchEditor(float center)
        {
            if (_editLayout == null)
            {
                _editLayout = Controls.Layout(Settings);
                _selected = 1;
            }
            var w = Gui.Width;
            Gui.Label(new Rect(0f, 155f, w, 26f), "Drag a button to move it. Pick one, then change its size.", 18, TextAnchor.MiddleCenter, Color.gray);
            for (var i = 0; i < _editLayout.Count; i++)
            {
                var b = _editLayout[i];
                PlayerHud.DrawButton(b, TouchLabel(b.id), i == _selected ? new Color(0.3f, 0.7f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.25f));
            }
            HandleLayoutDrag();

            var sel = _editLayout[_selected];
            Gui.Label(new Rect(center - 250f, 200f, 500f, 30f), $"{TouchLabel(sel.id)}  ·  size {sel.size * 100f:0} %", 20, TextAnchor.MiddleCenter, Color.white, true);
            if (Gui.Button(new Rect(center - 250f, 236f, 110f, 50f), "Smaller")) sel.size = Mathf.Max(0.6f, sel.size - 0.1f);
            if (Gui.Button(new Rect(center - 130f, 236f, 110f, 50f), "Bigger")) sel.size = Mathf.Min(1.8f, sel.size + 0.1f);
            if (Gui.Button(new Rect(center + 10f, 236f, 110f, 50f), "Reset")) _editLayout = Controls.DefaultLayout();
            if (Gui.Button(new Rect(center + 130f, 236f, 120f, 50f), "Save"))
            {
                Settings.touch = new List<TouchButton>();
                foreach (var b in _editLayout) Settings.touch.Add(b.Copy());
                SaveSettings();
                _editLayout = null;
                GoTo(SessionState.MainMenu);
                return;
            }
            if (Gui.Button(new Rect(center - 250f, 296f, 500f, 44f), "Cancel"))
            {
                _editLayout = null;
                _tab = SettingsTab.General;
            }
        }

        private void HandleLayoutDrag()
        {
            if (Event.current.type != EventType.Repaint) return;
            var w = Gui.Width;
            foreach (var p in PointerInput.Current)
            {
                if (p.Began && _grabbed < 0)
                    for (var i = 0; i < _editLayout.Count; i++)
                    {
                        var c = Controls.Center(_editLayout[i], w);
                        if (Vector2.Distance(p.Position, c) > Controls.Radius(_editLayout[i])) continue;
                        _grabbed = _selected = i;
                        _grabPointer = p.Id;
                        _grabOffset = c - p.Position;
                    }
                if (p.Id != _grabPointer || _grabbed < 0) continue;
                var b = _editLayout[_grabbed];
                var pos = p.Position + _grabOffset;
                var r = Controls.Radius(b);
                pos.x = Mathf.Clamp(pos.x, r, w - r);
                pos.y = Mathf.Clamp(pos.y, 350f, Gui.Height - r); // keep the top free for the editor's own buttons
                b.right = pos.x > w * 0.5f;
                b.x = b.right ? w - pos.x : pos.x;
                b.y = pos.y;
                if (p.Ended)
                {
                    _grabbed = -1;
                    _grabPointer = int.MinValue;
                }
            }
        }

        // ---- PC key rebinding ----

        private PcAction? _waitingFor;
        private int _waitingSince;

        private static string ActionName(PcAction a)
        {
            switch (a)
            {
                case PcAction.MoveUp: return "Move up";
                case PcAction.MoveDown: return "Move down";
                case PcAction.MoveLeft: return "Move left";
                case PcAction.MoveRight: return "Move right";
                case PcAction.WeaponPanel: return "Weapon panel";
                default: return a.ToString();
            }
        }

        private void DrawKeys(float center)
        {
            var actions = (PcAction[])System.Enum.GetValues(typeof(PcAction));
            for (var i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                var col = i % 2;
                var row = i / 2;
                var rect = new Rect(center - 440f + col * 450f, 170f + row * 60f, 430f, 50f);
                var label = _waitingFor == a ? $"{ActionName(a)}:  press a key…" : $"{ActionName(a)}:  {Controls.Label(Controls.KeyFor(a, Settings))}";
                if (Gui.Button(rect, label))
                {
                    _waitingFor = a;
                    _waitingSince = Time.frameCount;
                }
            }
            var y = 170f + (actions.Length + 1) / 2 * 60f + 10f;
            Gui.Label(new Rect(0f, y, Gui.Width, 26f), "Arrow keys always move too. Esc cancels.", 16, TextAnchor.MiddleCenter, Color.gray);
            if (Gui.Button(new Rect(center - 160f, y + 34f, 320f, 50f), "Reset to defaults"))
            {
                Settings.keys = new List<KeyBinding>();
                _waitingFor = null;
                SaveSettings();
            }
        }

        /// <summary>Called every frame: takes the next key or mouse button for the action being rebound.</summary>
        private void UpdateRebinding()
        {
            if (_waitingFor == null || State != SessionState.Settings || _tab != SettingsTab.Keys) return;
            // Skip the frame of the click that started it, or that click would bind the left mouse button.
            if (Time.frameCount <= _waitingSince + 1) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _waitingFor = null;
                return;
            }
            var key = Controls.AnyPressed();
            if (key == null) return;
            var action = _waitingFor.Value.ToString();
            Settings.keys.RemoveAll(k => k.action == action);
            Settings.keys.Add(new KeyBinding { action = action, key = key });
            _waitingFor = null;
            SaveSettings();
        }
    }
}
