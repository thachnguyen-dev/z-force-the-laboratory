using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>The Bomb and Vaccine stages and the SMG (owner, 2026-10-02).</summary>
    public class MissionTests
    {
        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        private IEnumerator Start(string stageId, bool withAi = false)
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stageId), _session.Tuning.characters[0], 41, withAi);
            yield return null;
            foreach (var z in Zombie.Alive.ToArray()) Object.Destroy(z.gameObject);
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

        private static void Teleport(Survivor s, Vector3 p)
        {
            var c = s.GetComponent<CharacterController>();
            c.enabled = false;
            s.transform.position = p;
            c.enabled = true;
        }

        [UnityTest]
        public IEnumerator PlantingTheC4OnTheTargetWins()
        {
            yield return Start("test_bomb");
            Assert.IsTrue(Player.Inventory.C4, "the first player starts with the C4");
            var target = Mission.Target;
            Assert.IsNotNull(target, "the bomb stage has its target incubator");
            target.TakeDamage(10000f, target.transform.position, true);
            Assert.IsFalse(target.IsDead, "only the C4 destroys the target");
            Assert.IsFalse(Player.CanPlantC4, "too far away to plant");
            Teleport(Player, target.transform.position + new Vector3(0f, 0f, -2f));
            yield return null;
            Assert.IsTrue(Player.CanPlantC4);
            Player.PlantC4();
            Assert.IsTrue(Mission.Planted);
            Assert.IsFalse(Player.Inventory.C4);
            Assert.AreEqual(20f, Mission.FuseLeft, 0.2f, "owner: 20 s fuse");
            // Run well clear (the blast hurts players too), then let the fuse run out.
            Teleport(Player, _session.Stage.Layout.PlayerSpawn);
            Mission.Tick(21f);
            yield return null;
            Assert.IsTrue(target == null || target.IsDead);
            Assert.AreEqual(SessionState.Victory, _session.State);
        }

        [UnityTest]
        public IEnumerator TheBlastHurtsPlayersCaughtInIt()
        {
            yield return Start("test_bomb");
            var target = Mission.Target;
            Teleport(Player, target.transform.position + new Vector3(0f, 0f, -2f));
            yield return null;
            Player.PlantC4();
            var hp = Player.Hp;
            Mission.Tick(21f);
            Assert.Less(Player.Hp, hp);
        }

        [UnityTest]
        public IEnumerator TheC4StaysOnTheBodyAndIsLostWithIt()
        {
            yield return Start("test_bomb");
            Player.TakeDamage(10000f, Player.transform.position, false);
            yield return null;
            Chest body = null;
            foreach (var chest in Chest.All)
                foreach (var entry in chest.Contents)
                    if (entry.Kind == LootKind.C4) body = chest;
            Assert.IsNotNull(body, "the C4 stays with the body");
            // Solo: the only player is dead, so this is a Defeat anyway; the item check is covered by Mission.Exists.
            Assert.IsTrue(Mission.Exists(LootKind.C4));
            Object.Destroy(body.gameObject);
            yield return null;
            Assert.IsFalse(Mission.Exists(LootKind.C4), "the body went: the C4 is lost");
        }

        [UnityTest]
        public IEnumerator TheAiCannotCarryMissionItems()
        {
            yield return Start("test_vaccine", withAi: true);
            var ai = _session.AiSurvivor;
            LootItem vaccine = null;
            foreach (var item in LootItem.All) if (item.Kind == LootKind.Vaccine) vaccine = item;
            Assert.IsNotNull(vaccine, "the Vaccine lies on its cell");
            Teleport(ai, vaccine.transform.position);
            yield return null;
            ai.TryPickup(vaccine);
            Assert.IsFalse(ai.Inventory.Vaccine);
            Assert.IsTrue(Mission.Exists(LootKind.Vaccine), "it stays on the ground");
        }

        [UnityTest]
        public IEnumerator BringingTheVaccineBackWins()
        {
            yield return Start("test_vaccine");
            var layout = _session.Stage.Layout;
            Assert.IsTrue(layout.Dark, "owner: a night stage");
            Assert.AreEqual(1f, layout.LootScale, 0.001f, "owner, 2026-10-09: night missions no longer have fewer items");
            LootItem vaccine = null;
            foreach (var item in LootItem.All) if (item.Kind == LootKind.Vaccine) vaccine = item;
            Teleport(Player, vaccine.transform.position);
            yield return null;
            Player.TryPickup(vaccine);
            Assert.IsTrue(Player.Inventory.Vaccine);
            yield return null;
            Assert.AreEqual(SessionState.Playing, _session.State, "not back yet");
            Teleport(Player, _session.Stage.Layout.PlayerSpawn);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, _session.State);
        }

        [UnityTest]
        public IEnumerator NightMissionsKeepTheirLoot()
        {
            // Owner, 2026-10-09: the item spawn rate in night missions is no longer reduced.
            foreach (var id in new[] { "m5_wing_a", "m6_vaccine_a", "m6_vaccine_b", "m7_collection_a", "m7_collection_b", "test_vaccine" })
                Assert.AreEqual(1f, StageLibrary.Load(id).LootScale, 0.001f, id);
            yield return null;
        }

        // ---- Owner, 2026-10-03 ----

        [UnityTest]
        public IEnumerator HurtingTheBossCallsTheHorde()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("test_stage"), _session.Tuning.characters[0], 41);
            yield return null;
            var boss = _session.Stage.MainBoss;
            var far = _session.Stage.SpawnZombie(ZombieType.Basic, _session.Stage.Layout.PlayerSpawn + new Vector3(20f, 0f, 20f));
            yield return null;
            Assert.AreEqual(ZombieMode.Idle, far.Mind.Mode);
            boss.TakeDamage(5f, boss.transform.position, false);
            Assert.AreEqual(ZombieMode.Investigate, far.Mind.Mode, "heading for the Boss Area");
            Assert.IsTrue(_session.Stage.Layout.BossArea.Contains(new Vector2(far.Mind.Destination.x, far.Mind.Destination.z)));
        }

        [UnityTest]
        public IEnumerator ItemsHaveAGlowingRing()
        {
            yield return Start("test_vaccine");
            foreach (var item in LootItem.All) Assert.IsNotNull(item.transform.Find("Glow ring"), item.Label);
        }

        [UnityTest]
        public IEnumerator PlantingTheBombAlertsTheZombies()
        {
            yield return Start("test_bomb");
            Assert.IsFalse(Mission.Alerted);
            Assert.AreEqual(1f, StageConditions.ZombieDetectionMultiplier, 0.001f);
            Teleport(Player, Mission.Target.transform.position + new Vector3(0f, 0f, -2f));
            yield return null;
            yield return null;
            // Owner, 2026-10-09: walking up isn't enough; planting the C4 raises their detection.
            Assert.IsFalse(Mission.Alerted, "not by walking up");
            Assert.IsTrue(Player.CanPlantC4);
            Player.PlantC4();
            yield return null;
            Assert.IsTrue(Mission.Alerted);
            Assert.AreEqual(3f, StageConditions.ZombieDetectionMultiplier, 0.001f, "owner: 3× detection");
            Assert.IsNull(Zombie.Hunted, "not every zombie comes at once");
        }

        private static System.Collections.Generic.List<Survivor> Rivals()
        {
            var list = new System.Collections.Generic.List<Survivor>();
            foreach (var s in Survivor.All) if (s.Team == Survivor.RivalTeam) list.Add(s);
            return list;
        }

        [UnityTest]
        public IEnumerator TheVaccineStageHasARivalTeamOfThree()
        {
            yield return Start("test_vaccine");
            var rivals = Rivals();
            Assert.AreEqual(3, rivals.Count, "owner: 3 AI members");
            foreach (var r in rivals)
            {
                Assert.IsFalse(r.IsHuman);
                Assert.IsNotNull(r.LongGun, "owner: Pistol + a long gun");
                Assert.Greater(r.Inventory.Ammo(r.LongGun.Def.ammo), 0);
                Assert.IsTrue(r.GetComponent<AiTeammate>().Rival);
            }
        }

        [UnityTest]
        public IEnumerator BulletsHitTheOtherTeamOnly()
        {
            yield return Start("test_vaccine");
            var rival = Rivals()[0];
            var spot = Player.transform.position + new Vector3(0f, 0f, 4f);
            Teleport(rival, spot);
            rival.InputSource = null;
            yield return new WaitForFixedUpdate();
            var hp = rival.Hp;
            Projectiles.Fire(Player.transform.position + Vector3.up, Vector3.forward, 40f, 10f, 20f, false, Player.Team);
            yield return new WaitForSeconds(0.3f);
            Assert.Less(rival.Hp, hp, "a player's bullet hurts a rival");
            var other = Rivals()[1];
            Teleport(other, spot + new Vector3(0f, 0f, 4f));
            other.InputSource = null;
            yield return new WaitForFixedUpdate();
            hp = other.Hp;
            var rivalHp = rival.Hp;
            Projectiles.Fire(rival.transform.position + Vector3.up + Vector3.forward * 0.6f, Vector3.forward, 40f, 10f, 20f, false, Survivor.RivalTeam);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(hp, other.Hp, "rivals don't hurt each other");
            Assert.AreEqual(rivalHp, rival.Hp);
        }

        [UnityTest]
        public IEnumerator RivalsGoForTheVaccineAndWinAtTheHelicopter()
        {
            yield return Start("test_vaccine");
            LootItem vaccine = null;
            foreach (var item in LootItem.All) if (item.Kind == LootKind.Vaccine) vaccine = item;
            var rival = Rivals()[0];
            Teleport(rival, vaccine.transform.position + new Vector3(0.5f, 0f, 0f));
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(rival.Inventory.Vaccine, "a rival takes the Vaccine");
            Assert.AreEqual(rival, Mission.RivalCarrier);
            // Owner, 2026-10-11: no 60 s rule: holding it a long time doesn't win it.
            Mission.Tick(61f);
            yield return null;
            Assert.AreEqual(SessionState.Playing, _session.State, "no timer");
            // Reaching the helicopter with it does (this test map has none: its start area).
            var home = _session.Stage.Layout.SpawnArea.center;
            Teleport(rival, new Vector3(home.x, 0f, home.y));
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(SessionState.Defeat, _session.State, "owner: the rivals got it to the helicopter");
        }

        [UnityTest]
        public IEnumerator KillingTheCarrierLetsThePlayersTakeItBack()
        {
            yield return Start("test_vaccine");
            LootItem vaccine = null;
            foreach (var item in LootItem.All) if (item.Kind == LootKind.Vaccine) vaccine = item;
            var rival = Rivals()[0];
            Teleport(rival, vaccine.transform.position);
            rival.TryPickup(vaccine);
            Assert.IsTrue(rival.Inventory.Vaccine);
            rival.TakeDamage(10000f, rival.transform.position, false);
            yield return null;
            Assert.IsTrue(Mission.Exists(LootKind.Vaccine), "on the rival's body");
            Chest body = null;
            foreach (var chest in Chest.All)
                for (var i = 0; i < chest.Contents.Count; i++)
                    if (chest.Contents[i].Kind == LootKind.Vaccine) body = chest;
            Teleport(Player, body.transform.position);
            yield return null;
            for (var i = 0; i < body.Contents.Count; i++)
                if (body.Contents[i].Kind == LootKind.Vaccine) { Player.TakeFromChest(body, i); break; }
            Assert.IsTrue(Player.Inventory.Vaccine);
            yield return null;
            Assert.IsNull(Mission.RivalCarrier, "the rivals no longer have it");
        }

        // ---- Owner, 2026-10-03 (second round) ----

        [UnityTest]
        public IEnumerator AfterTheBlastThePlayersMustGetBackToTheStart()
        {
            yield return Start("test_bomb");
            var target = Mission.Target;
            Teleport(Player, target.transform.position + new Vector3(0f, 0f, -2f));
            yield return null;
            Player.PlantC4();
            // The concentrated response: zombies pour in during the fuse.
            var before = Zombie.Alive.Count;
            Mission.Tick(0.1f);
            Assert.Greater(Zombie.Alive.Count, before, "the target's spawn points answer the C4");
            Teleport(Player, target.transform.position + new Vector3(0f, 0f, -14f));
            Mission.Tick(21f);
            yield return null;
            Assert.IsTrue(Mission.Exploded);
            Assert.AreEqual(SessionState.Playing, _session.State, "not won yet: back to the start first");
            foreach (var z in Zombie.Alive.ToArray()) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            Teleport(Player, _session.Stage.Layout.PlayerSpawn);
            yield return null;
            yield return null;
            Assert.AreEqual(SessionState.Victory, _session.State);
        }

        [UnityTest]
        public IEnumerator NonBossMissionsHaveSubBossesInsteadOfTheMainBoss()
        {
            yield return Start("m2_scientist_a");
            Assert.IsNull(_session.Stage.MainBoss);
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            _session.StartStage(StageLibrary.Load("m2_scientist_a"), _session.Tuning.characters[0], 5);
            yield return null;
            var subs = 0;
            foreach (var z in Zombie.Alive) if (z.Def.type == ZombieType.SubBoss) subs++;
            Assert.GreaterOrEqual(subs, 1);
        }

        [UnityTest]
        public IEnumerator FarZombiesWaitAsRecordsAndComeAliveNearby()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("m4_reserve_a"), _session.Tuning.characters[0], 9);
            yield return null;
            var pop = _session.Stage.Population;
            var t = _session.Tuning.spawn;
            Assert.Greater(pop.DormantCount, 0, "zombies far away are only records");
            Assert.LessOrEqual(Zombie.Alive.Count, t.maxActive + 4, "never more real zombies than the budget (+ bosses)");
            foreach (var z in Zombie.Alive)
                if (z.Def.type != ZombieType.SubBoss && !z.IsMainBoss)
                    Assert.LessOrEqual(Vector3.Distance(z.transform.position, Player.transform.position), t.activateRange + 3f);
            // Walk to where a record is: it becomes a real zombie.
            var record = pop.Records[0];
            var c = Player.GetComponent<CharacterController>();
            c.enabled = false;
            Player.transform.position = record.Position + new Vector3(6f, 0f, 0f);
            c.enabled = true;
            var dormantBefore = pop.DormantCount;
            yield return new WaitForSeconds(1.2f);
            Assert.Less(pop.DormantCount, dormantBefore);
        }

        [UnityTest]
        public IEnumerator DeadZombiesGoBackToThePool()
        {
            yield return Start("test_stage");
            var zombie = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(4f, 0f, 0f));
            zombie.TakeDamage(1000f, Player.transform.position, false);
            // Owner, 2026-10-07: the corpse stays on the ground a while before it goes.
            yield return new WaitForSeconds(Zombie.FallTime + Zombie.CorpseTime * 0.5f);
            Assert.IsTrue(zombie.gameObject.activeSelf, "the corpse is still there");
            yield return new WaitForSeconds(Zombie.CorpseTime * 0.5f + Zombie.SinkTime + 0.3f);
            Assert.IsTrue(zombie != null, "kept, not destroyed");
            Assert.IsFalse(zombie.gameObject.activeSelf);
            var again = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + new Vector3(-4f, 0f, 0f));
            Assert.AreSame(zombie, again, "reused");
            Assert.IsFalse(again.IsDead);
            Assert.AreEqual(again.Def.hp, again.Hp);
        }

        [UnityTest]
        public IEnumerator CoopHasMoreAndTougherZombies()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("m4_reserve_a"), _session.Tuning.characters[0], 77);
            yield return null;
            var solo = _session.Stage.Population.Total;
            var soloHp = _session.Stage.Def(ZombieType.Basic).hp;
            Assert.AreEqual(_session.Tuning.Zombie(ZombieType.Basic).hp, soloHp, 0.01f, "Solo keeps the base stats");
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            _session.StartStage(StageLibrary.Load("m4_reserve_a"), _session.Tuning.characters[0], 77, withAi: true);
            yield return null;
            var t = _session.Tuning.spawn;
            Assert.AreEqual(soloHp * t.coopHp, _session.Stage.Def(ZombieType.Basic).hp, 0.01f);
            Assert.Greater(_session.Stage.Def(ZombieType.Basic).chaseSpeed, _session.Tuning.Zombie(ZombieType.Basic).chaseSpeed);
            Assert.Greater(_session.Stage.Population.Total, solo, "more zombies in co-op");
        }

        [Test]
        public void ZombiesAreFasterThanBefore()
        {
            var t = GameTuning.Load();
            Assert.GreaterOrEqual(t.Zombie(ZombieType.Basic).chaseSpeed, 2.6f);
            Assert.Less(t.Zombie(ZombieType.Fast).chaseSpeed, t.player.moveSpeed, "players can still outrun the fastest");
        }

        [Test]
        public void BushesAreAboutAsTallAsAPlayer()
        {
            Assert.GreaterOrEqual(Art.ArtLibrary.BushHeight, 1.7f);
            Assert.GreaterOrEqual(Art.ArtLibrary.Get("bush").Height, 1.7f);
        }

        [Test]
        public void TheSmgFiresLikeTheArWithLessDamageAndLighterAmmo()
        {
            var t = GameTuning.Load();
            var smg = t.Firearm(WeaponId.SMG);
            var ar = t.Firearm(WeaponId.AR);
            Assert.IsNotNull(smg);
            Assert.Less(smg.damage, ar.damage);
            Assert.LessOrEqual(smg.shotInterval, ar.shotInterval, "continuous fire like the AR");
            Assert.Less(t.inventory.SlotCost(AmmoType.SMG), t.inventory.SlotCost(AmmoType.AR));
            CollectionAssert.Contains(Arsenal.LongGuns, WeaponId.SMG);
            CollectionAssert.Contains(Arsenal.AmmoTypes, AmmoType.SMG);
        }
    }
}
