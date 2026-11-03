using System;
using UnityEngine;

namespace ZombieGame
{
    public enum WeaponId { Pistol, Shotgun, AR, Rifle, Knife, SMG }

    /// <summary>Ammo groups (D-036). The Pistol has unlimited reserve ammo and no ammo item.</summary>
    public enum AmmoType { None, Shotgun, AR, Rifle, SMG }

    public static class Arsenal
    {
        /// <summary>Every ammo type that exists as an item (the Pistol has none).</summary>
        public static readonly AmmoType[] AmmoTypes = { AmmoType.Shotgun, AmmoType.AR, AmmoType.Rifle, AmmoType.SMG };
        /// <summary>Every long gun (one long-gun slot).</summary>
        public static readonly WeaponId[] LongGuns = { WeaponId.Shotgun, WeaponId.AR, WeaponId.Rifle, WeaponId.SMG };
    }

    /// <summary>What the weapon panel can select.</summary>
    public enum Slot { Pistol, LongGun, Knife, Grenade }

    /// <summary>SubBoss (owner, 2026-10-03): secondary bosses on the route of missions that aren't about the Main Boss.</summary>
    /// <summary>
    /// Zombie types (owner, 2026-10-07). New types go at the end: the tuning asset stores them as numbers.
    /// Humanoid: Basic and Fast (models zmb_1 / zmb_2; the Fast ones with red skin), Tank, Boss (boss_1: the tutorial
    /// boss), SubBoss. Animal: Dog (the four-legged zombie, formerly the placeholder "Jumper"), Rat, and the Tiger and
    /// Bear bosses (boss_bear: the first boss-killing stage's boss, later a secondary boss).
    /// </summary>
    public enum ZombieType { Basic, Fast, Tank, Boss, Dog, SubBoss, Rat, Tiger, Bear }

    public static class ZombieTypes
    {
        /// <summary>Bosses (main or secondary): never capped, never dormant, not stopped by bullet hits, health bar.</summary>
        public static bool IsBoss(ZombieType t) => t == ZombieType.Boss || t == ZombieType.SubBoss || t == ZombieType.Tiger || t == ZombieType.Bear;

        /// <summary>Animal zombies (owner, 2026-10-07: night stages favour them).</summary>
        public static bool IsAnimal(ZombieType t) => t == ZombieType.Dog || t == ZombieType.Rat || t == ZombieType.Tiger || t == ZombieType.Bear;
    }

    /// <summary>C4 and Vaccine: mission items (owner, 2026-10-02), carried by players only, no slots.</summary>
    public enum LootKind { LongGun, Ammo, Health, Grenade, C4, Vaccine }

    [Serializable]
    public class FirearmDef
    {
        public WeaponId id;
        public string displayName;
        public AmmoType ammo;
        public float damage;
        public int pellets = 1;
        /// <summary>Total width of the fixed pellet pattern (D-133); 0 for single-projectile guns.</summary>
        public float patternWidthDeg;
        public float shotInterval;
        public int magazine;
        public float range;
        public float reloadTime;
        /// <summary>
        /// Shell-by-shell reloading (2026-10-05, the shotgun): seconds per shell; 0 = the whole magazine at once. Each shell
        /// adds one round, and firing stops the reload at once with whatever is loaded.
        /// </summary>
        public float shellTime;
        public float projectileSpeed;
        /// <summary>Passes through every zombie without losing damage (D-136).</summary>
        public bool pierce;
        public float baseSpreadDeg;
        public float maxSpreadDeg;
        public float bloomPerShotDeg;
        public float bloomRecoveryDegPerSec;
        public float moveSpreadDeg;
        public float noiseRange;
        public int ammoPerBox;
    }

    [Serializable]
    public class MeleeDef
    {
        public float damage = 35f;          // PLACEHOLDER
        public float interval = 0.6f;       // PLACEHOLDER
        public float reach = 1.6f;          // PLACEHOLDER
        public float arcDeg = 100f;         // PLACEHOLDER
    }

    [Serializable]
    public class GrenadeDef
    {
        public float fuse = 2f;             // owner 2026-09-29: explodes 2 s after the throw
        public int slots = 20;              // D-147
        public float minRange = 3f;         // PLACEHOLDER
        public float maxRange = 14f;        // PLACEHOLDER
        public float chargeTime = 1f;       // PLACEHOLDER (Android: hold Fire, min → max)
        public float flightTime = 0.6f;     // PLACEHOLDER
        public float damage = 240f;         // PLACEHOLDER (owner 2026-10-09: doubled, was 120)
        /// <summary>Owner, 2026-10-09: players and AI take this share of a grenade's damage.</summary>
        public float survivorShare = 0.3f;
        public float radius = 5f;           // PLACEHOLDER
        public float noiseRange = 60f;      // PLACEHOLDER (D-152: louder than any gun)
    }

    [Serializable]
    public class BarrelDef
    {
        public float damage = 200f;         // PLACEHOLDER (owner 2026-10-09: doubled, was 100)
        /// <summary>Owner, 2026-10-09: players and AI take this share of a fuel barrel's damage.</summary>
        public float survivorShare = 0.3f;
        public float radius = 4.5f;         // PLACEHOLDER
        public float noiseRange = 50f;      // PLACEHOLDER
        public float chainDelay = 0.15f;    // PLACEHOLDER
    }

    [Serializable]
    public class PlayerTuning
    {
        public float maxHp = 100f;          // PLACEHOLDER (D-104: one value for all players)
        /// <summary>Owner, 2026-10-11: players and AI (teammates, rivals, the scientist) take this share of a bullet's damage.</summary>
        public float bulletDamageTaken = 0.4f;
        public float moveSpeed = 5f;        // PLACEHOLDER
        public float switchTime = 0.4f;     // PLACEHOLDER (D-122: not instant)
        public float pickupRange = 1.8f;    // PLACEHOLDER
        public float healPercent = 0.5f;    // PLACEHOLDER (D-083: a percentage of max HP)
        public float healTime = 2f;         // PLACEHOLDER (owner 2026-09-30: using a Health Item takes time)
        public float knockbackDamping = 8f; // PLACEHOLDER
        public float corpseSeconds = 20f;   // owner 2026-09-29
    }

    [Serializable]
    public class InventoryTuning
    {
        public int capacity = 800;          // D-139 (owner 2026-10-05: +100, was 200; 2026-10-08: +100; 2026-10-09: +200; 2026-10-11: +200)
        public int arSlots = 2;             // D-145
        public int shotgunSlots = 7;        // D-145 (owner 2026-10-09: 7, was 3)
        public int rifleSlots = 13;         // D-145 (owner 2026-10-09: the sniper's 13, was 4)
        /// <summary>Owner, 2026-10-02: the SMG's ammo takes fewer slots than the AR's.</summary>
        public int smgSlots = 1;
        public int healthSlots = 10;        // D-146
        public int grenadeSlots = 20;       // D-147

        public int SlotCost(AmmoType type)
        {
            switch (type)
            {
                case AmmoType.AR: return arSlots;
                case AmmoType.Shotgun: return shotgunSlots;
                case AmmoType.Rifle: return rifleSlots;
                case AmmoType.SMG: return smgSlots;
                default: return 0;
            }
        }
    }

    [Serializable]
    public class ZombieDef
    {
        public ZombieDef Copy() => (ZombieDef)MemberwiseClone();
        public ZombieType type;
        public float hp;
        public float wanderSpeed;
        public float chaseSpeed;
        public float damage;
        public float attackCooldown;
        public float attackReach;
        public float windup;
        public float knockback;
        public float radius = 0.4f;
        public float height = 1.8f;
        /// <summary>Multiplies how far it sees and hears players (owner, 2026-10-03: the Dog 1.5×).</summary>
        public float senseMultiplier = 1f;
        /// <summary>The Dog (owner, 2026-10-03): spotting a player calls the zombies around it into the chase.</summary>
        public bool alertsPack;
        public float packRadius = 12f;
        /// <summary>How long called zombies keep chasing the player without seeing them.</summary>
        public float packChaseSeconds = 8f;
        public Color color = Color.gray;

        // Boss-only placeholder mechanic: ground slam.
        public bool slam;
        public float slamInterval;
        public float slamTrigger;
        public float slamTelegraph;
        public float slamRadius;
        public float slamDamage;
        public float slamKnockback;
    }

    [Serializable]
    public class PerceptionTuning
    {
        public float proximity = 2.5f;      // PLACEHOLDER
        /// <summary>Owner, 2026-10-06: every sound reaches 30 % less far than its listed range (zombie hearing).</summary>
        public float hearingScale = 0.7f;
        public float visionRange = 12f;     // PLACEHOLDER
        public float visionConeDeg = 80f;   // D-050
        public float searchRadius = 5f;     // PLACEHOLDER (D-052)
        public float searchDuration = 8f;   // PLACEHOLDER (D-052)
        public float thinkInterval = 0.2f;  // technical
        /// <summary>Owner, 2026-10-04: a zombie hit by a bullet stops moving this long (bosses and sub-bosses don't).</summary>
        public float bulletHitStop = 0.25f; // PLACEHOLDER
    }

    [Serializable]
    public class AimAssistTuning
    {
        public float angleDeg = 10f;        // PLACEHOLDER
        public float strength = 0.5f;       // PLACEHOLDER
    }

    [Serializable]
    public class SpawnTuning
    {
        public int maxAlive = 70;               // PLACEHOLDER (bounded by the phone's budget)
        public float trickleInterval = 45f;     // PLACEHOLDER (owner 2026-10-09: longer gaps, was 30)
        /// <summary>
        /// Owner, 2026-10-07: day stages favour humanoid zombies, night (dark) stages animal zombies. This share of the
        /// zombies (placed Basic ones and new ones) are animals (Dog or Rat) by day and at night. PLACEHOLDER.
        /// </summary>
        public float animalShareDay = 0.15f;
        public float animalShareNight = 0.6f;
        /// <summary>Of those animals, this share are Rats (the rest Dogs). PLACEHOLDER.</summary>
        public float ratShare = 0.4f;
        /// <summary>
        /// Co-op (owner, 2026-10-03): harder through the zombies' number and stats; Solo keeps the base difficulty.
        /// PLACEHOLDER multipliers.
        /// </summary>
        public float coopCount = 1.5f;
        /// <summary>Owner, 2026-10-06: Solo is easier, 30 % fewer zombies (placed, spawned and the population cap).</summary>
        public float soloCount = 0.7f;
        public float coopHp = 1.3f;
        public float coopDamage = 1.2f;
        public float coopSpeed = 1.05f;
        /// <summary>
        /// Co-op is Hard (owner, 2026-10-07: Solo Normal, Co-op Hard with more zombies and more secondary bosses): each
        /// secondary boss spot gets this many more. PLACEHOLDER.
        /// </summary>
        public int coopExtraSubBosses = 1;
        /// <summary>
        /// The population (2026-10-03): at most this many real zombies at once; the rest wait as dormant records. Real
        /// zombies appear within the activate range of a survivor (beyond the screen edge: the view is about 34 × 17 m) and
        /// go dormant again when idle beyond the deactivate range. PLACEHOLDER numbers, sized for the phone.
        /// </summary>
        public int maxActive = 45;
        public int maxPopulation = 160;
        public float activateRange = 26f;
        public float deactivateRange = 36f;
        public int trickleCount = 2;            // PLACEHOLDER (owner 2026-10-09: fewer, was 3)
        public float trickleMinDistance = 25f;  // PLACEHOLDER
        public int noiseEventsPerWave = 18;     // PLACEHOLDER (owner 2026-10-09: rarer, was 12)
        public int noiseWaveCount = 3;          // PLACEHOLDER (owner 2026-10-09: fewer, was 4)
        /// <summary>
        /// Owner, 2026-10-09: no wave (timed, gunfire, bomb response) spawns while this many zombies are active, so they
        /// can't pile up. PLACEHOLDER.
        /// </summary>
        public int waveMaxActive = 30;
        /// <summary>
        /// Owner, 2026-10-11: the map's zombies thin out near the start and gather towards the objective: an area's count
        /// is scaled from this at the start to objectiveWeight at the objective (by how far along the way it lies).
        /// PLACEHOLDER.
        /// </summary>
        public float startWeight = 0.35f;
        public float objectiveWeight = 1.8f;
        public float noiseWaveMin = 20f;        // PLACEHOLDER
        public float noiseWaveMax = 35f;        // PLACEHOLDER
    }

    [Serializable]
    public class LootTuning
    {
        public int longGuns = 10;               // PLACEHOLDER (owner 2026-10-04: too few; was 6), per 112 × 112 m
        public int ammoBoxes = 18;              // PLACEHOLDER
        public int healthItems = 6;             // PLACEHOLDER
        public int grenades = 5;                // PLACEHOLDER
        public float ammoRespawnSeconds = 40f;  // PLACEHOLDER (D-206; owner 2026-10-11: sooner, was 60)
        /// <summary>Owner, 2026-10-11: 10 % more items on the map and in chests. PLACEHOLDER.</summary>
        public float amountMultiplier = 1.1f;
    }

    [Serializable]
    public class ChestTuning
    {
        public float chance = 0.8f;             // PLACEHOLDER: per building that may have one (was 0.5; owner 2026-10-04: too few)
        public int minItems = 2;                // PLACEHOLDER
        public int maxItems = 4;                // PLACEHOLDER
    }

    /// <summary>Bomb and Vaccine stages (owner, 2026-10-02). PLACEHOLDER numbers except the 20 s fuse.</summary>
    [Serializable]
    public class MissionTuning
    {
        /// <summary>Owner: the C4 explodes 20 s after it is planted.</summary>
        public float c4Fuse = 20f;
        /// <summary>How close to the target incubator the Plant button appears.</summary>
        public float plantRange = 3f;
        /// <summary>High damage (owner); it also hurts players caught in it (owner, 2026-10-02).</summary>
        public float c4Damage = 400f;
        public float c4Radius = 9f;
        public float c4NoiseRange = 70f;

        /// <summary>Owner, 2026-10-03: hurting the Main Boss calls every zombie to the Boss Area, at most this often.</summary>
        public float bossCallCooldown = 15f;
        /// <summary>Bomb stages (owner, 2026-10-03): during the fuse, zombies keep pouring from the spawn points near the target. PLACEHOLDER.</summary>
        public float responseInterval = 6f;     // owner 2026-10-09: longer gaps (was 3)
        public float responseRadius = 28f;
        public int responseCount = 2;           // owner 2026-10-09: fewer (was 4)
        /// <summary>Owner, 2026-10-03: zombies detect players 3× as well once a player is this close to the objective.</summary>
        public float alertDistance = 10f;
        public float alertDetectionMultiplier = 3f;

        /// <summary>Vaccine stages (owner, 2026-10-03): a rival AI team of three.</summary>
        public int rivalCount = 3;
        /// <summary>The players lose if the rivals keep the Vaccine this long (owner: 60 s).</summary>
        public float rivalHoldSeconds = 60f;
        /// <summary>Each rival starts with the Pistol, a random long gun and this many ammo boxes for it.</summary>
        public int rivalAmmoBoxes = 2;
        /// <summary>How far ahead the fleeing carrier aims to run, and how close the escorts stay to it.</summary>
        public float rivalFleeDistance = 14f;
        public float rivalEscortDistance = 4f;
    }

    /// <summary>Endless mode (owner, 2026-10-07). Everything but the 3-minute countdown and the ×1.5s is a PLACEHOLDER.</summary>
    [Serializable]
    public class EndlessTuning
    {
        /// <summary>Owner: 3 minutes to collect equipment before the zombies come.</summary>
        public float prepSeconds = 180f;
        /// <summary>The weather cycle (owner: day, rain, night and night rain in turn); seconds of each.</summary>
        public float daySeconds = 240f;
        public float rainSeconds = 120f;
        public float nightSeconds = 180f;
        /// <summary>Owner, 2026-10-07: night rain (rain, darkness, lightning and thunder) joins the cycle after the night.</summary>
        public float nightRainSeconds = 150f;
        /// <summary>A wave every this many seconds: waveBase zombies, wavePerWave more each wave (× coopMultiplier in Co-op).</summary>
        public float waveSeconds = 30f;
        public int waveBase = 6;
        public int wavePerWave = 2;
        public float coopMultiplier = 1.5f;
        /// <summary>No new wave zombies while this many are alive (the phone's budget).</summary>
        public int maxAlive = 60;
        /// <summary>Each minute survived, new zombies take this much less from each hit (0.06: 1.6× as tough after 10 min).</summary>
        public float toughnessPerMinute = 0.06f;
        /// <summary>Minutes survived before Fast zombies, animals and Tanks join the waves (then more of them as time goes on).</summary>
        public float fastFromMinute = 1f;
        public float animalsFromMinute = 3f;
        public float tankFromMinute = 6f;
        /// <summary>A secondary boss (two in Co-op) on this wave, then every bossEvery waves.</summary>
        public int firstBossWave = 6;
        public int bossEvery = 5;
        /// <summary>After the countdown, a random item appears this often on a loot spot, up to maxGroundLoot lying about.</summary>
        public float lootSeconds = 15f;     // owner 2026-10-11: sooner (was 20)
        public int maxGroundLoot = 60;
        /// <summary>Owner: backpack capacity ×1.5 and picked-up ammo +50 %.</summary>
        public float backpackMultiplier = 1.5f;
        public float ammoMultiplier = 1.5f;
    }

    /// <summary>
    /// The Tiger's charge (owner, 2026-10-08): a brief red warning on the ground along the way it will go, then it charges
    /// forward, hitting whoever is in its path. PLACEHOLDER numbers.
    /// </summary>
    [Serializable]
    public class ChargeTuning
    {
        /// <summary>It charges when its target is between these distances (m), at most this often (s).</summary>
        public float minRange = 5f;
        public float maxRange = 16f;
        public float interval = 6f;
        /// <summary>The red warning before it goes (s): the way is fixed when the warning appears, so it can be dodged.</summary>
        public float telegraph = 0.9f;
        public float speed = 16f;
        public float distance = 13f;
        public float damage = 45f;
        public float knockback = 9f;
    }

    [Serializable]
    public class IncubatorTuning
    {
        public float hp = 250f;                 // PLACEHOLDER
        public float interval = 20f;            // PLACEHOLDER: seconds between spawns
        public int maxAlive = 4;                // PLACEHOLDER: its own zombies alive at once
        public float activeRange = 40f;         // PLACEHOLDER: only spawns while a player is this close
    }

    /// <summary>
    /// A playable character (owner, 2026-09-30: chosen before each stage; characters differ by additional stats).
    /// Bonuses are added to the base values in <see cref="PlayerTuning"/>.
    /// </summary>
    [Serializable]
    public class CharacterDef
    {
        public string id = "protagonist";
        public string displayName = "Protagonist";      // PLACEHOLDER name
        public string description = "";
        public Color color = new Color(0.25f, 0.5f, 0.9f);
        // Owner, 2026-10-01: three base stats — HP, Attack (bonus gun damage), Speed — plus each character's perks.
        // Every bonus is a fraction of the base value: 0.2 = +20 %.
        public float hpBonus;
        public float attackBonus;                       // gun damage only (bullets and pellets)
        public float speedBonus;
        public float slotBonus;                         // extra inventory capacity
        public float meleeMultiplier = 1f;              // Knife damage ×
        public float noiseReduction;                    // 0..1, shrinks the range zombies hear this character's gunshots
    }

    [Serializable]
    public class AiTuning
    {
        public float maxSeparation = 15f;       // PLACEHOLDER (D-048/D-165): furthest from the human teammate
        public float followDistance = 6f;       // PLACEHOLDER: comes back when idle and further than this
        public float healThreshold = 0.5f;      // D-163 (owner 2026-10-09: below 50 %, was 40 %): uses a Health Item below this share of max HP
        /// <summary>Owner, 2026-10-09: the AI picks up Health Items first until it carries this many (and keeps room for them).</summary>
        public int healthItemsWanted = 2;
        /// <summary>Heals once no zombie is this close (m): out of reach of a hit, which would stop the healing. PLACEHOLDER.</summary>
        public float healSafeDistance = 3f;
        public float visionRange = 14f;         // PLACEHOLDER: sees zombies this far (walls block)
        public float closeRange = 3f;           // PLACEHOLDER: notices zombies this close even behind it
        public float keepAway = 3.5f;           // PLACEHOLDER: backs off from zombies closer than this
        public float turnRate = 540f;           // PLACEHOLDER: degrees per second when aiming
        public float fireAngle = 6f;            // PLACEHOLDER: fires once the aim is this close to the target
        public float lootRadius = 12f;          // PLACEHOLDER: looks for loot this far from itself
        public int grenadeCluster = 4;          // PLACEHOLDER: zombies within the blast before it throws
        public float grenadeCooldown = 8f;      // PLACEHOLDER
        public float thinkInterval = 0.2f;      // technical
    }

    [Serializable]
    public class ConditionsTuning
    {
        public float rainNoiseMultiplier = 0.6f;    // PLACEHOLDER: rain shortens how far sounds carry
        public float rainSpeedMultiplier = 1f;      // owner 2026-10-01: rain no longer slows anyone
        public float darkVisionMultiplier = 0.5f;   // PLACEHOLDER: darkness shortens everyone's sight
        public float lampRange = 22f;               // PLACEHOLDER (owner 2026-10-01: reach further than before)
        public float lampAngle = 80f;               // PLACEHOLDER: the head lamp's cone
        public float lampIntensity = 8f;            // PLACEHOLDER (owner 2026-10-01: brighter than before)
        public float darkAmbient = 0.16f;           // PLACEHOLDER (owner: not 100 % dark — a low level of visibility remains)
        /// <summary>
        /// Owner, 2026-10-07: the flashlight lights only the area in front of the character, not around the head: it sits at
        /// chest height just ahead of the body, tilted down this much. PLACEHOLDER position.
        /// </summary>
        public float lampHeight = 1.45f;
        public float lampAhead = 0.45f;
        public float lampPitch = 18f;
        /// <summary>
        /// Owner, 2026-10-07: street lights light a small area at night. The nearest few to the camera get a real light
        /// (the phone can't afford one per lamp); each lights a pool about 4 m across under its head. PLACEHOLDER.
        /// </summary>
        public int streetLightPool = 8;
        public float streetLightRange = 8f;
        public float streetLightAngle = 70f;
        public float streetLightIntensity = 6f;
        /// <summary>Night rain (owner, 2026-10-07): lightning at random, this many seconds apart; thunder follows it. PLACEHOLDER.</summary>
        public float lightningMinSeconds = 7f;
        public float lightningMaxSeconds = 22f;
    }

    [Serializable]
    public class RescueTuning
    {
        public float reachDistance = 2f;            // PLACEHOLDER: this close frees the hostage
        public float followDistance = 2.2f;         // PLACEHOLDER: the hostage keeps about this far behind
        public float hostageHp = 100f;              // PLACEHOLDER
    }

    /// <summary>
    /// Every gameplay number. Values marked PLACEHOLDER were picked by Claude as temporary stand-ins
    /// (docs/placeholders.md); values marked with a rule ID are the owner's.
    /// </summary>
    [CreateAssetMenu(menuName = "Zombie/Game Tuning")]
    public class GameTuning : ScriptableObject
    {
        public const string ResourceName = "GameTuning";

        public PlayerTuning player = new PlayerTuning();
        public InventoryTuning inventory = new InventoryTuning();
        public PerceptionTuning perception = new PerceptionTuning();
        public AimAssistTuning aimAssist = new AimAssistTuning();
        public MeleeDef knife = new MeleeDef();
        public GrenadeDef grenade = new GrenadeDef();
        public BarrelDef barrel = new BarrelDef();
        public SpawnTuning spawn = new SpawnTuning();
        public LootTuning loot = new LootTuning();
        public ChestTuning chest = new ChestTuning();
        public IncubatorTuning incubator = new IncubatorTuning();
        public MissionTuning missions = new MissionTuning();
        public AiTuning ai = new AiTuning();
        public ConditionsTuning conditions = new ConditionsTuning();
        public RescueTuning rescue = new RescueTuning();
        public EndlessTuning endless = new EndlessTuning();
        public ChargeTuning charge = new ChargeTuning();

        /// <summary>
        /// The character selection list (owner, 2026-10-01). Names are the owner's working names; colours are placeholders.
        /// </summary>
        /// <summary>The character selection's slots in order (owner, 2026-10-07): 1 New Character 01 (+20 % Attack),
        /// 2 New Character 02 (+20 % Speed), 3 Protagonist (+20 % HP), 4 New Character 04 (melee); names to change later.</summary>
        public CharacterDef[] characters =
        {
            new CharacterDef { id = "character_01", displayName = "Jack", description = "+20 % Attack", attackBonus = 0.2f,
                color = new Color(0.3f, 0.8f, 0.85f) },
            new CharacterDef { id = "character_02", displayName = "Maya", description = "+20 % Speed", speedBonus = 0.2f,
                color = new Color(0.45f, 0.75f, 0.3f) },
            new CharacterDef { id = "protagonist", displayName = "Ryan", description = "+20 % HP, +20 % backpack capacity",
                hpBonus = 0.2f, slotBonus = 0.2f, color = new Color(0.25f, 0.5f, 0.9f) },
            new CharacterDef { id = "character_04", displayName = "Ethan", description = "×2 melee damage, gunshots heard 30 % less far",
                meleeMultiplier = 2f, noiseReduction = 0.3f, color = new Color(0.6f, 0.4f, 0.8f) },
        };

        // PLACEHOLDER: every number in these tables.
        public FirearmDef[] firearms =
        {
            new FirearmDef
            {
                id = WeaponId.Pistol, displayName = "Pistol", ammo = AmmoType.None, damage = 20f, shotInterval = 0.4f,
                magazine = 12, range = 12f, reloadTime = 1.37f,  // 2026-10-05: matches its reload sound (was 1.2)
                projectileSpeed = 40f, baseSpreadDeg = 2f, maxSpreadDeg = 10f,
                bloomPerShotDeg = 1.5f, bloomRecoveryDegPerSec = 12f, moveSpreadDeg = 4f, noiseRange = 15f,
            },
            new FirearmDef
            {
                id = WeaponId.Shotgun, displayName = "Shotgun", ammo = AmmoType.Shotgun, damage = 12f, pellets = 9, patternWidthDeg = 24f,
                shotInterval = 0.9f, magazine = 6, range = 9f, reloadTime = 2.5f, shellTime = 0.38f,  // 2026-10-05: a shell per 0.38 s, as in its sound
                projectileSpeed = 35f, baseSpreadDeg = 2f,
                maxSpreadDeg = 8f, bloomPerShotDeg = 3f, bloomRecoveryDegPerSec = 10f, moveSpreadDeg = 3f, noiseRange = 25f, ammoPerBox = 18,
            },
            new FirearmDef
            {
                id = WeaponId.AR, displayName = "AR", ammo = AmmoType.AR, damage = 20f, // owner 2026-10-04: more damage (was 15)
                shotInterval = 0.1f, magazine = 30, range = 22f,
                reloadTime = 2.56f, projectileSpeed = 50f,  // 2026-10-05: matches the AR/SMG reload sound (was 2)
                baseSpreadDeg = 1.5f, maxSpreadDeg = 12f, bloomPerShotDeg = 0.8f,
                bloomRecoveryDegPerSec = 10f, moveSpreadDeg = 5f, noiseRange = 25f, ammoPerBox = 90,
            },
            new FirearmDef
            {
                id = WeaponId.Rifle, displayName = "Rifle", ammo = AmmoType.Rifle, damage = 90f, shotInterval = 1.3f, magazine = 5,
                range = 35f, reloadTime = 1.75f,  // 2026-10-05: matches its reload sound (was 2.8)
                projectileSpeed = 80f, pierce = true, baseSpreadDeg = 0.5f, maxSpreadDeg = 6f,
                bloomPerShotDeg = 4f, bloomRecoveryDegPerSec = 6f, moveSpreadDeg = 5f, noiseRange = 45f, ammoPerBox = 15,
            },
            // Owner, 2026-10-02: fires continuously like the AR, less damage, lighter ammo.
            new FirearmDef
            {
                id = WeaponId.SMG, displayName = "SMG", ammo = AmmoType.SMG, damage = 13f, // owner 2026-10-04: more damage (was 9)
                shotInterval = 0.075f, magazine = 35, range = 16f,
                reloadTime = 2.56f, projectileSpeed = 45f,  // 2026-10-05: matches the AR/SMG reload sound (was 1.7)
                baseSpreadDeg = 2.5f, maxSpreadDeg = 14f, bloomPerShotDeg = 0.7f,
                bloomRecoveryDegPerSec = 12f, moveSpreadDeg = 4f, noiseRange = 20f, ammoPerBox = 105,
            },
        };

        // PLACEHOLDER: every number in these tables.
        public ZombieDef[] zombies =
        {
            new ZombieDef
            {
                type = ZombieType.Basic, hp = 60f, wanderSpeed = 1f, chaseSpeed = 2.7f, damage = 10f, attackCooldown = 1.2f,
                attackReach = 1.3f, windup = 0.35f, knockback = 0.5f, color = new Color(0.45f, 0.6f, 0.4f),
            },
            new ZombieDef
            {
                type = ZombieType.Fast, hp = 45f, wanderSpeed = 1.2f, chaseSpeed = 4.8f, damage = 8f, attackCooldown = 0.9f,
                attackReach = 1.2f, windup = 0.25f, knockback = 0.3f, radius = 0.35f, height = 1.7f, color = new Color(0.85f, 0.75f, 0.2f),
            },
            new ZombieDef
            {
                type = ZombieType.Tank, hp = 400f, wanderSpeed = 0.75f, chaseSpeed = 2f, damage = 25f, attackCooldown = 2f,
                attackReach = 1.6f, windup = 0.5f, knockback = 6f, radius = 0.7f, height = 2.3f, color = new Color(0.45f, 0.3f, 0.6f),
            },
            new ZombieDef
            {
                type = ZombieType.Boss, hp = 1500f, wanderSpeed = 0.94f, chaseSpeed = 3f, damage = 30f, attackCooldown = 1.8f,
                attackReach = 2.2f, windup = 0.5f, knockback = 5f, radius = 1.1f, height = 3.2f, color = new Color(0.75f, 0.15f, 0.15f),
                slam = true, slamInterval = 6f, slamTrigger = 4.5f, slamTelegraph = 0.8f, slamRadius = 4.5f, slamDamage = 25f, slamKnockback = 8f,
            },
            // Owner, 2026-09-30 / 2026-10-03 / 2026-10-07: the Dog, a four-legged zombie (formerly the placeholder
            // "Jumper"). Same HP as the Basic zombie, faster and harder-hitting, sees and hears 50 % further, and calls
            // nearby zombies onto a player it spots (owner). The exact speeds and radius are PLACEHOLDER.
            new ZombieDef
            {
                type = ZombieType.Dog, hp = 60f, wanderSpeed = 1.6f, chaseSpeed = 4.4f, damage = 14f, attackCooldown = 1f,
                attackReach = 1.3f, windup = 0.3f, knockback = 0.6f, radius = 0.45f, height = 1.1f, color = new Color(0.2f, 0.65f, 0.65f),
                senseMultiplier = 1.5f, alertsPack = true,
            },
            // Owner, 2026-10-03: secondary bosses guard the routes of missions that aren't about the Main Boss. PLACEHOLDER:
            // a smaller Brute (about 40 % of its HP) with a weaker slam; the design comes with the official boss list.
            new ZombieDef
            {
                type = ZombieType.SubBoss, hp = 600f, wanderSpeed = 1.06f, chaseSpeed = 3.44f, damage = 22f, attackCooldown = 1.6f,
                attackReach = 1.9f, windup = 0.45f, knockback = 4f, radius = 1.3f, height = 3.6f, color = new Color(0.85f, 0.45f, 0.15f),
                slam = true, slamInterval = 8f, slamTrigger = 3.5f, slamTelegraph = 0.9f, slamRadius = 3.5f, slamDamage = 18f, slamKnockback = 6f,
            },
            // Owner, 2026-10-07: the Rat, an animal zombie: low damage, fast, low HP. PLACEHOLDER numbers.
            new ZombieDef
            {
                type = ZombieType.Rat, hp = 25f, wanderSpeed = 1.8f, chaseSpeed = 5.2f, damage = 5f, attackCooldown = 0.7f,
                attackReach = 0.9f, windup = 0.2f, knockback = 0.2f, radius = 0.25f, height = 0.45f, color = new Color(0.5f, 0.46f, 0.42f),
            },
            // Owner, 2026-10-07: the Tiger and the Bear, animal bosses. 2026-10-08: the Tiger charges (ChargeTuning).
            // PLACEHOLDER numbers.
            new ZombieDef
            {
                type = ZombieType.Tiger, hp = 1600f, wanderSpeed = 1.5f, chaseSpeed = 4.25f, damage = 32f, attackCooldown = 1.4f,
                attackReach = 2.4f, windup = 0.4f, knockback = 5f, radius = 1.15f, height = 2f, color = new Color(0.85f, 0.5f, 0.15f),
            },
            new ZombieDef
            {
                type = ZombieType.Bear, hp = 1400f, wanderSpeed = 1f, chaseSpeed = 3.13f, damage = 34f, attackCooldown = 1.7f,
                attackReach = 2f, windup = 0.5f, knockback = 6f, radius = 1.3f, height = 3.6f, color = new Color(0.5f, 0.5f, 0.52f),
            },
        };

        public FirearmDef Firearm(WeaponId id)
        {
            foreach (var def in firearms) if (def.id == id) return def;
            throw new ArgumentException($"No firearm definition for {id}");
        }

        public FirearmDef FirearmForAmmo(AmmoType ammo)
        {
            foreach (var def in firearms) if (def.ammo == ammo && ammo != AmmoType.None) return def;
            return null;
        }

        public ZombieDef Zombie(ZombieType type)
        {
            foreach (var def in zombies) if (def.type == type) return def;
            throw new ArgumentException($"No zombie definition for {type}");
        }

        private static GameTuning _loaded;

        /// <summary>The project's tuning asset, or built-in defaults when it is missing (tests).</summary>
        public static GameTuning Load()
        {
            if (_loaded != null) return _loaded;
            _loaded = Resources.Load<GameTuning>(ResourceName);
            if (_loaded == null) _loaded = CreateInstance<GameTuning>();
            return _loaded;
        }
    }
}
