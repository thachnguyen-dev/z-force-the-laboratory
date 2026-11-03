using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>The real test stage with real physics, NavMesh and zombie AI.</summary>
    public class StageTests
    {
        private GameSession _session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 12345);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            yield return null;
        }

        private Survivor Player => _session.LocalSurvivor;

        [UnityTest]
        public IEnumerator D227_StageStartsWithPistolAndKnifeOnly()
        {
            Assert.IsNotNull(Player.Pistol);
            Assert.IsNull(Player.LongGun);
            Assert.AreEqual(Slot.Pistol, Player.Selected);
            Assert.IsTrue(Player.CanSelect(Slot.Knife));
            Assert.AreEqual(0, Player.Inventory.UsedSlots);
            yield return null;
        }

        [UnityTest]
        public IEnumerator D196_MainBossIsInTheBossAreaFromTheStart()
        {
            var boss = _session.Stage.MainBoss;
            Assert.IsNotNull(boss);
            var p = boss.transform.position;
            Assert.IsTrue(_session.Stage.Layout.BossArea.Contains(new Vector2(p.x, p.z)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator D205_LootIsPlacedOnTheMap()
        {
            var t = _session.Tuning.loot;
            // 2026-10-04: loot scales with the map's area (and the stage's loot amount), each kind rounded.
            var size = _session.Stage.Layout.Size;
            // Owner, 2026-10-11: 10 % more items.
            var scale = _session.Stage.Layout.LootScale * StageRuntime.AreaScale(size.x, size.y) * t.amountMultiplier;
            int S(int n) => Mathf.RoundToInt(n * scale);
            Assert.AreEqual(S(t.longGuns) + S(t.ammoBoxes) + S(t.healthItems) + S(t.grenades), LootItem.All.Count);
            // 2026-10-03: far zombies wait as dormant records; the population as a whole counts.
            Assert.Greater(_session.Stage.Population.Total, 10);
            yield return null;
        }

        [UnityTest]
        public IEnumerator D067_KillingTheMainBossIsVictory()
        {
            var boss = _session.Stage.MainBoss;
            boss.TakeDamage(boss.Hp + 1f, boss.transform.position, false);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, _session.State);
        }

        [UnityTest]
        public IEnumerator D099_SoloDeathIsDefeat()
        {
            Player.TakeHit(Player.MaxHp + 1f, Player.transform.position, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Defeat, _session.State);
        }

        [UnityTest]
        public IEnumerator D035_PickingUpALongGunDropsTheCurrentOne()
        {
            var ar = LootItem.SpawnLongGun(Player.transform.position, _session.Tuning.Firearm(WeaponId.AR), 17);
            Assert.IsNull(Player.TryPickup(ar));
            Assert.AreEqual(WeaponId.AR, Player.LongGunId);
            var rifle = LootItem.SpawnLongGun(Player.transform.position, _session.Tuning.Firearm(WeaponId.Rifle), 5);
            Assert.IsNull(Player.TryPickup(rifle));
            Assert.AreEqual(WeaponId.Rifle, Player.LongGunId);
            yield return null;
            var dropped = Player.NearestLoot();
            Assert.IsNotNull(dropped);
            Assert.AreEqual(LootKind.LongGun, dropped.Kind);
            Assert.AreEqual(WeaponId.AR, dropped.Weapon);
            Assert.AreEqual(17, dropped.Magazine);
        }

        [UnityTest]
        public IEnumerator PickingUpALongGunSwitchesToIt()
        {
            // Owner, 2026-10-03.
            Assert.AreEqual(Slot.Pistol, Player.Selected);
            Player.TryPickup(LootItem.SpawnLongGun(Player.transform.position, _session.Tuning.Firearm(WeaponId.AR), 30));
            yield return new WaitForSeconds(_session.Tuning.player.switchTime + 0.2f);
            Assert.AreEqual(Slot.LongGun, Player.Selected);
        }

        [UnityTest]
        public IEnumerator EmptyLongGunSwitchesToPistol()
        {
            var ar = LootItem.SpawnLongGun(Player.transform.position, _session.Tuning.Firearm(WeaponId.AR), 0);
            Player.TryPickup(ar);
            Player.Select(Slot.LongGun);
            yield return new WaitForSeconds(_session.Tuning.player.switchTime * 3f + 0.2f);
            Assert.AreEqual(Slot.Pistol, Player.Selected);
        }

        [UnityTest]
        public IEnumerator HealthItemTakesTimeAndAHitCancelsIt()
        {
            var item = LootItem.Spawn(Player.transform.position, LootKind.Health);
            Player.TryPickup(item);
            Player.TakeHit(50f, Player.transform.position, 0f);
            var hp = Player.Hp;

            Player.UseHealthItem();
            Assert.IsTrue(Player.Healing);
            yield return new WaitForSeconds(_session.Tuning.player.healTime * 0.5f);
            Assert.AreEqual(hp, Player.Hp);
            Player.TakeHit(1f, Player.transform.position, 0f);
            Assert.IsFalse(Player.Healing);
            Assert.AreEqual(1, Player.Inventory.HealthItems);

            hp = Player.Hp;
            Player.UseHealthItem();
            yield return new WaitForSeconds(_session.Tuning.player.healTime + 0.2f);
            Assert.AreEqual(0, Player.Inventory.HealthItems);
            Assert.AreEqual(Mathf.Min(Player.MaxHp, hp + Player.MaxHp * _session.Tuning.player.healPercent), Player.Hp, 0.01f);
        }

        [UnityTest]
        public IEnumerator DroppedAmmoStaysAndCanBePickedUpAgain()
        {
            var box = LootItem.SpawnAmmo(Player.transform.position, AmmoType.AR, 30, respawns: false);
            Player.TryPickup(box);
            Assert.AreEqual(30, Player.Inventory.Ammo(AmmoType.AR));
            Player.DropAmmo(AmmoType.AR, 12);
            Assert.AreEqual(18, Player.Inventory.Ammo(AmmoType.AR));
            yield return new WaitForSeconds(0.5f);
            var dropped = Player.NearestLoot();
            Assert.IsNotNull(dropped);
            Assert.AreEqual(12, dropped.Amount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator D136_RifleShotPiercesALineOfZombies()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            // An open strip of ground in the south of the map.
            var origin = new Vector3(40f, 1.05f, 8f);
            var first = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(45f, 0f, 8f));
            var second = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(49f, 0f, 8f));
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            yield return new WaitForFixedUpdate();
            var rifle = _session.Tuning.Firearm(WeaponId.Rifle);
            Projectiles.Fire(origin, Vector3.right, rifle.projectileSpeed, rifle.range, 20f, true);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(first.Def.hp - 20f, first.Hp, 0.01f);
            Assert.AreEqual(second.Def.hp - 20f, second.Hp, 0.01f);
        }

        [UnityTest]
        public IEnumerator BulletHitBrieflyStopsAZombie()
        {
            // Owner, 2026-10-04: a zombie hit by a bullet stops moving for a moment.
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(45f, 0f, 8f));
            Assert.IsNotNull(zombie);
            zombie.Mind.Hear(new Vector3(60f, 0f, 8f));
            yield return new WaitForSeconds(0.6f);
            Projectiles.Fire(zombie.transform.position + new Vector3(-4f, 1.05f, 0f), Vector3.right, 60f, 10f, 1f, false);
            yield return new WaitForSeconds(0.1f);
            Assert.Less(zombie.Hp, zombie.Def.hp, "the bullet hit");
            var stopped = zombie.transform.position;
            yield return new WaitForSeconds(0.1f);
            Assert.Less(Vector3.Distance(stopped, zombie.transform.position), 0.05f, "stopped right after the hit");
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(Vector3.Distance(stopped, zombie.transform.position), 0.2f, "moving again afterwards");
        }

        [UnityTest]
        public IEnumerator D051_WallsBlockZombieVision()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            // The warehouse's north wall runs along z = 78 between x 10 and 42.
            Player.GetComponent<CharacterController>().enabled = false;
            Player.transform.position = new Vector3(26f, 0f, 74f);
            Player.GetComponent<CharacterController>().enabled = true;
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(26f, 0f, 83f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.back);
            yield return new WaitForSeconds(0.6f);
            Assert.AreNotEqual(ZombieMode.Chase, zombie.Mind.Mode);
        }

        [UnityTest]
        public IEnumerator D024_GunshotDrawsZombiesToIt()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            // Owner, 2026-10-06: sounds reach 30 % less far: a 15 m shot is heard at 8 m, not at 12 m.
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(8f, 0f, 0f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.right);
            var far = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(-12f, 0f, 0f));
            far.transform.rotation = Quaternion.LookRotation(Vector3.left);
            yield return new WaitForSeconds(0.3f);
            Noise.Emit(Player.transform.position, 15f);
            Assert.AreEqual(ZombieMode.Investigate, zombie.Mind.Mode);
            Assert.AreNotEqual(ZombieMode.Investigate, far.Mind.Mode, "beyond 70 % of the range");
        }

        [UnityTest]
        public IEnumerator BarrelShotExplodesAndHurtsZombies()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            var barrel = Barrel.Spawn(_session.StageRoot, new Vector3(40f, 0f, 14f), _session.Tuning.barrel);
            var zombie = _session.Stage.SpawnZombie(ZombieType.Tank, new Vector3(42f, 0f, 14f));
            yield return new WaitForFixedUpdate();
            barrel.TakeDamage(1f, Vector3.zero, false);
            yield return null;
            Assert.Less(zombie.Hp, zombie.Def.hp);
        }

        [UnityTest]
        public IEnumerator RoofLiftsNearADoorAndInsideAndReturnsOutside()
        {
            var house = FindBuilding("House 1"); // x 50..58, z 34..42, south door at (54, 34)
            Teleport(new Vector3(30f, 0f, 20f));
            yield return null;
            Assert.IsTrue(house.RoofShown);
            Teleport(new Vector3(54f, 0f, 32.5f));
            yield return null;
            Assert.IsFalse(house.RoofShown, "near the door");
            Teleport(new Vector3(54f, 0f, 38f));
            yield return null;
            Assert.IsFalse(house.RoofShown, "inside");
            Teleport(new Vector3(30f, 0f, 20f));
            yield return null;
            Assert.IsTrue(house.RoofShown, "left the building");
        }

        [UnityTest]
        public IEnumerator EquipmentChestItemsAreTakenWithPickupRules()
        {
            var chest = Chest.Spawn(_session.StageRoot, Player.transform.position + Vector3.right, 0f);
            chest.Contents.Add(LootData.AmmoBox(AmmoType.AR, 30));
            chest.Contents.Add(LootData.Single(LootKind.Health));
            chest.Contents.Add(LootData.LongGun(_session.Tuning.Firearm(WeaponId.Shotgun), 6));
            Assert.AreEqual(chest, Player.NearestChest());
            Player.TakeFromChest(chest, 2);
            Assert.AreEqual(WeaponId.Shotgun, Player.LongGunId);
            Player.TakeFromChest(chest, 0);
            Assert.AreEqual(30, Player.Inventory.Ammo(AmmoType.AR));
            Assert.AreEqual(1, chest.Contents.Count);
            Assert.AreEqual(LootKind.Health, chest.Contents[0].Kind);
            yield return null;
        }

        [UnityTest]
        public IEnumerator IncubatorSpawnsZombiesOverTimeAndCanBeDestroyed()
        {
            var rules = new IncubatorTuning { interval = 0.2f, maxAlive = 2, activeRange = 100f, hp = 50f };
            var before = Zombie.Alive.Count;
            var incubator = Incubator.Spawn(_session.Stage, new Vector3(30f, 0f, 20f), rules);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(before + 2, Zombie.Alive.Count, "spawns up to its own limit");
            incubator.TakeDamage(60f, Vector3.zero, false);
            Assert.IsTrue(incubator.IsDead);
        }

        [UnityTest]
        public IEnumerator StagesHaveBuildingsChestsAndIncubators()
        {
            Assert.GreaterOrEqual(Building.All.Count, 8);
            Assert.AreEqual(2, Incubator.All.Count);
            foreach (var chest in Chest.All) Assert.GreaterOrEqual(chest.Contents.Count, _session.Tuning.chest.minItems);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CharacterBonusesApply()
        {
            var character = new CharacterDef { displayName = "Test", hpBonus = 0.5f, speedBonus = 0.2f };
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), character, 7);
            yield return null;
            Assert.AreEqual(_session.Tuning.player.maxHp * 1.5f, Player.MaxHp, 0.001f);
            Assert.AreEqual(Player.MaxHp, Player.Hp);
            Assert.AreEqual(_session.Tuning.player.moveSpeed * 1.2f, Player.MoveSpeed, 0.001f);
        }

        [UnityTest]
        public IEnumerator CharacterSlotsDamageAndQuietShots()
        {
            var character = new CharacterDef { displayName = "Test", slotBonus = 0.2f, attackBonus = 0.2f, meleeMultiplier = 2f, noiseReduction = 0.5f };
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), character, 7);
            yield return null;
            Assert.AreEqual(Mathf.RoundToInt(_session.Tuning.inventory.capacity * 1.2f), Player.Inventory.Capacity);
            Assert.AreEqual(10f * 1.2f, Player.GunDamage(10f), 0.001f, "Attack adds gun damage");
            Assert.AreEqual(10f * 2f, Player.MeleeDamage(10f), 0.001f, "Attack does not touch the Knife; the melee multiplier does");

            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            // A Pistol shot (15 m) at half noise reaches 7.5 m: a zombie 10 m away, facing away, doesn't hear it.
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(10f, 0f, 0f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.right);
            yield return new WaitForSeconds(0.3f);
            Player.Select(Slot.Pistol);
            var gun = Player.Pistol;
            Assert.IsTrue(gun.ReadyToFire);
            var shoot = typeof(Survivor).GetMethod("Shoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            gun.TryFire();
            shoot.Invoke(Player, new object[] { gun, false });
            Assert.AreEqual(ZombieMode.Idle, zombie.Mind.Mode);
        }



        [UnityTest]
        public IEnumerator ZombiesDontSeeIntoBushesFromAfar()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
            // The placeholder bush patch at x 20..24, z 30..33.
            Teleport(new Vector3(21.5f, 0f, 31.5f));
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(21.5f, 0f, 38.5f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.back);
            yield return new WaitForSeconds(0.6f);
            Assert.AreNotEqual(ZombieMode.Chase, zombie.Mind.Mode);
        }

        [UnityTest]
        public IEnumerator AHandMadeStagePlaysFromItsMap()
        {
            // A stage nobody generated: a small map built cell by cell, like the owner would in the Map Editor.
            var map = StageMap.Blank("Hand made", 40, 40);
            for (var x = 0; x < 40; x++) map[x, 0] = map[x, 39] = Tile.Cliff;
            for (var z = 0; z < 40; z++) map[0, z] = map[39, z] = Tile.Cliff;
            for (var z = 10; z < 30; z++) map[20, z] = Tile.Wall;
            map[5, 5] = Tile.PlayerSpawn;
            map[34, 34] = Tile.BossSpawn;
            for (var i = 0; i < 12; i++) map[8 + i, 8] = Tile.LootSpawn;
            map[30, 10] = Tile.ZombieSpawn;
            map.spawnArea = new MapRect { x = 2, z = 2, w = 8, d = 8 };
            map.bossArea = new MapRect { x = 30, z = 30, w = 8, d = 8 };
            map.zombies.Add(new MapZombie { type = "Fast", x = 25, z = 5 });
            map.Pack();
            var layout = StageMapBuilder.ToLayout(StageMap.FromJson(map.ToJson()));
            layout.Id = "hand_made";

            _session.StartStage(layout, _session.Tuning.characters[0], 5);
            yield return null;
            Assert.AreEqual(StageMap.CellCenter(5, 5).x, Player.transform.position.x, 0.01f);
            Assert.IsNotNull(_session.Stage.MainBoss);
            Assert.AreEqual(34.5f, _session.Stage.MainBoss.transform.position.x, 0.6f);
            var fast = 0;
            foreach (var z in Zombie.Alive) if (z.Def.type == ZombieType.Fast) fast++;
            Assert.AreEqual(1, fast, "the zombie placed on its cell");
            var t = _session.Tuning.loot;
            Assert.AreEqual(12, LootItem.All.Count, "one item per loot cell when there are fewer cells than items");
            foreach (var item in LootItem.All) Assert.AreEqual(8.5f, item.transform.position.z, 0.01f, "loot only on loot cells");
        }
        private static Building FindBuilding(string name)
        {
            foreach (var b in Building.All) if (b.Def.Name == name) return b;
            Assert.Fail($"No building {name}");
            return null;
        }

        private void Teleport(Vector3 p)
        {
            var controller = Player.GetComponent<CharacterController>();
            controller.enabled = false;
            Player.transform.position = p;
            controller.enabled = true;
        }
    }
}
