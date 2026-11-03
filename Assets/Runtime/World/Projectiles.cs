using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Every bullet in flight: real travelling projectiles (D-138), removed at the weapon's range (D-137),
    /// the Rifle's piercing without damage loss (D-136). Bullets never hit survivors (D-114).
    /// Pooled; stepped in FixedUpdate with sphere casts.
    /// </summary>
    public class Projectiles : MonoBehaviour
    {
        /// <summary>The share of a bullet's damage a survivor takes (GameTuning player.bulletDamageTaken).</summary>
        private static float BulletShare => GameSession.Instance != null && GameSession.Instance.Tuning != null
            ? GameSession.Instance.Tuning.player.bulletDamageTaken : 0.4f;
        private struct Shot
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float RangeLeft;
            public float Damage;
            public bool Pierce;
            /// <summary>The shooter's team; survivors of the other team are hit (−1: hits no survivor, e.g. a client's tracer).</summary>
            public int Team;
            public int Id;
            public Transform Visual;
            /// <summary>Where the tracer starts relative to the shot (the gun's real muzzle), and how far the shot has flown.</summary>
            public Vector3 VisualOffset;
            public float Flown;
        }

        /// <summary>Over this distance (m) the tracer glides from the muzzle onto the shot's line.</summary>
        public const float LiftDistance = 2f;

        /// <summary>
        /// Shots hit zombies, barrels and survivors whatever their height and whatever the shot's (owner, 2026-10-07:
        /// "fix every issue related to height ... must not occur again"): those are found with a standing shape from
        /// <see cref="HitBottom"/> to <see cref="HitTop"/> swept along the ground; walls and cover still stop the shot
        /// only where it actually meets them, at its own height.
        /// </summary>
        public const float HitBottom = 0.15f, HitTop = 2.2f;

        private const int Damageable = (1 << Layers.Zombie) | (1 << Layers.Barrel) | (1 << Layers.Survivor);
        private readonly RaycastHit[] _bodyHits = new RaycastHit[16];
        private readonly List<RaycastHit> _sorted = new List<RaycastHit>(32);

        public const float Radius = 0.08f;
        private static Projectiles _instance;
        private readonly List<Shot> _shots = new List<Shot>(256);
        private readonly Stack<Transform> _pool = new Stack<Transform>();
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly HashSet<long> _pierced = new HashSet<long>();
        private int _nextId;

        public static int InFlight => _instance != null ? _instance._shots.Count : 0;

        /// <summary>
        /// A shot from <paramref name="origin"/>; <paramref name="visualFrom"/>: where the gun's muzzle is relative to it
        /// (owner, 2026-10-07: the bullet starts at the muzzle), so the tracer starts there and glides onto the shot's line.
        /// </summary>
        /// <param name="shooter">Who fired (sent to the online friend, who draws their own shots at once).</param>
        /// <param name="sync">False for the friend's own picture of their shot (nothing to send).</param>
        public static void Fire(Vector3 origin, Vector3 direction, float speed, float range, float damage, bool pierce, int team = -1, Vector3 visualFrom = default,
            int shooter = -1, bool sync = true)
        {
            if (sync) NetSync.Shot(origin, direction, speed, range, shooter);
            if (_instance == null)
            {
                var root = GameSession.Instance != null ? GameSession.Instance.StageRoot : null;
                _instance = new GameObject("Projectiles").AddComponent<Projectiles>();
                if (root != null) _instance.transform.SetParent(root, false);
            }
            _instance.Add(origin, direction, speed, range, damage, pierce, team, visualFrom);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Add(Vector3 origin, Vector3 direction, float speed, float range, float damage, bool pierce, int team, Vector3 visualFrom)
        {
            direction.y = 0f;
            direction.Normalize();
            var visual = _pool.Count > 0 ? _pool.Pop() : MakeVisual();
            visual.gameObject.SetActive(true);
            visual.position = origin + visualFrom;
            visual.rotation = Quaternion.LookRotation(direction);
            _shots.Add(new Shot
            {
                Position = origin, Direction = direction, Speed = speed, RangeLeft = range, Damage = damage, Pierce = pierce,
                Id = _nextId++, Visual = visual, Team = team, VisualOffset = visualFrom,
            });
        }

        private Transform MakeVisual()
        {
            var go = Primitives.Shape(PrimitiveType.Cube, transform, Vector3.zero, new Vector3(0.06f, 0.06f, 0.5f), new Color(1f, 0.9f, 0.5f));
            go.name = "Tracer";
            return go.transform;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            for (var i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                var step = Mathf.Min(shot.Speed * dt, shot.RangeLeft);
                var alive = Advance(ref shot, step);
                shot.RangeLeft -= step;
                shot.Flown += step;
                if (!alive || shot.RangeLeft <= 0f)
                {
                    Release(shot);
                    _shots.RemoveAt(i);
                    continue;
                }
                _shots[i] = shot;
            }
        }

        private void Update()
        {
            // Visuals only: interpolate between physics steps is unnecessary at these speeds.
            foreach (var shot in _shots)
                shot.Visual.position = shot.Position + shot.VisualOffset * Mathf.Clamp01(1f - shot.Flown / LiftDistance);
        }

        /// <summary>Moves one shot; false when it stops.</summary>
        private bool Advance(ref Shot shot, float step)
        {
            // Walls and cover at the shot's own height; bodies and barrels at any height (HitBottom..HitTop).
            var walls = Physics.SphereCastNonAlloc(shot.Position, Radius, shot.Direction, _hits, step, 1 << Layers.Obstacle, QueryTriggerInteraction.Ignore);
            var ground = new Vector3(shot.Position.x, 0f, shot.Position.z);
            var bodies = Physics.CapsuleCastNonAlloc(ground + Vector3.up * (HitBottom + Radius), ground + Vector3.up * (HitTop - Radius), Radius,
                shot.Direction, _bodyHits, step, Damageable, QueryTriggerInteraction.Ignore);
            _sorted.Clear();
            for (var i = 0; i < walls; i++) _sorted.Add(_hits[i]);
            for (var i = 0; i < bodies; i++) _sorted.Add(_bodyHits[i]);
            _sorted.Sort((a, b) => a.distance.CompareTo(b.distance));
            var count = _sorted.Count;
            for (var h = 0; h < count; h++)
            {
                var hit = _sorted[h];
                var layer = hit.collider.gameObject.layer;
                if (layer == Layers.Obstacle)
                {
                    shot.Position += shot.Direction * hit.distance;
                    return false;
                }
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead) continue;
                // D-114 within a team; the other team is hit (owner, 2026-10-03: the Vaccine stage's rivals).
                if (target is Survivor survivor && (shot.Team < 0 || survivor.Team == shot.Team || survivor.IsHostage)) continue;
                if (!World.Simulating)
                {
                    // Online client: the tracer is only a picture of the Host's shot; it stops at the first body, which
                    // shows the hit at once (owner, 2026-10-08: lowering the lag; the Host decides the damage).
                    if (target is Zombie seen) seen.PreviewHit(shot.Direction);
                    if (shot.Pierce) continue;
                    shot.Position += shot.Direction * hit.distance;
                    return false;
                }
                // Owner, 2026-10-11: players and AI take only part of a bullet's damage.
                var damage = target is Survivor ? shot.Damage * BulletShare : shot.Damage;
                if (shot.Pierce)
                {
                    var key = ((long)shot.Id << 32) | (uint)hit.collider.GetInstanceID();
                    if (!_pierced.Add(key)) continue;
                    target.TakeDamage(damage, shot.Position, false);
                    if (target is Zombie pierced) pierced.HitStop();
                    continue;
                }
                target.TakeDamage(damage, shot.Position, false);
                if (target is Zombie zombie) zombie.HitStop();
                shot.Position += shot.Direction * hit.distance;
                return false;
            }
            shot.Position += shot.Direction * step;
            return true;
        }

        private void Release(Shot shot)
        {
            if (shot.Pierce) _pierced.RemoveWhere(k => (k >> 32) == shot.Id);
            shot.Visual.gameObject.SetActive(false);
            _pool.Push(shot.Visual);
        }
    }
}
