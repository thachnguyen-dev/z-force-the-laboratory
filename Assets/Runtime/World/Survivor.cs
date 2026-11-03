using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// One frame of intent for a character. Humans (touch, keyboard/mouse), remote players and the AI teammate
    /// all produce this, so they go through the same character code (D-043).
    /// </summary>
    public struct CharacterInput
    {
        /// <summary>World XZ, magnitude ≤ 1.</summary>
        public Vector2 Move;
        /// <summary>World XZ aim direction; zero keeps the current facing.</summary>
        public Vector2 Aim;
        public bool FireHeld;
        public bool FirePressed;
        public bool FireReleased;
        public bool Reload;
        public bool Pickup;
        /// <summary>
        /// Grenade throw distance chosen by the player (clamped to the range): PC the cursor, Android how far the fire
        /// stick is dragged. Negative: none given (the old charge mode: hold Fire to push the marker out).
        /// </summary>
        public float GrenadeDistance;
        /// <summary>
        /// Android (owner, 2026-10-07: the throw point freely controlled while holding): the marker follows
        /// <see cref="GrenadeDistance"/> and the aim while Fire is held; letting go throws. PC: a click throws.
        /// </summary>
        public bool ThrowOnRelease;
        /// <summary>D-013: Android only.</summary>
        public bool AimAssist;
    }

    public interface ICharacterInputSource
    {
        CharacterInput Read(Survivor self);
    }

    /// <summary>
    /// A player character (human or AI): HP, movement, weapons, inventory, pickup and drop. Host-side simulation.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Survivor : MonoBehaviour, IDamageable
    {
        public static readonly List<Survivor> All = new List<Survivor>();

        public ICharacterInputSource InputSource;
        public bool IsHuman = true;
        /// <summary>The players' side (with their AI teammate and the hostage), and the Vaccine stage's rival team (owner, 2026-10-03).</summary>
        public const int PlayerTeam = 0;
        public const int RivalTeam = 1;
        /// <summary>The final stage's second rival team (owner, 2026-10-07). Rival teams fight each other too.</summary>
        public const int RivalTeam2 = 2;
        public bool IsRival => Team >= RivalTeam;
        /// <summary>Bullets and the Knife hurt only the other team (D-114 still holds within a team).</summary>
        public int Team = PlayerTeam;
        /// <summary>The rescue-stage hostage (owner, 2026-09-30): not a player, carries nothing.</summary>
        public bool IsHostage;
        /// <summary>Replicated for the HUD: the hostage has been reached and is following.</summary>
        public bool HostageFreed { get; private set; }
        public bool IsFreed => HostageFreed || (InputSource is HostageBrain brain && brain.Freed);
        public string DisplayName = "Player";

        public GameTuning Tuning { get; private set; }
        public Inventory Inventory { get; private set; }
        public FirearmState Pistol { get; private set; }
        public FirearmState LongGun { get; private set; }
        public float Hp { get; private set; }
        public CharacterDef Character { get; private set; }
        /// <summary>Owner, 2026-10-01: HP is a base stat; a character adds a percentage.</summary>
        public float MaxHp => Tuning.player.maxHp * (1f + (Character != null ? Character.hpBonus : 0f));
        /// <summary>Base speed, the character's bonus, and rain (owner, 2026-09-30: rain slows the players).</summary>
        public float MoveSpeed => Tuning.player.moveSpeed * (1f + (Character != null ? Character.speedBonus : 0f)) * StageConditions.PlayerSpeedMultiplier;
        public bool IsDead { get; private set; }
        public Slot Selected { get; private set; } = Slot.Pistol;
        public bool Switching => _switchLeft > 0f;
        public float SwitchProgress => Switching ? 1f - _switchLeft / Tuning.player.switchTime : 1f;
        /// <summary>Where the character aims (shots, the knife, grenades); the body may face elsewhere (owner, 2026-10-06).</summary>
        public Vector3 Facing => AimDirection;

        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        /// <summary>
        /// Owner, 2026-10-06: the body faces where the character walks; firing turns it to the aim, and it keeps facing
        /// the aim this long after the last shot or stab before turning back to the walking direction. PLACEHOLDER.
        /// </summary>
        public const float FaceAimTime = 0.6f;
        /// <summary>How fast the body turns (degrees a second): quick, so a shot turns it almost at once. PLACEHOLDER.</summary>
        public const float TurnSpeed = 1080f;
        private float _faceAimUntil;
        private bool _engaged;

        /// <summary>Firing (or just fired, stabbed, aiming a grenade): the body faces the aim and the weapon is raised.</summary>
        public bool FacingAim => _engaged;
        public bool Moving { get; private set; }

        /// <summary>Shown by the HUD while choosing a grenade throw.</summary>
        public bool GrenadeAiming { get; private set; }
        public Vector3 GrenadeTarget { get; private set; }

        /// <summary>Last time this survivor was hit (HUD flash).</summary>
        public float LastHitTime { get; private set; } = -10f;
        public event System.Action<Survivor> Died;
        public event System.Action<string> Message;

        private CharacterController _controller;
        private Vector3 _knockback;
        private float _switchLeft;
        private Slot _switchTarget;
        private float _knifeCooldown;
        private float _grenadeCharge;
        private bool _touchThrow;
        private float _touchDistance;
        private bool _charging;
        private System.Random _rng;
        private Transform _gunVisual;

        public void Init(GameTuning tuning, CharacterDef character, int seed)
        {
            Tuning = tuning;
            _rng = new System.Random(seed);
            Character = character;
            // Endless mode (owner, 2026-10-07): the backpack holds ×1.5.
            var capacity = tuning.inventory.capacity * (1f + (character != null ? character.slotBonus : 0f)) * (Endless.Active ? tuning.endless.backpackMultiplier : 1f);
            Inventory = new Inventory(tuning.inventory, Mathf.RoundToInt(capacity) - tuning.inventory.capacity);
            Hp = MaxHp;
            // D-005 / D-227: every stage starts with the Pistol (unlimited reserve, still reloads) and the Knife.
            var pistol = tuning.Firearm(WeaponId.Pistol);
            Pistol = new FirearmState(pistol, pistol.magazine, UnlimitedReserve.Instance);
            _controller = GetComponent<CharacterController>();
        }

        public Color BodyColor { get; private set; }

        /// <summary>
        /// Owner, 2026-10-04: proportions after Brawl Stars, where a character is about one tile wide. The body is drawn
        /// wider than its collision (0.7 m → 1 m); collision, movement and doorways are unchanged.
        /// </summary>
        public const float BodyWidth = 1.4f;

        private CharacterPuppet _puppet;

        /// <summary>The character's own model (owner's models, 2026-10-05), or null where it's still the capsule.</summary>
        public bool HasModel => _puppet != null;

        public void BuildVisual(Color body)
        {
            BodyColor = body;
            BlobShadow.Attach(transform, 1.0f, 1.2f, 1.8f);
            var visual = new GameObject("Body").transform;
            visual.SetParent(transform, false);
            _visual = visual;
            // The owner's model for this character (military camouflage, its backpack part of the mesh).
            var model = Character != null ? Art.CharacterRig.ForCharacter(Character.id) : null;
            var bones = model != null ? Art.CharacterRig.Build(model, visual, out _) : null;
            if (bones != null)
            {
                _puppet = gameObject.AddComponent<CharacterPuppet>();
                _puppet.Init(this, visual.GetChild(0), bones, model);
            }
            else
            {
                visual.localScale = new Vector3(BodyWidth, 1f, BodyWidth);
                Primitives.Shape(PrimitiveType.Capsule, visual, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), body);
                Primitives.Shape(PrimitiveType.Cube, visual, new Vector3(0f, 1.45f, 0.3f), new Vector3(0.3f, 0.12f, 0.2f), Color.white);
                WearBackpack();
            }
            _gunVisual = Primitives.Shape(PrimitiveType.Cube, transform, new Vector3(0.25f, 1.05f, 0.45f), new Vector3(0.1f, 0.1f, 0.5f),
                new Color(0.15f, 0.15f, 0.15f)).transform;
        }

        /// <summary>
        /// Owner, 2026-10-05: every character carries the owner's backpack (military camouflage) on their back. The model's
        /// straps are on its -z side, so it is turned round to put them against the back; it sits high on the back, its
        /// size in proportion to the character (about 0.75 m). Character models carry it in their own mesh
        /// (CharacterBuilder), flush on the back.
        /// </summary>
        private void WearBackpack()
        {
            var mesh = Art.PackArt.Model("backpack");
            if (mesh == null) return;
            const float size = 0.75f;
            var go = new GameObject("Backpack");
            go.transform.SetParent(transform, false);
            var bodyRadius = 0.35f * BodyWidth;
            go.transform.localPosition = new Vector3(0f, 0.68f, -(bodyRadius + size * 0.5f - 0.14f));
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Art.ArtMaterials.Stylized;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public FirearmState ActiveFirearm =>
            Selected == Slot.Pistol ? Pistol : Selected == Slot.LongGun ? LongGun : null;

        public WeaponId? LongGunId => LongGun?.Def.id;

        /// <summary>
        /// In the helicopter (owner, 2026-10-08: the players arrive in it and leave in it): the team's players, the AI
        /// teammate and a freed hostage. Hidden, can't act, can't be hurt, and nobody targets them.
        /// </summary>
        public bool Boarded => Helicopter.Aboard && Team == PlayerTeam && (!IsHostage || IsFreed) && !IsDead;

        private bool _shownBoarded;

        private void LateUpdate()
        {
            var boarded = Boarded;
            if (boarded == _shownBoarded) return;
            _shownBoarded = boarded;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !boarded;
            foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = !boarded;
        }

        private void Update()
        {
            if (IsDead || Tuning == null) return;
            if (GameSession.Instance != null && !GameSession.Instance.Playing) return;
            if (Boarded) return;
            var dt = Time.deltaTime;
            var input = InputSource != null ? InputSource.Read(this) : default;
            if (Replica)
            {
                UpdateReplica(input, dt);
                return;
            }
            HostageRegen(dt);

            Move(input, dt);

            if (_switchLeft > 0f)
            {
                _switchLeft -= dt;
                if (_switchLeft <= 0f) Selected = _switchTarget;
            }

            _knifeCooldown -= dt;
            UpdateHealing(dt);
            var holding = Selected;
            var firing = input.FireHeld && !Switching && !Healing;
            Pistol.Tick(dt, firing && holding == Slot.Pistol, holding == Slot.Pistol && !Switching);
            var longDone = LongGun != null && LongGun.Tick(dt, firing && holding == Slot.LongGun, holding == Slot.LongGun && !Switching);
            // Shell by shell (2026-10-05): a click per shell going in, the pump once the gun is full.
            if (LongGun != null && LongGun.PerShell)
            {
                if (LongGun.ShellsLoaded > 0) Sfx.Play(SoundId.ShotgunShell, transform.position, 0.9f);
                if (longDone && LongGun.Magazine > 0) Sfx.Play(SoundId.ShotgunPump, transform.position, 0.9f);
            }

            if (input.Reload) ActiveFirearm?.RequestReload();
            // The owner's reload sounds (2026-10-04): when a reload starts, by the button or on an empty magazine.
            var inHand = ActiveFirearm;
            var reloading = inHand != null && inHand.Reloading;
            if (reloading && !_wasReloading && !inHand.PerShell) Sfx.Play(ReloadSound(inHand.Def.id), transform.position, 0.9f);
            _wasReloading = reloading;
            if (input.Pickup) TryPickupNearest();

            AutoSwitchWhenEmpty();
            if (!Switching && !Healing) UseSelected(input, dt);
            else CancelGrenadeAim();

            UpdateGunVisual();
        }

        /// <summary>
        /// Owner, 2026-09-30: when the weapon in hand runs out of ammo (long gun: magazine and reserve empty; grenade:
        /// none left), switch to the Pistol automatically.
        /// </summary>
        private void AutoSwitchWhenEmpty()
        {
            if (Switching) return;
            var empty = (Selected == Slot.LongGun && (LongGun == null || LongGun.Dry)) ||
                        (Selected == Slot.Grenade && Inventory.Grenades <= 0);
            if (empty) Select(Slot.Pistol);
        }

        private void Move(CharacterInput input, float dt)
        {
            var move = new Vector3(input.Move.x, 0f, input.Move.y);
            if (move.sqrMagnitude > 1f) move.Normalize();
            Moving = move.sqrMagnitude > 0.01f;
            // D-009 (aim in any direction) with the owner's facing rule (2026-10-07): the body always faces the aim, walking
            // or not (the legs walk forwards or backwards under it).
            if (input.Aim.sqrMagnitude > 0.0001f) AimDirection = new Vector3(input.Aim.x, 0f, input.Aim.y).normalized;
            var engaged = Time.time < _faceAimUntil || GrenadeAiming || (input.FireHeld && Selected != Slot.Grenade && !Switching);
            _engaged = engaged;
            var face = AimDirection;
            if (face.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), TurnSpeed * dt);
            if (RemoteDriven)
            {
                // Host: an online teammate's own device moves them; follow its reported position (walls still apply).
                var step = RemoteTarget - transform.position;
                step.y = 0f;
                NoticeTeleport();
                if (_controller.enabled && step.sqrMagnitude > 0.0001f) _controller.Move(step);
                var q = transform.position;
                // Stay on the ground: the controller can ride up onto a zombie or barrel.
                if (Mathf.Abs(q.y) > 0.001f) transform.position = new Vector3(q.x, 0f, q.z);
                KeepOutOfWalls();
                return;
            }
            var velocity = move * MoveSpeed + _knockback;
            NoticeTeleport();
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, Tuning.player.knockbackDamping * dt * Mathf.Max(1f, _knockback.magnitude));
            if (_controller.enabled) _controller.Move(velocity * dt);
            var p = transform.position;
            if (Mathf.Abs(p.y) > 0.001f) transform.position = new Vector3(p.x, 0f, p.z);
            KeepOutOfWalls();
        }

        // ---- Walls hold (owner, 2026-10-09: pushed by zombies against the fence, a character went through it) ----

        /// <summary>What a character can't pass: walls and obstacles, fences and the Endless fence line.</summary>
        private const int WallMask = (1 << Layers.Obstacle) | (1 << Layers.ShootThrough);
        /// <summary>Where the guard last left the character: anything else that moves it since was a teleport.</summary>
        private Vector3 _afterGuard;

        /// <summary>
        /// Teleports (spawns, boarding the helicopter, a reconnect, a respawn) set the position directly, never through
        /// the controller: if the character isn't where the guard left it, its old clear place doesn't count. Every move
        /// through the controller is checked, however long: zombies crowding a character overlap it, and the controller
        /// pushes it out of them by a metre or more in a single frame (owner, 2026-10-11: still through the fence).
        /// </summary>
        private void NoticeTeleport()
        {
            if (_hasClear && (transform.position - _afterGuard).sqrMagnitude > 0.0001f) _hasClear = false;
        }

        private Vector3 _clear;
        private bool _hasClear;

        /// <summary>
        /// Zombies don't push through the physics: crowding a character against a thin wall, they overlap it, and the
        /// controller then resolves the overlap without regard for walls, so it could put the character into or through
        /// the wall. Each frame the character's last place clear of every wall is kept; if the line from there to where
        /// it is now crosses a wall, it was carried into or through one, and goes back.
        /// </summary>
        private void KeepOutOfWalls()
        {
            if (_controller == null || !_controller.enabled) return;
            var p = transform.position;
            if (_hasClear)
            {
                var from = _clear + Vector3.up * 0.6f;
                var to = p + Vector3.up * 0.6f;
                var gone = (to - from).magnitude;
                if (gone > 0.001f && Physics.Linecast(from, to, WallMask, QueryTriggerInteraction.Ignore))
                {
                    _controller.enabled = false;
                    transform.position = _clear;
                    _controller.enabled = true;
                    _knockback = Vector3.zero;
                    _afterGuard = transform.position;
                    return;
                }
            }
            var r = _controller.radius * 0.9f;
            var inWall = Physics.CheckCapsule(p + Vector3.up * (r + 0.05f), p + Vector3.up * Mathf.Max(r + 0.1f, _controller.height - r), r, WallMask,
                QueryTriggerInteraction.Ignore);
            if (!inWall)
            {
                _clear = p;
                _hasClear = true;
            }
            _afterGuard = p;
        }

        private void UseSelected(CharacterInput input, float dt)
        {
            switch (Selected)
            {
                case Slot.Pistol:
                case Slot.LongGun:
                    CancelGrenadeAim();
                    var gun = ActiveFirearm;
                    if (gun != null && input.FireHeld && gun.TryFire()) Shoot(gun, input.AimAssist);
                    break;
                case Slot.Knife:
                    CancelGrenadeAim();
                    if (input.FireHeld && _knifeCooldown <= 0f) Stab();
                    break;
                case Slot.Grenade:
                    AimGrenade(input, dt);
                    break;
            }
        }

        // ---- Online friend: their own shots at once (owner, 2026-10-08: lowering the lag) ----

        private float _predictCooldown;
        private readonly System.Collections.Generic.Queue<float> _predicted = new System.Collections.Generic.Queue<float>();

        /// <summary>
        /// The online friend's own gun, fired on their device the moment they shoot: the flash, the tracer, the sound and
        /// the kick at once, with the gun's own rhythm. Only a picture: the Host fires the real shot from their input and
        /// decides what it hits (its own copy of this shot isn't drawn here again). The magazine the friend knows comes
        /// from the Host a moment late, so the shots shown since then (one round trip) are counted off it.
        /// </summary>
        private void PredictFire(CharacterInput input, float dt)
        {
            _predictCooldown -= dt;
            var round = Mathf.Max(0.1f, (NetSession.Instance != null ? NetSession.Instance.PingMs : 150) / 1000f + 0.1f);
            while (_predicted.Count > 0 && Time.time - _predicted.Peek() > round) _predicted.Dequeue();
            if (!input.FireHeld || _predictCooldown > 0f || _switchLeft > 0f) return;
            if (Selected != Slot.Pistol && Selected != Slot.LongGun) return;
            var gun = ActiveFirearm;
            if (gun == null || gun.Reloading || gun.Magazine - _predicted.Count <= 0) return;
            _predictCooldown = gun.Def.shotInterval;
            _predicted.Enqueue(Time.time);
            FireShot(gun, input.AimAssist, true);
        }

        private void Shoot(FirearmState gun, bool assist) => FireShot(gun, assist, false);

        /// <param name="picture">The online friend's own copy (no damage, nothing sent, no noise for the zombies).</param>
        private void FireShot(FirearmState gun, bool assist, bool picture)
        {
            var def = gun.Def;
            // Owner, 2026-10-04: bullets leave from the gun's muzzle. If the muzzle pokes through a wall or into a zombie
            // (standing right against it), they start at the body instead, so the shot still meets that wall or zombie.
            // A character model holds the gun in a posed hold: its real muzzle, at about bullet height (bullets fly level
            // at chest height so low zombies and barrels are still hit).
            // Owner, 2026-10-07 ("fix every issue related to height ... must not occur again"): a shot goes exactly along
            // the aim line from the body, so what the player aims at is in its path whatever its size, and it hits
            // anything there whatever its height (Projectiles). It starts level with the gun's muzzle along that line;
            // the tracer is drawn from the muzzle itself and glides onto the line.
            var muzzle = HasModel && HeldTransform != null ? HeldMuzzle() : transform.TransformPoint(_muzzle);
            _faceAimUntil = Time.time + FaceAimTime;
            var aim = AimDirection;
            if (assist) aim = ShotMath.Assist(aim, transform.position, def.range, Tuning.aimAssist.angleDeg, Tuning.aimAssist.strength);
            var centre = new Vector3(transform.position.x, FlightHeight, transform.position.z);
            var toMuzzle = muzzle - centre;
            toMuzzle.y = 0f;
            var ahead = Mathf.Max(0f, Vector3.Dot(toMuzzle, aim));
            var origin = centre + aim * ahead;
            // A wall, zombie or barrel between the body and there (pressed against a wall, a zombie at your feet): the
            // shot starts at the body, so it meets it.
            var bottom = new Vector3(centre.x, Projectiles.HitBottom + PointBlankRadius, centre.z);
            var top = new Vector3(centre.x, Projectiles.HitTop - PointBlankRadius, centre.z);
            if (Physics.SphereCast(centre, Projectiles.Radius, aim, out _, ahead, 1 << Layers.Obstacle, QueryTriggerInteraction.Ignore)
                || Physics.CapsuleCast(bottom, top, PointBlankRadius, aim, out _, ahead, (1 << Layers.Zombie) | (1 << Layers.Barrel), QueryTriggerInteraction.Ignore)
                || Physics.CheckCapsule(bottom + aim * PointBlankRadius, top + aim * PointBlankRadius, PointBlankRadius, (1 << Layers.Zombie) | (1 << Layers.Barrel), QueryTriggerInteraction.Ignore))
                origin = centre;
            var visualFrom = muzzle - origin;
            // D-019: the whole shot (and the shotgun's fixed pattern) deviates inside the current cone.
            var cone = gun.SpreadDeg(Moving);
            var deviation = ((float)_rng.NextDouble() * 2f - 1f) * cone;
            for (var i = 0; i < def.pellets; i++)
            {
                var direction = ShotMath.Rotate(aim, deviation + ShotMath.PelletOffset(i, def.pellets, def.patternWidthDeg));
                if (picture) Projectiles.Fire(origin, direction, def.projectileSpeed, def.range, 0f, def.pierce, -1, visualFrom, NetId, false);
                else Projectiles.Fire(origin, direction, def.projectileSpeed, def.range, GunDamage(def.damage), def.pierce, Team, visualFrom, NetId);
            }
            if (!picture) Noise.Emit(transform.position, def.noiseRange * (1f - NoiseReduction));
            // The online friend hears their own shots as they fire (their picture); the Host doesn't send them back.
            if (picture || InputSource is RemoteInput) Sfx.PlayLocal(ShotSound(def.id), transform.position, 1f);
            else Sfx.Play(ShotSound(def.id), transform.position);
            // Owner, 2026-10-04: a simple muzzle flash.
            if (_flash == null) _flash = Art.MuzzleFlash.Attach(transform);
            // The flash at the gun's own muzzle.
            _flash.Fire(HasModel && HeldTransform != null ? transform.InverseTransformPoint(HeldMuzzle()) : _muzzle, def.id == WeaponId.Shotgun || def.id == WeaponId.Rifle ? 1.4f : def.id == WeaponId.Pistol ? 0.8f : 1.05f);
            _puppet?.Fired(def.id == WeaponId.Shotgun || def.id == WeaponId.Rifle ? 1f : def.id == WeaponId.Pistol ? 0.5f : 0.7f);
        }

        /// <summary>Development captures only: fires the gun in hand once (to see the muzzle flash and the shot).</summary>
        /// <summary>Development captures only: a knife stab.</summary>
        internal void DevStab() => Stab();

        internal void DevFire()
        {
            var gun = ActiveFirearm;
            if (gun != null && gun.TryFire()) Shoot(gun, false);
        }

        private float NoiseReduction => Character != null ? Mathf.Clamp01(Character.noiseReduction) : 0f;


        /// <summary>A bullet or pellet: the character's Attack bonus (guns only, owner 2026-10-01). No critical hits (owner, 2026-10-03).</summary>
        internal float GunDamage(float damage) => damage * (1f + (Character != null ? Character.attackBonus : 0f));

        /// <summary>A Knife hit: the character's melee multiplier.</summary>
        internal float MeleeDamage(float damage) => damage * (Character != null ? Character.meleeMultiplier : 1f);

        private bool _wasReloading;

        private static SoundId ReloadSound(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Shotgun: return SoundId.ShotgunReload;
                case WeaponId.AR:
                case WeaponId.SMG: return SoundId.ArReload;
                case WeaponId.Rifle: return SoundId.RifleReload;
                default: return SoundId.PistolReload;
            }
        }

        private static SoundId ShotSound(WeaponId id)
        {
            switch (id)
            {
                case WeaponId.Shotgun: return SoundId.ShotgunShot;
                case WeaponId.AR: return SoundId.ArShot;
                case WeaponId.SMG: return SoundId.SmgShot;
                case WeaponId.Rifle: return SoundId.RifleShot;
                default: return SoundId.PistolShot;
            }
        }

        private static readonly Collider[] MeleeHits = new Collider[32];

        private void Stab()
        {
            var knife = Tuning.knife;
            _knifeCooldown = knife.interval;
            _puppet?.Stabbed();
            // The stab goes where the character aims: turn there at once.
            _faceAimUntil = Time.time + FaceAimTime;
            transform.rotation = Quaternion.LookRotation(AimDirection);
            var center = transform.position + Vector3.up;
            var count = Physics.OverlapSphereNonAlloc(center, knife.reach, MeleeHits, (1 << Layers.Zombie) | (1 << Layers.Barrel) | (1 << Layers.Survivor));
            for (var i = 0; i < count; i++)
            {
                var target = MeleeHits[i].GetComponentInParent<IDamageable>();
                if (target is Survivor other && (other == this || other.Team == Team)) continue;
                if (target == null || target.IsDead) continue;
                var to = MeleeHits[i].transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(AimDirection, to) > knife.arcDeg * 0.5f) continue;
                target.TakeDamage(MeleeDamage(knife.damage), transform.position, false);
            }
            Sfx.Play(SoundId.KnifeSwing, transform.position);
            KnifeFx.Spawn(transform, knife.reach, knife.arcDeg);
        }

        private void AimGrenade(CharacterInput input, float dt)
        {
            var g = Tuning.grenade;
            if (Inventory.Grenades <= 0)
            {
                CancelGrenadeAim();
                return;
            }
            float distance;
            // Android: the marker goes where the fire stick points, as far out as it is dragged, moving freely while
            // held; letting go throws it there (owner, 2026-10-07).
            if (input.ThrowOnRelease || (_charging && _touchThrow))
            {
                if (input.FireHeld && input.GrenadeDistance >= 0f)
                {
                    _charging = true;
                    _touchThrow = true;
                    _touchDistance = Mathf.Clamp(input.GrenadeDistance, g.minRange, g.maxRange);
                    GrenadeAiming = true;
                    GrenadeTarget = ThrowPoint(_touchDistance);
                    return;
                }
                if (_charging && _touchThrow)
                {
                    _touchThrow = false;
                    Throw(_touchDistance);
                }
                return;
            }
            if (input.GrenadeDistance >= 0f)
            {
                // PC: the marker follows the cursor, clamped to the throw range; click throws.
                distance = Mathf.Clamp(input.GrenadeDistance, g.minRange, g.maxRange);
                GrenadeAiming = true;
                GrenadeTarget = ThrowPoint(distance);
                if (input.FirePressed) Throw(distance);
                return;
            }
            // Android: hold Fire, the marker moves out from the minimum range; release throws.
            if (input.FirePressed)
            {
                _charging = true;
                _grenadeCharge = 0f;
            }
            if (_charging && input.FireHeld) _grenadeCharge = Mathf.Min(1f, _grenadeCharge + dt / Mathf.Max(0.01f, g.chargeTime));
            distance = Mathf.Lerp(g.minRange, g.maxRange, _grenadeCharge);
            GrenadeAiming = _charging;
            GrenadeTarget = ThrowPoint(distance);
            if (_charging && (input.FireReleased || !input.FireHeld)) Throw(distance);
        }

        private Vector3 ThrowPoint(float distance)
        {
            var start = transform.position + Vector3.up * 0.5f;
            var direction = AimDirection;
            // Stop in front of a wall instead of flying through it.
            if (Physics.SphereCast(start, 0.15f, direction, out var hit, distance, Layers.ObstacleMask))
                distance = Mathf.Max(0.5f, hit.distance - 0.3f);
            var point = transform.position + direction * distance;
            point.y = 0f;
            return point;
        }

        private void Throw(float distance)
        {
            _charging = false;
            GrenadeAiming = false;
            if (!Inventory.RemoveGrenade()) return;
            Grenade.Throw(transform.position + Vector3.up * 1.2f, ThrowPoint(distance), Tuning.grenade);
            Sfx.Play(SoundId.Throw, transform.position);
        }

        private void CancelGrenadeAim()
        {
            _charging = false;
            _touchThrow = false;
            GrenadeAiming = false;
        }

        /// <summary>Held weapons are this much thicker and taller than the model's own proportions (2026-10-04).</summary>
        public const float Chunky = 1.5f;
        /// <summary>
        /// The held pistol's length (m). Owner, 2026-10-06: 0.7 looked much bigger than the other weapons; 0.42 keeps it
        /// in proportion with the rifles (1.0–1.6 m) and the character. PLACEHOLDER.
        /// </summary>
        public const float PistolSize = 0.42f;
        /// <summary>
        /// Held long-gun lengths (m). Owner, 2026-10-06 (option A): every long gun ×0.6 of its first size (rifle 1.4,
        /// shotgun 1.3, sniper 1.6, SMG 1.0) so the characters' own arms reach the real-world holds (support hand on the
        /// handguard / pump / under the forend). PLACEHOLDER.
        /// </summary>
        public const float RifleSize = 0.84f, ShotgunSize = 0.78f, SniperSize = 0.96f, SmgSize = 0.6f;
        /// <summary>The weapon hand, to the right of the body's centre (the body is drawn 1 m wide).</summary>
        public const float HandX = 0.42f;
        /// <summary>How wide a point-blank shot looks for a target in front of the body (m).</summary>
        public const float PointBlankRadius = 0.35f;
        /// <summary>The height shots fly at (m), for where walls and cover stop them; zombies are hit at any height.</summary>
        public const float FlightHeight = 1f;
        public const float MuzzleHeight = 1f;
        private MeshFilter _heldModel;
        private Vector3 _muzzle = new Vector3(0.25f, 1.05f, 1f);
        private Art.MuzzleFlash _flash;
        private string _heldId;
        private Vector3 _heldBasePos;
        private Quaternion _heldBaseRot = Quaternion.identity;

        /// <summary>The weapon model in hand (null when none shows), for the character animation.</summary>
        public Transform HeldTransform => _heldModel != null && _heldModel.gameObject.activeSelf ? _heldModel.transform : null;
        public Vector3 HeldBasePosition => _heldBasePos;
        public string HeldId => _heldId;

        /// <summary>The held weapon model's muzzle, in the world (as posed this frame).</summary>
        public Vector3 HeldMuzzle()
        {
            var held = HeldTransform;
            return held != null ? held.TransformPoint(Art.PackArt.MuzzleOf(_heldId)) : transform.TransformPoint(_muzzle);
        }
        public Quaternion HeldBaseRotation => _heldBaseRot;

        /// <summary>
        /// Where the hands go on the weapon in hand, in this survivor's space: the right on the grip, the left on the
        /// fore-end (long guns) or under the right (the Pistol). Weapon models lie along +x, muzzle at +x, longest side 1.
        /// </summary>
        public (Vector3 right, Vector3 left) HeldGrips()
        {
            var held = HeldTransform;
            if (held == null) return (new Vector3(HandX, MuzzleHeight, 0.3f), new Vector3(HandX - 0.1f, MuzzleHeight, 0.5f));
            var bore = Art.PackArt.MuzzleOf(_heldId).y;
            Vector3 r, l;
            switch (_heldId)
            {
                case "pistol": r = new Vector3(-0.22f, bore - 0.22f, 0f); l = new Vector3(-0.16f, bore - 0.26f, 0f); break;
                case "knife": r = new Vector3(-0.32f, 0f, 0f); l = r; break;
                case "grenade": r = Vector3.zero; l = r; break;
                default: r = new Vector3(-0.2f, bore - 0.12f, 0f); l = new Vector3(0.16f, bore - 0.08f, 0f); break;
            }
            Vector3 P(Vector3 p) => held.localPosition + held.localRotation * Vector3.Scale(held.localScale, p);
            return (P(r), P(l));
        }

        /// <summary>
        /// The pack model for what's in hand, and its length in metres (the owner's pack, 2026-10-04). Sizes after Brawl
        /// Stars (owner, 2026-10-04): chunky, oversized weapons; a long gun is about three quarters of the character's
        /// height (1.8 m), a pistol or knife about a third.
        /// </summary>
        private (string id, float size) HeldModel()
        {
            switch (Selected)
            {
                case Slot.Pistol: return ("pistol", PistolSize);
                case Slot.Knife: return ("knife", 0.8f);
                case Slot.Grenade: return Inventory.Grenades > 0 ? ("grenade", 0.3f) : (null, 0f);
                default:
                    if (LongGun == null) return (null, 0f);
                    switch (LongGun.Def.id)
                    {
                        case WeaponId.Shotgun: return ("shotgun", ShotgunSize);
                        case WeaponId.Rifle: return ("sniper_gun", SniperSize);
                        case WeaponId.SMG: return ("smg_gun", SmgSize);
                        default: return ("ar_gun", RifleSize);
                    }
            }
        }

        private void UpdateGunVisual()
        {
            if (_gunVisual == null) return;
            // Owner, 2026-10-09: the scientist carries nothing (no gun in hand; their arms hang).
            if (IsHostage)
            {
                _gunVisual.gameObject.SetActive(false);
                if (_heldModel != null) _heldModel.gameObject.SetActive(false);
                return;
            }
            var (id, size) = HeldModel();
            if (id != null && Art.PackArt.Has(id))
            {
                _gunVisual.gameObject.SetActive(false);
                if (_heldModel == null)
                {
                    var go = new GameObject("Held weapon");
                    go.transform.SetParent(transform, false);
                    _heldModel = go.AddComponent<MeshFilter>();
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = Art.ArtMaterials.Stylized;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (_heldId != id)
                {
                    _heldId = id;
                    // It turns with the character, so the whole model is kept (no hidden-face cut).
                    _heldModel.sharedMesh = Art.PackArt.Model(id);
                    // The models lie along +x with the muzzle at +x; the character faces +z.
                    _heldModel.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                    // Chunky, toy-like (after Brawl Stars): thicker than the model's own proportions.
                    _heldModel.transform.localScale = new Vector3(size, size * Chunky, size * Chunky);
                    // Held at the hip on the right, the grip near the body: the centre sits ahead of the hand.
                    // Placed so the muzzle is at bullet height (bullets fly at 1 m, so low zombies and barrels are hit).
                    var tip = Art.PackArt.MuzzleOf(id).y * size * Chunky;
                    _heldModel.transform.localPosition = new Vector3(HandX, MuzzleHeight - tip, 0.2f + size * 0.3f);
                    _heldBasePos = _heldModel.transform.localPosition;
                    _heldBaseRot = _heldModel.transform.localRotation;
                }
                _heldModel.gameObject.SetActive(true);
                var held = _heldModel.transform;
                _muzzle = held.localPosition + held.localRotation * Vector3.Scale(held.localScale, Art.PackArt.MuzzleOf(id));
                return;
            }
            if (_heldModel != null) _heldModel.gameObject.SetActive(false);
            var show = Selected == Slot.Pistol || (Selected == Slot.LongGun && LongGun != null);
            _gunVisual.gameObject.SetActive(show || Selected == Slot.Knife);
            var length = Selected == Slot.Pistol ? 0.3f : Selected == Slot.Knife ? 0.35f : 0.8f;
            _gunVisual.localScale = new Vector3(Selected == Slot.Knife ? 0.04f : 0.1f, 0.1f, length);
            _gunVisual.localPosition = new Vector3(0.25f, 1.05f, 0.25f + length * 0.5f);
            _muzzle = new Vector3(0.25f, MuzzleHeight, 0.3f + length);
        }

        // ---- Commands from the weapon panel / inventory screen (become server RPCs with networking) ----

        public bool CanSelect(Slot slot)
        {
            switch (slot)
            {
                case Slot.LongGun: return LongGun != null;
                case Slot.Grenade: return Inventory.Grenades > 0;
                default: return true;
            }
        }

        /// <summary>D-022: free manual switching; D-122: switching takes time.</summary>
        public void Select(Slot slot)
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.Select, (int)slot);
                return;
            }
            if (IsDead || !CanSelect(slot)) return;
            if (slot == Selected && !Switching) return;
            _switchTarget = slot;
            _switchLeft = Tuning.player.switchTime;
            CancelGrenadeAim();
            CancelHealing();
        }

        // ---- Healing (owner, 2026-09-30: using a Health Item takes time) ----

        private float _healLeft;
        public bool Healing => _healLeft > 0f;
        public float HealProgress => Healing ? 1f - _healLeft / Tuning.player.healTime : 0f;

        /// <summary>
        /// Starts using a Health Item. PLACEHOLDER rules: you can move but not attack while healing; switching weapon
        /// or taking a hit cancels it, and the item is only used up when the heal completes.
        /// </summary>
        public void UseHealthItem()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.UseHealth);
                return;
            }
            if (IsDead || Healing || Hp >= MaxHp || Inventory.HealthItems <= 0) return;
            _healLeft = Mathf.Max(0.01f, Tuning.player.healTime);
            CancelGrenadeAim();
        }

        private void UpdateHealing(float dt)
        {
            if (!Healing) return;
            _healLeft -= dt;
            if (_healLeft > 0f) return;
            _healLeft = 0f;
            if (!Inventory.RemoveHealthItem()) return;
            Hp = Mathf.Min(MaxHp, Hp + MaxHp * Tuning.player.healPercent);
            Sfx.Play(SoundId.Heal, transform.position);
        }

        public void CancelHealing() => _healLeft = 0f;

        public void DropLongGun()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.DropLongGun);
                return;
            }
            if (LongGun == null) return;
            LootItem.SpawnLongGun(transform.position, LongGun.Def, LongGun.Magazine);
            LongGun = null;
            Sfx.Play(SoundId.Drop, transform.position);
        }

        public void DropAmmo(AmmoType type, int amount)
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.DropAmmo, (int)type, amount);
                return;
            }
            var removed = Inventory.RemoveAmmo(type, amount);
            if (removed <= 0) return;
            LootItem.SpawnAmmo(transform.position, type, removed, respawns: false);
            Sfx.Play(SoundId.Drop, transform.position);
        }

        public void DropHealthItem()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.DropHealth);
                return;
            }
            if (!Inventory.RemoveHealthItem()) return;
            LootItem.Spawn(transform.position, LootKind.Health);
            Sfx.Play(SoundId.Drop, transform.position);
        }

        /// <summary>Drops the C4 or the Vaccine here; it stays on the ground until someone takes it.</summary>
        public void DropMissionItem(LootKind kind)
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.DropMission, (int)kind);
                return;
            }
            if (kind == LootKind.C4 && Inventory.C4) Inventory.C4 = false;
            else if (kind == LootKind.Vaccine && Inventory.Vaccine) Inventory.Vaccine = false;
            else return;
            LootItem.Spawn(transform.position, kind);
            Sfx.Play(SoundId.Drop, transform.position);
        }

        /// <summary>Bomb stages: close enough to the target incubator to plant the C4 (owner, 2026-10-02).</summary>
        public bool CanPlantC4 => !IsDead && Inventory.C4 && Mission.CanPlantFrom(transform.position);

        /// <summary>Rescue stages: next to the hostage, before they follow anyone (owner, 2026-10-03: press to rescue).</summary>
        public bool CanFreeHostage
        {
            get
            {
                var hostage = GameSession.Instance != null ? GameSession.Instance.Hostage : null;
                if (hostage == null || hostage.IsDead || hostage.IsFreed || IsDead) return false;
                return (hostage.transform.position - transform.position).magnitude <= Tuning.rescue.reachDistance;
            }
        }

        public void FreeHostage()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.FreeHostage);
                return;
            }
            var hostage = GameSession.Instance != null ? GameSession.Instance.Hostage : null;
            if (hostage != null && hostage.InputSource is HostageBrain brain) brain.Free(this);
        }

        public void PlantC4()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.PlantC4);
                return;
            }
            if (!CanPlantC4) return;
            Inventory.C4 = false;
            Mission.Plant();
        }

        public void DropGrenade()
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.DropGrenade);
                return;
            }
            if (!Inventory.RemoveGrenade()) return;
            LootItem.Spawn(transform.position, LootKind.Grenade);
            Sfx.Play(SoundId.Drop, transform.position);
        }

        // ---- Pickup ----

        public LootItem NearestLoot()
        {
            LootItem best = null;
            var bestSqr = Tuning.player.pickupRange * Tuning.player.pickupRange;
            foreach (var item in LootItem.All)
            {
                var d = item.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                best = item;
            }
            return best;
        }

        public Chest NearestChest()
        {
            Chest best = null;
            var bestSqr = Tuning.player.pickupRange * Tuning.player.pickupRange;
            foreach (var chest in Chest.All)
            {
                var d = chest.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                best = chest;
            }
            return best;
        }

        /// <summary>Raised when Pickup is pressed next to an equipment chest: the HUD opens its contents.</summary>
        public event System.Action<Chest> ChestOpened;

        /// <summary>The Pickup button: the nearer of a ground item and an equipment chest.</summary>
        public void TryPickupNearest()
        {
            var item = NearestLoot();
            var chest = NearestChest();
            if (chest != null && (item == null || Distance(chest.transform) < Distance(item.transform)))
            {
                chest.MarkOpened();
                ChestOpened?.Invoke(chest);
                return;
            }
            if (item != null) TryPickup(item);
        }

        private float Distance(Transform other)
        {
            var d = other.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>D-032: only on request. Returns a message for the HUD when nothing was taken.</summary>
        public string TryPickup(LootItem item)
        {
            if (Replica)
            {
                NetSync.Command(this, NetCommand.Pickup, item.NetId);
                return null;
            }
            var data = item.Data;
            var before = data.Amount;
            var result = Receive(ref data, item.transform.position);
            // Take() also reports an emptied respawning ammo box (D-206).
            if (before - data.Amount > 0) item.Take(before - data.Amount);
            return result;
        }

        /// <summary>Takes one entry from an equipment chest (same rules as picking up from the ground).</summary>
        public string TakeFromChest(Chest chest, int index)
        {
            // The AI (and online requests) open a chest by taking from it.
            chest.MarkOpened();
            if (Replica)
            {
                NetSync.Command(this, NetCommand.TakeFromChest, chest.NetId, index);
                return null;
            }
            if (IsDead || chest == null || index < 0 || index >= chest.Contents.Count) return null;
            if (Distance(chest.transform) > Tuning.player.pickupRange + 1f) return null;
            var data = chest.Contents[index];
            var result = Receive(ref data, transform.position);
            Chest.Changed();
            if (data.Amount <= 0) chest.Contents.RemoveAt(index);
            else chest.Contents[index] = data;
            return result;
        }

        /// <summary>
        /// Moves loot into this survivor's hands or inventory. <paramref name="data"/>.Amount is what's left afterwards
        /// (0 when all of it was taken). Returns a message when nothing could be taken.
        /// </summary>
        private string Receive(ref LootData data, Vector3 swapDropPosition)
        {
            string result = null;
            switch (data.Kind)
            {
                case LootKind.LongGun:
                    // Owner, 2026-10-09: an AI teammate keeps up to three, one of each kind; the one in its hands goes spare.
                    if (KeepsSpareGuns && LongGun != null && !HasGun(data.Weapon) && LongGunCount < AiLongGuns)
                    {
                        SpareGuns.Add(LongGun);
                        LongGun = null;
                    }
                    // D-035 (owner, 2026-09-30): one long gun; picking up another drops the current one here.
                    if (LongGun != null)
                    {
                        LootItem.SpawnLongGun(swapDropPosition, LongGun.Def, LongGun.Magazine);
                        LongGun = null;
                    }
                    var def = Tuning.Firearm(data.Weapon);
                    LongGun = new FirearmState(def, data.Magazine, new InventoryReserve(Inventory, def.ammo));
                    // Owner, 2026-10-03: picking up a long gun switches to it (unless it has nothing to shoot).
                    if (!LongGun.Dry) Select(Slot.LongGun);
                    data.Amount = 0;
                    break;
                case LootKind.Ammo:
                    // Endless mode (owner, 2026-10-07): picked-up ammo +50 %.
                    var bonus = Endless.Active ? Tuning.endless.ammoMultiplier : 1f;
                    var added = Inventory.AddAmmo(data.Ammo, Mathf.CeilToInt(data.Amount * bonus));
                    if (added <= 0) result = "Inventory full";
                    data.Amount = Mathf.Max(0, data.Amount - Mathf.CeilToInt(added / bonus));
                    break;
                case LootKind.Health:
                    if (Inventory.AddHealthItem()) data.Amount = 0;
                    else result = "Inventory full";
                    break;
                case LootKind.C4:
                case LootKind.Vaccine:
                    // Owner, 2026-10-02: only players carry mission items, and only one of each exists.
                    // Owner, 2026-10-03: the Vaccine stage's rival team may take the Vaccine (never the C4).
                    if (Team == PlayerTeam ? !IsHuman : data.Kind != LootKind.Vaccine) result = "Only players can carry this";
                    else
                    {
                        if (data.Kind == LootKind.C4) Inventory.C4 = true;
                        else Inventory.Vaccine = true;
                        data.Amount = 0;
                    }
                    break;
                case LootKind.Grenade:
                    if (Inventory.AddGrenade()) data.Amount = 0;
                    else result = "Inventory full";
                    break;
            }
            if (result != null) Message?.Invoke(result);
            else Sfx.Play(SoundId.Pickup, transform.position);
            return result;
        }


        // ---- Networking ----

        public int NetId;
        /// <summary>Online client: this character is shown, not simulated. The client's own one still moves locally.</summary>
        public bool Replica;
        /// <summary>Host: this is the online teammate; their device reports where they are.</summary>
        public bool RemoteDriven;
        public Vector3 RemoteTarget;
        private Transform _visual;
        private readonly RemoteMotion _motion = new RemoteMotion();
        private RemoteVisual _remoteVisual;

        /// <summary>
        /// What is drawn of the character (the body, its legs' speed, the flashlight, the shadow). It is the character
        /// itself, except on the Host for the online friend: drawn where their smooth playback is, a moment behind, while
        /// the character (hits, pickups, zombies) stays where they really are (owner, 2026-10-07).
        /// </summary>
        public Transform Visual => _visual != null ? _visual : transform;

        /// <summary>Host: one of the online friend's own movement updates (their clock, their position, their turn).</summary>
        public void AddRemoteSample(float sent, Vector3 position, float yaw)
        {
            _motion.Add(sent, Time.unscaledTime, position, yaw);
            if (_remoteVisual == null && _visual != null) _remoteVisual = gameObject.AddComponent<RemoteVisual>().Init(_visual, _motion);
        }
        private Vector3 _netPosition;
        private bool _hasNetState;
        private float _netYaw;

        public void AddKnockback(Vector3 push) => _knockback += push;

        private void UpdateReplica(CharacterInput input, float dt)
        {
            if (InputSource != null)
            {
                // The client's own character: movement is local (client-authoritative); everything else goes to the Host.
                Move(input, dt);
                PredictFire(input, dt);
                if (input.Pickup) TryPickupNearest();
                if (input.Reload) NetSync.Command(this, NetCommand.Reload);
            }
            else if (_hasNetState)
            {
                // Played back smoothly from the Host's updates (owner, 2026-10-07: the teammate looked jittery).
                if (_motion.Get(Time.unscaledTime, out var p, out var yaw))
                {
                    transform.position = new Vector3(p.x, transform.position.y, p.z);
                    transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }
            UpdateGunVisual();
        }

        public SurvivorState Capture()
        {
            var p = transform.position;
            return new SurvivorState
            {
                X = p.x, Z = p.z, Yaw = transform.eulerAngles.y, Hp = Hp, Dead = IsDead, Selected = (byte)Selected,
                SwitchTarget = (byte)_switchTarget, Switch = SwitchProgress, PistolMag = (byte)Pistol.Magazine,
                PistolReload = Pistol.ReloadProgress, LongGun = (sbyte)(LongGun != null ? (int)LongGun.Def.id : -1),
                LongMag = (byte)(LongGun != null ? LongGun.Magazine : 0), LongReload = LongGun != null ? LongGun.ReloadProgress : 0f,
                Shotgun = (short)Inventory.Ammo(AmmoType.Shotgun), Ar = (short)Inventory.Ammo(AmmoType.AR), Rifle = (short)Inventory.Ammo(AmmoType.Rifle), Smg = (short)Inventory.Ammo(AmmoType.SMG), Mission = (byte)((Inventory.C4 ? 1 : 0) | (Inventory.Vaccine ? 2 : 0)),
                Health = (byte)Inventory.HealthItems, Grenades = (byte)Inventory.Grenades, Heal = HealProgress,
                GrenadeAiming = GrenadeAiming, GrenadeX = GrenadeTarget.x, GrenadeZ = GrenadeTarget.z, Moving = Moving,
                Freed = IsHostage && InputSource is HostageBrain brain && brain.Freed,
            };
        }

        /// <summary>Online client: take the Host's view of this character (position only for the other player).</summary>
        /// <param name="sent">The Host's clock when this was sent.</param>
        public void ApplyState(in SurvivorState s, float sent)
        {
            if (InputSource == null)
            {
                _netPosition = new Vector3(s.X, 0f, s.Z);
                _hasNetState = true;
                _netYaw = s.Yaw;
                Moving = s.Moving;
                _motion.Add(sent, Time.unscaledTime, _netPosition, s.Yaw);
            }
            if (s.Hp < Hp - 0.01f) LastHitTime = Time.time;
            Hp = s.Hp;
            Selected = (Slot)s.Selected;
            _switchTarget = (Slot)s.SwitchTarget;
            _switchLeft = s.Switch < 1f ? (1f - s.Switch) * Tuning.player.switchTime : 0f;
            Pistol.SetReplica(s.PistolMag, s.PistolReload);
            if (s.LongGun < 0) LongGun = null;
            else
            {
                var def = Tuning.Firearm((WeaponId)s.LongGun);
                if (LongGun == null || LongGun.Def != def) LongGun = new FirearmState(def, s.LongMag, new InventoryReserve(Inventory, def.ammo));
                LongGun.SetReplica(s.LongMag, s.LongReload);
            }
            Inventory.SetReplica(s.Shotgun, s.Ar, s.Rifle, s.Smg, s.Health, s.Grenades);
            Inventory.C4 = (s.Mission & 1) != 0;
            Inventory.Vaccine = (s.Mission & 2) != 0;
            _healLeft = s.Heal > 0f ? (1f - s.Heal) * Tuning.player.healTime : 0f;
            GrenadeAiming = s.GrenadeAiming;
            HostageFreed = s.Freed;
            GrenadeTarget = new Vector3(s.GrenadeX, 0f, s.GrenadeZ);
            if (s.Dead && !IsDead) Die();
        }

        /// <summary>After host migration: take the character over as a normally simulated one.</summary>
        public void BecomeSimulated()
        {
            Replica = false;
            RemoteDriven = false;
            if (InputSource == null && _hasNetState) transform.position = _netPosition;
        }
        // ---- Damage ----

        public void TakeDamage(float damage, Vector3 from, bool fromExplosion) => TakeHit(damage, from, fromExplosion ? 6f : 0f);

        /// <summary>Owner, 2026-10-09: the scientist heals this share of their HP a second, once unhurt this long (s). PLACEHOLDER.</summary>
        public const float HostageRegenPerSecond = 0.05f;
        public const float HostageRegenDelay = 5f;

        private void HostageRegen(float dt)
        {
            if (!IsHostage || Hp >= MaxHp || Time.time - LastHitTime < HostageRegenDelay) return;
            Hp = Mathf.Min(MaxHp, Hp + MaxHp * HostageRegenPerSecond * dt);
        }

        // ---- The AI's spare long guns (owner, 2026-10-09) ----

        /// <summary>An AI teammate carries up to this many long guns, one of each kind (owner, 2026-10-09).</summary>
        public const int AiLongGuns = 3;

        /// <summary>An AI teammate's long guns besides the one in its hands.</summary>
        public readonly List<FirearmState> SpareGuns = new List<FirearmState>();

        /// <summary>Keeps several long guns (an AI teammate; players keep one, D-035).</summary>
        public bool KeepsSpareGuns => !IsHuman && !IsHostage && Team == PlayerTeam;

        /// <summary>Has a long gun of this kind (in hand or spare).</summary>
        public bool HasGun(WeaponId id)
        {
            if (LongGun != null && LongGun.Def.id == id) return true;
            foreach (var g in SpareGuns) if (g.Def.id == id) return true;
            return false;
        }

        public int LongGunCount => (LongGun != null ? 1 : 0) + SpareGuns.Count;

        /// <summary>The AI's gun in hand ran dry: takes a spare that can still shoot. True when it swapped.</summary>
        public bool SwapToLoadedGun()
        {
            if (LongGun != null && !LongGun.Dry) return false;
            var spare = SpareGuns.Find(g => !g.Dry);
            if (spare == null) return false;
            SpareGuns.Remove(spare);
            if (LongGun != null) SpareGuns.Add(LongGun);
            LongGun = spare;
            Select(Slot.LongGun);
            return true;
        }

        /// <summary>A hit: HP and knockback (D-108). Owner, 2026-10-05: a hit no longer interrupts a reload (D-121 retired).</summary>
        public void TakeHit(float damage, Vector3 from, float knockback)
        {
            if (IsDead || Boarded) return;
            Hp -= damage;
            LastHitTime = Time.time;
            CancelHealing();
            _puppet?.Hurt();
            if (knockback > 0f)
            {
                var away = transform.position - from;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
                // An online teammate's device owns their movement: it applies the push there.
                if (RemoteDriven) NetSync.Knockback(this, away.normalized * knockback);
                else _knockback += away.normalized * knockback;
            }
            Sfx.Play(SoundId.PlayerHurt, transform.position);
            if (IsHuman && GameSession.Instance != null) GameSession.Instance.Haptic();
            if (Hp <= 0f) Die();
        }

        /// <summary>
        /// The corpse stays 20 s and everything in the inventory can be taken (owner, 2026-09-29): long gun, ammo,
        /// Health Items, grenades. The Pistol and Knife belong to every character and are not lootable.
        /// </summary>
        private void LeaveCorpse()
        {
            var parent = GameSession.Instance != null ? GameSession.Instance.StageRoot : transform.parent;
            var corpse = Chest.Corpse(parent, this, Tuning.player.corpseSeconds);
            if (LongGun != null) corpse.Contents.Add(LootData.LongGun(LongGun.Def, LongGun.Magazine));
            foreach (var spare in SpareGuns) corpse.Contents.Add(LootData.LongGun(spare.Def, spare.Magazine));
            SpareGuns.Clear();
            foreach (var type in Arsenal.AmmoTypes)
                if (Inventory.Ammo(type) > 0) corpse.Contents.Add(LootData.AmmoBox(type, Inventory.RemoveAmmo(type, Inventory.Ammo(type))));
            while (Inventory.RemoveHealthItem()) corpse.Contents.Add(LootData.Single(LootKind.Health));
            while (Inventory.RemoveGrenade()) corpse.Contents.Add(LootData.Single(LootKind.Grenade));
            // Owner, 2026-10-02: a mission item stays with the body; if the body goes before someone takes it, it is lost.
            if (Inventory.C4) corpse.Contents.Add(LootData.Single(LootKind.C4));
            if (Inventory.Vaccine) corpse.Contents.Add(LootData.Single(LootKind.Vaccine));
            Inventory.C4 = Inventory.Vaccine = false;
            LongGun = null;
        }

        private void Die()
        {
            Hp = 0f;
            IsDead = true;
            CancelGrenadeAim();
            _controller.enabled = false;
            gameObject.layer = Layers.Ground;
            // Corpse pose (placeholder): lie down.
            transform.rotation = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(-90f, 0f, 0f);
            transform.position += Vector3.up * 0.35f;
            Sfx.Play(SoundId.PlayerDeath, transform.position);
            if (World.Simulating) LeaveCorpse();
            Died?.Invoke(this);
        }
    }

    /// <summary>
    /// The knife's slash (owner, 2026-10-07: an arc, not a straight line): a crescent at chest height across the knife's
    /// arc in front of the character, sweeping open from right to left, then thinning away. It shows the swing; the hit
    /// area is the knife's own reach and arc (Survivor.Stab).
    /// </summary>
    public class KnifeFx : MonoBehaviour
    {
        private const int Segments = 20;
        private const float Sweep = 0.1f, Fade = 0.12f;
        private float _t;
        private float _reach, _arc;
        private Mesh _mesh;
        private readonly Vector3[] _v = new Vector3[(Segments + 1) * 2];

        public static void Spawn(Transform owner, float reach, float arcDeg = 100f)
        {
            NetSync.Knife(owner);
            var go = new GameObject("Knife slash");
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            var fx = go.AddComponent<KnifeFx>();
            fx._reach = reach;
            fx._arc = arcDeg;
            fx._mesh = new Mesh { name = "Knife slash" };
            // Both faces, so it shows whichever way the material culls.
            var tris = new int[Segments * 12];
            for (var i = 0; i < Segments; i++)
            {
                var a = i * 2;
                var t = i * 12;
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
                tris[t + 6] = a; tris[t + 7] = a + 1; tris[t + 8] = a + 2;
                tris[t + 9] = a + 1; tris[t + 10] = a + 3; tris[t + 11] = a + 2;
            }
            fx.Build(0f, 1f);
            fx._mesh.triangles = tris;
            go.AddComponent<MeshFilter>().sharedMesh = fx._mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Primitives.Material(new Color(1f, 1f, 1f, 0.75f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>The crescent drawn from its right end to <paramref name="open"/> (0..1 of the arc), <paramref name="width"/> 0..1 thick.</summary>
        private void Build(float open, float width)
        {
            var half = _arc * 0.5f;
            for (var i = 0; i <= Segments; i++)
            {
                var k = i / (float)Segments;
                // From the right (+half) to the left (-half); segments past the sweep collapse onto its leading edge.
                var a = Mathf.Lerp(half, -half, Mathf.Min(k, open)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                // Thickest in the middle, pointed at both ends.
                var thick = Mathf.Sin(k * Mathf.PI) * 0.32f * width * _reach;
                var outer = _reach * 0.95f;
                _v[i * 2] = dir * outer;
                _v[i * 2 + 1] = dir * (outer - thick);
            }
            _mesh.vertices = _v;
            _mesh.RecalculateBounds();
        }

        private void Update()
        {
            _t += Time.deltaTime;
            if (_t >= Sweep + Fade)
            {
                Destroy(_mesh);
                Destroy(gameObject);
                return;
            }
            Build(Mathf.Clamp01(_t / Sweep), _t < Sweep ? 1f : 1f - (_t - Sweep) / Fade);
        }
    }

    /// <summary>
    /// Host, the online friend: draws their body where their smooth playback is, each frame before the legs are animated
    /// (the character itself stays at their real, latest position).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RemoteVisual : MonoBehaviour
    {
        private Transform _visual;
        private RemoteMotion _motion;

        public RemoteVisual Init(Transform visual, RemoteMotion motion)
        {
            _visual = visual;
            _motion = motion;
            return this;
        }

        private void LateUpdate()
        {
            if (_visual == null || _motion == null || !_motion.Get(Time.unscaledTime, out var p, out var yaw)) return;
            _visual.SetPositionAndRotation(new Vector3(p.x, transform.position.y, p.z), Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
