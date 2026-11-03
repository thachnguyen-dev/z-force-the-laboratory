using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    /// <summary>Owner, 2026-09-30: every stage's map is freely editable; the checks tell the author what's missing.</summary>
    public class StageAuthoringTests
    {
        private static StageMap Minimal()
        {
            var map = StageMap.Blank("m", 30, 30);
            map[3, 3] = Tile.PlayerSpawn;
            map[25, 25] = Tile.BossSpawn;
            map[10, 10] = Tile.LootSpawn;
            map[12, 12] = Tile.ZombieSpawn;
            map.spawnArea = new MapRect { x = 1, z = 1, w = 6, d = 6 };
            map.bossArea = new MapRect { x = 22, z = 22, w = 6, d = 6 };
            return map;
        }

        /// <summary>Owner, 2026-10-01 (final): base ground, then surfaces (looks only), then the gameplay 3D blocks.</summary>
        [Test]
        public void LayersSaveAndLoad()
        {
            var map = Minimal();
            map.baseGround = "earth";
            map.SetSurface(5, 5, Surface.Grass);
            map.SetSurface(6, 5, Surface.Grass2);
            map.SetSurface(7, 5, Surface.Road);
            map.SetSurface(8, 5, Surface.Road2);
            map.SetSurface(9, 5, Surface.Floor);
            map[9, 5] = Tile.Wall;
            map.decorations.Add(new MapDecoration { id = "rubble", x = 4.25f, z = 9.5f, rotation = 30f, scale = 1.5f });
            map.Pack();
            var loaded = StageMap.FromJson(map.ToJson());
            Assert.AreEqual("earth", loaded.baseGround);
            Assert.AreEqual(Surface.Grass, loaded.SurfaceAt(5, 5));
            Assert.AreEqual(Surface.Grass2, loaded.SurfaceAt(6, 5));
            Assert.AreEqual(Surface.Road, loaded.SurfaceAt(7, 5));
            Assert.AreEqual(Surface.Road2, loaded.SurfaceAt(8, 5));
            Assert.AreEqual(Surface.Floor, loaded.SurfaceAt(9, 5), "a surface lies under a block");
            Assert.AreEqual(Tile.Wall, loaded[9, 5]);
            Assert.AreEqual(Tile.Ground, loaded[5, 5], "surfaces are not gameplay cells");
            Assert.AreEqual(1, loaded.decorations.Count);
            Assert.AreEqual(4.25f, loaded.decorations[0].x);
            Assert.AreEqual(1.5f, loaded.decorations[0].scale);
        }

        [Test]
        public void OlderFilesAreConverted()
        {
            // Before 2026-10-01 floors, grass and roads were cells, and '~' was water (now removed).
            var json = "{\"name\":\"old\",\"width\":4,\"height\":2,\"rows\":[\"_l_~\",\",=#.\"]}";
            var map = StageMap.FromJson(json);
            Assert.AreEqual(Tile.Ground, map[0, 1]);
            Assert.AreEqual(Surface.Floor, map.SurfaceAt(0, 1));
            Assert.AreEqual(Tile.LootSpawn, map[1, 1]);
            Assert.AreEqual(Surface.Floor, map.SurfaceAt(1, 1), "a marker takes its neighbours' surface");
            Assert.AreEqual(Tile.Ground, map[3, 1], "water became ground");
            Assert.AreEqual(Surface.Grass, map.SurfaceAt(0, 0));
            Assert.AreEqual(Surface.Road, map.SurfaceAt(1, 0));
            Assert.AreEqual(Tile.Wall, map[2, 0]);
        }

        [Test]
        public void TileSetOverridesBlocks()
        {
            var set = ScriptableObject.CreateInstance<TileSet>();
            set.wall = new GameObject("wall");
            var crate = new GameObject("crate");
            set.cover = new GameObject("cover");
            set.coverVariants = new[] { crate };
            Assert.IsNull(set.Block(Tile.Ground, 3, 3), "ground has no block: the base ground shows");
            Assert.IsNull(ScriptableObject.CreateInstance<TileSet>().Block(Tile.Wall, 3, 3), "empty slots: the built-in art");
            Assert.IsNull(set.Block(Tile.LootSpawn, 3, 3));
            Assert.AreEqual(set.wall, set.Block(Tile.Wall, 3, 3));
            var seen = new System.Collections.Generic.HashSet<GameObject>();
            for (var x = 0; x < 20; x++) seen.Add(set.Block(Tile.Cover, x, 0));
            Assert.IsTrue(seen.Contains(crate) && seen.Contains(set.cover), "cover variants are mixed");
            foreach (var o in new Object[] { set.wall, crate, set.cover, set }) Object.DestroyImmediate(o);
        }

        [Test]
        public void BlocksKeepOnlyTheFacesTheCameraSees()
        {
            // A unit cube: 6 faces = 12 triangles. The fixed camera sees the top and the south side; the north and east sides
            // face the sun (in the north-east) and stay for its shadow (owner, 2026-10-07: walls cast shadows).
            var parts = TileAssembler.Cull(TileAssembler.PlaceholderBlock(Tile.Wall));
            Assert.AreEqual(1, parts.Top.Count);
            Assert.AreEqual(1, parts.Front.Count);
            Assert.AreEqual(18, parts.Top[0].Mesh.vertexCount, "top and the two sun sides: 6 triangles");
            Assert.AreEqual(6, parts.Front[0].Mesh.vertexCount, "south side: 2 triangles");
            foreach (var n in parts.Top[0].Mesh.normals) Assert.IsTrue(n.y > 0.9f || n.x > 0.9f || n.z > 0.9f);
            foreach (var n in parts.Front[0].Mesh.normals) Assert.Less(n.z, -0.9f);
        }

        [Test]
        public void HiddenSouthSidesAreSkipped()
        {
            var map = StageMap.Blank("m", 6, 6);
            map[2, 3] = Tile.Wall;
            map[2, 2] = Tile.Wall;   // in front of (2, 3): hides its south side
            map[3, 3] = Tile.Cover;
            map[3, 2] = Tile.Wall;   // taller, in front of the cover: hides it
            map[4, 3] = Tile.Wall;
            map[4, 2] = Tile.Cover;  // lower, in front of the wall: the wall's side shows above it
            map[1, 0] = Tile.Wall;   // south edge: faces off the map
            Assert.IsTrue(TileAssembler.FrontHidden(map, 2, 3, Tile.Wall));
            Assert.IsTrue(TileAssembler.FrontHidden(map, 3, 3, Tile.Cover));
            Assert.IsFalse(TileAssembler.FrontHidden(map, 4, 3, Tile.Wall));
            Assert.IsFalse(TileAssembler.FrontHidden(map, 2, 2, Tile.Wall));
            Assert.IsFalse(TileAssembler.FrontHidden(map, 1, 0, Tile.Wall));
        }

        [Test]
        public void UnknownDecorationsAndGroundsAreReported()
        {
            var map = Minimal();
            map.baseGround = "lava";
            map.decorations.Add(new MapDecoration { id = "no_such_thing", x = 5f, z = 5f });
            map.decorations.Add(new MapDecoration { id = "rubble", x = -3f, z = 5f });
            var problems = string.Join("\n", StageMapBuilder.Validate(map));
            StringAssert.Contains("lava", problems);
            StringAssert.Contains("no_such_thing", problems);
            StringAssert.Contains("outside the map", problems);
        }

        [Test]
        public void EveryBaseGroundIsPlain()
        {
            // Owner, 2026-10-01: the base ground stays simple so it never competes with the blocks (2026-10-03: brighter).
            foreach (var p in BaseGround.Presets)
            {
                Assert.Less(p.Light.maxColorComponent - p.Dark.maxColorComponent, 0.08f, $"{p.Id}: low contrast");
            }
        }

        [Test]
        public void AMinimalStageHasNoErrors() => CollectionAssert.IsEmpty(StageMapBuilder.Validate(Minimal()).FindAll(p => p.StartsWith("Error")));

        [Test]
        public void MissingEssentialsAreErrors()
        {
            var map = StageMap.Blank("empty", 20, 20);
            var problems = string.Join("\n", StageMapBuilder.Validate(map));
            StringAssert.Contains("no player spawn", problems);
            StringAssert.Contains("Main Boss", problems);
            StringAssert.Contains("Spawn Area", problems);

            var rescue = Minimal();
            rescue.mission = "rescue";
            StringAssert.Contains("hostage", string.Join("\n", StageMapBuilder.Validate(rescue)));
        }

        [Test]
        public void BadZombiesAndClosedBuildingsAreReported()
        {
            var map = Minimal();
            map.zombies.Add(new MapZombie { type = "Dragon", x = 5, z = 5 });
            map.buildings.Add(new MapBuilding { name = "Box", x = 14, z = 14, w = 4, d = 4 });
            for (var x = 14; x <= 18; x++) map[x, 14] = map[x, 18] = Tile.Wall;
            for (var z = 14; z <= 18; z++) map[14, z] = map[18, z] = Tile.Wall;
            var problems = string.Join("\n", StageMapBuilder.Validate(map));
            StringAssert.Contains("Dragon", problems);
            StringAssert.Contains("no doorway", problems);
        }

        [Test]
        public void PlacedZombiesAndConditionsReachTheLayout()
        {
            var map = Minimal();
            map.zombies.Add(new MapZombie { type = "Tank", x = 15, z = 8 });
            map.weather = "rain";
            map.dark = true;
            map.tileSet = "night_city";
            var layout = StageMapBuilder.ToLayout(StageMap.FromJson(map.ToJsonPacked()));
            Assert.AreEqual(1, layout.PlacedZombies.Count);
            Assert.AreEqual(ZombieType.Tank, layout.PlacedZombies[0].type);
            Assert.AreEqual(StageMap.CellCenter(15, 8), layout.PlacedZombies[0].position);
            Assert.IsTrue(layout.Rain);
            Assert.IsTrue(layout.Dark);
            Assert.AreEqual("night_city", layout.TileSetName);
        }

        [Test]
        public void EveryListedStageLoadsWithoutErrors()
        {
            Assert.Greater(StageLibrary.Ids.Count, 0);
            foreach (var id in StageLibrary.Ids)
            {
                var asset = Resources.Load<TextAsset>($"Stages/{id}");
                Assert.IsNotNull(asset, $"stage file {id}");
                var errors = StageMapBuilder.Validate(StageMap.FromJson(asset.text)).FindAll(p => p.StartsWith("Error"));
                CollectionAssert.IsEmpty(errors, $"stage {id}");
            }
        }
    }

    internal static class StageMapTestExtensions
    {
        public static string ToJsonPacked(this StageMap map)
        {
            map.Pack();
            return map.ToJson();
        }
    }
}
