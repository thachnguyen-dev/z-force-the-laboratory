using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-07: buildings and props cast real shadows; characters and zombies keep dark ellipse shadows; meshes
    /// keep only the faces that can be seen or that cast a shadow.
    /// </summary>
    public class ShadowTests
    {
        private GameSession _session;
        private GameObject _sunObject;

        private IEnumerator Start(string stageId)
        {
            // The test scene has no sun; the game scene does.
            if (!Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Any(l => l.type == LightType.Directional))
                _sunObject = new GameObject("Sun", typeof(Light)) { };
            if (_sunObject != null) _sunObject.GetComponent<Light>().type = LightType.Directional;
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stageId), _session.Tuning.characters[0], 41);
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
            if (_sunObject != null) Object.Destroy(_sunObject);
            yield return null;
        }

        private static Light Sun => Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);

        [UnityTest]
        public IEnumerator BuildingsAndPropsCastShadowsByDay()
        {
            yield return Start("m1_gate_a");
            Assert.AreNotEqual(LightShadows.None, Sun.shadows, "the sun casts shadows by day");
            var renderers = _session.StageRoot.GetComponentsInChildren<MeshRenderer>();
            bool Casts(string prefix) => renderers.Where(r => r.name.StartsWith(prefix) && !r.name.Contains("indoors")).All(r => r.shadowCastingMode == ShadowCastingMode.On);
            // Owner, 2026-10-07: nothing inside a building casts (the roof covers it).
            var buildings = _session.Stage.Layout.Map.buildings;
            bool Inside(Vector3 p) => buildings.Any(b => p.x > b.x && p.x < b.x + b.w && p.z > b.z && p.z < b.z + b.d);
            Assert.IsTrue(renderers.Any(r => r.name.Contains("indoors")), "furniture inside buildings built");
            Assert.IsTrue(renderers.Where(r => r.name.Contains("indoors")).All(r => r.shadowCastingMode == ShadowCastingMode.Off), "furniture inside buildings casts no shadow");
            Assert.IsTrue(renderers.Any(r => r.name.StartsWith("Blocks")), "walls built");
            Assert.IsTrue(Casts("Blocks"), "walls cast");
            Assert.IsTrue(Casts("Roof"), "roofs cast");
            Assert.IsTrue(Casts("Prop "), "props cast");
            // The owner's obstacle models (under "Props"; chests, barrels and items are shown as "Model" too and don't cast).
            var models = renderers.Where(r => r.name.StartsWith("Model ") && r.transform.parent != null && r.transform.parent.name == "Props").ToList();
            Assert.IsTrue(models.Count > 0, "the owner's obstacle models placed");
            Assert.IsTrue(models.Where(r => !Inside(r.transform.position)).All(r => r.shadowCastingMode == ShadowCastingMode.On), "the owner's obstacle models cast");
            Assert.IsTrue(models.Where(r => Inside(r.transform.position)).All(r => r.shadowCastingMode == ShadowCastingMode.Off), "but not inside buildings");
            Assert.IsTrue(renderers.Where(r => r.name.StartsWith("Decoration ")).All(r => r.shadowCastingMode == ShadowCastingMode.Off), "flat decorations don't");
            // Characters and zombies: no real shadow, a dark ellipse.
            var player = _session.LocalSurvivor;
            Assert.IsTrue(player.GetComponentsInChildren<Renderer>().Where(r => r.name != "Blob shadow").All(r => r.shadowCastingMode == ShadowCastingMode.Off), "the character casts no real shadow");
            Assert.IsNotNull(player.transform.Find("Blob shadow"), "but has its ellipse");
            foreach (var z in Zombie.Alive.Take(10))
            {
                Assert.IsTrue(z.GetComponentsInChildren<Renderer>().All(r => r.shadowCastingMode == ShadowCastingMode.Off), "zombies cast no real shadow");
                Assert.IsTrue(z.GetComponentsInChildren<Transform>().Any(t => t.name == "Blob shadow"), "but have their ellipse");
            }
        }

        [UnityTest]
        public IEnumerator TheEllipseLiesOnTheShadowSideByDayAndUnderTheFeetAtNight()
        {
            yield return Start("m1_gate_a");
            var player = _session.LocalSurvivor;
            var blob = player.transform.Find("Blob shadow");
            player.transform.rotation = Quaternion.Euler(0f, 70f, 0f);
            yield return null;
            blob.GetComponent<BlobShadowPlacer>().Place();
            var away = new Vector3(-TileAssembler.ToSun.x, 0f, -TileAssembler.ToSun.z).normalized;
            var offset = blob.position - player.transform.position;
            offset.y = 0f;
            Assert.Greater(Vector3.Dot(offset, away), 0.2f, "by day it lies away from the sun, beside the body");
            Assert.Less(away.x, -0.5f, "toward the west");
            Assert.Less(away.z, -0.5f, "and the south (owner, 2026-10-07)");
            Assert.Greater(Vector3.Dot(blob.forward, away), 0.99f, "stretched that way, whichever way the character faces");
            _session.StageRoot.GetComponent<StageAtmosphere>().Set(false, true);
            StageConditions.Set(false, true, _session.Tuning);
            yield return null;
            blob.GetComponent<BlobShadowPlacer>().Place();
            offset = blob.position - player.transform.position;
            offset.y = 0f;
            Assert.Less(offset.magnitude, 0.01f, "at night it sits under the feet");
            Assert.AreEqual("BlobShadow", blob.GetComponent<MeshRenderer>().sharedMaterial.shader.name.Replace("Zombie/", ""), "drawn with its own darkening shader");
        }

        [UnityTest]
        public IEnumerator NoShadowMapAtNight()
        {
            yield return Start("m7_collection_a");
            Assert.AreEqual(LightShadows.None, Sun.shadows, "no shadows at night");
            _session.StageRoot.GetComponent<StageAtmosphere>().Set(false, false);
            Assert.AreNotEqual(LightShadows.None, Sun.shadows, "they come back by day");
        }

        [UnityTest]
        public IEnumerator MeshesKeepOnlyFacesTheCameraOrTheSunCanReach()
        {
            yield return Start("m1_gate_a");
            var checkedModels = 0;
            var mirrored = 0;
            foreach (var r in _session.StageRoot.GetComponentsInChildren<MeshRenderer>())
            {
                var model = r.name.StartsWith("Model ") && r.transform.parent != null && r.transform.parent.name == "Props";
                if (!(model || r.name.StartsWith("Blocks") || r.name.StartsWith("Prop") || r.name.StartsWith("Roof"))) continue;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var m = r.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var n = mesh.normals;
                var t = mesh.triangles;
                var outward = 0;
                for (var i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                    var face = Vector3.Cross(b - a, c - a);
                    if (face.sqrMagnitude < 1e-14f) continue;
                    Assert.IsTrue(Art.MeshCull.Keep(face, -0.005f), $"{r.name} ({mesh.name}, turned {r.transform.eulerAngles}, scale {r.transform.lossyScale}, parent {r.transform.parent?.name}): a face neither the camera nor the sun can reach: {face.normalized}");
                    var smooth = m.MultiplyVector(n[t[i]] + n[t[i + 1]] + n[t[i + 2]]);
                    if (Vector3.Dot(face, smooth) > 0f) outward++;
                }
                if (model)
                {
                    checkedModels++;
                    // Mirrored ones too: the faces still point the way their normals do (not inside out).
                    Assert.Greater(outward, t.Length / 3 * 0.8f, $"{r.name}: faces point outward");
                    if (mesh.name.EndsWith("placed")) mirrored++;
                }
            }
            Assert.Greater(checkedModels, 0, "the owner's models checked");
        }

        [Test]
        public void AModelLosesItsHiddenFacesButKeepsItsColours()
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var source = Object.Instantiate(box.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(box);
            var colors = Enumerable.Repeat(new Color(0.3f, 0.5f, 0.2f), source.vertexCount).ToArray();
            source.colors = colors;
            foreach (var flip in new[] { false, true })
            {
                var placed = Art.MeshCull.Placed(source, 0f, Quaternion.identity, 2f, flip);
                Assert.Less(placed.triangles.Length, source.triangles.Length, "hidden faces dropped");
                // A cube under this camera and sun: the top, the south face (seen), the north and east faces (towards the
                // sun) stay; the west face and the bottom go.
                Assert.AreEqual(4 * 2 * 3, placed.triangles.Length, "top, front and the two sun sides");
                Assert.IsTrue(placed.colors.All(c => c == colors[0]), "its own colours");
            }
        }
    }
}
