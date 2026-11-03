using UnityEngine;

namespace ZombieGame
{
    public enum ZombieMode
    {
        /// <summary>Normal behaviour: standing or wandering near home.</summary>
        Idle,
        /// <summary>Walking to a sound (D-052).</summary>
        Investigate,
        /// <summary>Searching around a sound or the last known position (D-052, D-054).</summary>
        Search,
        /// <summary>Sees a target and chases it (D-053).</summary>
        Chase,
        /// <summary>Lost sight; walking to the last known position (D-054).</summary>
        LostTarget,
    }

    /// <summary>
    /// A zombie's decision state, independent of movement and physics so the rules can be tested:
    /// vision beats sound (D-056), the newest sound wins (D-154), search a fixed radius for a fixed time
    /// then return to normal (D-052).
    /// </summary>
    public class ZombieMind
    {
        public ZombieMode Mode { get; private set; } = ZombieMode.Idle;
        /// <summary>Where the zombie wants to go now.</summary>
        public Vector3 Destination { get; private set; }
        public Vector3 LastKnown { get; private set; }
        public Vector3 SearchCenter { get; private set; }
        /// <summary>Bumped whenever the destination changes, so movement only re-paths when needed.</summary>
        public int DestinationVersion { get; private set; }

        private readonly PerceptionTuning _rules;
        private readonly System.Random _rng;
        private float _searchLeft;
        private float _nextSearchPoint;

        public ZombieMind(PerceptionTuning rules, int seed)
        {
            _rules = rules;
            _rng = new System.Random(seed);
        }

        public void See(Vector3 targetPosition)
        {
            LastKnown = targetPosition;
            Mode = ZombieMode.Chase;
            SetDestination(targetPosition);
        }

        /// <summary>A sound never reveals the player (D-056): it only matters when the zombie isn't seeing anyone.</summary>
        public void Hear(Vector3 soundPosition)
        {
            if (Mode == ZombieMode.Chase) return;
            Mode = ZombieMode.Investigate;
            SetDestination(soundPosition);
        }

        public void LoseSight()
        {
            if (Mode != ZombieMode.Chase) return;
            Mode = ZombieMode.LostTarget;
            SetDestination(LastKnown);
        }

        /// <summary>The movement layer reached <see cref="Destination"/>.</summary>
        public void Arrived()
        {
            if (Mode == ZombieMode.Investigate || Mode == ZombieMode.LostTarget)
            {
                Mode = ZombieMode.Search;
                SearchCenter = Destination;
                _searchLeft = _rules.searchDuration;
                _nextSearchPoint = 0f;
            }
            else if (Mode == ZombieMode.Search)
            {
                _nextSearchPoint = Mathf.Min(_nextSearchPoint, 0.5f);
            }
        }

        public void Tick(float dt)
        {
            if (Mode != ZombieMode.Search) return;
            _searchLeft -= dt;
            if (_searchLeft <= 0f)
            {
                Mode = ZombieMode.Idle;
                return;
            }
            _nextSearchPoint -= dt;
            if (_nextSearchPoint > 0f) return;
            _nextSearchPoint = 2f;
            var angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
            var radius = Mathf.Sqrt((float)_rng.NextDouble()) * _rules.searchRadius;
            SetDestination(SearchCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }

        public float SearchTimeLeft => Mode == ZombieMode.Search ? _searchLeft : 0f;

        private void SetDestination(Vector3 destination)
        {
            Destination = destination;
            DestinationVersion++;
        }
    }
}
