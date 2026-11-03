using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Endless mode (owner, 2026-10-07), Solo or Co-op, on the largest map, ringed by a fence with gaps:
    /// - First a countdown (3 minutes) to collect equipment, with no zombies; items keep appearing after it.
    /// - Then zombies come in through the gaps in waves, more of them and tougher kinds as time goes on.
    /// - The weather cycles: day, rain, night, night rain (lightning and thunder), and round again.
    /// - The backpack holds ×1.5 and every ammo pickup gives 50 % more (Survivor).
    /// - A run lasts until the whole team is down; the result is the time survived, and the best time is kept.
    /// Lives on the stage root. Online, the Host runs it and the friend's copy follows the Host's clock.
    /// The numbers are first guesses (<see cref="EndlessTuning"/>).
    /// </summary>
    public class Endless : MonoBehaviour
    {
        public static Endless Current { get; private set; }

        /// <summary>True while an Endless run is on (the backpack and ammo rules read it).</summary>
        public static bool Active => Current != null;

        public static readonly string[] PhaseNames = { "Day", "Rain", "Night", "Night rain" };

        /// <summary>Endless mode's map.</summary>
        public const string StageId = "endless";

        private StageRuntime _stage;
        private EndlessTuning _t;
        private System.Random _rng;
        private bool _replica;
        private float _nextWave;
        private float _nextLoot;

        /// <summary>Seconds since the run began (the countdown included).</summary>
        public float Clock { get; private set; }
        /// <summary>Seconds of the countdown left (0 once the zombies come).</summary>
        public float PrepLeft => Mathf.Max(0f, _t.prepSeconds - Clock);
        /// <summary>Seconds survived since the zombies came (the score).</summary>
        public float Survived => Mathf.Max(0f, Clock - _t.prepSeconds);
        public int Kills { get; private set; }
        public int Wave { get; private set; }
        /// <summary>The weather now: 0 day, 1 rain, 2 night, 3 night rain (-1 before the first frame).</summary>
        public int Phase { get; private set; } = -1;

        public void Setup(StageRuntime stage, GameTuning tuning, int seed, bool replica)
        {
            _stage = stage;
            _t = tuning.endless;
            _rng = new System.Random(seed);
            _replica = replica;
            Current = this;
            _nextWave = _t.prepSeconds;
            _nextLoot = _t.prepSeconds;
            if (!replica) Zombie.Killed += OnKilled;
        }

        // The stage is switched off before it is destroyed; the next stage must not see this run.
        private void OnDisable()
        {
            Zombie.Killed -= OnKilled;
            if (Current == this) Current = null;
        }

        private void OnKilled(Zombie z) => Kills++;

        /// <summary>Online friend: the Host's run as it stands.</summary>
        public void ApplyReplica(float clock, int kills, int wave)
        {
            Clock = clock;
            Kills = kills;
            Wave = wave;
        }

        /// <summary>Tests: jumps the run's clock (the waves that would have come so far are counted, not spawned).</summary>
        public void SkipTo(float clock)
        {
            Clock = clock;
            Wave = Mathf.Max(Wave, Mathf.FloorToInt((clock - _t.prepSeconds) / _t.waveSeconds));
            _nextWave = Mathf.Max(_t.prepSeconds, _t.prepSeconds + (Wave) * _t.waveSeconds);
            _nextLoot = Mathf.Max(_t.prepSeconds, clock);
        }

        /// <summary>The weather at this point of the run: day, rain, night, night rain (owner, 2026-10-07), and round again.</summary>
        public static int PhaseAt(EndlessTuning t, float clock)
        {
            var inCycle = clock % (t.daySeconds + t.rainSeconds + t.nightSeconds + t.nightRainSeconds);
            if (inCycle < t.daySeconds) return 0;
            if (inCycle < t.daySeconds + t.rainSeconds) return 1;
            return inCycle < t.daySeconds + t.rainSeconds + t.nightSeconds ? 2 : 3;
        }

        public static bool Rainy(int phase) => phase == 1 || phase == 3;
        public static bool Night(int phase) => phase == 2 || phase == 3;

        private void Update()
        {
            var game = GameSession.Instance;
            if (game != null && !game.Playing) return;
            if (!_replica)
            {
                if (!World.Simulating) return;
                Clock += Time.deltaTime;
                if (Clock >= _nextWave)
                {
                    // Every gap on a player's screen: the wave waits a moment (owner, 2026-10-09).
                    if (SpawnWave()) _nextWave += _t.waveSeconds;
                    else _nextWave = Clock + 1f;
                }
                if (Clock >= _nextLoot)
                {
                    _stage.SpawnRandomLoot(_t.maxGroundLoot);
                    _nextLoot += _t.lootSeconds;
                }
            }
            SetPhase(PhaseAt(_t, Clock));
        }

        private void SetPhase(int phase)
        {
            if (phase == Phase) return;
            var first = Phase < 0;
            Phase = phase;
            var tuning = GameSession.Instance != null ? GameSession.Instance.Tuning : null;
            if (tuning != null) StageConditions.Set(Rainy(phase), Night(phase), tuning);
            var atmosphere = GetComponent<StageAtmosphere>();
            if (atmosphere != null) atmosphere.Set(Rainy(phase), Night(phase));
            if (!first) GameSession.Instance?.Hud?.Toast(phase == 3 ? "A storm rolls in." : phase == 2 ? "Night falls." : phase == 1 ? "It starts to rain." : "Day breaks.");
        }

        /// <summary>
        /// One wave through the gaps: bigger with each wave (bigger still in Co-op), tougher kinds as time goes on, a boss
        /// every few waves. Every zombie heads straight for the nearest player. Held back while too many are alive.
        /// </summary>
        private bool SpawnWave()
        {
            var gaps = _stage.Layout.ZombieSpawnPoints;
            if (gaps.Count == 0) return true;
            // Owner, 2026-10-09: only through gaps no player can see.
            var hidden = gaps.FindAll(g => _stage.MayAppearAt(g));
            if (hidden.Count == 0) return false;
            Wave++;
            var count = Mathf.RoundToInt((_t.waveBase + _t.wavePerWave * (Wave - 1)) * (_stage.Coop ? _t.coopMultiplier : 1f));
            count = Mathf.Min(count, _t.maxAlive - Zombie.Alive.Count);
            var minutes = Survived / 60f;
            var toughness = 1f + _t.toughnessPerMinute * minutes;
            for (var i = 0; i < count; i++)
            {
                var gap = hidden[_rng.Next(hidden.Count)];
                var at = gap + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 3f;
                Send(_stage.SpawnZombie(PickType(minutes), at), toughness);
            }
            if (Wave >= _t.firstBossWave && (Wave - _t.firstBossWave) % _t.bossEvery == 0)
            {
                var bosses = new[] { ZombieType.SubBoss, ZombieType.Tiger, ZombieType.Bear, ZombieType.Tank };
                var bossCount = _stage.Coop ? 2 : 1;
                for (var i = 0; i < bossCount; i++)
                    Send(_stage.SpawnZombie(bosses[_rng.Next(bosses.Length)], hidden[_rng.Next(hidden.Count)]), toughness);
            }
            if (Wave == 1)
            {
                GameSession.Instance?.Hud?.Toast("They're coming through the gaps in the fence!");
                NetSync.ToastAll("They're coming through the gaps in the fence!");
            }
            return true;
        }

        /// <summary>Sends a zombie at the nearest player, with the run's extra toughness.</summary>
        private static void Send(Zombie z, float toughness)
        {
            if (z == null) return;
            z.Toughness = toughness;
            Survivor nearest = null;
            var best = float.MaxValue;
            foreach (var s in Survivor.All)
            {
                if (s.IsDead || s.Team != Survivor.PlayerTeam) continue;
                var d = (s.transform.position - z.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = s;
                }
            }
            if (nearest != null) z.JoinChase(nearest, 600f);
        }

        /// <summary>Tougher kinds as the run goes on: Basic at first, then more and more Fast, animals and Tanks.</summary>
        private ZombieType PickType(float minutes)
        {
            float Share(float from, float perMinute, float max) => minutes < from ? 0f : Mathf.Min(max, (minutes - from + 1f) * perMinute);
            var roll = (float)_rng.NextDouble();
            var tank = Share(_t.tankFromMinute, 0.02f, 0.12f);
            if (roll < tank) return ZombieType.Tank;
            roll -= tank;
            var animal = Share(_t.animalsFromMinute, 0.05f, 0.3f) * (Night(Phase) ? 1.5f : 1f);
            if (roll < animal) return _rng.NextDouble() < 0.4 ? ZombieType.Rat : ZombieType.Dog;
            roll -= animal;
            var fast = Share(_t.fastFromMinute, 0.06f, 0.35f);
            return roll < fast ? ZombieType.Fast : ZombieType.Basic;
        }

        /// <summary>The run's result for the end screen; keeps the best time (Solo and Co-op apart).</summary>
        public string Finish(SettingsData settings, bool coop)
        {
            var survived = Survived;
            var best = coop ? settings.endlessBestCoop : settings.endlessBestSolo;
            var record = survived > best;
            if (record)
            {
                if (coop) settings.endlessBestCoop = survived;
                else settings.endlessBestSolo = survived;
            }
            return $"Survived {Minutes(survived)} · wave {Wave} · {Kills} kills" + (record ? " · NEW BEST!" : $" · best {Minutes(best)}");
        }

        public static string Minutes(float seconds) => $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";

        /// <summary>The line at the top of the screen.</summary>
        public string ObjectiveText()
        {
            if (PrepLeft > 0f) return $"Collect equipment! The zombies come in {Minutes(PrepLeft)}";
            return $"Survive · {Minutes(Survived)} · wave {Wave} · {Kills} kills · {(Phase >= 0 ? PhaseNames[Phase] : "")}";
        }
    }
}
