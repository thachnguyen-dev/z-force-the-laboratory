using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The Bomb and Vaccine stages (owner, 2026-10-02).
    ///
    /// Bomb: one C4 exists. It starts with the first player; only players carry it, one at a time. Near the target
    /// incubator the carrier can plant it; it explodes 20 s later with high damage (players caught in it are hurt too)
    /// and that completes the stage. If the carrier dies the C4 stays on the body; if the body disappears first, the C4 is
    /// lost and the stage is failed.
    ///
    /// Vaccine: one Vaccine lies on the map. A player picks it up and brings it back to the Spawn Area to win. Only
    /// players carry it; the same body rule applies.
    ///
    /// The Host runs all of it; the client gets <see cref="Planted"/> and <see cref="FuseLeft"/> in each snapshot.
    /// </summary>
    public static class Mission
    {
        public static bool Planted { get; private set; }
        public static bool Exploded { get; private set; }
        public static float FuseLeft { get; private set; }
        public static Vector3 BombPosition { get; private set; }
        /// <summary>Why the stage was failed, when a mission item was lost (shown on the Defeat screen).</summary>
        public static string FailReason { get; internal set; }
        /// <summary>A player has approached the objective: zombies detect players 3× as well (owner, 2026-10-03).</summary>
        public static bool Alerted { get; private set; }
        /// <summary>Vaccine stages: seconds left before the rivals win by keeping the Vaccine (below 0: they don't have it).</summary>
        public static float RivalHoldLeft { get; private set; } = -1f;
        private static float _nextBossCall;
        private static float _responseLeft;

        private static GameObject _charge;

        private static GameSession Game => GameSession.Instance;
        private static StageLayout Layout => Game != null && Game.Stage != null ? Game.Stage.Layout : null;
        private static MissionTuning Tuning => Game != null ? Game.Tuning.missions : new MissionTuning();

        public static void Reset()
        {
            Planted = Exploded = false;
            FuseLeft = 0f;
            FailReason = null;
            Alerted = false;
            RivalHoldLeft = -1f;
            _nextBossCall = 0f;
            StageConditions.ZombieDetectionMultiplier = 1f;
            if (_charge != null) Object.Destroy(_charge);
            _charge = null;
        }

        /// <summary>The bomb stage's target incubator (the incubator standing on the B cell).</summary>
        public static Incubator Target
        {
            get
            {
                var layout = Layout;
                if (layout == null || !layout.Bomb) return null;
                foreach (var i in Incubator.All)
                    if (i.IsTarget) return i;
                return null;
            }
        }

        public static bool CanPlantFrom(Vector3 position)
        {
            var target = Target;
            if (target == null || Planted || target.IsDead) return false;
            var d = target.transform.position - position;
            d.y = 0f;
            return d.magnitude <= Tuning.plantRange;
        }

        /// <summary>Host: the C4 goes on the target; the fuse starts.</summary>
        public static void Plant()
        {
            var target = Target;
            if (target == null || Planted) return;
            Planted = true;
            // Owner, 2026-10-09: planting raises the zombies' detection (not walking up to the target).
            RaiseAlert();
            FuseLeft = Tuning.c4Fuse;
            _responseLeft = 0f;
            BombPosition = target.transform.position;
            ShowCharge();
            Sfx.Play(SoundId.Drop, BombPosition);
            Game.Hud?.Toast("C4 planted. Get clear!");
            NetSync.ToastAll("C4 planted. Get clear!");
        }

        /// <summary>Host, every frame.</summary>
        public static void Tick(float dt)
        {
            CheckAlert();
            // Owner, 2026-10-11: no 60 s hold rule; the rivals have to reach the helicopter with it.
            TickFuse(dt);
        }

        // ---- Alert: 3× detection once a player approaches the objective (owner, 2026-10-03) ----

        /// <summary>Where the stage's objective is now: the target incubator, the hostage, or the Vaccine wherever it is.</summary>
        public static Vector3? ObjectivePosition
        {
            get
            {
                var layout = Layout;
                if (layout == null) return null;
                if (layout.Bomb) return Target != null ? Target.transform.position : (Vector3?)null;
                if (layout.Rescue)
                {
                    var hostage = Game.Hostage;
                    return hostage != null && !hostage.IsDead ? hostage.transform.position : (Vector3?)null;
                }
                if (layout.VaccineMission) return VaccinePosition();
                return null;
            }
        }

        public static Vector3? VaccinePosition()
        {
            foreach (var s in Survivor.All)
                if (!s.IsDead && s.Inventory.Vaccine) return s.transform.position;
            foreach (var item in LootItem.All)
                if (item.Kind == LootKind.Vaccine && item.Amount > 0) return item.transform.position;
            foreach (var chest in Chest.All)
                foreach (var entry in chest.Contents)
                    if (entry.Kind == LootKind.Vaccine && entry.Amount > 0) return chest.transform.position;
            return null;
        }

        /// <summary>The zombies go on alert now (3x detection for the rest of the stage).</summary>
        public static void RaiseAlert()
        {
            if (Alerted) return;
            Alerted = true;
            StageConditions.ZombieDetectionMultiplier = Tuning.alertDetectionMultiplier;
            Game.Hud?.Toast("The zombies are on alert!");
            NetSync.ToastAll("The zombies are on alert!");
        }

        private static void CheckAlert()
        {
            if (Alerted) return;
            // Owner, 2026-10-08: on a rescue stage nothing happens by walking up to the scientist; the alert comes when they
            // start following (after the talk).
            if (Layout != null && Layout.Rescue)
            {
                var hostage = Game.Hostage;
                if (hostage != null && hostage.IsFreed) RaiseAlert();
                return;
            }
            // Owner, 2026-10-09: on a bomb stage the alert comes when the C4 is planted (Plant).
            if (Layout != null && Layout.Bomb) return;
            var objective = ObjectivePosition;
            if (!objective.HasValue) return;
            var reach = Tuning.alertDistance;
            foreach (var s in Survivor.All)
            {
                if (!s.IsHuman || s.IsDead || s.Team != Survivor.PlayerTeam) continue;
                var d = s.transform.position - objective.Value;
                d.y = 0f;
                if (d.magnitude > reach) continue;
                Alerted = true;
                StageConditions.ZombieDetectionMultiplier = Tuning.alertDetectionMultiplier;
                Game.Hud?.Toast("The zombies are on alert!");
                NetSync.ToastAll("The zombies are on alert!");
                return;
            }
        }

        // ---- The boss calls the horde (owner, 2026-10-03) ----

        /// <summary>Host: the Main Boss was hurt. Every zombie alive heads for the Boss Area, at most every 15 s.</summary>
        public static void BossHurt(Zombie boss)
        {
            var layout = Layout;
            if (layout == null || layout.Mission != "boss" || Time.time < _nextBossCall) return;
            _nextBossCall = Time.time + Tuning.bossCallCooldown;
            var area = layout.BossArea;
            var center = area.width > 0f ? new Vector3(area.center.x, 0f, area.center.y) : boss.transform.position;
            var spread = area.width > 0f ? Mathf.Min(area.width, area.height) * 0.35f : 6f;
            foreach (var z in Zombie.Alive)
            {
                if (z == boss || z.IsMainBoss || ZombieTypes.IsBoss(z.Def.type)) continue;
                var offset = Random.insideUnitCircle * spread;
                z.Hear(center + new Vector3(offset.x, 0f, offset.y));
            }
            Game.Stage.Population?.CallTo(center, spread);
            Game.Hud?.Toast("The boss calls the horde!");
            NetSync.ToastAll("The boss calls the horde!");
        }

        // ---- The rival team keeps the Vaccine (owner, 2026-10-03) ----

        /// <summary>The rival holding the Vaccine, if one is.</summary>
        public static Survivor RivalCarrier
        {
            get
            {
                foreach (var s in Survivor.All)
                    if (!s.IsDead && s.IsRival && s.Inventory.Vaccine) return s;
                return null;
            }
        }

        private static void TickRivalHold(float dt)
        {
            var layout = Layout;
            if (layout == null || !layout.VaccineMission) return;
            if (RivalCarrier == null)
            {
                RivalHoldLeft = -1f;
                return;
            }
            // Each time the rivals get it, the players have a fresh 60 s to take it back.
            if (RivalHoldLeft < 0f)
            {
                RivalHoldLeft = Tuning.rivalHoldSeconds;
                Game.Hud?.Toast("The rivals took the Vaccine! Get it back!");
                NetSync.ToastAll("The rivals took the Vaccine! Get it back!");
            }
            else RivalHoldLeft = Mathf.Max(0f, RivalHoldLeft - dt);
        }

        /// <summary>Online client: the rivals' timer from the snapshot.</summary>
        public static void ApplyRivalReplica(float holdLeft) => RivalHoldLeft = holdLeft;

        private static void TickFuse(float dt)
        {
            if (!Planted || Exploded) return;
            // Owner, 2026-10-03: the target's many spawn points answer the C4 with a concentrated response.
            _responseLeft -= dt;
            if (_responseLeft <= 0f)
            {
                _responseLeft = Tuning.responseInterval;
                Game.Stage.SpawnResponse(BombPosition, Tuning.responseRadius, Tuning.responseCount);
            }
            FuseLeft -= dt;
            if (FuseLeft > 0f) return;
            FuseLeft = 0f;
            Exploded = true;
            var t = Tuning;
            Explosion.Detonate(BombPosition + Vector3.up * 0.5f, t.c4Damage, t.c4Radius, t.c4NoiseRange);
            Target?.Destroy();
            if (_charge != null) Object.Destroy(_charge);
        }

        /// <summary>Online client: the Host's mission state, from the snapshot.</summary>
        public static void ApplyReplica(bool planted, float fuseLeft, Vector3 position)
        {
            if (planted && !Planted)
            {
                Planted = true;
                BombPosition = position;
                ShowCharge();
            }
            FuseLeft = fuseLeft;
            if (planted && fuseLeft <= 0f && _charge != null)
            {
                Object.Destroy(_charge);
                _charge = null;
            }
        }

        private static void ShowCharge()
        {
            if (_charge != null || Game == null || Game.StageRoot == null) return;
            _charge = new GameObject("Planted C4");
            _charge.transform.SetParent(Game.StageRoot, false);
            _charge.transform.position = BombPosition + new Vector3(0f, 0f, -0.9f);
            if (Art.PackArt.Show(_charge.transform, "c4", 0.55f, Vector3.zero) == null)
                Primitives.Shape(PrimitiveType.Cube, _charge.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.55f, 0.3f, 0.35f), new Color(0.75f, 0.68f, 0.5f));
            var light = Primitives.Shape(PrimitiveType.Sphere, _charge.transform, new Vector3(0f, 0.36f, 0f), Vector3.one * 0.12f, new Color(1f, 0.1f, 0.05f));
            light.AddComponent<Blink>();
        }

        // ---- Lost items ----

        /// <summary>True while a mission item of this kind still exists: carried, on the ground, or on a body.</summary>
        public static bool Exists(LootKind kind)
        {
            foreach (var s in Survivor.All)
                if (!s.IsDead && (kind == LootKind.C4 ? s.Inventory.C4 : s.Inventory.Vaccine)) return true;
            foreach (var item in LootItem.All)
                if (item.Kind == kind && item.Amount > 0) return true;
            foreach (var chest in Chest.All)
                foreach (var entry in chest.Contents)
                    if (entry.Kind == kind && entry.Amount > 0) return true;
            return false;
        }

        /// <summary>How close to the helicopter counts as back at it (m). PLACEHOLDER.</summary>
        public const float HelicopterReach = 7f;

        /// <summary>At the helicopter: within reach of it, or (no helicopter on the map) in the start area.</summary>
        public static bool AtTheHelicopter(Vector3 p, StageLayout layout)
        {
            var heli = Helicopter.Current;
            if (heli == null) return layout.SpawnArea.Contains(new Vector2(p.x, p.z));
            var d = p - heli.transform.position;
            d.y = 0f;
            return d.magnitude <= HelicopterReach;
        }

        /// <summary>Host: the stage's result so far.</summary>
        public static MatchOutcome Evaluate(int humansAlive)
        {
            var layout = Layout;
            if (layout == null) return MatchOutcome.Ongoing;
            if (layout.Bomb)
            {
                // Owner, 2026-10-03: after the explosion the players must get back to the start to win.
                if (Exploded)
                {
                    if (humansAlive <= 0) return MatchOutcome.Defeat;
                    var home = layout.SpawnArea;
                    foreach (var s in Survivor.All)
                        if (s.IsHuman && !s.IsDead && home.Contains(new Vector2(s.transform.position.x, s.transform.position.z))) return MatchOutcome.Victory;
                    return MatchOutcome.Ongoing;
                }
                if (humansAlive <= 0) return MatchOutcome.Defeat;
                if (!Planted && !Exists(LootKind.C4))
                {
                    FailReason = "The C4 was lost.";
                    return MatchOutcome.Defeat;
                }
                return MatchOutcome.Ongoing;
            }
            if (layout.VaccineMission)
            {
                if (humansAlive <= 0) return MatchOutcome.Defeat;
                // Owner, 2026-10-11: a rival team carrying the Vaccine to the helicopter wins it.
                var rival = RivalCarrier;
                if (rival != null && AtTheHelicopter(rival.transform.position, layout))
                {
                    FailReason = "A rival team got away with the Vaccine.";
                    return MatchOutcome.Defeat;
                }
                // Owner, 2026-10-11: the Vaccine has to be carried back to the helicopter (its pad; the start where there is none).
                foreach (var s in Survivor.All)
                    if (s.IsHuman && !s.IsDead && s.Inventory.Vaccine && AtTheHelicopter(s.transform.position, layout))
                        return MatchOutcome.Victory;
                if (!Exists(LootKind.Vaccine))
                {
                    FailReason = "The Vaccine was lost.";
                    return MatchOutcome.Defeat;
                }
                return MatchOutcome.Ongoing;
            }
            return MatchOutcome.Ongoing;
        }

        /// <summary>The line at the top of the screen.</summary>
        public static string ObjectiveText(Survivor local)
        {
            var layout = Layout;
            if (layout == null) return "";
            if (layout.Bomb)
            {
                if (Exploded) return "Target destroyed! Get back to the start";
                if (Planted) return $"Hold out! The C4 explodes in {Mathf.CeilToInt(FuseLeft)} s — they are coming!";
                if (local != null && local.Inventory.C4) return "Objective: plant the C4 on the marked incubator";
                foreach (var chest in Chest.All)
                    foreach (var entry in chest.Contents)
                        if (entry.Kind == LootKind.C4) return "The C4 is on a body — pick it up before the body is gone!";
                return "Objective: the C4 must be planted on the marked incubator";
            }
            if (layout.VaccineMission)
            {
                if (local != null && local.Inventory.Vaccine) return "Objective: bring the Vaccine to the helicopter";
                if (RivalCarrier != null) return "The rivals have the Vaccine! Stop them reaching the helicopter";
                foreach (var chest in Chest.All)
                    foreach (var entry in chest.Contents)
                        if (entry.Kind == LootKind.Vaccine) return "The Vaccine is on a body — pick it up!";
                foreach (var s in Survivor.All)
                    if (!s.IsDead && s.Inventory.Vaccine) return "Objective: bring the Vaccine to the helicopter";
                return "Objective: find the Vaccine";
            }
            return "";
        }
    }

    /// <summary>Blinks its renderer (the planted C4's light).</summary>
    public class Blink : MonoBehaviour
    {
        private Renderer _renderer;

        private void Awake() => _renderer = GetComponent<Renderer>();

        private void Update()
        {
            var fuse = Mission.FuseLeft;
            var rate = fuse < 5f ? 8f : 2f;
            if (_renderer != null) _renderer.enabled = Mathf.Repeat(Time.time * rate, 1f) < 0.5f;
        }
    }
}
