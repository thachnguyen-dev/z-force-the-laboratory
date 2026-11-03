using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-08: on the first rescue mission an invisible car blocked movement and bullets. On every story map,
    /// every obstacle model that is placed (and so blocks) must be drawn: a mesh with triangles.
    /// </summary>
    public class VisibleObstacleTests
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

        [UnityTest]
        public IEnumerator EveryPlacedObstacleModelIsDrawn()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            var problems = new List<string>();
            var stages = new List<string>();
            foreach (var m in Campaign.Missions) stages.AddRange(m.stages);
            foreach (var id in stages)
            {
                var layout = StageLibrary.Load(id);
                _session.StartStage(layout, _session.Tuning.characters[0], 41);
                yield return null;
                // Every prop of the owner's pack the map places.
                var wanted = layout.Map.props.Where(p => { var piece = Art.ArtLibrary.Get(p.id); return piece != null && piece.Model != null && !piece.Bush; }).ToList();
                var drawn = _session.StageRoot.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.StartsWith("Model ") || f.transform.parent != null && f.transform.parent.name.StartsWith("Model ")).ToList();
                Debug.Log($"[ZOMBIE-UNSEEN-COUNT] {id}: {wanted.Count} placed, {drawn.Count} drawn, cars {wanted.Count(p => p.id == "car" || p.id == "van")}");
                foreach (var p in wanted)
                {
                    var near = drawn.Where(f => Vector2.Distance(new Vector2(f.transform.position.x, f.transform.position.z), new Vector2(p.x, p.z)) < 0.6f).ToList();
                    var seen = near.Any(f => f.sharedMesh != null && f.sharedMesh.triangles.Length > 0 && f.sharedMesh.bounds.size.y > 0.3f && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled);
                    if (!seen)
                        problems.Add($"{id}: {p.id} at {p.x:0.0},{p.z:0.0} (turn {p.rotation:0}, roll {p.roll:0}, pitch {p.pitch:0}, flip {p.flip}, variant {p.variant}, scale {p.scale:0.##}): {(near.Count == 0 ? "no model" : "an empty or flat mesh")}");
                }
                _session.GoTo(SessionState.MainMenu);
                yield return null;
            }
            foreach (var p in problems) Debug.Log("[ZOMBIE-UNSEEN] " + p);
            Assert.IsEmpty(problems, string.Join("\n", problems.Take(20)));
        }
    }
}
