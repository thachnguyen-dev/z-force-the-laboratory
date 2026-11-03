using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-09: the scientist, the Vaccine's place, explosions, passing through, the bosses.</summary>
    public class BatchRuleTests
    {
        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        private IEnumerator Start(string stage)
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stage), _session.Tuning.characters[0], 41);
            for (var i = 0; i < 3; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator TheScientistIsTheOwnersModelWithMoreHpAndHeals()
        {
            yield return Start("test_rescue");
            var scientist = _session.Hostage;
            Assert.IsNotNull(scientist);
            Assert.IsTrue(scientist.HasModel, "the owner's scientist model, not the placeholder");
            Assert.IsNull(scientist.transform.Find("Body/Backpack"), "no backpack");
            Assert.AreEqual(_session.Tuning.player.maxHp * 1.5f, scientist.MaxHp, 0.01f, "+50 % HP");
            // Hurt: nothing for 5 s, then 5 % a second.
            scientist.TakeHit(scientist.MaxHp * 0.5f, scientist.transform.position + Vector3.forward, 0f);
            var hurt = scientist.Hp;
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(hurt, scientist.Hp, 0.01f, "not yet");
            yield return new WaitForSeconds(4f);
            Assert.Greater(scientist.Hp, hurt + scientist.MaxHp * 0.02f, "healing");
        }

        [UnityTest]
        public IEnumerator CharactersPassThroughEachOther()
        {
            yield return Start(StageLibrary.TestStageId);
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(Layers.Survivor, Layers.Survivor));
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(Layers.Survivor, Layers.Zombie), "zombies still block");
        }

        [UnityTest]
        public IEnumerator GrenadesAndBarrelsHurtPlayersThirtyPercent()
        {
            yield return Start(StageLibrary.TestStageId);
            var g = _session.Tuning.grenade;
            Assert.AreEqual(240f, g.damage, "doubled");
            Assert.AreEqual(200f, _session.Tuning.barrel.damage, "doubled");
            var before = Player.Hp;
            var at = Player.transform.position + Vector3.up * 0.5f;
            Explosion.Detonate(at, g.damage, g.radius, 0f, g.survivorShare);
            yield return null;
            var taken = before - Player.Hp;
            Assert.Greater(taken, 0f);
            Assert.LessOrEqual(taken, g.damage * 0.3f + 0.01f, "30 % at most");
        }

        [Test]
        public void BossesAreFasterAndTheTigerBigger()
        {
            var t = GameTuning.Load();
            Assert.AreEqual(3f, t.Zombie(ZombieType.Boss).chaseSpeed, 0.01f);
            Assert.AreEqual(4.25f, t.Zombie(ZombieType.Tiger).chaseSpeed, 0.01f);
            Assert.AreEqual(2f, t.Zombie(ZombieType.Tiger).height, 0.01f);
            Assert.AreEqual("Jack", t.characters.First(c => c.id == "character_01").displayName);
            Assert.AreEqual("Maya", t.characters.First(c => c.id == "character_02").displayName);
            Assert.AreEqual("Ryan", t.characters.First(c => c.id == "protagonist").displayName);
            Assert.AreEqual("Ethan", t.characters.First(c => c.id == "character_04").displayName);
        }
    }
}
