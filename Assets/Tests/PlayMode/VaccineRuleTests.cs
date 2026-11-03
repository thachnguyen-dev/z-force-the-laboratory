using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-11: on the Vaccine stages the Vaccine lies at the exact centre of the map, it has to be carried to
    /// the helicopter, and it never vanishes with a body. The rival teams are soldiers with models.
    /// </summary>
    public class VaccineRuleTests
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
            for (var i = 0; i < 4; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator TheVaccineIsAtTheCentreOfEveryVaccineMap()
        {
            foreach (var id in new[] { "m6_vaccine_a", "m6_vaccine_b", "m7_collection_a", "m7_collection_b" })
            {
                yield return Start(id);
                var size = _session.Stage.Layout.Size;
                var centre = new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
                var vaccine = LootItem.All.First(i => i.Kind == LootKind.Vaccine);
                var off = vaccine.transform.position - centre;
                off.y = 0f;
                Debug.Log($"[ZOMBIE-VACCINE] {id}: {off.magnitude:0.0} m from the centre");
                Assert.Less(off.magnitude, 6f, $"{id}: the Vaccine is {off.magnitude:0.0} m from the map's centre");
                // Every rival is a soldier with a model.
                foreach (var r in Survivor.All.Where(s => s.IsRival)) Assert.IsTrue(r.HasModel, $"{id}: {r.name} has a model");
                _session.GoTo(SessionState.MainMenu);
                yield return null;
                foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
                Object.Destroy(_session.gameObject);
                _session = null;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator TheVaccineGoesToTheHelicopter()
        {
            yield return Start("m6_vaccine_a");
            var heli = Helicopter.Current;
            Assert.IsNotNull(heli);
            var layout = _session.Stage.Layout;
            Assert.IsTrue(Mission.AtTheHelicopter(heli.transform.position + new Vector3(3f, 0f, 0f), layout));
            Assert.IsFalse(Mission.AtTheHelicopter(heli.transform.position + new Vector3(25f, 0f, 0f), layout));
            // Carrying it far from the helicopter: not yet.
            var me = _session.LocalSurvivor;
            void Put(Vector3 p)
            {
                var cc = me.GetComponent<CharacterController>();
                cc.enabled = false;
                me.transform.position = p;
                cc.enabled = true;
            }
            var size = layout.Size;
            Put(new Vector3(size.x * 0.5f, 0f, size.y * 0.5f));
            me.Inventory.Vaccine = true;
            Assert.AreEqual(MatchOutcome.Ongoing, Mission.Evaluate(1), "away from the helicopter");
            Assert.AreEqual("Objective: bring the Vaccine to the helicopter", Mission.ObjectiveText(me));
            // Back at the helicopter with it: won.
            Put(heli.transform.position + new Vector3(3f, 0f, 0f));
            Assert.AreEqual(MatchOutcome.Victory, Mission.Evaluate(1), "at the helicopter");
        }

        [UnityTest]
        public IEnumerator TheVaccineOutlastsTheBody()
        {
            yield return Start("m6_vaccine_a");
            foreach (var item in LootItem.All.Where(i => i.Kind == LootKind.Vaccine).ToArray()) Object.Destroy(item.gameObject);
            yield return null;
            var me = _session.LocalSurvivor;
            var at = me.transform.position + new Vector3(4f, 0f, 0f);
            var body = Chest.Corpse(_session.StageRoot, me, 0.3f);
            body.transform.position = at;
            body.Contents.Add(LootData.Single(LootKind.Vaccine));
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(body == null || !body.isActiveAndEnabled, "the body is gone");
            var left = LootItem.All.FirstOrDefault(i => i.Kind == LootKind.Vaccine && i.Amount > 0);
            Assert.IsNotNull(left, "the Vaccine stays");
            Assert.Less(Vector3.Distance(left.transform.position, at), 1.5f, "where the body was");
        }
    }
}
