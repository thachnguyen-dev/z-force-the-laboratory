using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-07: the final stage (Vaccine Collection, two rival teams), Co-op as Hard (more secondary bosses), and
    /// Endless mode (a countdown, waves through the fence's gaps, the weather cycle, the bigger backpack and ammo).
    /// </summary>
    public class ModeTests
    {
        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        private IEnumerator Start(string stageId, bool withAi = false)
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stageId), _session.Tuning.characters[0], 41, withAi);
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
            _session = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheFinalStageHasTwoRivalTeams()
        {
            yield return Start("m7_collection_a");
            var missions = Campaign.Missions;
            Assert.AreEqual("m7_collection", missions[missions.Count - 1].id, "Vaccine Collection is the final mission");
            Assert.AreEqual("m6_vaccine", missions[missions.Count - 2].id, "The Vaccine comes just before it");
            var first = Survivor.All.Count(s => s.Team == Survivor.RivalTeam);
            var second = Survivor.All.Count(s => s.Team == Survivor.RivalTeam2);
            Assert.AreEqual(_session.Tuning.missions.rivalCount, first, "the first rival team");
            Assert.AreEqual(_session.Tuning.missions.rivalCount, second, "the second rival team");
            var ids = Survivor.All.Select(s => s.NetId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "every survivor has its own network id");
            // It is larger than every other story map.
            var layout = _session.Stage.Layout;
            foreach (var m in missions)
            {
                if (m.id == "m7_collection") continue;
                foreach (var id in m.stages)
                {
                    var other = StageLibrary.Load(id);
                    Assert.Greater(layout.Size.x * layout.Size.y, other.Size.x * other.Size.y, $"larger than {id}");
                }
            }
        }

        [UnityTest]
        public IEnumerator CoopIsHardWithMoreSecondaryBosses()
        {
            yield return Start("m7_collection_a");
            var solo = Zombie.Alive.Count(z => z.Def.type == _session.Stage.Layout.SubBossType);
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            _session.StartStage(StageLibrary.Load("m7_collection_a"), _session.Tuning.characters[0], 41, withAi: true);
            yield return null;
            var coop = Zombie.Alive.Count(z => z.Def.type == _session.Stage.Layout.SubBossType);
            Assert.Greater(solo, 0, "secondary bosses in Solo");
            Assert.AreEqual(solo * (1 + _session.Tuning.spawn.coopExtraSubBosses), coop, "Co-op has more secondary bosses");
        }

        [UnityTest]
        public IEnumerator CoopHasNoTutorial()
        {
            yield return Start("test_stage");
            var missions = Campaign.Missions;
            var tutorial = missions.ToList().FindIndex(m => m.id == Campaign.TutorialId);
            Assert.GreaterOrEqual(tutorial, 0, "the tutorial is in the campaign");
            Assert.Contains(tutorial, Campaign.Listed(false), "Solo lists the tutorial");
            CollectionAssert.DoesNotContain(Campaign.Listed(true), tutorial, "Co-op doesn't");
            Assert.AreEqual(missions.Count - 1, Campaign.Listed(true).Count, "every other mission is listed in Co-op");
        }

        [UnityTest]
        public IEnumerator EndlessPlayersCantLeaveThroughTheGapsButZombiesComeIn()
        {
            yield return Start(Endless.StageId);
            var gaps = _session.Stage.Layout.ZombieSpawnPoints;
            // The gap on the south side nearest the middle; the player just inside it, walking out.
            var gap = gaps.Where(g => g.z < 3f).OrderBy(g => Mathf.Abs(g.x - _session.Stage.Layout.Size.x * 0.5f)).First();
            var c = Player.GetComponent<CharacterController>();
            c.enabled = false;
            Player.transform.position = new Vector3(gap.x, 0f, 5f);
            c.enabled = true;
            var input = new Scripted { Value = new CharacterInput { Move = new Vector2(0f, -1f), GrenadeDistance = -1f } };
            Player.InputSource = input;
            yield return new WaitForSeconds(2.5f);
            Assert.Greater(Player.transform.position.z, 2f, "the player stops at the fence line");
            Player.InputSource = null;
            // A zombie in the gap, outside the fence, comes in after the player.
            var z = _session.Stage.SpawnZombie(ZombieType.Basic, gap);
            Assert.IsNotNull(z, "a zombie appears in the gap");
            z.JoinChase(Player, 60f);
            yield return new WaitForSeconds(3f);
            Assert.Greater(z.transform.position.z, 2.5f, "and comes in through it");
        }

        [UnityTest]
        public IEnumerator CoopIsOpenWithoutTouchingTheStory()
        {
            yield return Start("test_stage");
            var settings = new SettingsData();
            var missions = Campaign.Missions;
            for (var i = 0; i < missions.Count; i++) Assert.IsTrue(Campaign.Unlocked(i, settings, coop: true), $"{missions[i].id} open in Co-op");
            Assert.IsFalse(Campaign.Unlocked(missions.Count - 1, settings, coop: false), "the Solo story still unlocks in order");
        }

        [UnityTest]
        public IEnumerator EndlessGivesTimeToGearUpThenWavesComeThroughTheGaps()
        {
            yield return Start(Endless.StageId);
            var run = Endless.Current;
            Assert.IsNotNull(run, "an Endless run");
            for (var i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(0, Zombie.Alive.Count, "no zombies during the countdown");
            Assert.Greater(run.PrepLeft, _session.Tuning.endless.prepSeconds - 5f);
            StringAssert.Contains("Collect equipment", run.ObjectiveText());
            Assert.Greater(LootItem.All.Count, 20, "equipment to collect");
            var gaps = _session.Stage.Layout.ZombieSpawnPoints;
            Assert.GreaterOrEqual(gaps.Count, 8, "several gaps");

            run.SkipTo(_session.Tuning.endless.prepSeconds - 0.05f);
            for (var i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(1, run.Wave, "the first wave");
            Assert.GreaterOrEqual(Zombie.Alive.Count, _session.Tuning.endless.waveBase - 1, "the first wave came");
            foreach (var z in Zombie.Alive)
            {
                var nearest = gaps.Min(g => Vector2.Distance(new Vector2(g.x, g.z), new Vector2(z.transform.position.x, z.transform.position.z)));
                Assert.Less(nearest, 6f, "every zombie came in through a gap");
                Assert.AreEqual(Player, z.Target, "and comes for the players");
            }

            // Ten minutes in: bigger waves of tougher zombies.
            foreach (var z in Zombie.Alive.ToArray()) z.TakeDamage(1e6f, z.transform.position, false);
            yield return new WaitForSeconds(0.2f);
            var first = Mathf.RoundToInt(_session.Tuning.endless.waveBase * 1f);
            run.SkipTo(_session.Tuning.endless.prepSeconds + 600f - 0.05f);
            for (var i = 0; i < 10; i++) yield return null;
            var later = Zombie.Alive.Where(z => !z.IsDead).ToList();
            Assert.Greater(later.Count, first, "later waves are bigger");
            Assert.IsTrue(later.All(z => z.Toughness > 1.4f), "and tougher");
        }

        [UnityTest]
        public IEnumerator EndlessCyclesDayRainAndNight()
        {
            yield return Start(Endless.StageId);
            var run = Endless.Current;
            var t = _session.Tuning.endless;
            yield return null;
            Assert.IsFalse(StageConditions.Rain || StageConditions.Dark, "day first");
            run.SkipTo(t.daySeconds + 1f);
            yield return null;
            Assert.IsTrue(StageConditions.Rain && !StageConditions.Dark, "then rain");
            run.SkipTo(t.daySeconds + t.rainSeconds + 1f);
            yield return null;
            Assert.IsTrue(StageConditions.Dark && !StageConditions.Rain, "then night");
            run.SkipTo(t.daySeconds + t.rainSeconds + t.nightSeconds + 1f);
            yield return null;
            Assert.IsTrue(StageConditions.Dark && StageConditions.Rain, "then night rain");
            Assert.IsTrue(_session.StageRoot.GetComponent<StageAtmosphere>().NightRain, "with its storm");
            run.SkipTo(t.daySeconds + t.rainSeconds + t.nightSeconds + t.nightRainSeconds + 1f);
            yield return null;
            Assert.IsFalse(StageConditions.Rain || StageConditions.Dark, "and day again");
        }

        [UnityTest]
        public IEnumerator NightRainHasLightningThatLightsTheMapAndThunder()
        {
            yield return Start("m7_collection_a");
            var atmosphere = _session.StageRoot.GetComponent<StageAtmosphere>();
            Assert.IsTrue(atmosphere.NightRain, "the final stage is night rain");
            yield return null;
            var dark = RenderSettings.ambientLight.grayscale;
            atmosphere.Strike();
            yield return null;
            Assert.Greater(atmosphere.Flash, 0.5f, "lightning");
            Assert.Greater(RenderSettings.ambientLight.grayscale, dark * 2f, "lights up the map");
            yield return new WaitForSeconds(2.2f);
            Assert.AreEqual(0f, atmosphere.Flash, "and is gone");
            var thunder = atmosphere.GetComponents<AudioSource>().Any(a => a.clip == null && !a.loop);
            Assert.IsTrue(thunder, "thunder followed");
            Assert.AreEqual(dark, RenderSettings.ambientLight.grayscale, 0.001f, "the night is back");
        }

        [UnityTest]
        public IEnumerator StreetLightsLightTheNightNearby()
        {
            yield return Start("m7_collection_a");
            var atmosphere = _session.StageRoot.GetComponent<StageAtmosphere>();
            for (var i = 0; i < 5; i++) yield return null;
            Assert.Greater(atmosphere.StreetLightsLit, 0, "street lights near the player are lit");
            foreach (var l in atmosphere.GetComponentsInChildren<Light>().Where(l => l.name == "Street light" && l.enabled))
            {
                var d = l.transform.position - Player.transform.position;
                Assert.Less(new Vector2(d.x, d.z).magnitude, 30f, "near the view");
                Assert.Greater(l.transform.position.y, 5f, "from the lamp head");
                Assert.Less(l.range, 10f, "a small area");
            }
            // By day they are off.
            atmosphere.Set(false, false);
            for (var i = 0; i < 2; i++) yield return null;
            Assert.AreEqual(0, atmosphere.StreetLightsLit, "off by day");
        }

        [UnityTest]
        public IEnumerator TheFlashlightLightsOnlyTheAreaInFront()
        {
            yield return Start("m7_collection_a");
            for (var i = 0; i < 3; i++) yield return null;
            var lights = Player.GetComponentsInChildren<Light>();
            Assert.AreEqual(1, lights.Length, "one light: the flashlight, nothing glowing round the character");
            var lamp = lights[0];
            Assert.AreEqual(LightType.Spot, lamp.type);
            var local = Player.transform.InverseTransformPoint(lamp.transform.position);
            Assert.Greater(local.z, 0.3f, "ahead of the body");
            Assert.Less(local.y, 1.7f, "below the head");
            Assert.Greater(Vector3.Dot(lamp.transform.forward, Player.transform.forward), 0.9f, "pointing forward");
            // The cone's lower edge reaches the ground in front of the character, not under them.
            var lower = Quaternion.AngleAxis(lamp.spotAngle * 0.5f, Player.transform.right) * lamp.transform.forward;
            var reach = local.z + local.y / Mathf.Tan(Mathf.Asin(-lower.y));
            Assert.Greater(reach, 1f, "the lit ground starts in front");
        }

        [UnityTest]
        public IEnumerator EndlessHasABiggerBackpackAndMoreAmmo()
        {
            yield return Start("test_stage");
            var normalCapacity = Player.Inventory.Capacity;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            _session.StartStage(StageLibrary.Load(Endless.StageId), _session.Tuning.characters[0], 41);
            yield return null;
            var t = _session.Tuning.endless;
            Assert.AreEqual(Mathf.RoundToInt(normalCapacity * t.backpackMultiplier), Player.Inventory.Capacity, "the backpack holds ×1.5");
            var ammo = AmmoType.AR;
            var before = Player.Inventory.Ammo(ammo);
            var box = LootItem.SpawnAmmo(Player.transform.position, ammo, 20, false);
            Player.TryPickup(box);
            Assert.AreEqual(before + Mathf.CeilToInt(20 * t.ammoMultiplier), Player.Inventory.Ammo(ammo), "ammo pickups give 50 % more");
        }

        [UnityTest]
        public IEnumerator AnEndlessRunEndsWhenTheTeamIsDownAndShowsTheTime()
        {
            yield return Start(Endless.StageId);
            Endless.Current.SkipTo(_session.Tuning.endless.prepSeconds + 75f);
            yield return null;
            Player.TakeDamage(1e6f, Player.transform.position, false);
            for (var i = 0; i < 10 && _session.State == SessionState.Playing; i++) yield return null;
            Assert.AreEqual(SessionState.Defeat, _session.State, "the run is over");
            StringAssert.Contains("Survived 1:1", Mission.FailReason, "the time survived is shown");
            Assert.GreaterOrEqual(_session.Settings.endlessBestSolo, 75f, "the best time is kept");
        }
    }
}
