using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-11: many zombies spawned stuck inside walls or obstacles. On every story map, no zombie (real or
    /// dormant) may stand inside a wall, a building or an obstacle.
    /// </summary>
    public class ZombieSpawnPlaceTests
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

        private const int Solid = (1 << Layers.Obstacle) | (1 << Layers.ShootThrough);

        /// <summary>What solid thing a zombie standing here would be inside, or null.</summary>
        public static Collider InsideOf(Vector3 p, float radius)
        {
            var hits = Physics.OverlapCapsule(p + Vector3.up * (radius + 0.1f), p + Vector3.up * 1.2f, radius, Solid, QueryTriggerInteraction.Ignore);
            return hits.Length > 0 ? hits[0] : null;
        }

        [UnityTest]
        public IEnumerator EveryAreaGetsItsZombies()
        {
            // Owner, 2026-10-11: on the Wildlife Reserve the zombies were all on the boss's side (placing stopped at the
            // population limit in the map's order). Every area that places zombies gets some, and the limit holds.
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var problems = new List<string>();
            var report = new System.Text.StringBuilder();
            foreach (var m in Campaign.Missions)
                foreach (var id in m.stages)
                {
                    _session.StartStage(StageLibrary.Load(id), _session.Tuning.characters[0], 41);
                    for (var i = 0; i < 3; i++) yield return null;
                    var layout = _session.Stage.Layout;
                    var pts = Zombie.Alive.Select(z => z.transform.position).Concat(_session.Stage.Population.Records.Select(r => r.Position)).ToList();
                    int areas = 0, empty = 0;
                    foreach (var area in layout.Areas)
                    {
                        if (area.FreePoints.Count == 0 || area.Placed.Sum(p => p.count) == 0) continue;
                        areas++;
                        var grown = new Rect(area.Bounds.x - 2f, area.Bounds.y - 2f, area.Bounds.width + 4f, area.Bounds.height + 4f);
                        if (pts.Any(p => grown.Contains(new Vector2(p.x, p.z)))) continue;
                        empty++;
                        problems.Add($"{id}: {area.Name} has no zombies");
                    }
                    var total = _session.Stage.Population.Total;
                    report.AppendLine($"{id}: {areas} areas with zombies, {empty} empty; {total} zombies, limit {_session.Stage.MaxPopulation}");
                    if (total > _session.Stage.MaxPopulation + 2) problems.Add($"{id}: {total} zombies over the limit {_session.Stage.MaxPopulation}");
                    _session.GoTo(SessionState.MainMenu);
                    yield return null;
                }
            Debug.Log("[ZOMBIE-AREAS]\n" + report);
            Assert.IsEmpty(problems, report + string.Join("\n", problems.Take(25)));
        }

        [UnityTest]
        public IEnumerator NoZombieStartsInsideAWallOrObstacle()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var problems = new List<string>();
            var report = new System.Text.StringBuilder();
            foreach (var m in Campaign.Missions)
                foreach (var id in m.stages)
                {
                    _session.StartStage(StageLibrary.Load(id), _session.Tuning.characters[0], 41);
                    for (var i = 0; i < 3; i++) yield return null;
                    int real = 0, dormant = 0, stuck = 0;
                    foreach (var z in Zombie.Alive)
                    {
                        real++;
                        var c = InsideOf(z.transform.position, Mathf.Min(0.3f, z.Def.radius * 0.6f));
                        if (c == null) continue;
                        stuck++;
                        if (problems.Count < 40) problems.Add($"{id}: {z.Def.type} at {z.transform.position.x:0.0},{z.transform.position.z:0.0} inside {c.name}");
                    }
                    foreach (var r in _session.Stage.Population.Records)
                    {
                        dormant++;
                        var c = InsideOf(r.Position, 0.25f);
                        if (c == null) continue;
                        stuck++;
                        if (problems.Count < 40) problems.Add($"{id}: dormant {r.Type} at {r.Position.x:0.0},{r.Position.y:0.00},{r.Position.z:0.0} inside {c.name} (box {c.bounds.min.y:0.0}-{c.bounds.max.y:0.0} y, centre {c.bounds.center.x:0.0},{c.bounds.center.z:0.0} size {c.bounds.size.x:0.0}x{c.bounds.size.z:0.0}; home {r.Home.x:0.0},{r.Home.z:0.0})");
                    }
                    report.AppendLine($"{id}: {real} real, {dormant} dormant, {stuck} inside something");
                    _session.GoTo(SessionState.MainMenu);
                    yield return null;
                }
            Debug.Log("[ZOMBIE-STUCK]\n" + report + string.Join("\n", problems));
            Assert.IsEmpty(problems, report + string.Join("\n", problems.Take(25)));
        }
    }
}
