using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>Stage conditions (owner, 2026-09-30): rain, darkness, the rescue mission, the water-jumping zombie.</summary>
    public class ConditionTests
    {
        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        private IEnumerator Start(string stageId)
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stageId), _session.Tuning.characters[0], 31);
            yield return null;
        }

        private IEnumerator ClearZombies()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            yield return null;
        }

        private void Teleport(Survivor s, Vector3 p)
        {
            var c = s.GetComponent<CharacterController>();
            c.enabled = false;
            s.transform.position = p;
            c.enabled = true;
        }

        [UnityTest]
        public IEnumerator RainMufflesGunshotsButDoesNotSlow()
        {
            yield return Start("test_rain");
            var t = _session.Tuning;
            var dogs = 0;
            foreach (var z in Zombie.Alive) if (z.Def.type == ZombieType.Dog) dogs++;
            foreach (var r in _session.Stage.Population.Records) if (r.Type == ZombieType.Dog) dogs++;
            Assert.Greater(dogs, 0, "Dogs appear in Rain stages (owner)");
            // Owner, 2026-10-01: rain no longer slows anyone.
            Assert.AreEqual(t.player.moveSpeed * (1f + Player.Character.speedBonus), Player.MoveSpeed, 0.001f);
            yield return ClearZombies();
            // 12 m away: a 15 m Pistol shot reaches it in clear weather, but not in the rain (15 × 0.6 = 9 m).
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(12f, 0f, 0f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.right);
            yield return new WaitForSeconds(0.3f);
            Noise.Emit(Player.transform.position, 15f);
            Assert.AreEqual(ZombieMode.Idle, zombie.Mind.Mode);
        }

        // ---- The Dog (owner, 2026-10-03) ----

        [UnityTest]
        public IEnumerator NoDogsAtNight()
        {
            // Owner, 2026-10-09: Dog zombies don't appear in night missions.
            yield return Start("test_dark");
            var dogs = 0;
            foreach (var z in Zombie.Alive) if (z.Def.type == ZombieType.Dog) dogs++;
            foreach (var r in _session.Stage.Population.Records) if (r.Type == ZombieType.Dog) dogs++;
            Assert.AreEqual(0, dogs);
        }

        // ---- Day and night (owner, 2026-10-07): day stages favour humanoid zombies, night stages animal zombies ----

        /// <summary>Basic zombies and the animals the Basic share turns into (Dogs, Rats), real and dormant.</summary>
        private (int basic, int dogs, int rats) CountBasicShare()
        {
            int basic = 0, dogs = 0, rats = 0;
            void Count(ZombieType t)
            {
                if (t == ZombieType.Basic) basic++;
                else if (t == ZombieType.Dog) dogs++;
                else if (t == ZombieType.Rat) rats++;
            }
            foreach (var z in Zombie.Alive) Count(z.Def.type);
            foreach (var r in _session.Stage.Population.Records) Count(r.Type);
            return (basic, dogs, rats);
        }

        [UnityTest]
        public IEnumerator NightStagesFavourAnimalZombies()
        {
            yield return Start("test_dark");
            var (basic, dogs, rats) = CountBasicShare();
            Assert.Greater(dogs + rats, basic, "owner: night stages favour animal zombies");
            Assert.Greater(rats, 0, "owner: the Rat");
        }

        [UnityTest]
        public IEnumerator DayStagesFavourHumanoidZombies()
        {
            yield return Start("test_stage");
            var (basic, dogs, rats) = CountBasicShare();
            Assert.Greater(basic, (dogs + rats) * 2, "owner: day stages favour humanoid zombies");
        }

        [Test]
        public void TheRatIsWeakFastAndFragile()
        {
            var t = GameTuning.Load();
            var basic = t.Zombie(ZombieType.Basic);
            var rat = t.Zombie(ZombieType.Rat);
            Assert.Less(rat.hp, basic.hp, "owner: low HP");
            Assert.Less(rat.damage, basic.damage, "owner: low damage");
            Assert.Greater(rat.chaseSpeed, basic.chaseSpeed, "owner: fast");
            Assert.IsTrue(ZombieTypes.IsAnimal(ZombieType.Rat));
            Assert.IsFalse(ZombieTypes.IsBoss(ZombieType.Rat));
        }

        [Test]
        public void TheTigerAndTheBearAreBossesWithoutAbilitiesYet()
        {
            var t = GameTuning.Load();
            foreach (var type in new[] { ZombieType.Tiger, ZombieType.Bear })
            {
                Assert.IsTrue(ZombieTypes.IsBoss(type));
                Assert.IsTrue(ZombieTypes.IsAnimal(type));
                Assert.IsFalse(t.Zombie(type).slam, "owner: no abilities yet");
                Assert.Greater(t.Zombie(type).hp, t.Zombie(ZombieType.Tank).hp);
            }
        }

        [UnityTest]
        public IEnumerator AStageCanNameItsMainBoss()
        {
            // Owner, 2026-10-07: three bosses; a stage says which one is its Main Boss.
            var map = StageMap.FromJson(Resources.Load<TextAsset>("Stages/test_stage").text);
            map.bossType = "Bear";
            var layout = StageMapBuilder.ToLayout(map);
            Assert.AreEqual(ZombieType.Bear, layout.BossType);
            map.bossType = "Basic";
            Assert.AreEqual(ZombieType.Boss, StageMapBuilder.ToLayout(map).BossType, "only boss types can be a Main Boss");
            yield break;
        }

        [Test]
        public void TheDogIsFasterAndSensesFurther()
        {
            var t = GameTuning.Load();
            var basic = t.Zombie(ZombieType.Basic);
            var dog = t.Zombie(ZombieType.Dog);
            Assert.Greater(dog.chaseSpeed, 3.2f, "owner: faster than before");
            Assert.AreEqual(1.5f, dog.senseMultiplier, 0.001f, "owner: +50 % sight and hearing");
            Assert.AreEqual(1f, basic.senseMultiplier, 0.001f);
            Assert.IsTrue(dog.alertsPack);
        }

        [UnityTest]
        public IEnumerator ADogThatSpotsAPlayerCallsThePack()
        {
            yield return Start("test_dark");
            yield return ClearZombies();
            var p = Player.transform.position;
            // The Dog faces the player 8 m away (seen even at night: 12 m × 0.5 × 1.5 = 9 m); a Basic zombie behind it
            // faces away and could never see the player.
            var dog = _session.Stage.SpawnZombie(ZombieType.Dog, p + new Vector3(0f, 0f, 8f));
            dog.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var basic = _session.Stage.SpawnZombie(ZombieType.Basic, p + new Vector3(3f, 0f, 16f));
            basic.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var far = _session.Stage.SpawnZombie(ZombieType.Basic, p + new Vector3(40f, 0f, 40f));
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(ZombieMode.Chase, dog.Mind.Mode);
            Assert.AreEqual(ZombieMode.Chase, basic.Mind.Mode, "called into the chase");
            Assert.AreEqual(Player, basic.Target);
            Assert.AreNotEqual(ZombieMode.Chase, far.Mind.Mode, "too far to be called");
        }

        [UnityTest]
        public IEnumerator DarknessShortensSightAndPlayersCarryLamps()
        {
            yield return Start("test_dark");
            yield return null;
            Assert.IsNotNull(Player.GetComponentInChildren<Light>(), "a lamp above the head");
            yield return ClearZombies();
            // 9 m in front of a zombie that faces the player: seen in daylight (12 m), not in the dark (6 m).
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(0f, 0f, 9f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.back);
            yield return new WaitForSeconds(0.6f);
            Assert.AreNotEqual(ZombieMode.Chase, zombie.Mind.Mode);
        }

        [UnityTest]
        public IEnumerator RescueHostageFollowsAndTheZombiesAreAlerted()
        {
            yield return Start("test_rescue");
            var hostage = _session.Hostage;
            Assert.IsNotNull(hostage);
            Assert.IsFalse(hostage.IsFreed);
            yield return ClearZombies();
            var far = _session.Stage.SpawnZombie(ZombieType.Basic, new Vector3(60f, 0f, 20f));

            Teleport(Player, hostage.transform.position + new Vector3(1f, 0f, 0f));
            yield return null;
            yield return null;
            // Owner, 2026-10-03: reaching them is not enough; 2026-10-08: the player talks to them, and nothing happens
            // until the talk is over (the zombies aren't alerted by walking up).
            Assert.IsFalse(hostage.IsFreed, "waits for the talk");
            Assert.IsTrue(Player.CanFreeHostage);
            Assert.IsFalse(Mission.Alerted, "walking up to the scientist doesn't alert the zombies");
            Player.FreeHostage();
            yield return null;
            yield return null;
            Assert.IsTrue(hostage.IsFreed);
            Assert.IsTrue(Mission.Alerted, "they are alerted once the scientist follows");
            Assert.AreEqual(3f, StageConditions.ZombieDetectionMultiplier, 0.001f);
            // Owner, 2026-10-09: better detection, but the zombies don't all know where the player is.
            Assert.IsNull(Zombie.Hunted);
            yield return new WaitForSeconds(0.6f);
            Assert.AreNotEqual(ZombieMode.Chase, far.Mind.Mode, "a zombie far away doesn't come for them");
            Assert.IsNull(_session.Stage.MainBoss, "owner, 2026-10-03: no Main Boss outside boss missions");

            // The hostage follows.
            Teleport(Player, hostage.transform.position + new Vector3(6f, 0f, 0f));
            var gap = Vector3.Distance(Player.transform.position, hostage.transform.position);
            yield return new WaitForSeconds(1.5f);
            Assert.Less(Vector3.Distance(Player.transform.position, hostage.transform.position), gap - 1f);

            // Back at the start together: Victory.
            foreach (var z in Zombie.Alive.ToArray()) z.gameObject.SetActive(false);
            var start = _session.Stage.Layout.PlayerSpawn;
            Teleport(Player, start);
            Teleport(hostage, start + new Vector3(1f, 0f, 1f));
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, _session.State);
        }


        [UnityTest]
        public IEnumerator ZombiesLeaveTheHostageAloneUntilReached()
        {
            yield return Start("test_rescue");
            var hostage = _session.Hostage;
            yield return ClearZombies();
            // Right next to the waiting hostage, facing them.
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, hostage.transform.position + new Vector3(1.5f, 0f, 0f));
            zombie.transform.rotation = Quaternion.LookRotation(Vector3.left);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(hostage.MaxHp, hostage.Hp, "not attacked while waiting");
            Assert.AreNotEqual(ZombieMode.Chase, zombie.Mind.Mode);
        }
        [UnityTest]
        public IEnumerator RescueFailsIfTheHostageDies()
        {
            yield return Start("test_rescue");
            _session.Hostage.TakeHit(1000f, Vector3.zero, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Defeat, _session.State);
        }


        private static float Length(NavMeshPath path)
        {
            var total = 0f;
            for (var i = 1; i < path.corners.Length; i++) total += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return total;
        }
    }
}
