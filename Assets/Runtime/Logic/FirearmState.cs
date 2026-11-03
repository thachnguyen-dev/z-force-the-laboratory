using System;

namespace ZombieGame
{
    /// <summary>Where a gun's reserve ammo comes from: the owner's inventory, or nowhere-and-endless for the Pistol.</summary>
    public interface IAmmoReserve
    {
        int Reserve { get; }
        bool Unlimited { get; }
        int Take(int amount);
    }

    public sealed class UnlimitedReserve : IAmmoReserve
    {
        public static readonly UnlimitedReserve Instance = new UnlimitedReserve();
        public int Reserve => int.MaxValue;
        public bool Unlimited => true;
        public int Take(int amount) => amount;
    }

    public sealed class InventoryReserve : IAmmoReserve
    {
        private readonly Inventory _inventory;
        private readonly AmmoType _type;

        public InventoryReserve(Inventory inventory, AmmoType type)
        {
            _inventory = inventory;
            _type = type;
        }

        public int Reserve => _inventory.Ammo(_type);
        public bool Unlimited => false;
        public int Take(int amount) => _inventory.RemoveAmmo(_type, amount);
    }

    /// <summary>
    /// One firearm's magazine, reload, fire cooldown and spread bloom (D-118–D-130, D-019). Pure logic.
    /// </summary>
    public class FirearmState
    {
        public readonly FirearmDef Def;
        public int Magazine;
        public float Bloom { get; private set; }

        /// <summary>Guns firing this far apart (s) or more fire one shot at a time (their spread settles between shots).</summary>
        public const float SingleShotInterval = 0.3f;

        private float _cooldown;
        private float _reloadLeft;
        private IAmmoReserve _reserve;

        public FirearmState(FirearmDef def, int magazine, IAmmoReserve reserve)
        {
            Def = def;
            Magazine = Math.Max(0, Math.Min(def.magazine, magazine));
            _reserve = reserve;
        }

        public IAmmoReserve ReserveSource => _reserve;
        public void SetReserve(IAmmoReserve reserve) => _reserve = reserve;

        public bool Reloading => _reloadLeft > 0f;

        /// <summary>Shell-by-shell reloading (the shotgun, 2026-10-05).</summary>
        public bool PerShell => Def.shellTime > 0f;

        /// <summary>The length of one reload step: a shell, or the whole magazine.</summary>
        private float StepTime => PerShell ? Def.shellTime : Def.reloadTime;

        /// <summary>0 → 1 through the current reload step (a shell, or the whole magazine).</summary>
        public float ReloadProgress => Reloading ? 1f - _reloadLeft / StepTime : 0f;
        public int Reserve => _reserve.Reserve;
        public bool CanReload => !Reloading && Magazine < Def.magazine && _reserve.Reserve > 0;
        /// <summary>D-142: magazine and reserve both empty.</summary>
        public bool Dry => Magazine == 0 && _reserve.Reserve == 0;

        /// <summary>Shells that went in during the last tick (for the shell sound).</summary>
        public int ShellsLoaded { get; private set; }

        /// <summary>Manual reload (D-130). Returns false when there's nothing to reload.</summary>
        public bool RequestReload()
        {
            if (!CanReload) return false;
            _reloadLeft = StepTime;
            return true;
        }

        public void CancelReload() => _reloadLeft = 0f;

        /// <summary>Online client: mirror the Host's magazine and reload progress.</summary>
        public void SetReplica(int magazine, float reloadProgress)
        {
            Magazine = magazine;
            _reloadLeft = reloadProgress > 0f ? (1f - reloadProgress) * StepTime : 0f;
        }

        /// <summary>
        /// Returns true when a reload finished during this tick (a magazine went in, or the last shell). A gun that isn't
        /// in hand (<paramref name="inHand"/> false) doesn't reload: switching away cancels it (PLACEHOLDER rule).
        /// Shell by shell (owner, 2026-10-05): every step adds one round, so the count climbs as you reload, and the gun
        /// can fire between shells (firing ends the reload).
        /// </summary>
        public bool Tick(float dt, bool firing, bool inHand = true)
        {
            ShellsLoaded = 0;
            _cooldown = Math.Max(0f, _cooldown - dt);
            // D-019: the spread widens with continuous firing. Automatic guns (the AR, the SMG) keep widening while the
            // trigger is held; a gun that fires one shot at a time (the pistol, the shotgun, the sniper) settles back
            // between its shots even with the trigger held (owner, 2026-10-07: the sniper, held, sprayed at its widest
            // and couldn't hit a Rat).
            if (!firing || Def.shotInterval >= SingleShotInterval) Bloom = Math.Max(0f, Bloom - Def.bloomRecoveryDegPerSec * dt);
            if (!inHand)
            {
                _reloadLeft = 0f;
                return false;
            }

            var finished = false;
            if (Reloading)
            {
                _reloadLeft -= dt;
                if (_reloadLeft <= 0f)
                {
                    if (PerShell)
                    {
                        var shell = _reserve.Take(1);
                        Magazine += shell;
                        ShellsLoaded += shell;
                        if (shell > 0 && Magazine < Def.magazine && _reserve.Reserve > 0) _reloadLeft += Def.shellTime;
                        else
                        {
                            _reloadLeft = 0f;
                            finished = true;
                        }
                    }
                    else
                    {
                        _reloadLeft = 0f;
                        Magazine += _reserve.Take(Def.magazine - Magazine);
                        finished = true;
                    }
                }
            }
            // D-130: an empty magazine reloads by itself.
            if (!Reloading && Magazine == 0 && _reserve.Reserve > 0) _reloadLeft = StepTime;
            return finished;
        }

        /// <summary>Shell-by-shell guns can fire mid-reload with what's loaded; others wait for the magazine.</summary>
        public bool ReadyToFire => _cooldown <= 0f && Magazine > 0 && (!Reloading || PerShell);

        public bool TryFire()
        {
            if (!ReadyToFire) return false;
            // Firing a shell-by-shell gun ends its reload; press reload (or empty it) to carry on loading.
            if (Reloading) _reloadLeft = 0f;
            Magazine--;
            _cooldown = Def.shotInterval;
            Bloom = Math.Min(Def.maxSpreadDeg, Bloom + Def.bloomPerShotDeg);
            return true;
        }

        /// <summary>Current cone half-angle in degrees (D-019: grows with continuous fire and movement).</summary>
        public float SpreadDeg(bool moving) =>
            Math.Min(Def.maxSpreadDeg, Def.baseSpreadDeg + Bloom + (moving ? Def.moveSpreadDeg : 0f));
    }
}
