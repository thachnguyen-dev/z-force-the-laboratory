using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    public class InventoryTests
    {
        private static Inventory NewInventory() => new Inventory(new InventoryTuning());

        [Test]
        public void D139_CapacityIs800()
        {
            Assert.AreEqual(800, NewInventory().Capacity); // owner 2026-10-09: +200; 2026-10-11: +200
            Assert.AreEqual(830, new Inventory(new InventoryTuning(), 30).Capacity); // character bonus slots
        }

        [Test]
        public void D145_AmmoSlotCostsPerBullet()
        {
            var inv = NewInventory();
            inv.AddAmmo(AmmoType.AR, 10);
            inv.AddAmmo(AmmoType.Shotgun, 10);
            inv.AddAmmo(AmmoType.Rifle, 10);
            Assert.AreEqual(10 * 2 + 10 * 7 + 10 * 13, inv.UsedSlots); // owner 2026-10-09: shotgun 7, sniper 13
        }

        [Test]
        public void D146_D147_HealthAndGrenadeSlots()
        {
            var inv = NewInventory();
            Assert.IsTrue(inv.AddHealthItem());
            Assert.IsTrue(inv.AddGrenade());
            Assert.AreEqual(10 + 20, inv.UsedSlots);
        }

        [Test]
        public void D091_FullInventoryTakesNothing()
        {
            var inv = NewInventory();
            Assert.AreEqual(61, inv.AddAmmo(AmmoType.Rifle, 100)); // 793 of 800 slots (13 a sniper round)
            Assert.AreEqual(3, inv.AddAmmo(AmmoType.AR, 3));        // 799 (2 a round)
            Assert.AreEqual(1, inv.AddAmmo(AmmoType.SMG, 5));       // the last slot (1 a round)
            Assert.AreEqual(0, inv.AddAmmo(AmmoType.AR, 1));
            Assert.IsFalse(inv.AddHealthItem());
            Assert.IsFalse(inv.AddGrenade());
        }

        [Test]
        public void AmmoPickupTakesOnlyWhatFits()
        {
            var inv = NewInventory();
            inv.AddAmmo(AmmoType.Rifle, 61); // 793 slots used, 7 free
            Assert.AreEqual(3, inv.AddAmmo(AmmoType.AR, 30));
            Assert.AreEqual(1, inv.AddAmmo(AmmoType.SMG, 30));
            Assert.AreEqual(800, inv.UsedSlots);
        }

        [Test]
        public void D143_AmmoTypesAreSeparate()
        {
            var inv = NewInventory();
            inv.AddAmmo(AmmoType.AR, 5);
            Assert.AreEqual(0, inv.Ammo(AmmoType.Rifle));
            Assert.AreEqual(0, inv.RemoveAmmo(AmmoType.Shotgun, 3));
            Assert.AreEqual(5, inv.Ammo(AmmoType.AR));
        }
    }

    public class FirearmTests
    {
        private static readonly GameTuning Tuning = ScriptableObject.CreateInstance<GameTuning>();

        private static (FirearmState gun, Inventory inv) Ar(int magazine, int reserve)
        {
            var inv = new Inventory(Tuning.inventory);
            inv.AddAmmo(AmmoType.AR, reserve);
            return (new FirearmState(Tuning.Firearm(WeaponId.AR), magazine, new InventoryReserve(inv, AmmoType.AR)), inv);
        }

        private static void Run(FirearmState gun, float seconds, bool firing = false)
        {
            for (var t = 0f; t < seconds; t += 0.02f) gun.Tick(0.02f, firing);
        }

        [Test]
        public void D129_ReloadMovesReserveIntoMagazine()
        {
            var (gun, inv) = Ar(10, 50);
            Assert.IsTrue(gun.RequestReload());
            Run(gun, gun.Def.reloadTime + 0.1f);
            Assert.AreEqual(30, gun.Magazine);
            Assert.AreEqual(30, inv.Ammo(AmmoType.AR));
        }

        [Test]
        public void D130_EmptyMagazineReloadsAutomatically()
        {
            var (gun, _) = Ar(1, 50);
            Assert.IsTrue(gun.TryFire());
            gun.Tick(0.02f, true);
            Assert.IsTrue(gun.Reloading);
        }

        [Test]
        public void D130_ManualReloadNeedsReserve()
        {
            var (gun, _) = Ar(10, 0);
            Assert.IsFalse(gun.RequestReload());
        }

        private static (FirearmState gun, Inventory inv) Shotgun(int magazine, int reserve)
        {
            var inv = new Inventory(Tuning.inventory);
            inv.AddAmmo(AmmoType.Shotgun, reserve);
            return (new FirearmState(Tuning.Firearm(WeaponId.Shotgun), magazine, new InventoryReserve(inv, AmmoType.Shotgun)), inv);
        }

        [Test]
        public void ShotgunLoadsShellByShell()
        {
            // Owner, 2026-10-05: one shell at a time, the count climbing as it loads.
            var (gun, inv) = Shotgun(2, 20);
            Assert.IsTrue(gun.PerShell);
            Assert.IsTrue(gun.RequestReload());
            Run(gun, gun.Def.shellTime + 0.03f);
            Assert.AreEqual(3, gun.Magazine, "one shell after one shell-time");
            Assert.AreEqual(19, inv.Ammo(AmmoType.Shotgun));
            Assert.IsTrue(gun.Reloading, "and it carries on");
            Run(gun, gun.Def.shellTime * 3f + 0.05f);
            Assert.AreEqual(6, gun.Magazine);
            Assert.IsFalse(gun.Reloading, "stops when full");
        }

        [Test]
        public void ShotgunCanFireMidReload()
        {
            // Firing between shells is allowed at once; it ends the reload with what is loaded.
            var (gun, _) = Shotgun(0, 20);
            gun.Tick(0.02f, false);   // empty: reloads by itself
            Assert.IsTrue(gun.Reloading);
            Run(gun, gun.Def.shellTime * 2f + 0.03f);
            Assert.AreEqual(2, gun.Magazine);
            Assert.IsTrue(gun.ReadyToFire, "loaded shells can be fired during the reload");
            Assert.IsTrue(gun.TryFire());
            Assert.AreEqual(1, gun.Magazine);
            Assert.IsFalse(gun.Reloading, "firing ended the reload");
        }

        [Test]
        public void MagazineGunsStillWaitForTheReload()
        {
            var (gun, _) = Ar(5, 50);
            gun.RequestReload();
            Run(gun, 0.2f);
            Assert.IsFalse(gun.ReadyToFire, "the AR can't fire mid-reload");
        }

        [Test]
        public void D142_DryGunCannotFire()
        {
            var (gun, _) = Ar(0, 0);
            Assert.IsTrue(gun.Dry);
            Assert.IsFalse(gun.TryFire());
        }

        [Test]
        public void PistolReserveNeverRunsOutButStillReloads()
        {
            var def = Tuning.Firearm(WeaponId.Pistol);
            var pistol = new FirearmState(def, def.magazine, UnlimitedReserve.Instance);
            for (var round = 0; round < 5; round++)
            {
                while (pistol.Magazine > 0)
                {
                    Assert.IsTrue(pistol.TryFire());
                    Run(pistol, def.shotInterval + 0.02f, true);
                }
                Assert.IsTrue(pistol.Reloading || pistol.Magazine == def.magazine);
                Run(pistol, def.reloadTime + 0.1f);
                Assert.AreEqual(def.magazine, pistol.Magazine);
            }
        }

        [Test]
        public void D019_SpreadGrowsWithFireAndMovementAndRecovers()
        {
            var (gun, _) = Ar(30, 0);
            var still = gun.SpreadDeg(false);
            Assert.Greater(gun.SpreadDeg(true), still);
            for (var i = 0; i < 5; i++)
            {
                gun.TryFire();
                Run(gun, gun.Def.shotInterval + 0.001f, true);
            }
            Assert.Greater(gun.SpreadDeg(false), still);
            Run(gun, 5f);
            Assert.AreEqual(still, gun.SpreadDeg(false), 0.001f);
        }

        [Test]
        public void SwitchingAwayCancelsReload()
        {
            var (gun, _) = Ar(10, 50);
            gun.RequestReload();
            gun.Tick(0.02f, false, inHand: false);
            Assert.IsFalse(gun.Reloading);
        }
    }

    public class ShotTests
    {
        [Test]
        public void D133_ShotgunPatternIsFixedAndSymmetric()
        {
            var def = ScriptableObject.CreateInstance<GameTuning>().Firearm(WeaponId.Shotgun);
            Assert.AreEqual(9, def.pellets); // D-116
            var first = new float[9];
            for (var i = 0; i < 9; i++) first[i] = ShotMath.PelletOffset(i, 9, def.patternWidthDeg);
            for (var i = 0; i < 9; i++)
            {
                Assert.AreEqual(first[i], ShotMath.PelletOffset(i, 9, def.patternWidthDeg));
                Assert.AreEqual(-first[8 - i], first[i], 0.0001f);
            }
            Assert.AreEqual(0f, first[4], 0.0001f);
        }

        [Test]
        public void D136_RiflePierces()
        {
            Assert.IsTrue(ScriptableObject.CreateInstance<GameTuning>().Firearm(WeaponId.Rifle).pierce);
        }
    }

    public class ZombieMindTests
    {
        private static ZombieMind NewMind() => new ZombieMind(new PerceptionTuning(), 1);

        [Test]
        public void D052_HearingSendsZombieToTheSound()
        {
            var mind = NewMind();
            mind.Hear(new Vector3(10f, 0f, 0f));
            Assert.AreEqual(ZombieMode.Investigate, mind.Mode);
            Assert.AreEqual(new Vector3(10f, 0f, 0f), mind.Destination);
        }

        [Test]
        public void D056_VisionBeatsSound()
        {
            var mind = NewMind();
            mind.See(new Vector3(1f, 0f, 1f));
            mind.Hear(new Vector3(30f, 0f, 30f));
            Assert.AreEqual(ZombieMode.Chase, mind.Mode);
        }

        [Test]
        public void D154_NewestSoundWins()
        {
            var mind = NewMind();
            mind.Hear(new Vector3(10f, 0f, 0f));
            mind.Hear(new Vector3(-5f, 0f, 3f));
            Assert.AreEqual(new Vector3(-5f, 0f, 3f), mind.Destination);
        }

        [Test]
        public void D054_LostTargetSearchesLastKnownPosition()
        {
            var mind = NewMind();
            mind.See(new Vector3(4f, 0f, 4f));
            mind.LoseSight();
            Assert.AreEqual(ZombieMode.LostTarget, mind.Mode);
            Assert.AreEqual(new Vector3(4f, 0f, 4f), mind.Destination);
            mind.Arrived();
            Assert.AreEqual(ZombieMode.Search, mind.Mode);
        }

        [Test]
        public void D052_SearchEndsAfterFixedTimeAndStaysInRadius()
        {
            var rules = new PerceptionTuning();
            var mind = new ZombieMind(rules, 3);
            mind.Hear(Vector3.zero);
            mind.Arrived();
            var ticks = (int)((rules.searchDuration - 0.5f) / 0.1f);
            for (var i = 0; i < ticks; i++)
            {
                mind.Tick(0.1f);
                Assert.AreEqual(ZombieMode.Search, mind.Mode);
                Assert.LessOrEqual(mind.Destination.magnitude, rules.searchRadius + 0.001f);
            }
            mind.Tick(0.7f);
            Assert.AreEqual(ZombieMode.Idle, mind.Mode);
        }
    }

    public class MatchTests
    {
        [Test]
        public void D067_MainBossDeathIsVictory() => Assert.AreEqual(MatchOutcome.Victory, MatchRules.Evaluate(true, 1));

        [Test]
        public void D098_NoHumanAliveIsDefeat() => Assert.AreEqual(MatchOutcome.Defeat, MatchRules.Evaluate(false, 0));

        [Test]
        public void D103_BossDeathWinsEvenAfterAHumanDied() => Assert.AreEqual(MatchOutcome.Victory, MatchRules.Evaluate(true, 0));

        [Test]
        public void OngoingWhileAHumanLives() => Assert.AreEqual(MatchOutcome.Ongoing, MatchRules.Evaluate(false, 1));

        [Test]
        public void RescueWinsOnlyWithTheHostageBackAndLosesWithoutThem()
        {
            Assert.AreEqual(MatchOutcome.Ongoing, MatchRules.EvaluateRescue(false, false, 1));
            Assert.AreEqual(MatchOutcome.Victory, MatchRules.EvaluateRescue(true, false, 1));
            Assert.AreEqual(MatchOutcome.Defeat, MatchRules.EvaluateRescue(false, true, 1));
            Assert.AreEqual(MatchOutcome.Defeat, MatchRules.EvaluateRescue(false, false, 0));
        }
    }

    public class DecidedValueTests
    {
        [Test]
        public void OwnerDecidedValues()
        {
            var t = ScriptableObject.CreateInstance<GameTuning>();
            Assert.AreEqual(2f, t.grenade.fuse);
            Assert.AreEqual(20f, t.player.corpseSeconds);
            Assert.AreEqual(80f, t.perception.visionConeDeg);
            Assert.AreEqual(10, t.inventory.healthSlots);
            Assert.AreEqual(20, t.inventory.grenadeSlots);
            // Owner, 2026-10-01: these characters and perks; 2026-10-03: critical hits and Character 03 removed.
            Assert.AreEqual(4, t.characters.Length);
            // The selection's slots (owner, 2026-10-07): 1 New Character 01, 2 New Character 02, 3 Protagonist,
            // 4 New Character 04, each wearing its own model.
            CollectionAssert.AreEqual(new[] { "character_01", "character_02", "protagonist", "character_04" }, t.characters.Select(c => c.id).ToArray());
            // Their perks (owner, 2026-10-07): 1 +20 % Attack, 2 speed, 3 HP, 4 melee.
            Assert.AreEqual(0.2f, t.characters[0].attackBonus);
            Assert.AreEqual(0.2f, t.characters[1].speedBonus);
            Assert.AreEqual(0.2f, t.characters[2].hpBonus);
            Assert.AreEqual(0.2f, t.characters[2].slotBonus);
            for (var i = 0; i < 4; i++)
            {
                var c = t.characters[i];
                var perks = (c.attackBonus > 0f ? 1 : 0) + (c.speedBonus > 0f ? 1 : 0) + (c.hpBonus > 0f ? 1 : 0) + (c.meleeMultiplier > 1f ? 1 : 0);
                Assert.AreEqual(1, perks, $"{c.id}: one perk");
            }
            Assert.AreEqual(2f, t.characters[3].meleeMultiplier);
            Assert.AreEqual(0.3f, t.characters[3].noiseReduction);
            // Owner, 2026-09-30: the Dog has the Basic zombie's HP, and is faster and hits harder.
            var basic = t.Zombie(ZombieType.Basic);
            var dog = t.Zombie(ZombieType.Dog);
            Assert.AreEqual(basic.hp, dog.hp);
            Assert.Greater(dog.chaseSpeed, basic.chaseSpeed);
            Assert.Greater(dog.damage, basic.damage);
        }
    }
}
