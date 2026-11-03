using System;

namespace ZombieGame
{
    /// <summary>
    /// The general inventory (D-090, §11): one slot budget shared by ammo, Health Items and grenades.
    /// Weapons use no slots (D-148). Pure logic, no Unity objects.
    /// </summary>
    public class Inventory
    {
        private readonly InventoryTuning _rules;
        private readonly int[] _ammo = new int[Enum.GetValues(typeof(AmmoType)).Length];

        public int HealthItems { get; private set; }
        public int Grenades { get; private set; }
        /// <summary>Mission items (owner, 2026-10-02): no slots; only players carry them.</summary>
        public bool C4 { get; set; }
        public bool Vaccine { get; set; }

        private readonly int _bonusSlots;

        public Inventory(InventoryTuning rules, int bonusSlots = 0)
        {
            _rules = rules;
            _bonusSlots = bonusSlots;
        }

        /// <summary>D-139 base capacity plus the character's bonus slots.</summary>
        public int Capacity => _rules.capacity + _bonusSlots;

        public int UsedSlots
        {
            get
            {
                var used = HealthItems * _rules.healthSlots + Grenades * _rules.grenadeSlots;
                for (var i = 0; i < _ammo.Length; i++) used += _ammo[i] * _rules.SlotCost((AmmoType)i);
                return used;
            }
        }

        public int FreeSlots => Math.Max(0, Capacity - UsedSlots);

        public int Ammo(AmmoType type) => type == AmmoType.None ? 0 : _ammo[(int)type];

        /// <summary>Adds as much as fits; returns how many rounds were added (D-091: nothing when full).</summary>
        public int AddAmmo(AmmoType type, int amount)
        {
            if (type == AmmoType.None || amount <= 0) return 0;
            var fits = FreeSlots / _rules.SlotCost(type);
            var added = Math.Min(amount, fits);
            _ammo[(int)type] += added;
            return added;
        }

        public int RemoveAmmo(AmmoType type, int amount)
        {
            if (type == AmmoType.None || amount <= 0) return 0;
            var removed = Math.Min(amount, _ammo[(int)type]);
            _ammo[(int)type] -= removed;
            return removed;
        }

        public bool AddHealthItem()
        {
            if (FreeSlots < _rules.healthSlots) return false;
            HealthItems++;
            return true;
        }

        public bool RemoveHealthItem()
        {
            if (HealthItems <= 0) return false;
            HealthItems--;
            return true;
        }

        public bool AddGrenade()
        {
            if (FreeSlots < _rules.grenadeSlots) return false;
            Grenades++;
            return true;
        }

        /// <summary>Online client: mirror the Host's inventory.</summary>
        public void SetReplica(int shotgun, int ar, int rifle, int smg, int health, int grenades)
        {
            _ammo[(int)AmmoType.Shotgun] = shotgun;
            _ammo[(int)AmmoType.AR] = ar;
            _ammo[(int)AmmoType.Rifle] = rifle;
            _ammo[(int)AmmoType.SMG] = smg;
            HealthItems = health;
            Grenades = grenades;
        }

        public bool RemoveGrenade()
        {
            if (Grenades <= 0) return false;
            Grenades--;
            return true;
        }
    }
}
