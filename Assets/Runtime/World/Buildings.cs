using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    public enum Side { North, South, East, West }

    public struct Door
    {
        public Side Side;
        /// <summary>Door centre, measured along the wall from its west (N/S walls) or south (E/W walls) end.</summary>
        public float Offset;
        public float Width;

        public Door(Side side, float offset, float width = 1.8f)
        {
            Side = side;
            Offset = offset;
            Width = width;
        }
    }

    /// <summary>An enterable building: walls with doorways and a roof (owner, 2026-09-30).</summary>
    public class BuildingDef
    {
        public string Name;
        public Rect Bounds;
        public float Height = 3f;
        public float Wall = 0.4f;
        public Color WallColor = new Color(0.5f, 0.48f, 0.45f);
        public Color RoofColor = new Color(0.42f, 0.2f, 0.16f);
        public readonly List<Door> Doors = new List<Door>();
        /// <summary>May get an equipment chest (chance in <see cref="ChestTuning"/>).</summary>
        public bool MayHaveChest = true;
        /// <summary>Has a zombie incubator that spawns zombies over time.</summary>
        public bool Incubator;
        /// <summary>Tile stages: the chest and incubator cells, and the doorway cells (from the map).</summary>
        public Vector3 ChestPoint;
        public Vector3 IncubatorPoint;
        public readonly List<Vector3> DoorPoints = new List<Vector3>();

        public Vector3 DoorPosition(Door door)
        {
            var b = Bounds;
            switch (door.Side)
            {
                case Side.North: return new Vector3(b.xMin + door.Offset, 0f, b.yMax);
                case Side.South: return new Vector3(b.xMin + door.Offset, 0f, b.yMin);
                case Side.East: return new Vector3(b.xMax, 0f, b.yMin + door.Offset);
                default: return new Vector3(b.xMin, 0f, b.yMin + door.Offset);
            }
        }

        /// <summary>The wall segments between doorways, as obstacle blocks.</summary>
        public IEnumerable<Block> WallBlocks()
        {
            var b = Bounds;
            foreach (var side in new[] { Side.North, Side.South, Side.East, Side.West })
            {
                var horizontal = side == Side.North || side == Side.South;
                var length = horizontal ? b.width : b.height;
                var gaps = new List<(float from, float to)>();
                foreach (var door in Doors)
                    if (door.Side == side) gaps.Add((door.Offset - door.Width * 0.5f, door.Offset + door.Width * 0.5f));
                gaps.Sort((x, y) => x.from.CompareTo(y.from));
                var start = 0f;
                foreach (var (from, to) in gaps)
                {
                    if (from > start) yield return Segment(side, start, from, length);
                    start = Mathf.Max(start, to);
                }
                if (start < length) yield return Segment(side, start, length, length);
            }
        }

        private Block Segment(Side side, float from, float to, float length)
        {
            var b = Bounds;
            // Close the corners; leave doorways their full width.
            if (from <= 0f) from -= Wall * 0.5f;
            if (to >= length) to += Wall * 0.5f;
            var mid = (from + to) * 0.5f;
            var len = to - from;
            switch (side)
            {
                case Side.North: return new Block(b.xMin + mid, b.yMax, len, Wall, Height, WallColor);
                case Side.South: return new Block(b.xMin + mid, b.yMin, len, Wall, Height, WallColor);
                case Side.East: return new Block(b.xMax, b.yMin + mid, Wall, len, Height, WallColor);
                default: return new Block(b.xMin, b.yMin + mid, Wall, len, Height, WallColor);
            }
        }
    }

    /// <summary>
    /// The roof of a building: covers the interior so it can't be seen, and lifts while the local player is inside or
    /// near one of its doors (owner, 2026-09-30). Visual only — it never blocks movement, shots or zombie vision.
    /// </summary>
    public class Building : MonoBehaviour
    {
        public static readonly List<Building> All = new List<Building>();
        private const float DoorReveal = 3f; // PLACEHOLDER

        public BuildingDef Def { get; private set; }
        public bool RoofShown { get; private set; } = true;
        private GameObject _roof;
        private readonly List<Vector3> _doors = new List<Vector3>();

        public static Building Create(Transform parent, BuildingDef def)
        {
            var go = new GameObject($"Building {def.Name}");
            go.transform.SetParent(parent, false);
            var building = go.AddComponent<Building>();
            building.Def = def;
            foreach (var door in def.Doors) building._doors.Add(def.DoorPosition(door));
            building._doors.AddRange(def.DoorPoints);
            var b = def.Bounds;
            // The built-in roof art (2026-10-02).
            building._roof = new GameObject("Roof");
            building._roof.transform.SetParent(go.transform, false);
            building._roof.AddComponent<MeshFilter>().sharedMesh = Art.RoofArt.Build(b, def.Height, $"Roof {def.Name}", def.Name);
            var roofRenderer = building._roof.AddComponent<MeshRenderer>();
            roofRenderer.sharedMaterial = Art.ArtMaterials.Stylized;
            // Owner, 2026-10-07: buildings cast shadows.
            roofRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return building;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 p, float margin = 0f)
        {
            var b = Def.Bounds;
            return p.x > b.xMin - margin && p.x < b.xMax + margin && p.z > b.yMin - margin && p.z < b.yMax + margin;
        }

        private void LateUpdate()
        {
            var session = GameSession.Instance;
            var viewer = session != null ? session.LocalSurvivor : null;
            var open = false;
            if (viewer != null && !viewer.IsDead)
            {
                var p = viewer.transform.position;
                open = Contains(p, 0.3f);
                foreach (var door in _doors)
                    if (!open && (door - p).sqrMagnitude < DoorReveal * DoorReveal) open = true;
            }
            if (RoofShown == !open) return;
            RoofShown = !open;
            _roof.SetActive(RoofShown);
        }

        /// <summary>Hidden under a closed roof (the HUD doesn't label loot there).</summary>
        public static bool Covered(Vector3 p)
        {
            foreach (var building in All)
                if (building.RoofShown && building.Contains(p)) return true;
            return false;
        }
    }

    /// <summary>
    /// An equipment chest: holds several items, opened with Pickup and emptied through a panel like the inventory
    /// (owner, 2026-09-30). Taking follows the normal pickup rules.
    /// </summary>
    public class Chest : MonoBehaviour
    {
        public static readonly List<Chest> All = new List<Chest>();
        public readonly List<LootData> Contents = new List<LootData>();
        public int NetId;
        /// <summary>For a corpse: the dead survivor's network id (-1 for a chest).</summary>
        public int OwnerId = -1;
        /// <summary>Bumped on every change to any container, so the Host only resends them when something changed.</summary>
        public static int Version;

        public static void Changed() => Version++;

        /// <summary>Shown on the panel and the Pickup prompt.</summary>
        public string Title = "Equipment chest";
        /// <summary>
        /// Seconds until it disappears with whatever is left (a corpse: 20 s, owner 2026-09-29); negative = stays.
        /// </summary>
        public float Lifetime = -1f;
        /// <summary>Destroyed together with the container (the corpse body).</summary>
        public GameObject RemoveWith;
        private GameObject _model;
        private bool _shownOpen;
        /// <summary>Length of the chest model (2026-10-04: bigger, easier to spot).</summary>
        public const float ChestSize = 1.45f;

        /// <summary>
        /// Owner, 2026-10-04: a chest shows the open model from the first time it's opened, whatever is still inside.
        /// (An emptied chest also shows it, which covers chests opened by another player online.)
        /// </summary>
        public void MarkOpened()
        {
            if (_model == null || _shownOpen) return;
            _shownOpen = true;
            Destroy(_model);
            _model = Art.PackArt.Show(transform, "chest_opend", ChestSize, Vector3.zero);
        }

        private void LateUpdate()
        {
            if (Contents.Count == 0) MarkOpened();
        }

        public static Chest Spawn(Transform parent, Vector3 position, float yaw)
        {
            var go = new GameObject("Equipment chest");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var chest0 = go.AddComponent<Chest>();
            chest0.NetId = NetIds.Next();
            // The owner's chest models (closed, and open once emptied), 2026-10-04.
            chest0._model = Art.PackArt.Show(go.transform, "chest_closed", ChestSize, Vector3.zero);
            // A gold ring around it, like the items' rings, so a chest is easy to spot (owner, 2026-10-04: chests were hard to see).
            if (chest0._model != null) LootItem.GlowRing(go.transform, new Color(1f, 0.78f, 0.25f)).transform.localScale = new Vector3(1.9f, 1f, 1.9f);
            if (chest0._model != null) return chest0;
            Object.Destroy(chest0);
            Primitives.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.1f, 0.6f, 0.6f), new Color(0.35f, 0.42f, 0.25f));
            Primitives.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.62f, 0f), new Vector3(1.15f, 0.06f, 0.65f), new Color(0.25f, 0.3f, 0.18f));
            Primitives.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.45f, 0.31f), new Vector3(0.2f, 0.15f, 0.02f), new Color(0.9f, 0.8f, 0.3f));
            var chest = go.AddComponent<Chest>();
            chest.NetId = NetIds.Next();
            return chest;
        }

        /// <summary>A dead character's inventory, lootable with Pickup until the corpse disappears.</summary>
        public static Chest Corpse(Transform parent, Survivor dead, float lifetime)
        {
            var go = new GameObject($"Corpse of {dead.DisplayName}");
            go.transform.SetParent(parent, false);
            var p = dead.transform.position;
            go.transform.position = new Vector3(p.x, 0f, p.z);
            var corpse = go.AddComponent<Chest>();
            corpse.Title = $"{dead.DisplayName}'s body";
            corpse.Lifetime = lifetime;
            corpse.RemoveWith = dead.gameObject;
            corpse.NetId = NetIds.Next();
            corpse.OwnerId = dead.NetId;
            return corpse;
        }

        private void OnEnable()
        {
            All.Add(this);
            Version++;
        }

        private void OnDisable()
        {
            All.Remove(this);
            Version++;
        }

        private void Update()
        {
            if (Lifetime < 0f || !World.Simulating) return;
            Lifetime -= Time.deltaTime;
            if (Lifetime > 0f) return;
            // Anything still on the corpse is gone for good, except the Vaccine (owner, 2026-10-11): it stays here.
            foreach (var entry in Contents)
                if (entry.Kind == LootKind.Vaccine && entry.Amount > 0) LootItem.Spawn(transform.position, LootKind.Vaccine);
            Contents.RemoveAll(e => e.Kind == LootKind.Vaccine);
            if (RemoveWith != null) Destroy(RemoveWith);
            Destroy(gameObject);
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// A zombie nest / incubator inside a building: spawns zombies over time (owner, 2026-09-30). It can be destroyed by
    /// damage and only spawns while a player is within range (owner confirmed 2026-09-30).
    /// </summary>
    public class Incubator : MonoBehaviour, IDamageable
    {
        public static readonly List<Incubator> All = new List<Incubator>();

        public float Hp { get; private set; }
        public bool IsDead { get; private set; }
        public int NetId;
        /// <summary>Online client: shows the Host's incubator; never spawns.</summary>
        public bool Replica { get; private set; }

        private IncubatorTuning _def;
        private StageRuntime _stage;
        private float _nextSpawn;
        // With the life number: a pooled zombie that came back as someone else no longer counts.
        private readonly List<(Zombie zombie, int life)> _spawned = new List<(Zombie, int)>();
        private Transform _pulse;
        private Vector3 _pulseBase = Vector3.one;

        public static Incubator Spawn(StageRuntime stage, Vector3 position, IncubatorTuning def)
        {
            var go = new GameObject("Zombie incubator") { layer = Layers.Zombie };
            go.transform.SetParent(stage.transform, false);
            go.transform.position = position;
            var incubator = go.AddComponent<Incubator>();
            incubator._def = def;
            incubator._stage = stage;
            incubator.Hp = def.hp;
            incubator.NetId = NetIds.Next();
            incubator._nextSpawn = def.interval;
            var color = new Color(0.5f, 0.3f, 0.45f);
            // The owner's incubator model (2026-10-04); placeholder spheres without the pack.
            var model = Art.PackArt.Show(go.transform, "zombie_incubator", 1.8f, Vector3.zero);
            incubator._pulse = model != null ? model.transform : Primitives.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 1.2f, 1.1f), color).transform;
            incubator._pulseBase = model != null ? Vector3.one : new Vector3(1.1f, 1.2f, 1.1f);
            if (model == null)
            foreach (var (x, z) in new[] { (0.7f, 0.2f), (-0.6f, 0.4f), (0.1f, -0.7f) })
                Primitives.Shape(PrimitiveType.Sphere, go.transform, new Vector3(x, 0.3f, z), new Vector3(0.55f, 0.6f, 0.55f), color * 0.8f);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.8f;
            capsule.height = 1.4f;
            capsule.center = new Vector3(0f, 0.7f, 0f);
            go.AddComponent<Rigidbody>().isKinematic = true;
            return incubator;
        }

        /// <summary>Online client: an incubator the Host has.</summary>
        public static Incubator SpawnReplica(StageRuntime stage, int id, Vector3 position, IncubatorTuning def)
        {
            var incubator = Spawn(stage, position, def);
            incubator.NetId = id;
            incubator.Replica = true;
            return incubator;
        }

        public void ApplyReplica(float hp)
        {
            if (hp < Hp - 0.01f) HitFx.Flash(transform);
            Hp = hp;
        }

        /// <summary>After host migration: start spawning again.</summary>
        public void BecomeSimulated() => Replica = false;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (IsDead || (GameSession.Instance != null && !GameSession.Instance.Playing)) return;
            var t = Time.time * 2.5f;
            _pulse.localScale = _pulseBase * (1f + 0.04f * Mathf.Sin(t));
            if (Replica) return;

            _spawned.RemoveAll(s => s.zombie == null || s.zombie.IsDead || !s.zombie.isActiveAndEnabled || s.zombie.Life != s.life);
            _nextSpawn -= Time.deltaTime;
            if (_nextSpawn > 0f) return;
            _nextSpawn = _def.interval;
            if (_spawned.Count >= _def.maxAlive || !PlayerWithin(_def.activeRange)) return;
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var zombie = _stage.SpawnZombie(ZombieType.Basic, transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.4f);
            if (zombie != null) _spawned.Add((zombie, zombie.Life));
        }

        private bool PlayerWithin(float range)
        {
            foreach (var s in Survivor.All)
                if (!s.IsDead && (s.transform.position - transform.position).sqrMagnitude < range * range) return true;
            return false;
        }

        /// <summary>
        /// Bomb stages: the incubator the C4 must be planted on (owner, 2026-10-02). Only the C4 destroys it, so the
        /// stage can't be won by shooting it.
        /// </summary>
        public bool IsTarget { get; private set; }

        public void MarkTarget()
        {
            IsTarget = true;
            // ⚠ Placeholder: a red ring on the ground and a red light column, readable from far away.
            var ring = Primitives.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.03f, 0f), new Vector3(3.2f, 0.01f, 3.2f), new Color(0.9f, 0.15f, 0.1f));
            ring.name = "Target ring";
            Primitives.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.1f, 2.5f, 0.1f), new Color(1f, 0.25f, 0.15f));
        }

        /// <summary>The C4 went off.</summary>
        public void Destroy()
        {
            if (IsDead) return;
            Hp = 0f;
            IsDead = true;
            Sfx.Play(SoundId.ZombieDeath, transform.position);
            Destroy(gameObject);
        }

        public void TakeDamage(float damage, Vector3 from, bool fromExplosion)
        {
            if (IsDead || Replica || IsTarget) return;
            Hp -= damage;
            HitFx.Flash(transform);
            if (Hp > 0f) return;
            IsDead = true;
            Sfx.Play(SoundId.ZombieDeath, transform.position);
            Destroy(gameObject);
        }
    }
}
