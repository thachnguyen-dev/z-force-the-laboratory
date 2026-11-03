using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The stage's zombie population (2026-10-03, owner: don't put hundreds of zombie objects in the scene up front).
    ///
    /// Zombies far from every survivor are <b>dormant</b>: a small record (type, position, HP, where it is heading), no
    /// GameObject, no NavMesh agent, no rendering. Every half second the records are stepped along cheaply (towards a
    /// sound they heard, the Boss Area when the boss calls, a hunted player) and any record that comes within
    /// <see cref="SpawnTuning.activateRange"/> of a survivor becomes a real zombie, nearest first, up to
    /// <see cref="SpawnTuning.maxActive"/>. A real zombie that is idle (or only investigating) and further than
    /// <see cref="SpawnTuning.deactivateRange"/> from everyone for a few seconds goes back to being a record.
    ///
    /// Real zombies come from a pool per type: they are built once and reused, so play never creates or destroys
    /// zombie objects (death returns them to the pool after the death animation). Host-side only; the online client
    /// only ever sees the active zombies.
    /// </summary>
    public class ZombiePopulation : MonoBehaviour
    {
        public class Dormant
        {
            public ZombieType Type;
            public Vector3 Position;
            public Vector3 Home;
            public float Hp;
            public Vector3? Destination;
            public float Speed;
        }

        private readonly List<Dormant> _dormant = new List<Dormant>();
        private readonly Dictionary<ZombieType, Stack<Zombie>> _pool = new Dictionary<ZombieType, Stack<Zombie>>();
        private readonly Dictionary<Zombie, float> _farFor = new Dictionary<Zombie, float>();
        private readonly List<Survivor> _observers = new List<Survivor>();
        private StageRuntime _stage;
        private GameTuning _tuning;
        private float _tickLeft;

        public int DormantCount => _dormant.Count;
        public int ActiveCount => Zombie.Alive.Count;
        public int Total => DormantCount + ActiveCount;
        public IReadOnlyList<Dormant> Records => _dormant;

        public void Setup(StageRuntime stage, GameTuning tuning)
        {
            _stage = stage;
            _tuning = tuning;
            Noise.Emitted += OnNoise;
        }

        private void OnDestroy() => Noise.Emitted -= OnNoise;

        // ------------------------------------------------------------------ adding

        /// <summary>
        /// Adds a zombie to the population: real right away if a survivor is near (and there is room), else dormant.
        /// Returns the real zombie, or null when it went dormant or the population is full.
        /// </summary>
        public Zombie Add(ZombieType type, Vector3 position, Vector3? heading = null)
        {
            if (Total >= _stage.MaxPopulation) return null;
            // Near a survivor it is real at once, unless a player would see it appear (owner, 2026-10-09): then it waits dormant.
            if (Zombie.Alive.Count < _stage.MaxActive && NearestObserver(position) <= _tuning.spawn.activateRange && _stage.MayAppearAt(position))
            {
                var zombie = _stage.SpawnZombie(type, position);
                if (zombie != null && heading.HasValue) zombie.Hear(heading.Value);
                if (zombie != null) return zombie;
            }
            // Ground only (the NavMesh also covers the roofs of props, out of reach), as for real zombies.
            if (!NavMesh.SamplePosition(position, out var hit, 3f, NavAreas.WalkMask) || hit.position.y > 0.3f) return null;
            // Owner, 2026-10-11: never waiting inside a wall or an obstacle.
            var spot = hit.position;
            if (!StageRuntime.ClearSpot(ref spot, 0.3f)) return null;
            var def = _stage.Def(type);
            _dormant.Add(new Dormant { Type = type, Position = spot, Home = spot, Hp = def.hp, Destination = heading, Speed = def.wanderSpeed });
            return null;
        }

        /// <summary>Tests and dev tools: forget every dormant zombie.</summary>
        public void ClearDormant() => _dormant.Clear();

        // ------------------------------------------------------------------ pool

        internal Zombie Take(ZombieType type)
        {
            if (!_pool.TryGetValue(type, out var stack)) return null;
            while (stack.Count > 0)
            {
                var z = stack.Pop();
                if (z != null) return z;
            }
            return null;
        }

        internal void Release(Zombie zombie)
        {
            if (zombie == null) return;
            _farFor.Remove(zombie);
            zombie.gameObject.SetActive(false);
            if (!_pool.TryGetValue(zombie.Def.type, out var stack)) _pool[zombie.Def.type] = stack = new Stack<Zombie>();
            stack.Push(zombie);
        }

        // ------------------------------------------------------------------ calls from the game

        /// <summary>The Main Boss calls the horde (owner, 2026-10-03): dormant zombies head for the Boss Area too.</summary>
        public void CallTo(Vector3 center, float spread)
        {
            foreach (var d in _dormant)
            {
                var offset = Random.insideUnitCircle * spread;
                d.Destination = center + new Vector3(offset.x, 0f, offset.y);
                d.Speed = Mathf.Lerp(_stage.Def(d.Type).wanderSpeed, _stage.Def(d.Type).chaseSpeed, 0.6f);
            }
        }

        /// <summary>Sounds reach dormant zombies like real ones; they start walking towards them.</summary>
        private void OnNoise(Vector3 position, float range)
        {
            foreach (var d in _dormant)
            {
                var mult = _stage.Def(d.Type).senseMultiplier;
                if ((d.Position - position).sqrMagnitude > range * range * mult * mult) continue;
                d.Destination = position;
                d.Speed = Mathf.Lerp(_stage.Def(d.Type).wanderSpeed, _stage.Def(d.Type).chaseSpeed, 0.6f);
            }
        }

        // ------------------------------------------------------------------ the tick

        private void Update()
        {
            if (!World.Simulating || _stage == null || (GameSession.Instance != null && !GameSession.Instance.Playing)) return;
            _tickLeft -= Time.deltaTime;
            if (_tickLeft > 0f) return;
            var step = 0.5f - _tickLeft;
            _tickLeft = 0.5f;
            CollectObservers();
            if (_observers.Count == 0) return;
            MoveDormant(step);
            Activate();
            Deactivate(step);
        }

        /// <summary>Who makes zombies real: every living survivor except a hostage still waiting.</summary>
        private void CollectObservers()
        {
            _observers.Clear();
            foreach (var s in Survivor.All)
                if (!s.IsDead && !(s.IsHostage && !s.IsFreed)) _observers.Add(s);
        }

        private float NearestObserver(Vector3 p)
        {
            var best = float.MaxValue;
            var list = _observers.Count > 0 ? (IEnumerable<Survivor>)_observers : Survivor.All;
            foreach (var s in list)
            {
                if (s == null || s.IsDead || (s.IsHostage && !s.IsFreed)) continue;
                var d = s.transform.position - p;
                d.y = 0f;
                best = Mathf.Min(best, d.magnitude);
            }
            return best;
        }

        private void MoveDormant(float dt)
        {
            var hunted = Zombie.Hunted != null && !Zombie.Hunted.IsDead ? Zombie.Hunted : null;
            foreach (var d in _dormant)
            {
                // Rescue stages: every zombie hunts the player who freed the hostage (owner, 2026-09-30).
                if (hunted != null)
                {
                    d.Destination = hunted.transform.position;
                    d.Speed = _stage.Def(d.Type).chaseSpeed * 0.8f;
                }
                if (!d.Destination.HasValue) continue;
                var to = d.Destination.Value - d.Position;
                to.y = 0f;
                var move = d.Speed * dt;
                if (to.magnitude <= move)
                {
                    d.Position = d.Destination.Value;
                    d.Destination = null;
                    d.Speed = _stage.Def(d.Type).wanderSpeed;
                }
                else d.Position += to.normalized * move;
            }
        }

        private readonly List<(float distance, Dormant record)> _candidates = new List<(float, Dormant)>();

        private void Activate()
        {
            var room = _stage.MaxActive - Zombie.Alive.Count;
            if (room <= 0 || _dormant.Count == 0) return;
            _candidates.Clear();
            foreach (var d in _dormant)
            {
                var distance = NearestObserver(d.Position);
                // Owner, 2026-10-09: never wakes on a player's screen (it wakes once out of view).
                if (distance <= _tuning.spawn.activateRange && _stage.MayAppearAt(d.Position)) _candidates.Add((distance, d));
            }
            if (_candidates.Count == 0) return;
            _candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < _candidates.Count && room > 0; i++)
            {
                var d = _candidates[i].record;
                // Its straight-line walk may have crossed a wall: snap back onto the NavMesh.
                if (!NavMesh.SamplePosition(d.Position, out var hit, 4f, NavAreas.WalkMask))
                {
                    _dormant.Remove(d);
                    continue;
                }
                var zombie = _stage.SpawnZombie(d.Type, hit.position, d.Hp, d.Home);
                _dormant.Remove(d);
                if (zombie == null) continue;
                if (d.Destination.HasValue) zombie.Hear(d.Destination.Value);
                room--;
            }
        }

        private void Deactivate(float dt)
        {
            var range = _tuning.spawn.deactivateRange;
            foreach (var z in Zombie.Alive.ToArray())
            {
                if (z.IsMainBoss || ZombieTypes.IsBoss(z.Def.type) || z.Replica) continue;
                var calm = z.Mind.Mode == ZombieMode.Idle || z.Mind.Mode == ZombieMode.Search || z.Mind.Mode == ZombieMode.Investigate;
                if (!calm || z.Target != null || NearestObserver(z.transform.position) <= range)
                {
                    _farFor.Remove(z);
                    continue;
                }
                _farFor.TryGetValue(z, out var t);
                t += dt;
                _farFor[z] = t;
                if (t < 3f) continue;
                _dormant.Add(new Dormant
                {
                    Type = z.Def.type, Position = z.transform.position, Home = z.Home, Hp = z.Hp, Destination = z.Heading,
                    Speed = z.Mind.Mode == ZombieMode.Investigate ? Mathf.Lerp(z.Def.wanderSpeed, z.Def.chaseSpeed, 0.6f) : z.Def.wanderSpeed,
                });
                Release(z);
            }
        }
    }
}
