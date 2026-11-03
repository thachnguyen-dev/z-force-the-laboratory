using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>Co-op with one human and the AI teammate, in the real test stage.</summary>
    public class CoopTests
    {
        private GameSession _session;

        private Survivor Human => _session.LocalSurvivor;
        private Survivor Ai => _session.AiSurvivor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 4242, withAi: true);
            yield return null;
            // A quiet start: only the Main Boss stays.
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            foreach (var item in LootItem.All.ToArray()) Object.Destroy(item.gameObject);
            foreach (var chest in Chest.All.ToArray()) Object.Destroy(chest.gameObject);
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

        [UnityTest]
        public IEnumerator TheAiCollectsTwoHealthItemsFirstAndHealsBelowHalf()
        {
            // Owner, 2026-10-09: Health Items first until it has two; it uses one below 50 % HP.
            var t = _session.Tuning;
            var at = Ai.transform.position;
            LootItem.Spawn(at + new Vector3(2f, 0f, 0f), LootData.AmmoBox(AmmoType.AR, 30));
            LootItem.Spawn(at + new Vector3(-6f, 0f, 0f), LootKind.Health);
            LootItem.Spawn(at + new Vector3(-6f, 0f, 1.5f), LootKind.Health);
            LootItem.Spawn(at + new Vector3(-6f, 0f, -1.5f), LootKind.Health);
            Ai.TryPickup(LootItem.SpawnLongGun(at, t.Firearm(WeaponId.AR), 30));
            for (var i = 0; i < 300 && Ai.Inventory.HealthItems < 2; i++) yield return null;
            Assert.AreEqual(2, Ai.Inventory.HealthItems, "two Health Items, before the ammo next to it");
            for (var i = 0; i < 60; i++) yield return null;
            Assert.AreEqual(2, Ai.Inventory.HealthItems, "no third");
            Assert.AreEqual(1, System.Linq.Enumerable.Count(LootItem.All, item => item.Kind == LootKind.Health), "the third stays on the ground");
            // Below half its HP, nothing near: it heals.
            Ai.TakeHit(Ai.MaxHp * 0.55f, Ai.transform.position + Vector3.forward, 0f);
            for (var i = 0; i < 20 && !Ai.Healing; i++) yield return null;
            Assert.IsTrue(Ai.Healing, "heals below 50 %");
        }

        [UnityTest]
        public IEnumerator TheAiKeepsThreeLongGunsOneOfEachKind()
        {
            // Owner, 2026-10-09: up to 3 long guns, each kind picked up once; then only ammo and items.
            var t = _session.Tuning;
            Ai.TryPickup(LootItem.SpawnLongGun(Ai.transform.position, t.Firearm(WeaponId.AR), 30));
            Ai.TryPickup(LootItem.SpawnLongGun(Ai.transform.position, t.Firearm(WeaponId.Shotgun), 6));
            Ai.TryPickup(LootItem.SpawnLongGun(Ai.transform.position, t.Firearm(WeaponId.SMG), 35));
            yield return null;
            Assert.AreEqual(3, Ai.LongGunCount, "three guns, none dropped");
            Assert.IsTrue(Ai.HasGun(WeaponId.AR) && Ai.HasGun(WeaponId.Shotgun) && Ai.HasGun(WeaponId.SMG));
            var wants = typeof(AiTeammate).GetMethod("Wants", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var brain = Ai.GetComponent<AiTeammate>();
            Assert.IsFalse((bool)wants.Invoke(brain, new object[] { LootData.LongGun(t.Firearm(WeaponId.AR), 30) }), "a kind it has");
            Assert.IsFalse((bool)wants.Invoke(brain, new object[] { LootData.LongGun(t.Firearm(WeaponId.Rifle), 5) }), "a fourth gun");
            Assert.IsTrue((bool)wants.Invoke(brain, new object[] { LootData.AmmoBox(AmmoType.Shotgun, 18) }), "ammo for a spare gun");
            // The gun in hand runs dry: it takes a loaded spare instead of looking for another gun.
            var inHand = Ai.LongGun.Def.id;
            while (Ai.Inventory.Ammo(Ai.LongGun.Def.ammo) > 0) Ai.Inventory.RemoveAmmo(Ai.LongGun.Def.ammo, Ai.Inventory.Ammo(Ai.LongGun.Def.ammo));
            Ai.LongGun.Magazine = 0;
            Assert.IsTrue(Ai.LongGun.Dry);
            Assert.IsTrue(Ai.SwapToLoadedGun());
            Assert.AreNotEqual(inHand, Ai.LongGun.Def.id);
            Assert.IsFalse(Ai.LongGun.Dry);
            Assert.AreEqual(3, Ai.LongGunCount);
        }

        [UnityTest]
        public IEnumerator D170_CoopHasOneHumanAndOneAi()
        {
            Assert.IsTrue(Human.IsHuman);
            Assert.IsNotNull(Ai);
            Assert.IsFalse(Ai.IsHuman);
            Assert.AreEqual(2, Survivor.All.Count);
            // D-043: the AI starts with exactly what a human gets.
            Assert.IsNotNull(Ai.Pistol);
            Assert.IsNull(Ai.LongGun);
            yield return null;
        }

        [UnityTest]
        public IEnumerator D044_AiPicksUpALongGunAndItsAmmo()
        {
            var at = Ai.transform.position;
            LootItem.SpawnLongGun(at + new Vector3(4f, 0f, 2f), _session.Tuning.Firearm(WeaponId.AR), 30);
            LootItem.SpawnAmmo(at + new Vector3(-3f, 0f, 3f), AmmoType.AR, 30, respawns: false);
            LootItem.SpawnAmmo(at + new Vector3(0f, 0f, -4f), AmmoType.Rifle, 5, respawns: false);
            yield return new WaitForSeconds(6f);
            Assert.AreEqual(WeaponId.AR, Ai.LongGunId);
            Assert.AreEqual(30, Ai.Inventory.Ammo(AmmoType.AR));
            Assert.AreEqual(0, Ai.Inventory.Ammo(AmmoType.Rifle), "only ammo for its own gun (placeholder)");
        }

        [UnityTest]
        public IEnumerator D158_AiShootsAZombieItSees()
        {
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Ai.transform.position + new Vector3(0f, 0f, 9f));
            Assert.IsNotNull(zombie);
            yield return new WaitForSeconds(4f);
            Assert.IsTrue(zombie == null || zombie.IsDead || zombie.Hp < zombie.Def.hp);
        }

        [UnityTest]
        public IEnumerator D166_AiComesBackWhenTheHumanGoesTooFar()
        {
            Teleport(Human, new Vector3(40f, 0f, 14f));
            var before = Vector3.Distance(Ai.transform.position, Human.transform.position);
            Assert.Greater(before, _session.Tuning.ai.maxSeparation);
            yield return new WaitForSeconds(6f);
            var after = Vector3.Distance(Ai.transform.position, Human.transform.position);
            Assert.Less(after, before - 10f);
        }

        [UnityTest]
        public IEnumerator D163_AiHealsBelowTheThreshold()
        {
            LootItem.Spawn(Ai.transform.position, LootKind.Health);
            Ai.TryPickup(LootItem.All[0]);
            Assert.AreEqual(1, Ai.Inventory.HealthItems);
            Ai.TakeHit(Ai.MaxHp * 0.8f, Ai.transform.position, 0f);
            var hurt = Ai.Hp;
            yield return new WaitForSeconds(_session.Tuning.player.healTime + 1f);
            Assert.Greater(Ai.Hp, hurt);
            Assert.AreEqual(0, Ai.Inventory.HealthItems);
        }

        [UnityTest]
        public IEnumerator D101_OnlyTheAiLeftIsDefeat()
        {
            Human.TakeHit(Human.MaxHp + 1f, Human.transform.position, 0f);
            yield return null;
            yield return null;
            Assert.IsFalse(Ai.IsDead);
            Assert.AreEqual(SessionState.Defeat, _session.State);
        }

        [UnityTest]
        public IEnumerator DeadAiLeavesALootableCorpseThatDisappears()
        {
            var rifle = _session.Tuning.Firearm(WeaponId.Rifle);
            Ai.TryPickup(LootItem.SpawnLongGun(Ai.transform.position, rifle, 3));
            Ai.TryPickup(LootItem.SpawnAmmo(Ai.transform.position, AmmoType.Rifle, 5, respawns: false));
            Ai.TakeHit(Ai.MaxHp + 1f, Ai.transform.position, 0f);
            yield return null;
            Assert.AreEqual(SessionState.Playing, _session.State, "D-102: the human carries on");
            Assert.AreEqual(1, Chest.All.Count);
            var corpse = Chest.All[0];
            Assert.AreEqual(_session.Tuning.player.corpseSeconds, corpse.Lifetime, 0.5f);
            Assert.AreEqual(2, corpse.Contents.Count);

            Teleport(Human, corpse.transform.position + Vector3.right);
            Assert.IsNull(Human.TakeFromChest(corpse, 0));
            Assert.AreEqual(WeaponId.Rifle, Human.LongGunId);
            Assert.AreEqual(3, Human.LongGun.Magazine);

            corpse.Lifetime = 0.1f;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, Chest.All.Count, "gone, with what was left on it");
            Assert.IsTrue(Ai == null);
        }

        [UnityTest]
        public IEnumerator D164_AiThrowsAGrenadeIntoACrowdAwayFromTheHuman()
        {
            Ai.TryPickup(LootItem.Spawn(Ai.transform.position, LootKind.Grenade));
            Teleport(Human, Ai.transform.position + new Vector3(-3f, 0f, -2f));
            var crowd = Ai.transform.position + new Vector3(0f, 0f, 10f);
            for (var i = 0; i < 5; i++) _session.Stage.SpawnZombie(ZombieType.Tank, crowd + new Vector3((i - 2) * 0.9f, 0f, 0f));
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(0, Ai.Inventory.Grenades);
        }

        private static void Teleport(Survivor s, Vector3 p)
        {
            var controller = s.GetComponent<CharacterController>();
            controller.enabled = false;
            s.transform.position = p;
            controller.enabled = true;
        }
    }
}
