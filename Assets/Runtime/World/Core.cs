using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>Physics layers. Indices are fixed; the editor setup names them in the Tag Manager.</summary>
    public static class Layers
    {
        public const int Ground = 0;
        public const int Obstacle = 8;
        public const int Survivor = 9;
        public const int Zombie = 10;
        public const int Barrel = 12;
        /// <summary>Obstacles you can't walk through but can shoot and see through (the owner's pack, 2026-10-04).</summary>
        public const int ShootThrough = 11;

        /// <summary>What blocks sight (D-051) and bullets.</summary>
        public const int ObstacleMask = 1 << Obstacle;
        public const int ShotMask = (1 << Obstacle) | (1 << Zombie) | (1 << Barrel);
        public const int ExplosionTargets = (1 << Survivor) | (1 << Zombie) | (1 << Barrel);
    }

    public interface IDamageable
    {
        /// <summary><paramref name="fromExplosion"/> lets barrels chain with a short delay.</summary>
        void TakeDamage(float damage, Vector3 from, bool fromExplosion);
        bool IsDead { get; }
    }

    /// <summary>⚠ Placeholder greybox shapes. One URP material asset per colour, shared (SRP Batcher-friendly).</summary>
    public static class Primitives
    {
        public const string BaseMaterialResource = "ZombieBase";
        private static Material _base;
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        public static Material Material(Color color)
        {
            if (Materials.TryGetValue(color, out var material) && material != null) return material;
            if (_base == null) _base = Resources.Load<Material>(BaseMaterialResource);
            if (_base == null) _base = new Material(Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard"));
            material = new Material(_base) { color = color };
            if (color.a < 1f) MakeTransparent(material);
            Materials[color] = material;
            return material;
        }

        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color,
            bool keepCollider = false, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            if (name != null) go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Material(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }
    }

    /// <summary>
    /// Sound events for zombie hearing (D-149–D-156). Not blocked by walls; every zombie in range hears it and
    /// investigates the newest one.
    /// </summary>
    public static class Noise
    {
        public static event System.Action<Vector3, float> Emitted;

        public static void Emit(Vector3 position, float range)
        {
            // Rain shortens how far sounds carry (owner, 2026-09-30).
            range *= StageConditions.NoiseMultiplier;
            // Zombies on alert hear from further (owner, 2026-10-03). Only zombies listen to Noise.
            range *= StageConditions.ZombieDetectionMultiplier;
            if (range <= 0f) return;
            var sqr = range * range;
            foreach (var zombie in Zombie.Alive)
                // The Dog hears 50 % further (owner, 2026-10-03).
                if ((zombie.transform.position - position).sqrMagnitude <= sqr * zombie.Def.senseMultiplier * zombie.Def.senseMultiplier) zombie.Hear(position);
            Emitted?.Invoke(position, range);
        }
    }

    public static class Explosion
    {
        private static readonly Collider[] Hits = new Collider[128];
        private static readonly HashSet<IDamageable> Done = new HashSet<IDamageable>();

        /// <summary>
        /// D-113: damage falls off with distance. Hurts survivors (D-115 — teammates too), zombies, bosses
        /// and barrels. PLACEHOLDER: walls block the blast.
        /// </summary>
        /// <param name="survivorShare">The share of the damage players and AI take (owner, 2026-10-09: 30 % from grenades and fuel barrels).</param>
        public static void Detonate(Vector3 center, float damage, float radius, float noiseRange, float survivorShare = 1f)
        {
            Done.Clear();
            var count = Physics.OverlapSphereNonAlloc(center, radius, Hits, Layers.ExplosionTargets, QueryTriggerInteraction.Collide);
            var eye = center + Vector3.up * 0.6f;
            for (var i = 0; i < count; i++)
            {
                var target = Hits[i].GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead || !Done.Add(target)) continue;
                var targetPoint = Hits[i].bounds.center;
                if (StageGrid.SightBlocked(eye, targetPoint)) continue;
                var distance = Vector3.Distance(new Vector3(center.x, 0f, center.z), new Vector3(targetPoint.x, 0f, targetPoint.z));
                var amount = damage * Mathf.Clamp01(1f - distance / radius);
                if (target is Survivor) amount *= survivorShare;
                if (amount > 0f) target.TakeDamage(amount, center, true);
            }
            Noise.Emit(center, noiseRange);
            Sfx.Play(SoundId.Explosion, center);
            ExplosionFx.Spawn(center, radius);
        }
    }

    /// <summary>⚠ Placeholder explosion visual: an expanding, fading sphere.</summary>
    public class ExplosionFx : MonoBehaviour
    {
        private float _t;
        private float _radius;

        public static void Spawn(Vector3 center, float radius)
        {
            NetSync.Explosion(center, radius);
            var go = Primitives.Shape(PrimitiveType.Sphere, null, center + Vector3.up * 0.5f, Vector3.one, new Color(1f, 0.55f, 0.1f, 0.6f));
            go.name = "ExplosionFx";
            var fx = go.AddComponent<ExplosionFx>();
            fx._radius = radius;
            if (GameSession.Instance != null && GameSession.Instance.StageRoot != null) go.transform.SetParent(GameSession.Instance.StageRoot, true);
        }

        private void Update()
        {
            _t += Time.deltaTime / 0.35f;
            transform.localScale = Vector3.one * (_radius * 2f * Mathf.Lerp(0.3f, 1f, _t));
            if (_t >= 1f) Destroy(gameObject);
        }
    }
}
