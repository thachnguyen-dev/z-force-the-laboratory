using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>One stack of loot: on the ground, in an equipment chest, or (later) on a corpse.</summary>
    public struct LootData
    {
        public LootKind Kind;
        public WeaponId Weapon;
        public AmmoType Ammo;
        /// <summary>Rounds for ammo; 1 for everything else.</summary>
        public int Amount;
        /// <summary>A long gun's loaded magazine.</summary>
        public int Magazine;

        public static LootData LongGun(FirearmDef def, int magazine) =>
            new LootData { Kind = LootKind.LongGun, Weapon = def.id, Amount = 1, Magazine = magazine };

        public static LootData AmmoBox(AmmoType type, int amount) => new LootData { Kind = LootKind.Ammo, Ammo = type, Amount = amount };

        public static LootData Single(LootKind kind) => new LootData { Kind = kind, Amount = 1 };

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case LootKind.LongGun: return Weapon.ToString();
                    case LootKind.Ammo: return $"{Ammo} ammo ×{Amount}";
                    case LootKind.Health: return "Health Item";
                    case LootKind.C4: return "C4";
                    case LootKind.Vaccine: return "Vaccine";
                    default: return "Grenade";
                }
            }
        }
    }

    /// <summary>
    /// Something on the ground that can be picked up (D-032): a long gun, an ammo box (one type, D-141),
    /// a Health Item or a grenade. Dropped items stay until the stage ends.
    /// </summary>
    public class LootItem : MonoBehaviour
    {
        public static readonly List<LootItem> All = new List<LootItem>();
        /// <summary>Raised when an ammo box that respawns (D-206) is emptied.</summary>
        public static event System.Action<LootItem> Emptied;

        public LootData Data;
        public int NetId;
        /// <summary>Bumped on every change to ground loot, so the Host only resends it when something changed.</summary>
        public static int Version;
        public LootKind Kind => Data.Kind;
        public WeaponId Weapon => Data.Weapon;
        public AmmoType Ammo => Data.Ammo;
        public int Amount => Data.Amount;
        public int Magazine => Data.Magazine;
        public bool Respawns { get; set; }
        public string Label => Data.Label;

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

        public void Consume()
        {
            Data.Amount = 0;
            Destroy(gameObject);
            gameObject.SetActive(false);
        }

        /// <summary>Takes part of an ammo box; the rest stays (PLACEHOLDER rule).</summary>
        public void Take(int amount)
        {
            Data.Amount -= amount;
            Version++;
            if (Data.Amount > 0) return;
            if (Respawns) Emptied?.Invoke(this);
            Consume();
        }

        // ---- Spawning ----

        private static Transform Parent => GameSession.Instance != null ? GameSession.Instance.StageRoot : null;

        private static LootItem Create(Vector3 position, LootData data)
        {
            var go = new GameObject($"Loot {data.Kind}");
            go.transform.SetParent(Parent, false);
            go.transform.position = new Vector3(position.x, 0f, position.z);
            var item = go.AddComponent<LootItem>();
            item.Data = data;
            item.NetId = NetIds.Next();
            // Owner, 2026-10-03: every item has a small glowing circle so it is easy to spot.
            GlowRing(go.transform, RingColor(data));
            return item;
        }

        private static readonly Dictionary<Color, Mesh> RingMeshes = new Dictionary<Color, Mesh>();

        private static Color RingColor(LootData data)
        {
            switch (data.Kind)
            {
                case LootKind.Ammo: return AmmoColor(data.Ammo);
                case LootKind.LongGun: return AmmoColor(GameTuning.Load().Firearm(data.Weapon).ammo);
                case LootKind.Health: return new Color(1f, 0.4f, 0.4f);
                case LootKind.C4: return new Color(1f, 0.3f, 0.2f);
                case LootKind.Vaccine: return new Color(0.35f, 1f, 0.45f);
                default: return new Color(0.55f, 0.95f, 0.45f);
            }
        }

        /// <summary>A flat glowing ring with a faint fill, lying just above the ground (one mesh per colour, shared).</summary>
        public static GameObject GlowRing(Transform parent, Color color)
        {
            if (!RingMeshes.TryGetValue(color, out var mesh) || mesh == null)
            {
                var kit = new Art.MeshKit();
                const int n = 24;
                const float inner = 0.38f, outer = 0.47f, y = 0.015f;
                var ring = Art.MeshKit.Glow(color, 0.85f);
                var fill = Art.MeshKit.Glow(color * 0.35f, 0.4f);
                for (var i = 0; i < n; i++)
                {
                    var a0 = i / (float)n * Mathf.PI * 2f;
                    var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                    Vector3 P(float r, float a) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    kit.Quad(P(inner, a0), P(inner, a1), P(outer, a1), P(outer, a0), ring);
                    kit.Tri(new Vector3(0f, y * 0.8f, 0f), P(inner, a1), P(inner, a0), fill);
                }
                mesh = kit.ToMesh("Item ring");
                RingMeshes[color] = mesh;
            }
            var go = new GameObject("Glow ring");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Art.ArtMaterials.Stylized;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>The owner's pack model for an item, and its size (2026-10-04). Null id: none, keep the placeholder.</summary>
        public static (string id, float size, bool flat) ModelFor(LootData data)
        {
            switch (data.Kind)
            {
                case LootKind.LongGun:
                    switch (data.Weapon)
                    {
                        case WeaponId.Shotgun: return ("shotgun", 1.3f, true);
                        case WeaponId.Rifle: return ("sniper_gun", 1.6f, true);
                        case WeaponId.SMG: return ("smg_gun", 1.0f, true);
                        default: return ("ar_gun", 1.4f, true);
                    }
                case LootKind.Ammo:
                    switch (data.Ammo)
                    {
                        case AmmoType.Shotgun: return ("shotgun_magazine", 0.42f, false);
                        case AmmoType.Rifle: return ("sniper_magazine", 0.42f, false);
                        case AmmoType.SMG: return ("smg_magazine", 0.42f, false);
                        default: return ("ar_magazine", 0.42f, false);
                    }
                case LootKind.Health: return ("medical", 0.55f, false);
                case LootKind.Grenade: return ("grenade", 0.32f, false);
                case LootKind.C4: return ("c4", 0.5f, false);
                case LootKind.Vaccine: return ("syringe", 0.55f, false);
                default: return (null, 0f, false);
            }
        }

        /// <summary>Replaces an item's placeholder shapes with its pack model, if the pack has one.</summary>
        private static void UsePackModel(LootItem item)
        {
            var (id, size, flat) = ModelFor(item.Data);
            if (id == null || !Art.PackArt.Has(id)) return;
            foreach (Transform child in item.transform)
                if (child.name != "Glow ring" && child.name != "Beacon") Object.Destroy(child.gameObject);
            Art.PackArt.Show(item.transform, id, size, Vector3.zero, 0f, flat);
        }

        public static LootItem Spawn(Vector3 position, LootData data, bool respawns = false)
        {
            switch (data.Kind)
            {
                case LootKind.LongGun: return SpawnLongGun(position, GameTuning.Load().Firearm(data.Weapon), data.Magazine);
                case LootKind.Ammo: return SpawnAmmo(position, data.Ammo, data.Amount, respawns);
                default: return Spawn(position, data.Kind);
            }
        }

        public static LootItem Spawn(Vector3 position, LootKind kind)
        {
            var item = Create(position, LootData.Single(kind));
            if (kind == LootKind.Health)
            {
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.45f, 0.3f, 0.45f), Color.white);
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.36f, 0f), new Vector3(0.35f, 0.02f, 0.1f), Color.red);
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.36f, 0f), new Vector3(0.1f, 0.02f, 0.35f), Color.red);
            }
            else if (kind == LootKind.C4)
            {
                // ⚠ Placeholder: a charge block with a red light, on a beacon so it can be found again.
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.22f, 0.32f), new Color(0.75f, 0.68f, 0.5f));
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.22f, 0.05f, 0.14f), new Color(0.1f, 0.1f, 0.1f));
                Primitives.Shape(PrimitiveType.Sphere, item.transform, new Vector3(0.08f, 0.3f, 0f), Vector3.one * 0.07f, new Color(1f, 0.15f, 0.1f));
                MissionBeacon(item.transform, new Color(1f, 0.3f, 0.2f, 0.35f));
            }
            else if (kind == LootKind.Vaccine)
            {
                // ⚠ Placeholder: a glowing vial in a white case.
                Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.5f, 0.22f, 0.3f), new Color(0.9f, 0.92f, 0.95f));
                Primitives.Shape(PrimitiveType.Cylinder, item.transform, new Vector3(0f, 0.34f, 0f), new Vector3(0.12f, 0.12f, 0.12f), new Color(0.35f, 1f, 0.45f));
                MissionBeacon(item.transform, new Color(0.35f, 1f, 0.45f, 0.35f));
            }
            else
            {
                Primitives.Shape(PrimitiveType.Sphere, item.transform, new Vector3(0f, 0.2f, 0f), Vector3.one * 0.35f, new Color(0.25f, 0.4f, 0.2f));
            }
            item.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            UsePackModel(item);
            return item;
        }

        /// <summary>A tall thin light column over a mission item, so it reads from far away.</summary>
        private static void MissionBeacon(Transform parent, Color color)
        {
            var beam = Primitives.Shape(PrimitiveType.Cylinder, parent, new Vector3(0f, 1.6f, 0f), new Vector3(0.08f, 1.6f, 0.08f), color);
            beam.name = "Beacon";
        }

        /// <param name="yaw">The turn on the ground (null: random); map loot passes one that keeps it clear of walls.</param>
        public static LootItem SpawnLongGun(Vector3 position, FirearmDef def, int magazine, float? yaw = null)
        {
            var item = Create(position, LootData.LongGun(def, magazine));
            var length = def.id == WeaponId.Rifle ? 1.2f : def.id == WeaponId.Shotgun ? 0.9f : def.id == WeaponId.SMG ? 0.7f : 1f;
            Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.1f, 0f), new Vector3(0.18f, 0.12f, length), new Color(0.1f, 0.1f, 0.1f));
            Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.18f, -length * 0.3f), new Vector3(0.2f, 0.06f, 0.3f), AmmoColor(def.ammo));
            item.transform.rotation = Quaternion.Euler(0f, yaw ?? Random.Range(0f, 360f), 0f);
            UsePackModel(item);
            return item;
        }

        public static LootItem SpawnAmmo(Vector3 position, AmmoType type, int amount, bool respawns)
        {
            var item = Create(position, LootData.AmmoBox(type, amount));
            item.Respawns = respawns;
            Primitives.Shape(PrimitiveType.Cube, item.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.5f, 0.3f, 0.35f), AmmoColor(type));
            item.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            UsePackModel(item);
            return item;
        }

        /// <summary>Each ammo type reads differently from above (Handoff 3 §1).</summary>
        public static Color AmmoColor(AmmoType type)
        {
            switch (type)
            {
                case AmmoType.Shotgun: return new Color(0.85f, 0.25f, 0.2f);
                case AmmoType.AR: return new Color(0.2f, 0.5f, 0.9f);
                case AmmoType.Rifle: return new Color(0.9f, 0.7f, 0.15f);
                case AmmoType.SMG: return new Color(0.6f, 0.35f, 0.85f);
                default: return Color.gray;
            }
        }
    }

    /// <summary>
    /// Gas/fuel barrel: explodes when shot or caught in an explosion (owner, 2026-09-29). The blast hurts
    /// zombies, bosses and teammates (D-115) and makes noise (D-055).
    /// </summary>
    public class Barrel : MonoBehaviour, IDamageable
    {
        public static readonly List<Barrel> All = new List<Barrel>();
        /// <summary>The barrel's index in the stage layout: the same on every device.</summary>
        public int NetId;
        private BarrelDef _def;
        private float _fuse = -1f;

        public bool IsDead { get; private set; }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Online client: the Host says this barrel blew up.</summary>
        public void RemoveReplica()
        {
            IsDead = true;
            Destroy(gameObject);
        }

        public static Barrel Spawn(Transform parent, Vector3 position, BarrelDef def)
        {
            var go = new GameObject("Barrel") { layer = Layers.Barrel };
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var barrel = go.AddComponent<Barrel>();
            barrel._def = def;
            // The owner's fuel barrel model when the pack has it (2026-10-04).
            if (Art.PackArt.Show(go.transform, "fuel", 1.05f, Vector3.zero) == null)
            {
            Primitives.Shape(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.7f, 0.5f, 0.7f), new Color(0.8f, 0.15f, 0.1f));
            Primitives.Shape(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 1.01f, 0f), new Vector3(0.5f, 0.01f, 0.5f), new Color(0.95f, 0.85f, 0.2f));
            }
            BlobShadow.Attach(go.transform, 0.9f, 0.9f);
            // The hitbox reaches up into the bullets' flight height (up to 1.1 m) like the zombies' (owner, 2026-10-07:
            // the fuel barrel, 1 m tall, couldn't be hit); the barrel is drawn and blocks the way at its own size.
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f;
            capsule.height = Zombie.MinHitHeight;
            capsule.center = new Vector3(0f, Zombie.MinHitHeight * 0.5f, 0f);
            var obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Capsule;
            obstacle.radius = 0.4f;
            obstacle.height = 1f;
            obstacle.center = new Vector3(0f, 0.5f, 0f);
            return barrel;
        }

        public void TakeDamage(float damage, Vector3 from, bool fromExplosion)
        {
            if (IsDead || _fuse >= 0f || !World.Simulating) return;
            if (fromExplosion) _fuse = _def.chainDelay;
            else Explode();
        }

        private void Update()
        {
            if (_fuse < 0f || IsDead || !World.Simulating) return;
            _fuse -= Time.deltaTime;
            if (_fuse <= 0f) Explode();
        }

        private void Explode()
        {
            if (IsDead) return;
            IsDead = true;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            NetSync.BarrelGone(NetId);
            Explosion.Detonate(transform.position, _def.damage, _def.radius, _def.noiseRange, _def.survivorShare);
            Destroy(gameObject);
        }
    }

    /// <summary>A thrown grenade: flies to the marker, then explodes 2 s after the throw (owner, 2026-09-29).</summary>
    public class Grenade : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private GrenadeDef _def;
        private float _t;

        public static void Throw(Vector3 from, Vector3 to, GrenadeDef def)
        {
            NetSync.GrenadeThrown(from, to);
            Spawn(from, to, def, live: true);
        }

        /// <summary>Online client: a grenade the Host threw — it flies, but the Host does the explosion.</summary>
        public static void ThrowReplica(Vector3 from, Vector3 to, GrenadeDef def) => Spawn(from, to, def, live: false);

        private bool _live;

        private static void Spawn(Vector3 from, Vector3 to, GrenadeDef def, bool live)
        {
            var go = Primitives.Shape(PrimitiveType.Sphere, GameSession.Instance != null ? GameSession.Instance.StageRoot : null,
                from, Vector3.one * 0.25f, new Color(0.2f, 0.35f, 0.15f));
            go.name = "Grenade";
            go.transform.position = from;
            var grenade = go.AddComponent<Grenade>();
            grenade._from = from;
            grenade._to = to + Vector3.up * 0.12f;
            grenade._def = def;
            grenade._live = live;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            var k = Mathf.Clamp01(_t / Mathf.Max(0.01f, _def.flightTime));
            var arc = Mathf.Sin(k * Mathf.PI) * Mathf.Lerp(1.5f, 3f, Vector3.Distance(_from, _to) / Mathf.Max(1f, _def.maxRange));
            transform.position = Vector3.Lerp(_from, _to, k) + Vector3.up * arc;
            if (_t < _def.fuse) return;
            if (_live && World.Simulating) Explosion.Detonate(_to, _def.damage, _def.radius, _def.noiseRange, _def.survivorShare);
            Destroy(gameObject);
        }
    }
}
