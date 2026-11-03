using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-10-11 (second batch): zombies towards the objective, four bomb-site incubators, 40 % bullet damage.</summary>
    public class BatchRuleTests2
    {
        private GameSession _session;

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
        public IEnumerator MoreZombiesTowardsTheObjectiveThanNearTheStart()
        {
            foreach (var id in new[] { "m4_reserve_a", "m2_scientist_a", "m3_breeding_a", "m6_vaccine_a" })
            {
                yield return Start(id);
                var stage = _session.Stage;
                var start = stage.Layout.SpawnArea.center;
                var goal = stage.ObjectivePoint.Value;
                var pts = Zombie.Alive.Select(z => z.transform.position).Concat(stage.Population.Records.Select(r => r.Position)).ToList();
                // The third of the way nearest the start against the third nearest the objective.
                float Along(Vector3 p)
                {
                    var a = Vector2.Distance(new Vector2(p.x, p.z), start);
                    var b = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(goal.x, goal.z));
                    return a + b > 0.01f ? a / (a + b) : 0.5f;
                }
                var nearStart = pts.Count(p => Along(p) < 0.33f);
                var nearGoal = pts.Count(p => Along(p) > 0.67f);
                Debug.Log($"[ZOMBIE-WEIGHT] {id}: {pts.Count} zombies, {nearStart} in the third nearest the start, {nearGoal} in the third nearest the objective");
                Assert.Greater(nearGoal, nearStart * 1.5f, $"{id}: more towards the objective ({nearGoal}) than near the start ({nearStart})");
                yield return TearDown();
            }
        }

        [UnityTest]
        public IEnumerator FourIncubatorsAtTheBombSite()
        {
            yield return Start("m3_breeding_a");
            var site = _session.Stage.Layout.BombTargetPosition;
            var near = Object.FindObjectsByType<Incubator>(FindObjectsSortMode.None).Count(i => Vector3.Distance(i.transform.position, site) < StageRuntime.BombSiteSpread + 1.5f);
            Assert.AreEqual(StageRuntime.BombSiteIncubators, near, "owner: 4 incubators where the bomb goes");
        }

        [UnityTest]
        public IEnumerator PlayersAndAiTakeFortyPercentOfABullet()
        {
            yield return Start("test_vaccine");
            var rival = Survivor.All.First(s => s.IsRival);
            var me = _session.LocalSurvivor;
            // Only the test's own bullet: the rivals hold fire, no zombies about.
            foreach (var s in Survivor.All.Where(s => s.IsRival).ToArray()) s.InputSource = null;
            foreach (var z in Zombie.Alive.ToArray()) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            var cc = me.GetComponent<CharacterController>();
            cc.enabled = false;
            me.transform.position = rival.transform.position + new Vector3(0f, 0f, 3f);
            cc.enabled = true;
            yield return null;
            var before = me.Hp;
            Projectiles.Fire(rival.transform.position + Vector3.up + Vector3.forward * 0.6f, Vector3.forward, 40f, 10f, 20f, false, Survivor.RivalTeam);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(before - 20f * 0.4f, me.Hp, 0.01f, "a 20-damage bullet takes 8");
        }
    }
}
