using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The hostage in a rescue stage (owner, 2026-09-30): waits until a player reaches them, then follows that player
    /// automatically. Reaching them makes every zombie — the Main Boss too — hunt that player.
    /// Carries no weapons and never picks anything up.
    /// </summary>
    public class HostageBrain : MonoBehaviour, ICharacterInputSource
    {
        /// <summary>
        /// What the scientist and the player say before the scientist follows (owner, 2026-10-08: only after approaching and
        /// talking). PLACEHOLDER lines; the speaker first.
        /// </summary>
        public static readonly (string who, string line)[] Dialogue =
        {
            ("Scientist", "You came! I thought nobody would ever get through to this place."),
            ("You", "We're here to get you out. Stay close and keep your head down."),
            ("Scientist", "I'll follow you. Please, just get me to the helicopter."),
        };

        public Survivor Leader { get; private set; }
        public bool Freed => Leader != null || _freed;

        /// <summary>Raised once, with the player who reached the hostage.</summary>
        public event System.Action<Survivor> FreedBy;

        private Survivor _self;
        private bool _freed;
        private NavMeshPath _path;
        private float _repathLeft;
        private int _corner;

        private void Awake()
        {
            _self = GetComponent<Survivor>();
            _path = new NavMeshPath();
        }

        public CharacterInput Read(Survivor self)
        {
            _self = self;
            var input = new CharacterInput { GrenadeDistance = -1f };
            var rules = self.Tuning.rescue;
            if (!Freed)
            {
                // Owner, 2026-10-03: a player next to the hostage must press Rescue before they follow.
                return input;
            }
            // If the player they follow dies, follow the other one.
            if (Leader == null || Leader.IsDead) Leader = NearestHuman();
            if (Leader == null) return input;
            var to = Flat(Leader.transform.position - self.transform.position);
            if (to.magnitude <= rules.followDistance) return input;
            input.Move = Steer(self.transform.position, Leader.transform.position);
            input.Aim = input.Move;
            return input;
        }

        /// <summary>A player close enough to rescue the hostage (the Rescue button shows).</summary>
        public bool CanFree(Survivor player)
        {
            if (Freed || _self == null || player == null || !player.IsHuman || player.IsDead || player.Team != Survivor.PlayerTeam) return false;
            return Flat(player.transform.position - _self.transform.position).magnitude <= _self.Tuning.rescue.reachDistance;
        }

        /// <summary>After host migration: an already rescued hostage keeps following.</summary>
        public void Resume(Survivor player)
        {
            Leader = player;
            _freed = true;
        }

        /// <summary>Host: the player pressed Rescue; the hostage follows them, and every zombie hunts them.</summary>
        public void Free(Survivor player)
        {
            if (!CanFree(player)) return;
            Leader = player;
            _freed = true;
            FreedBy?.Invoke(player);
        }

        private Survivor NearestHuman()
        {
            Survivor best = null;
            var bestSqr = float.MaxValue;
            foreach (var s in Survivor.All)
            {
                if (!s.IsHuman || s.IsDead) continue;
                var d = (s.transform.position - _self.transform.position).sqrMagnitude;
                if (d >= bestSqr) continue;
                bestSqr = d;
                best = s;
            }
            return best;
        }

        private Vector2 Steer(Vector3 from, Vector3 goal)
        {
            _repathLeft -= Time.deltaTime;
            if (_repathLeft <= 0f)
            {
                _repathLeft = 0.4f;
                _corner = 1;
                if (!NavMesh.CalculatePath(from, goal, NavAreas.WalkMask, _path)) _path.ClearCorners();
            }
            var corners = _path.corners;
            while (_corner < corners.Length && Flat(corners[_corner] - from).magnitude < 0.4f) _corner++;
            var next = _corner < corners.Length ? corners[_corner] : goal;
            var d = Flat(next - from);
            if (d.sqrMagnitude < 0.0001f) return Vector2.zero;
            d.Normalize();
            return new Vector2(d.x, d.z);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
