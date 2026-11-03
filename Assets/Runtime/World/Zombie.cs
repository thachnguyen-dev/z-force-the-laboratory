using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// A zombie or boss: common perception (proximity, 80° vision blocked by walls, hearing — D-050),
    /// the <see cref="ZombieMind"/> decisions, NavMesh movement around obstacles (D-053) and melee attacks (D-027).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Zombie : MonoBehaviour, IDamageable
    {
        public static readonly List<Zombie> Alive = new List<Zombie>();
        public static event System.Action<Zombie> Killed;

        /// <summary>
        /// Rescue stages (owner, 2026-09-30): once the hostage is reached, every zombie — the Main Boss too — hunts this
        /// player wherever they are, sight or no sight.
        /// </summary>
        public static Survivor Hunted;

        public ZombieDef Def { get; private set; }
        public ZombieMind Mind { get; private set; }
        public float Hp { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsMainBoss { get; set; }
        public Survivor Target { get; private set; }
        public bool Attacking => _windupLeft > 0f;
        /// <summary>How far the attack's windup has gone, 0..1 (0 when not attacking), for the animation.</summary>
        public float AttackProgress => Attacking && Def != null && Def.windup > 0f ? 1f - _windupLeft / Def.windup : 0f;
        public bool SlamTelegraphing => _slamLeft > 0f;

        private PerceptionTuning _perception;
        private NavMeshAgent _agent;
        private float _thinkLeft;
        private float _attackCooldown;
        private float _windupLeft;
        private float _slamCooldown;
        private float _slamLeft;
        private Transform _slamRing;
        private int _appliedDestination = -1;
        private Vector3 _home;
        private float _wanderLeft;
        private float _deathT = -1f;
        private System.Random _rng;
        private float _groanLeft;
        private bool _repath;

        public void Init(ZombieDef def, PerceptionTuning perception, int seed)
        {
            Def = def;
            _perception = perception;
            _rng = new System.Random(seed);
            Mind = new ZombieMind(perception, seed);
            Hp = def.hp;
            _home = transform.position;
            _agent = GetComponent<NavMeshAgent>();
            _agent.radius = def.radius;
            _agent.height = def.height;
            _agent.speed = def.wanderSpeed;
            _agent.acceleration = 20f;
            _agent.angularSpeed = 540f;
            _agent.stoppingDistance = 0.2f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            _agent.areaMask = NavAreas.MaskFor(def);
            // Stagger thinking so a crowd doesn't all raycast on the same frame.
            _thinkLeft = (float)_rng.NextDouble() * perception.thinkInterval;
            _wanderLeft = 1f + (float)_rng.NextDouble() * 4f;
            _groanLeft = 3f + (float)_rng.NextDouble() * 10f;
            _slamCooldown = def.slamInterval * 0.5f;
        }

        /// <summary>
        /// Owner, 2026-10-04: proportions after Brawl Stars (a regular body about one tile wide, bosses several). Bodies
        /// are drawn this much wider than their collision; collision and pathing are unchanged.
        /// </summary>
        public const float BodyWidth = 1.25f;

        /// <summary>The lowest a zombie's hitbox goes up (m): above the bullets' flight height (up to 1.1 m).</summary>
        public const float MinHitHeight = 1.4f;

        public void BuildVisual()
        {
            var r = Def.radius * 2f;
            var visual = new GameObject("Body").transform;
            visual.SetParent(transform, false);
            // The owner's model for this type (2026-10-07: connected), scaled to the type's height and animated in code.
            var model = Art.CharacterModelLibrary.Find(ModelFor(Def.type, GetInstanceID()));
            var bones = model != null ? Art.CharacterRig.Build(model, visual, out _) : null;
            if (bones != null)
            {
                var root = visual.GetChild(0);
                root.localScale = Vector3.one * (Def.height / Mathf.Max(0.1f, model.height));
                BlobShadow.Attach(transform, r * 1.5f * BodyWidth, r * 1.7f * BodyWidth, Def.height);
                gameObject.AddComponent<ZombiePuppet>().Init(this, root, bones, model);
            }
            else
            {
                visual.localScale = new Vector3(BodyWidth, 1f, BodyWidth);
                // Four-legged placeholders for the four-legged animal zombies (the Bear stands on two legs).
                if (Def.type == ZombieType.Dog || Def.type == ZombieType.Rat || Def.type == ZombieType.Tiger) BuildQuadruped(visual);
                else
                {
                    BlobShadow.Attach(transform, r * 1.5f * BodyWidth, r * 1.7f * BodyWidth);
                    Primitives.Shape(PrimitiveType.Capsule, visual, new Vector3(0f, Def.height * 0.5f, 0f), new Vector3(r, Def.height * 0.5f, r), Def.color);
                    // Arms reaching forward, so facing reads from above.
                    Primitives.Shape(PrimitiveType.Cube, visual, new Vector3(0f, Def.height * 0.72f, Def.radius + 0.2f),
                        new Vector3(r * 0.9f, 0.12f, 0.5f), Def.color * 0.7f);
                }
            }
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = Def.radius;
            // Bullets fly level at chest height (Survivor.Shoot): a low zombie's hitbox still reaches up into their path
            // (owner, 2026-10-07: the Rat, 0.45 m tall, couldn't be hit). Its body is drawn at its own height.
            var hitHeight = Mathf.Max(Def.height, MinHitHeight);
            capsule.height = hitHeight;
            capsule.center = new Vector3(0f, hitHeight * 0.5f, 0f);
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
        }


        /// <summary>
        /// The owner's model for each type (owner, 2026-10-07): Basic zombie_1 or zombie_2 (by <paramref name="pick"/>),
        /// Fast the same with red skin, Tank and Boss boss_1 (the slow, heavy type later), SubBoss and Bear the Bear,
        /// the animals their own.
        /// </summary>
        public static string ModelFor(ZombieType type, int pick)
        {
            var second = (pick & 1) != 0;
            switch (type)
            {
                case ZombieType.Basic: return second ? "zmb_2" : "zmb_1";
                case ZombieType.Fast: return second ? "zmb_2_fast" : "zmb_1_fast";
                case ZombieType.Tank:
                case ZombieType.Boss: return "boss_1";
                case ZombieType.SubBoss:
                case ZombieType.Bear: return "boss_bear";
                case ZombieType.Dog: return "zmb_dog";
                case ZombieType.Rat: return "zmb_rat";
                case ZombieType.Tiger: return "boss_tiger";
                default: return "zmb_1";
            }
        }

        /// <summary>
        /// ⚠ Placeholder four-legged body (owner, 2026-09-30) for the Dog, Rat and Tiger: a long low body, a head, four
        /// legs, scaled by the type's height (the Dog's is 1.1 m).
        /// </summary>
        private void BuildQuadruped(Transform visual)
        {
            var c = Def.color;
            var k = Def.height / 1.1f;
            BlobShadow.Attach(transform, 1f * BodyWidth * k, 1.6f * BodyWidth * k);
            Primitives.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 0.65f, 0f) * k, new Vector3(0.55f, 0.4f, 1.2f) * k, c);
            Primitives.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 0.8f, 0.72f) * k, new Vector3(0.4f, 0.35f, 0.4f) * k, c * 0.8f);
            foreach (var (x, z) in new[] { (-0.22f, 0.42f), (0.22f, 0.42f), (-0.22f, -0.42f), (0.22f, -0.42f) })
                Primitives.Shape(PrimitiveType.Cube, visual, new Vector3(x, 0.25f, z) * k, new Vector3(0.14f, 0.5f, 0.14f) * k, c * 0.7f);
        }
        // ---- Pooling (2026-10-03: no creating or destroying zombies during play) ----

        /// <summary>The pool this zombie returns to when it dies or goes dormant (null: destroyed instead).</summary>
        internal ZombiePopulation Pool;
        /// <summary>Bumped on every reuse, so anything holding on to this object can tell it's a different zombie now.</summary>
        public int Life { get; private set; }

        /// <summary>A pooled zombie comes back as a fresh one of the same type at <paramref name="position"/>.</summary>
        internal void Reuse(ZombieDef def, PerceptionTuning perception, int seed, Vector3 position, float yaw, float hp)
        {
            Life++;
            IsDead = false;
            Toughness = 1f;
            _charge = ChargeStep.None;
            _chargeCooldown = 0.8f;
            HideLane();
            IsMainBoss = false;
            Target = null;
            _windupLeft = _slamLeft = _attackCooldown = _stopLeft = 0f;
            _deathT = -1f;
            _appliedDestination = -1;
            _repath = false;
            _packTarget = null;
            _packLeft = 0f;
            _nextPackCall = 0f;
            _shown = true;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            transform.localScale = Vector3.one;
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in _renderers) r.enabled = true;
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            gameObject.SetActive(true);
            Init(def, perception, seed);
            Hp = Mathf.Min(hp, def.hp);
            _agent.enabled = true;
            _agent.Warp(position);
        }

        /// <summary>Where this zombie was heading (for its dormant record), if anywhere.</summary>
        internal Vector3? Heading => Mind != null && Mind.Mode != ZombieMode.Idle ? Mind.Destination : (Vector3?)null;
        internal Vector3 Home => _home;

        private void OnEnable() => Alive.Add(this);
        private void OnDisable() => Alive.Remove(this);

        private void Update()
        {
            UpdateBushVisibility();
            if (IsDead)
            {
                UpdateDeath();
                return;
            }
            if (Def == null || (GameSession.Instance != null && !GameSession.Instance.Playing)) return;
            if (Replica)
            {
                UpdateReplica(Time.deltaTime);
                return;
            }
            var dt = Time.deltaTime;

            _thinkLeft -= dt;
            if (_thinkLeft <= 0f)
            {
                _thinkLeft += _perception.thinkInterval;
                Perceive();
            }
            Mind.Tick(dt);
            _attackCooldown -= dt;
            _packLeft -= dt;

            if (Def.slam) UpdateSlam(dt);
            if (Def.type == ZombieType.Tiger && UpdateCharge(dt)) { }
            else if (Attacking) UpdateAttack(dt);
            else if (!SlamTelegraphing) Steer(dt);

            _groanLeft -= dt;
            if (_groanLeft <= 0f)
            {
                _groanLeft = 6f + (float)_rng.NextDouble() * 12f;
                Sfx.Play(Sfx.VoiceOf(Def.type), transform.position, 0.35f);
            }
        }

        private void Perceive()
        {
            Survivor seen = null;
            var bestSqr = float.MaxValue;
            var eye = transform.position + Vector3.up * (Def.height * 0.85f);
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || survivor.Boarded) continue;
                // Owner, 2026-10-01: zombies leave the hostage alone until a player has reached them.
                if (survivor.IsHostage && !survivor.IsFreed) continue;
                var to = survivor.transform.position - transform.position;
                to.y = 0f;
                var sqr = to.sqrMagnitude;
                if (sqr >= bestSqr) continue;
                if (!Detects(survivor, eye, to, sqr)) continue;
                seen = survivor;
                bestSqr = sqr;
            }

            // A Dog that sees a player calls the zombies around it onto them, refreshed every second while it sees them.
            if (seen != null && Def.alertsPack && Time.time >= _nextPackCall)
            {
                _nextPackCall = Time.time + 1f;
                CallPack(seen);
            }
            if (seen == null && Hunted != null && !Hunted.IsDead) seen = Hunted;
            // Called in by a Dog: keeps after that player for a while even without seeing them (owner, 2026-10-03).
            if (seen == null && _packTarget != null && !_packTarget.IsDead && _packLeft > 0f) seen = _packTarget;
            if (seen != null)
            {
                Target = seen;
                _repath = true;
                Mind.See(seen.transform.position);
            }
            else
            {
                Target = null;
                Mind.LoseSight();
            }
        }

        // ---- The Dog's pack call (owner, 2026-10-03) ----

        private Survivor _packTarget;
        private float _packLeft;
        private float _nextPackCall;

        /// <summary>Calls every zombie within the pack radius (not the Main Boss) to chase this player together.</summary>
        private void CallPack(Survivor player)
        {
            var first = _packTarget != player;
            _packTarget = player;
            _packLeft = Def.packChaseSeconds;
            var r2 = Def.packRadius * Def.packRadius;
            foreach (var z in Alive)
            {
                if (z == this || z.IsMainBoss || ZombieTypes.IsBoss(z.Def.type) || z.IsDead) continue;
                if ((z.transform.position - transform.position).sqrMagnitude > r2) continue;
                z.JoinChase(player, Def.packChaseSeconds);
            }
            if (first) Sfx.Play(Sfx.VoiceOf(Def.type), transform.position);
        }

        /// <summary>Called in by a Dog: chase this player for a while, seen or not.</summary>
        public void JoinChase(Survivor player, float seconds)
        {
            if (IsDead || player == null) return;
            _packTarget = player;
            _packLeft = Mathf.Max(_packLeft, seconds);
            Target = player;
            _repath = true;
            Mind.See(player.transform.position);
        }

        private bool Detects(Survivor survivor, Vector3 eye, Vector3 to, float sqr)
        {
            var p = _perception;
            if (sqr <= p.proximity * p.proximity) return true;
            // Darkness shortens everyone's sight (owner, 2026-09-30).
            var range = p.visionRange * StageConditions.VisionMultiplier * StageConditions.ZombieDetectionMultiplier * Def.senseMultiplier;
            if (sqr > range * range) return false;
            if (Vector3.Angle(transform.forward, to) > p.visionConeDeg * 0.5f) return false;
            // D-051: walls and obstacles block sight.
            // Bush rule (owner-approved): someone in a bush is only seen up close.
            if (StageGrid.HiddenByBush(transform.position, survivor.transform.position)) return false;
            return !StageGrid.SightBlocked(eye, survivor.transform.position);
        }

        public void Hear(Vector3 position)
        {
            if (IsDead) return;
            Mind.Hear(position);
        }

        private void Steer(float dt)
        {
            if (!_agent.isOnNavMesh) return;
            // Owner, 2026-10-04: briefly stopped after a bullet hit.
            if (_stopLeft > 0f)
            {
                _stopLeft -= dt;
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
                return;
            }
            if (_agent.isStopped) _agent.isStopped = false;
            var mode = Mind.Mode;
            _agent.speed = mode == ZombieMode.Chase ? Def.chaseSpeed
                : mode == ZombieMode.Idle ? Def.wanderSpeed
                : Mathf.Lerp(Def.wanderSpeed, Def.chaseSpeed, 0.6f);

            if (mode == ZombieMode.Chase && Target != null)
            {
                var to = Target.transform.position - transform.position;
                to.y = 0f;
                // Keep re-pathing to the target's actual position while it's in sight.
                if (_repath) _agent.SetDestination(Target.transform.position);
                _repath = false;
                if (to.magnitude <= Def.attackReach + 0.35f && _attackCooldown <= 0f) StartAttack();
                return;
            }

            if (mode == ZombieMode.Idle)
            {
                _wanderLeft -= dt;
                if (_wanderLeft <= 0f)
                {
                    _wanderLeft = 4f + (float)_rng.NextDouble() * 5f;
                    var angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                    var point = _home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (float)_rng.NextDouble() * 4f;
                    if (NavMesh.SamplePosition(point, out var navHit, 2f, _agent.areaMask)) _agent.SetDestination(navHit.position);
                }
                return;
            }

            if (_appliedDestination != Mind.DestinationVersion)
            {
                _appliedDestination = Mind.DestinationVersion;
                if (NavMesh.SamplePosition(Mind.Destination, out var navHit, 4f, _agent.areaMask)) _agent.SetDestination(navHit.position);
                else Mind.Arrived();
                return;
            }
            if (!_agent.pathPending && (_agent.remainingDistance <= 0.8f || _agent.pathStatus == NavMeshPathStatus.PathInvalid))
                Mind.Arrived();
        }

        private void StartAttack()
        {
            _windupLeft = Def.windup;
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            Sfx.Play(SoundId.ZombieAttack, transform.position, 0.6f);
        }

        private void UpdateAttack(float dt)
        {
            if (Target != null)
            {
                var face = Target.transform.position - transform.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 360f * dt);
            }
            _windupLeft -= dt;
            if (_windupLeft > 0f) return;
            _attackCooldown = Def.attackCooldown;
            if (Target == null || Target.IsDead) return;
            var to = Target.transform.position - transform.position;
            to.y = 0f;
            // The hit lands only if the target is still in reach and in front.
            if (to.magnitude <= Def.attackReach + 0.45f && Vector3.Angle(transform.forward, to) < 70f)
                Target.TakeHit(Def.damage, transform.position, Def.knockback);
        }

        // ---- Placeholder boss mechanic: ground slam ----

        private void UpdateSlam(float dt)
        {
            if (SlamTelegraphing)
            {
                _slamLeft -= dt;
                if (_slamRing != null)
                {
                    var k = 1f - _slamLeft / Def.slamTelegraph;
                    _slamRing.localScale = new Vector3(Def.slamRadius * 2f * k, 0.02f, Def.slamRadius * 2f * k);
                }
                if (_slamLeft <= 0f) Slam();
                return;
            }
            _slamCooldown -= dt;
            if (_slamCooldown > 0f || Target == null || Attacking) return;
            if ((Target.transform.position - transform.position).magnitude > Def.slamTrigger) return;
            _slamLeft = Def.slamTelegraph;
            _slamCooldown = Def.slamInterval;
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _slamRing = Primitives.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.05f, 0f), Vector3.zero,
                new Color(1f, 0.1f, 0.1f, 0.45f)).transform;
        }

        private void Slam()
        {
            if (_slamRing != null) Destroy(_slamRing.gameObject);
            _slamRing = null;
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || survivor.Boarded || (survivor.IsHostage && !survivor.IsFreed)) continue;
                var d = survivor.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude <= Def.slamRadius) survivor.TakeHit(Def.slamDamage, transform.position, Def.slamKnockback);
            }
            Noise.Emit(transform.position, 20f);
            Sfx.Play(SoundId.Explosion, transform.position, 0.7f);
            ExplosionFx.Spawn(transform.position, Def.slamRadius * 0.6f);
        }


        // ---- The Tiger's charge (owner, 2026-10-08) ----

        private enum ChargeStep { None, Warning, Charging }

        private ChargeStep _charge;
        private float _chargeLeft;
        private float _chargeCooldown = 0.8f;
        private float _chargeTravel;
        private Vector3 _chargeDir;
        private readonly HashSet<Survivor> _chargeHit = new HashSet<Survivor>();
        private Transform _chargeLane;

        /// <summary>True while it is warning or charging.</summary>
        public bool Charging => _charge != ChargeStep.None;
        /// <summary>0 → 1 through the warning (sent online, like the slam's, for the red lane).</summary>
        public float ChargeWarning => _charge == ChargeStep.Warning ? 1f - _chargeLeft / Mathf.Max(0.01f, Tuning.charge.telegraph) : 0f;

        private GameTuning Tuning => GameSession.Instance != null ? GameSession.Instance.Tuning : _tuningFallback ?? (_tuningFallback = new GameTuning());
        private static GameTuning _tuningFallback;

        /// <summary>True while the charge has it (the warning or the run); otherwise false and it acts as usual.</summary>
        private bool UpdateCharge(float dt)
        {
            var c = Tuning.charge;
            switch (_charge)
            {
                case ChargeStep.Warning:
                    _chargeLeft -= dt;
                    ShowLane(1f - _chargeLeft / Mathf.Max(0.01f, c.telegraph));
                    if (_chargeLeft > 0f) return true;
                    HideLane();
                    _charge = ChargeStep.Charging;
                    _chargeTravel = 0f;
                    _chargeHit.Clear();
                    return true;
                case ChargeStep.Charging:
                    var step = Mathf.Min(c.speed * dt, c.distance - _chargeTravel);
                    var before = transform.position;
                    if (_agent.isOnNavMesh) _agent.Move(_chargeDir * step);
                    var moved = Vector3.Distance(before, transform.position);
                    _chargeTravel += step;
                    foreach (var s in Survivor.All)
                    {
                        if (s.IsDead || s.Boarded || (s.IsHostage && !s.IsFreed) || _chargeHit.Contains(s)) continue;
                        var d = s.transform.position - transform.position;
                        d.y = 0f;
                        if (d.magnitude > Def.radius + 0.8f) continue;
                        _chargeHit.Add(s);
                        s.TakeHit(c.damage, transform.position - _chargeDir, c.knockback);
                    }
                    // Done when it has gone the distance, or a wall stopped it.
                    if (_chargeTravel >= c.distance - 0.01f || moved < step * 0.3f)
                    {
                        _charge = ChargeStep.None;
                        _chargeCooldown = c.interval;
                        _attackCooldown = Mathf.Max(_attackCooldown, 0.6f);
                    }
                    return true;
            }
            _chargeCooldown -= dt;
            if (_chargeCooldown > 0f || Target == null || Target.IsDead || Attacking) return false;
            var to = Target.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude < c.minRange || to.magnitude > c.maxRange) return false;
            // The way is fixed now: it charges where the target was when it roared.
            _charge = ChargeStep.Warning;
            _chargeLeft = c.telegraph;
            _chargeDir = to.normalized;
            transform.rotation = Quaternion.LookRotation(_chargeDir);
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            Sfx.Play(Sfx.VoiceOf(Def.type), transform.position, 0.9f);
            ShowLane(0f);
            return true;
        }

        private static Material _laneMaterial;

        /// <summary>The red warning: a lane on the ground the length of the charge, as wide as the Tiger, brightening.</summary>
        private void ShowLane(float k)
        {
            if (_chargeLane == null)
            {
                if (_laneMaterial == null)
                {
                    var shader = Resources.Load<Shader>("BloodDecal");
                    _laneMaterial = new Material(shader != null ? shader : Shader.Find("Zombie/BloodDecal")) { name = "Charge warning" };
                    _laneMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    _laneMaterial.SetColor("_BaseColor", new Color(1f, 0.12f, 0.08f, 0.6f));
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Charge warning";
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = _laneMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _chargeLane = go.transform;
            }
            var length = Tuning.charge.distance;
            var dir = Replica ? transform.forward : _chargeDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();
            // It grows out to its full length through the warning, and pulses. (A quad faces -z: turned to face up, its
            // height along the way.)
            var reach = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(k * 1.6f));
            _chargeLane.SetPositionAndRotation(transform.position + dir * (length * reach * 0.5f) + Vector3.up * 0.04f,
                Quaternion.LookRotation(Vector3.down, dir));
            _chargeLane.localScale = new Vector3(Def.radius * 2.2f, length * reach, 1f);
            _chargeLane.gameObject.SetActive(Mathf.Repeat(Time.time * 8f, 1f) < 0.75f || k > 0.7f);
        }

        private void HideLane()
        {
            if (_chargeLane != null) Destroy(_chargeLane.gameObject);
            _chargeLane = null;
        }

        // ---- Network replica (online client): shows the Host's zombie, runs no AI ----

        public int NetId;
        public bool Replica { get; private set; }
        private Vector3 _netPosition;
        private float _netYaw;
        private float _netSlam;

        /// <summary>0 → 1 while the boss telegraphs its slam (replicated for the red ring).</summary>
        public float SlamProgress => Def != null && Def.type == ZombieType.Tiger ? ChargeWarning : SlamTelegraphing ? 1f - _slamLeft / Def.slamTelegraph : 0f;

        public void InitReplica(ZombieDef def, PerceptionTuning perception, int id, Vector3 position, float yaw)
        {
            Init(def, perception, id);
            NetId = id;
            Replica = true;
            _agent.enabled = false;
            _netPosition = position;
            _netYaw = yaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            BuildVisual();
        }

        public void ApplyReplica(Vector3 position, float yaw, float hp, bool dead, float slam)
        {
            if (IsDead) return;
            _netPosition = position;
            _netYaw = yaw;
            _netSlam = slam;
            if (hp < Hp - 0.01f && Time.time > _previewedUntil)
            {
                HitFx.Flash(transform);
                // The friend's own blood spray (owner, 2026-10-08), any way (the shot's direction isn't sent).
                BloodSpray.Burst(transform.position, Def.height, Vector3.zero, BloodSize);
            }
            Hp = hp;
            if (dead) Die();
        }

        /// <summary>
        /// Host migration (D-172): this replica becomes a real zombie where it last was. Its memory isn't replicated,
        /// so it starts from normal behaviour — any sound it was investigating is forgotten.
        /// </summary>
        public void BecomeSimulated()
        {
            if (!Replica) return;
            Replica = false;
            if (_slamRing != null) Destroy(_slamRing.gameObject);
            _slamRing = null;
            if (IsDead) return;
            transform.position = _netPosition;
            _home = _netPosition;
            _agent.enabled = true;
            _agent.Warp(_netPosition);
        }

        private void UpdateReplica(float dt)
        {
            var k = 1f - Mathf.Exp(-12f * dt);
            transform.position = Vector3.Lerp(transform.position, _netPosition, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _netYaw, 0f), k);
            // The Tiger's charge warning (the slam's channel carries it).
            if (Def.type == ZombieType.Tiger)
            {
                if (_netSlam > 0f) ShowLane(_netSlam);
                else HideLane();
                return;
            }
            if (_netSlam > 0f)
            {
                if (_slamRing == null)
                    _slamRing = Primitives.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.05f, 0f), Vector3.zero,
                        new Color(1f, 0.1f, 0.1f, 0.45f)).transform;
                _slamRing.localScale = new Vector3(Def.slamRadius * 2f * _netSlam, 0.02f, Def.slamRadius * 2f * _netSlam);
            }
            else if (_slamRing != null)
            {
                Destroy(_slamRing.gameObject);
                _slamRing = null;
            }
        }


        // ---- Bushes (owner-approved rule): a zombie in a bush is invisible to you unless you're close ----

        private Renderer[] _renderers;
        private bool _shown = true;

        private void UpdateBushVisibility()
        {
            var session = GameSession.Instance;
            var viewer = session != null ? session.LocalSurvivor : null;
            var hide = viewer != null && StageGrid.HiddenByBush(viewer.transform.position, transform.position);
            if (hide != _shown) return;
            _shown = !hide;
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in _renderers) r.enabled = _shown;
        }
        // ---- Damage & death ----

        private float _stopLeft;

        /// <summary>Owner, 2026-10-04: hit by a bullet, a zombie stops moving for a moment (it can still attack).</summary>
        public void HitStop()
        {
            if (IsDead || Replica || IsMainBoss || ZombieTypes.IsBoss(Def.type)) return;
            _stopLeft = Mathf.Max(_stopLeft, _perception.bulletHitStop);
        }

        internal void SetHp(float hp) => Hp = Mathf.Min(hp, Def.hp);
        internal void SetHome(Vector3 home) => _home = home;

        private float _previewedUntil;

        /// <summary>
        /// Online friend: a shot's tracer reached this replica: the hit shows now (flash and spray, along the shot), not when
        /// the Host's damage comes back a moment later (which then doesn't show it again).
        /// </summary>
        public void PreviewHit(Vector3 shot)
        {
            if (!Replica || IsDead) return;
            HitFx.Flash(transform);
            BloodSpray.Burst(transform.position, Def.height, shot, BloodSize);
            _previewedUntil = Time.time + 0.6f;
        }

        /// <summary>How big its blood spray is: with its size (a Rat's small, a Bear's large).</summary>
        private float BloodSize => Def != null ? Mathf.Clamp(Def.radius * 2.2f, 0.6f, 1.8f) : 1f;

        /// <summary>Endless mode (owner, 2026-10-07): later zombies take less from each hit (1 = normal).</summary>
        public float Toughness = 1f;

        public void TakeDamage(float damage, Vector3 from, bool fromExplosion)
        {
            if (IsDead || Replica) return;
            damage /= Mathf.Max(1f, Toughness);
            Hp -= damage;
            // Owner, 2026-10-08: green blood sprays where a shot (or the knife) hits; explosions don't.
            if (!fromExplosion && damage > 0f) BloodSpray.Burst(transform.position, Def.height, transform.position - from, BloodSize);
            // Owner, 2026-10-03: hurting the Main Boss calls the horde to the Boss Area.
            if (IsMainBoss && damage > 0f) Mission.BossHurt(this);
            HitFx.Flash(transform);
            if (Hp <= 0f)
            {
                Die();
                return;
            }
            // Being shot from outside its senses: turn toward where it came from (placeholder).
            if (Mind.Mode != ZombieMode.Chase) Mind.Hear(from);
        }

        private void Die()
        {
            HideLane();
            _charge = ChargeStep.None;
            Hp = 0f;
            IsDead = true;
            Alive.Remove(this);
            if (_slamRing != null) Destroy(_slamRing.gameObject);
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.enabled = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            _deathT = 0f;
            Sfx.Play(Sfx.DeathOf(Def.type), transform.position, 0.7f);
            Killed?.Invoke(this);
        }

        /// <summary>How long a corpse lies on the ground before it sinks away (s; owner, 2026-10-07). PLACEHOLDER.</summary>
        public const float CorpseTime = 4f;
        /// <summary>The fall, and the sinking away after <see cref="CorpseTime"/> (s).</summary>
        public const float FallTime = 0.5f, SinkTime = 1.5f;

        /// <summary>D-112: a death animation (placeholder: topple), the corpse lying a while, then sinking away and removed.</summary>
        private void UpdateDeath()
        {
            _deathT += Time.deltaTime;
            if (_deathT < FallTime)
                transform.rotation = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(-90f * (_deathT / FallTime), 0f, 0f);
            else if (_deathT < FallTime + CorpseTime) { }
            else if (_deathT < FallTime + CorpseTime + SinkTime) transform.position += Vector3.down * Time.deltaTime * 0.6f;
            else if (Pool != null) Pool.Release(this);
            else Destroy(gameObject);
        }
    }

    /// <summary>⚠ Placeholder hit feedback: a brief white scale pop.</summary>
    public class HitFx : MonoBehaviour
    {
        private float _t;
        private Vector3 _scale;

        public static void Flash(Transform target)
        {
            var fx = target.GetComponent<HitFx>();
            if (fx == null)
            {
                fx = target.gameObject.AddComponent<HitFx>();
                fx._scale = target.localScale;
            }
            fx._t = 0f;
            fx.enabled = true;
        }

        private void Update()
        {
            _t += Time.deltaTime / 0.1f;
            transform.localScale = _scale * (1f + 0.12f * (1f - Mathf.Clamp01(_t)));
            if (_t >= 1f)
            {
                transform.localScale = _scale;
                enabled = false;
            }
        }
    }
}
