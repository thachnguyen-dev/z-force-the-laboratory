using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    /// <summary>The main story and its maps (owner, 2026-10-03).</summary>
    public class StoryTests
    {
        [Test]
        public void TheVaccineMissionComesLast()
        {
            // Owner, 2026-10-07: Vaccine Collection (two rival teams) is the final mission; "6. The Vaccine" just before it.
            var missions = Campaign.Missions;
            Assert.GreaterOrEqual(missions.Count, 7);
            var last = missions[missions.Count - 1];
            Assert.AreEqual("m7_collection", last.id);
            Assert.AreEqual("m6_vaccine", missions[missions.Count - 2].id);
            foreach (var id in last.stages)
            {
                var map = EditorTools.StageFiles.Load(id);
                Assert.AreEqual("vaccine", map.mission);
                var rivals = 0;
                for (var x = 0; x < map.width; x++)
                for (var z = 0; z < map.height; z++)
                    if (map[x, z] == Tile.RivalSpawn) rivals++;
                Assert.AreEqual(2, rivals, $"{id}: two rival teams");
            }
            for (var i = 0; i < missions.Count - 2; i++)
                foreach (var id in missions[i].stages) Assert.AreNotEqual("vaccine", EditorTools.StageFiles.Load(id).mission, missions[i].title);
        }

        [Test]
        public void EveryMissionHasSeveralMapsThatPlay()
        {
            foreach (var m in Campaign.Missions)
            {
                // The tutorial is played once; every other mission has several maps for replays.
                if (m.id != "m0_tutorial") Assert.GreaterOrEqual(m.stages.Count, 2, $"{m.title}: replays use different maps");
                foreach (var id in m.stages)
                {
                    Assert.IsTrue(StageLibrary.Exists(id), id);
                    var map = EditorTools.StageFiles.Load(id);
                    CollectionAssert.IsEmpty(StageMapBuilder.Validate(map).FindAll(p => p.StartsWith("Error")), id);
                    CollectionAssert.IsEmpty(EditorTools.StageForge.Unreachable(map), $"{id}: every marker can be reached");
                }
            }
        }

        [Test]
        public void SoloUnlocksOneByOneCoopHasEverything()
        {
            var settings = new SettingsData();
            Assert.IsTrue(Campaign.Unlocked(0, settings, false));
            Assert.IsFalse(Campaign.Unlocked(1, settings, false), "Solo: the second waits for the first");
            Assert.IsTrue(Campaign.Unlocked(5, settings, true), "Co-op: everything open");
            settings.completedMissions.Add(Campaign.Missions[0].id);
            Assert.IsTrue(Campaign.Unlocked(1, settings, false));
            Assert.IsFalse(Campaign.Unlocked(2, settings, false));
            settings.completedCoop.Add(Campaign.Missions[1].id);
            Assert.IsFalse(Campaign.Unlocked(2, settings, false), "a co-op win doesn't unlock Solo");
        }

        [Test]
        public void ReplaysRotateThroughTheMaps()
        {
            // A mission with more than one map (the tutorial has one).
            CampaignMission mission = null;
            foreach (var m in Campaign.Missions)
                if (m.stages.Count > 1) { mission = m; break; }
            var settings = new SettingsData();
            var first = Campaign.NextStage(mission, settings);
            settings.SetLastPlayed(mission.id, first);
            var second = Campaign.NextStage(mission, settings);
            Assert.AreNotEqual(first, second);
            settings.SetLastPlayed(mission.id, second);
            Assert.AreEqual(first, Campaign.NextStage(mission, settings));
        }

        private static int Count(StageMap map, Tile tile)
        {
            var n = 0;
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == tile) n++;
            return n;
        }

        [Test]
        public void OnlyBossMissionsHaveTheMainBossTheOthersHaveSubBosses()
        {
            foreach (var m in Campaign.Missions)
                foreach (var id in m.stages)
                {
                    var map = EditorTools.StageFiles.Load(id);
                    if (map.mission == "boss")
                    {
                        Assert.AreEqual(1, Count(map, Tile.BossSpawn), id);
                        Assert.Greater(map.bossArea.w, 0, id);
                    }
                    else
                    {
                        Assert.AreEqual(0, Count(map, Tile.BossSpawn), id);
                        Assert.GreaterOrEqual(Count(map, Tile.SubBossSpawn), 1, $"{id}: secondary bosses on the route");
                    }
                }
        }

        [Test]
        public void TheCampaignStartsWithTheTutorialThenTheBear()
        {
            // Owner, 2026-10-07: 1. the tutorial (Basic, Fast, Rat zombies and Boss 1), 2. the Bear's boss stage.
            Assert.AreEqual("m0_tutorial", Campaign.Missions[0].id);
            var tutorial = EditorTools.StageFiles.Load(Campaign.Missions[0].stages[0]);
            Assert.IsTrue(tutorial.tutorial);
            Assert.AreEqual("boss", tutorial.mission);
            Assert.AreEqual("Boss", tutorial.bossType, "Boss 1 at the end of the tutorial");
            Assert.AreEqual("m1_gate", Campaign.Missions[1].id);
            foreach (var id in Campaign.Missions[1].stages)
                Assert.AreEqual("Bear", EditorTools.StageFiles.Load(id).bossType, $"{id}: the Bear is the boss");
            // Boss 1 is only the large zombie after the tutorial: the Wildlife Reserve's boss is the Tiger.
            foreach (var m in Campaign.Missions)
                if (m.id == "m4_reserve")
                    foreach (var id in m.stages)
                        Assert.AreEqual("Tiger", EditorTools.StageFiles.Load(id).bossType, $"{id}: the Tiger is the boss");
        }

        [Test]
        public void TheBombTargetIsRingedBySpawnPoints()
        {
            foreach (var m in Campaign.Missions)
                foreach (var id in m.stages)
                {
                    var map = EditorTools.StageFiles.Load(id);
                    if (map.mission != "bomb") continue;
                    Vector2 target = default;
                    var spawns = new List<Vector2>();
                    for (var x = 0; x < map.width; x++)
                    for (var z = 0; z < map.height; z++)
                    {
                        if (map[x, z] == Tile.BombTarget) target = new Vector2(x, z);
                        if (map[x, z] == Tile.ZombieSpawn) spawns.Add(new Vector2(x, z));
                    }
                    Assert.GreaterOrEqual(spawns.FindAll(s => (s - target).magnitude <= 25f).Count, 12, id);
                }
        }

        [Test]
        public void MapsDifferEnough()
        {
            // Two maps of one mission share well under half of their wall cells.
            foreach (var m in Campaign.Missions)
            {
                if (m.stages.Count < 2) continue;
                var a = EditorTools.StageFiles.Load(m.stages[0]);
                var b = EditorTools.StageFiles.Load(m.stages[1]);
                int walls = 0, shared = 0;
                for (var x = 0; x < Mathf.Min(a.width, b.width); x++)
                for (var z = 0; z < Mathf.Min(a.height, b.height); z++)
                {
                    if (a[x, z] != Tile.Wall) continue;
                    walls++;
                    if (b[x, z] == Tile.Wall) shared++;
                }
                Assert.Less(shared, walls * 0.4f, m.title);
            }
        }

        /// <summary>
        /// Owner, 2026-10-05: no hiding grass on roads or floors; no scratch marks on the ground; wind-blown leaves and
        /// paper only in normal stages, brown puddles only in rainy ones; street and traffic lights along town roads; the
        /// helicopter centred on its pad; road lines all asphalt outside the reserve.
        /// </summary>
        [Test]
        public void StoryMapsFollowTheStreetRules()
        {
            foreach (var m in Campaign.Missions)
                foreach (var id in m.stages)
                {
                    var map = EditorTools.StageFiles.Load(id);
                    for (var x = 0; x < map.width; x++)
                    for (var z = 0; z < map.height; z++)
                    {
                        var s = map.SurfaceAt(x, z);
                        if (map[x, z] == Tile.Bush)
                            Assert.IsFalse(s == Surface.Road || s == Surface.Road2 || s == Surface.Floor, $"{id}: grass on a road or floor at {x},{z}");
                    }
                    var normal = map.weather != "rain" && !map.dark;
                    foreach (var d in map.decorations)
                    {
                        Assert.AreNotEqual("claw_marks", d.id, $"{id}: scratch marks belong on buildings");
                        Assert.AreNotEqual("crack", d.id, $"{id}: cracks belong on buildings");
                        if (d.id == "wind_leaves" || d.id == "wind_paper") Assert.IsTrue(normal, $"{id}: wind-blown bits only in normal stages");
                        if (d.id == "rain_puddle") Assert.AreEqual("rain", map.weather, $"{id}: puddles only when it rains");
                    }
                    var heli = map.props.Find(p => p.id == "helicopter");
                    Assert.IsNotNull(heli, id);
                    Assert.Less(Vector2.Distance(new Vector2(heli.x, heli.z), new Vector2(map.spawnArea.x + map.spawnArea.w * 0.5f, map.spawnArea.z + map.spawnArea.d * 0.5f)), 0.6f,
                        $"{id}: the helicopter sits on the H");
                    // Street and traffic lights are the town's; the tutorial's training ground has none.
                    if (map.theme == "reserve" || map.tutorial) continue;
                    Assert.Greater(map.props.FindAll(p => p.id == "street_light").Count, 10, $"{id}: street lights");
                    Assert.Greater(map.props.FindAll(p => p.id == "traffic_light").Count, 4, $"{id}: traffic lights");
                    var dirt = 0;
                    for (var x = 0; x < map.width; x++)
                    for (var z = 0; z < map.height; z++)
                        if (map.SurfaceAt(x, z) == Surface.Road2) dirt++;
                    Assert.AreEqual(0, dirt, $"{id}: town, lab and checkpoint roads are all asphalt");
                }
        }
    }
}
