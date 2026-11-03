using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The AI teammate (Co-op, D-170). It drives an ordinary <see cref="Survivor"/> through the same inputs and commands
    /// a human uses (D-043): no separate combat system, no cheats. Priorities: survival → combat → loot (D-157).
    /// It decides its own movement (D-047), but never strays further than a fixed distance from the human (D-048, D-166).
    /// Nobody can give it orders (D-049).
    /// </summary>
    public class AiTeammate : MonoBehaviour, ICharacterInputSource
    {
        public Survivor Leader;
        /// <summary>
        /// The Vaccine stage's rival team (owner, 2026-10-03): no leader, attacks zombies and players, goes for the Vaccine
        /// first; whoever carries it runs from the players while the others guard it.
        /// </summary>
        public bool Rival;

        private Survivor _self;
        private GameTuning _t;
        private AiTuning _ai;
        private float _thinkLeft;
        private Transform _target;
        private readonly List<Zombie> _visible = new List<Zombie>();
        private Vector2 _aim = new Vector2(0f, 1f);

        // Movement along a NavMesh path.
        private NavMeshPath _path; // created in Awake: Unity forbids it in field initializers
        private Vector3 _pathGoal = new Vector3(float.NaN, 0f, 0f);
        private int _corner;
        private float _repathLeft;

        // Looting and grenades.
        private LootItem _loot;
        private Chest _chest;
        private float _grenadeCooldown;
        private bool _grenadePending;
        private Vector3 _grenadePoint;

        public string State { get; private set; } = "idle";

        private void Awake()
        {
            _self = GetComponent<Survivor>();
            _path = new NavMeshPath();
        }

        public CharacterInput Read(Survivor self)
        {
            _self = self;
            _t = self.Tuning;
            _ai = _t.ai;
            var input = new CharacterInput { GrenadeDistance = -1f };
            var dt = Time.deltaTime;
            _grenadeCooldown -= dt;

            _thinkLeft -= dt;
            if (_thinkLeft <= 0f)
            {
                _thinkLeft = _ai.thinkInterval;
                Perceive();
            }

            Vector3? goal = null;
            Vector3? face = null;
            var p = self.transform.position;
            var leaderDistance = Leader != null && !Leader.IsDead ? Flat(Leader.transform.position - p).magnitude : 0f;

            // ---- Survival ----
            var nearest = NearestThreatDistance();
            // D-163 (owner, 2026-10-09: below 50 %, whenever no zombie can reach it).
            if (self.Hp < self.MaxHp * _ai.healThreshold && self.Inventory.HealthItems > 0 && !self.Healing && nearest > _ai.healSafeDistance)
                self.UseHealthItem();
            var backOff = _target != null && nearest < _ai.keepAway;

            // ---- Combat ----
            if (_target != null && !self.Healing)
            {
                State = "fighting";
                face = _target.transform.position;
                if (!TryGrenade(ref input, ref face))
                {
                    ChooseGun();
                    var gun = self.ActiveFirearm;
                    if (gun != null && AimError(face.Value) < _ai.fireAngle) input.FireHeld = true;
                }
                if (backOff) goal = p + Flat(p - _target.transform.position).normalized * 3f;
            }
            else
            {
                _grenadePending = false;
                ChooseGun();
                var gun = self.ActiveFirearm;
                // Top up between fights.
                if (gun != null && gun.CanReload && gun.Magazine < gun.Def.magazine * 0.5f) input.Reload = true;
            }

            // ---- Rivals: the Vaccine comes first (owner, 2026-10-03); they keep shooting while they move ----
            if (Rival)
            {
                var rivalGoal = RivalGoal(p);
                // A zombie in its face still makes it step back, unless it carries the Vaccine (then it just runs).
                if (rivalGoal.HasValue && (!backOff || self.Inventory.Vaccine)) goal = rivalGoal;
            }

            // ---- Loot (only when nothing is threatening) ----
            if (_target == null && goal == null)
            {
                if (UpdateLoot(p, out var lootGoal))
                {
                    State = "looting";
                    goal = lootGoal;
                }
                else if (Leader != null && !Leader.IsDead && leaderDistance > _ai.followDistance)
                {
                    State = "following";
                    goal = Leader.transform.position;
                }
                else State = "idle";
            }

            // ---- Leash: never further than the fixed distance from the human (D-048, D-166) ----
            if (Leader != null && !Leader.IsDead && leaderDistance > _ai.maxSeparation)
            {
                State = "regrouping";
                goal = Leader.transform.position;
            }

            input.Move = goal.HasValue ? Steer(p, goal.Value, direct: backOff && leaderDistance <= _ai.maxSeparation) : Vector2.zero;
            if (!face.HasValue && input.Move.sqrMagnitude > 0.01f) face = p + new Vector3(input.Move.x, 0f, input.Move.y);
            if (face.HasValue) TurnToward(face.Value, dt);
            input.Aim = _aim;
            return input;
        }

        // ------------------------------------------------------------------ perception

        /// <summary>
        /// Sees zombies in range with a clear line (walls block, like everyone else), or hears them right next to it. Also
        /// sees survivors of the other team (owner, 2026-10-03: the Vaccine stage's rivals fight players and zombies, and the
        /// players' AI teammate fights the rivals).
        /// </summary>
        private void Perceive()
        {
            _visible.Clear();
            _target = null;
            var best = float.MaxValue;
            var eye = _self.transform.position + Vector3.up * 1.3f;
            bool Seen(Vector3 at, out float d)
            {
                d = Flat(at - _self.transform.position).magnitude;
                if (d > _ai.visionRange * StageConditions.VisionMultiplier) return false;
                return d <= _ai.closeRange || (!StageGrid.SightBlocked(eye, at) && !StageGrid.HiddenByBush(_self.transform.position, at));
            }
            foreach (var z in Zombie.Alive)
            {
                if (!Seen(z.transform.position, out var d)) continue;
                _visible.Add(z);
                // D-167: bosses are fought like any other target. Nearest first.
                if (d < best)
                {
                    best = d;
                    _target = z.transform;
                }
            }
            foreach (var s in Survivor.All)
            {
                if (s == _self || s.IsDead || s.Boarded || s.Team == _self.Team || s.IsHostage) continue;
                if (!Seen(s.transform.position, out var d)) continue;
                // Rivals go for players first; it's who they're racing.
                var weighted = Rival ? d * 0.7f : d;
                if (weighted < best)
                {
                    best = weighted;
                    _target = s.transform;
                }
            }
        }

        // ------------------------------------------------------------------ the rival team (Vaccine stages)

        /// <summary>
        /// Carrier: run from the nearest player. Others: guard the carrier, standing between it and the players. Nobody has
        /// it: go to wherever the Vaccine is and take it (from the ground or a body), or hunt the player carrying it.
        /// </summary>
        private Vector3? RivalGoal(Vector3 p)
        {
            var m = _t.missions;
            // Owner, 2026-10-11: with the Vaccine the rivals make for the helicopter (whoever reaches it with it wins).
            if (_self.Inventory.Vaccine && Helicopter.Current != null)
            {
                State = "escaping";
                return Helicopter.Current.transform.position;
            }
            if (_self.Inventory.Vaccine)
            {
                State = "fleeing";
                var threat = NearestEnemy(p);
                var away = threat != null ? Flat(p - threat.transform.position) : Vector3.forward;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                away.Normalize();
                // Try straight away first, then turning left or right, so a wall behind doesn't trap it.
                foreach (var turn in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f })
                {
                    var candidate = p + Quaternion.Euler(0f, turn, 0f) * away * m.rivalFleeDistance;
                    if (NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavAreas.WalkMask) && Flat(hit.position - p).magnitude > m.rivalFleeDistance * 0.5f)
                        return hit.position;
                }
                return p + away * 3f;
            }
            var carrier = Mission.RivalCarrier;
            if (carrier != null)
            {
                State = "guarding";
                var threat = NearestEnemy(carrier.transform.position);
                var toward = threat != null ? Flat(threat.transform.position - carrier.transform.position).normalized : Vector3.zero;
                var post = carrier.transform.position + toward * m.rivalEscortDistance;
                return Flat(post - p).magnitude > 1.5f ? post : (Vector3?)null;
            }
            // Someone on the players' side has it: hunt them.
            foreach (var s in Survivor.All)
                if (!s.IsDead && s.Team != _self.Team && s.Inventory.Vaccine)
                {
                    State = "hunting";
                    return s.transform.position;
                }
            // On the ground or on a body: go and take it.
            var reach = _t.player.pickupRange * 0.8f;
            foreach (var item in LootItem.All)
            {
                if (item.Kind != LootKind.Vaccine || item.Amount <= 0) continue;
                State = "seeking";
                if (Flat(item.transform.position - p).magnitude <= reach) _self.TryPickup(item);
                return item.transform.position;
            }
            foreach (var chest in Chest.All)
                for (var i = 0; i < chest.Contents.Count; i++)
                {
                    if (chest.Contents[i].Kind != LootKind.Vaccine) continue;
                    State = "seeking";
                    if (Flat(chest.transform.position - p).magnitude <= reach) _self.TakeFromChest(chest, i);
                    return chest.transform.position;
                }
            return null;
        }

        /// <summary>The nearest living survivor of the other team (rivals: the players), seen or not.</summary>
        private Survivor NearestEnemy(Vector3 from)
        {
            Survivor nearest = null;
            var best = float.MaxValue;
            foreach (var s in Survivor.All)
            {
                if (s.IsDead || s.Boarded || s.Team == _self.Team || s.IsHostage) continue;
                var d = Flat(s.transform.position - from).magnitude;
                if (d >= best) continue;
                best = d;
                nearest = s;
            }
            return nearest;
        }

        private float NearestThreatDistance()
        {
            var best = float.MaxValue;
            foreach (var z in _visible)
                if (z != null && !z.IsDead) best = Mathf.Min(best, Flat(z.transform.position - _self.transform.position).magnitude);
            return best;
        }

        // ------------------------------------------------------------------ weapons

        /// <summary>D-159: prefers its long gun over the Pistol while it has ammo; falls back to the Pistol.</summary>
        private void ChooseGun()
        {
            if (_self.Switching || _grenadePending) return;
            // Owner, 2026-10-09: the gun in hand ran dry: a spare that can still shoot.
            if (_self.SwapToLoadedGun()) return;
            var want = _self.LongGun != null && !_self.LongGun.Dry ? Slot.LongGun : Slot.Pistol;
            if (_self.Selected != want) _self.Select(want);
        }

        /// <summary>D-164: throws a grenade into a crowd, never where the human (or itself) would be caught (D-168).</summary>
        private bool TryGrenade(ref CharacterInput input, ref Vector3? face)
        {
            var g = _t.grenade;
            if (_grenadePending)
            {
                face = _grenadePoint;
                if (_self.Selected != Slot.Grenade)
                {
                    if (!_self.Switching && !_self.CanSelect(Slot.Grenade)) _grenadePending = false;
                    return _grenadePending;
                }
                if (_self.Switching) return true;
                if (AimError(_grenadePoint) < 4f)
                {
                    input.GrenadeDistance = Flat(_grenadePoint - _self.transform.position).magnitude;
                    input.FirePressed = true;
                    _grenadePending = false;
                    _grenadeCooldown = _ai.grenadeCooldown;
                }
                return true;
            }
            if (_grenadeCooldown > 0f || _self.Inventory.Grenades <= 0 || _self.Switching) return false;
            foreach (var center in _visible)
            {
                if (center == null) continue;
                var point = center.transform.position;
                var d = Flat(point - _self.transform.position).magnitude;
                if (d < g.minRange + 1f || d > g.maxRange || d < g.radius + 1f) continue;
                // D-168: never where a teammate (the human, or another rival) would be caught.
                var friendly = false;
                foreach (var s in Survivor.All)
                    if (s != _self && !s.IsDead && s.Team == _self.Team && Flat(s.transform.position - point).magnitude < g.radius + 1.5f) friendly = true;
                if (friendly) continue;
                var crowd = 0;
                foreach (var z in _visible)
                    if (z != null && Flat(z.transform.position - point).magnitude < g.radius * 0.7f) crowd++;
                if (crowd < _ai.grenadeCluster) continue;
                _grenadePending = true;
                _grenadePoint = new Vector3(point.x, 0f, point.z);
                _self.Select(Slot.Grenade);
                face = _grenadePoint;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ loot (D-161)

        /// <summary>The slots kept free for the Health Items it still wants (owner, 2026-10-09: ammo never crowds them out).</summary>
        private int HealthRoom => Mathf.Max(0, _ai.healthItemsWanted - _self.Inventory.HealthItems) * _t.inventory.healthSlots;

        private bool WantsHealth => !Rival && _self.Inventory.HealthItems < _ai.healthItemsWanted;

        private bool HasGunFor(AmmoType ammo)
        {
            if (_self.LongGun != null && _self.LongGun.Def.ammo == ammo) return true;
            foreach (var g in _self.SpareGuns) if (g.Def.ammo == ammo) return true;
            return false;
        }

        private bool Wants(LootData item)
        {
            // The rivals don't stop to loot: they want the Vaccine and nothing else.
            if (Rival) return item.Kind == LootKind.Vaccine;
            var inv = _self.Inventory;
            switch (item.Kind)
            {
                // Owner, 2026-10-09: up to three long guns, each kind once; then only ammo and items (no more swapping
                // between two empty guns).
                case LootKind.LongGun: return !_self.HasGun(item.Weapon) && _self.LongGunCount < Survivor.AiLongGuns;
                // Ammo for any gun it carries.
                case LootKind.Ammo:
                    return HasGunFor(item.Ammo) && inv.FreeSlots - HealthRoom >= _t.inventory.SlotCost(item.Ammo);
                // Owner, 2026-10-09: Health Items until it carries two.
                case LootKind.Health: return inv.HealthItems < _ai.healthItemsWanted && inv.FreeSlots >= _t.inventory.healthSlots;
                // Owner, 2026-10-02: the AI never takes the C4 or the Vaccine.
                case LootKind.C4:
                case LootKind.Vaccine: return false;
                default: return inv.FreeSlots - HealthRoom >= _t.inventory.grenadeSlots;
            }
        }

        private bool InReach(Vector3 p) =>
            Leader == null || Leader.IsDead || Flat(p - Leader.transform.position).magnitude <= _ai.maxSeparation - 2f;

        private bool UpdateLoot(Vector3 p, out Vector3 goal)
        {
            goal = p;
            if (_loot != null && (!_loot.isActiveAndEnabled || !Wants(_loot.Data))) _loot = null;
            if (_chest != null && (!_chest.isActiveAndEnabled || FirstWanted(_chest) < 0)) _chest = null;
            if (_loot == null && _chest == null) PickLootTarget(p);

            var reach = _t.player.pickupRange * 0.8f;
            if (_loot != null)
            {
                goal = _loot.transform.position;
                if (Flat(goal - p).magnitude <= reach)
                {
                    _self.TryPickup(_loot); // same command as the Pickup button
                    _loot = null;
                }
                return true;
            }
            if (_chest != null)
            {
                goal = _chest.transform.position;
                if (Flat(goal - p).magnitude <= reach)
                {
                    var index = WantsHealth ? HealthIn(_chest) : -1;
                    if (index < 0) index = FirstWanted(_chest);
                    if (index >= 0) _self.TakeFromChest(_chest, index);
                }
                return true;
            }
            return false;
        }

        private void PickLootTarget(Vector3 p)
        {
            // Owner, 2026-10-09: Health Items come first (a little further afield) until it carries two.
            if (WantsHealth && PickHealth(p)) return;
            var best = _ai.lootRadius;
            foreach (var item in LootItem.All)
            {
                var d = Flat(item.transform.position - p).magnitude;
                if (d >= best || !InReach(item.transform.position) || !Wants(item.Data)) continue;
                best = d;
                _loot = item;
            }
            foreach (var chest in Chest.All)
            {
                var d = Flat(chest.transform.position - p).magnitude;
                if (d >= best || !InReach(chest.transform.position) || FirstWanted(chest) < 0) continue;
                best = d;
                _chest = chest;
                _loot = null;
            }
        }

        private bool PickHealth(Vector3 p)
        {
            var best = _ai.lootRadius * 1.5f;
            foreach (var item in LootItem.All)
            {
                if (item.Kind != LootKind.Health || item.Amount <= 0) continue;
                var d = Flat(item.transform.position - p).magnitude;
                if (d >= best || !InReach(item.transform.position) || !Wants(item.Data)) continue;
                best = d;
                _loot = item;
            }
            foreach (var chest in Chest.All)
            {
                var d = Flat(chest.transform.position - p).magnitude;
                if (d >= best || !InReach(chest.transform.position) || HealthIn(chest) < 0) continue;
                best = d;
                _chest = chest;
                _loot = null;
            }
            return _loot != null || _chest != null;
        }

        private int HealthIn(Chest chest)
        {
            for (var i = 0; i < chest.Contents.Count; i++)
                if (chest.Contents[i].Kind == LootKind.Health && Wants(chest.Contents[i])) return i;
            return -1;
        }

        private int FirstWanted(Chest chest)
        {
            for (var i = 0; i < chest.Contents.Count; i++)
                if (Wants(chest.Contents[i])) return i;
            return -1;
        }

        // ------------------------------------------------------------------ movement & aim

        private Vector2 Steer(Vector3 from, Vector3 goal, bool direct)
        {
            Vector3 next;
            if (direct) next = goal;
            else
            {
                _repathLeft -= Time.deltaTime;
                if (_repathLeft <= 0f || (goal - _pathGoal).sqrMagnitude > 1f)
                {
                    _repathLeft = 0.5f;
                    _pathGoal = goal;
                    _corner = 1;
                    if (!NavMesh.SamplePosition(goal, out var hit, 3f, NavAreas.WalkMask) ||
                        !NavMesh.CalculatePath(from, hit.position, NavAreas.WalkMask, _path)) _path.ClearCorners();
                }
                var corners = _path.corners;
                while (_corner < corners.Length && Flat(corners[_corner] - from).magnitude < 0.5f) _corner++;
                next = _corner < corners.Length ? corners[_corner] : goal;
            }
            var to = Flat(next - from);
            if (Flat(goal - from).magnitude < 0.6f) return Vector2.zero;
            to.Normalize();
            return new Vector2(to.x, to.z);
        }

        private float AimError(Vector3 point)
        {
            var to = Flat(point - _self.transform.position);
            if (to.sqrMagnitude < 0.0001f) return 0f;
            return Vector3.Angle(new Vector3(_aim.x, 0f, _aim.y), to);
        }

        private void TurnToward(Vector3 point, float dt)
        {
            var to = Flat(point - _self.transform.position);
            if (to.sqrMagnitude < 0.0001f) return;
            var current = new Vector3(_aim.x, 0f, _aim.y);
            var turned = Vector3.RotateTowards(current, to.normalized, _ai.turnRate * Mathf.Deg2Rad * dt, 0f);
            _aim = new Vector2(turned.x, turned.z).normalized;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
