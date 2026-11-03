using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Host migration (D-172) without a network: an online client's replica world is taken over and must play on as a
    /// normal simulated match.
    /// </summary>
    public class MigrationTests
    {
        private GameSession _session;
        private Survivor _me;
        private Survivor _formerHost;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var infos = new List<NetSurvivorInfo>
            {
                new NetSurvivorInfo { Id = 0, Name = "Host", Color = Color.blue, IsHuman = true, Position = new Vector3(20f, 0f, 14f) },
                new NetSurvivorInfo { Id = 1, Name = "Me", Color = Color.green, IsHuman = true, Position = new Vector3(14f, 0f, 14f) },
            };
            foreach (var s in _session.StartReplicaStage(StageLibrary.TestStageId, 99, infos, 1))
                if (s.NetId == 0) _formerHost = s;
            _me = _session.LocalSurvivor;
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
        public IEnumerator ReplicasBecomeARealMatch()
        {
            var stage = _session.Stage;
            var zombie = stage.SpawnZombieReplica(ZombieType.Basic, 40, new Vector3(14f, 0f, 20f), 180f);
            var boss = stage.SpawnZombieReplica(ZombieType.Boss, 41, _session.Stage.Layout.BossPosition, 0f);
            var nest = Incubator.SpawnReplica(stage, 42, new Vector3(30f, 0f, 30f), _session.Tuning.incubator);
            Assert.IsFalse(World.Simulating);
            Assert.IsTrue(zombie.Replica);
            Assert.IsTrue(_me.Replica);

            var former = _session.TakeOverWorld();
            Assert.IsTrue(World.Simulating);
            Assert.AreEqual(_formerHost, former);
            Assert.IsFalse(zombie.Replica);
            Assert.IsFalse(nest.Replica);
            Assert.IsFalse(_me.Replica);
            Assert.IsTrue(zombie.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh);
            Assert.AreEqual(boss, stage.MainBoss);

            // The zombie right next to me now thinks for itself (D-172: it restarts from normal behaviour).
            Assert.AreEqual(ZombieMode.Idle, zombie.Mind.Mode);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(ZombieMode.Chase, zombie.Mind.Mode);

            // My actions are mine again, not commands to a Host.
            _me.Select(Slot.Knife);
            Assert.IsTrue(_me.Switching);

            // D-067: the match still ends when the Main Boss dies.
            boss.TakeDamage(boss.Hp + 1f, boss.transform.position, false);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, _session.State);
        }

        [UnityTest]
        public IEnumerator D179_TheFormerHostStandsDefenceless()
        {
            _session.Stage.SpawnZombieReplica(ZombieType.Boss, 41, _session.Stage.Layout.BossPosition, 0f);
            var former = _session.TakeOverWorld();
            var start = former.transform.position;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(start.x, former.transform.position.x, 0.01f);
            Assert.AreEqual(start.z, former.transform.position.z, 0.01f);
            former.TakeHit(30f, former.transform.position, 0f);
            Assert.AreEqual(former.MaxHp - 30f, former.Hp, 0.01f);
            Assert.AreEqual(SessionState.Playing, _session.State);
        }
    }
}
