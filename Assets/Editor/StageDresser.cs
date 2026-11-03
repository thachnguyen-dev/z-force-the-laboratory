using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// ⚠ Dresses Claude's placeholder test stages with the map art (2026-10-02): surfaces (grass patches, a concrete road,
    /// dirt paths), props (cars, trees, rocks, barrels, barriers, lamps, bushes; lab furniture indoors) and decorations.
    /// It never touches a stage that already has props or decorations, and never the owner's stages (only the ids below).
    /// Doorways, markers and lanes are kept clear.
    /// </summary>
    public static class StageDresser
    {
        public static readonly string[] TestStages = { "test_stage", "test_rain", "test_dark", "test_rescue", "test_bomb", "test_vaccine" };

        [MenuItem("Zombie/Developer (Claude)/Dress Test Stages (only undressed ones)")]
        public static void DressAll()
        {
            foreach (var id in TestStages)
            {
                if (!StageFiles.Exists(id)) continue;
                var map = StageFiles.Load(id);
                if (map.props.Count > 0 || map.decorations.Count > 0) continue;
                Dress(map, 4242);
                StageFiles.Save(id, map);
                Debug.Log($"[ZOMBIE-ART] Dressed {id}: {map.props.Count} props, {map.decorations.Count} decorations");
            }
        }

        private class Ctx
        {
            public StageMap Map;
            public System.Random Rng;
            public bool[,] Taken;
            public bool[,] Indoor;
            public Vector3 Spawn;
            public string Flavor;

            public float R() => (float)Rng.NextDouble();
            public float R(float a, float b) => a + R() * (b - a);
        }

        /// <summary>Dresses a map. <paramref name="flavor"/>: the generated stages' theme (null = the test stage's fixed roads).</summary>
        public static void Dress(StageMap map, int seed, string flavor = null)
        {
            var c = new Ctx { Map = map, Rng = new System.Random(seed), Taken = new bool[map.width, map.height], Indoor = new bool[map.width, map.height], Flavor = flavor };
            foreach (var b in map.buildings)
                for (var x = b.x; x <= b.x + b.w; x++)
                for (var z = b.z; z <= b.z + b.d; z++)
                    if (x >= 0 && z >= 0 && x < map.width && z < map.height) c.Indoor[x, z] = true;
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == Tile.PlayerSpawn) c.Spawn = StageMap.CellCenter(x, z);
            if (flavor == null) PaintSurfaces(c);
            // Props already placed (the generator's set pieces) keep their room.
            foreach (var p in map.props)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                if (piece == null) continue;
                var half = Mathf.Max(piece.Footprint.x, piece.Footprint.y) * 0.5f * (p.scale > 0f ? p.scale : 1f);
                Take(c, new Vector3(p.x, 0f, p.z), half + 1f, half + 1f);
            }
            PlaceOutdoorProps(c);
            PlaceIndoorProps(c);
            PlaceDecorations(c);
        }

        // ------------------------------------------------------------------ surfaces

        private static void PaintSurfaces(Ctx c)
        {
            var map = c.Map;
            void Paint(int x0, int z0, int x1, int z1, Surface s)
            {
                for (var x = Mathf.Max(1, x0); x <= Mathf.Min(map.width - 2, x1); x++)
                for (var z = Mathf.Max(1, z0); z <= Mathf.Min(map.height - 2, z1); z++)
                    if (!c.Indoor[x, z] && !Tiles.IsSolid(map[x, z])) map.SetSurface(x, z, s);
            }
            // Grass patches with natural outlines, a second, wilder grass at their edges.
            for (var x = 1; x < map.width - 1; x++)
            for (var z = 1; z < map.height - 1; z++)
            {
                if (c.Indoor[x, z] || Tiles.IsSolid(map[x, z])) continue;
                var n = Art.MeshKit.Noise2(x / 9f, z / 9f) * 0.7f + Art.MeshKit.Noise2(x / 3.5f + 40f, z / 3.5f) * 0.3f;
                if (n > 0.6f) map.SetSurface(x, z, Surface.Grass);
                else if (n > 0.53f) map.SetSurface(x, z, Surface.Grass2);
                // Bushes always sit in grass.
                if (map[x, z] == Tile.Bush) map.SetSurface(x, z, Surface.Grass);
            }
            // A concrete road across the stage and one along the Checkpoint; dirt paths between areas.
            Paint(2, 79, map.width - 3, 82, Surface.Road);
            Paint(83, 2, 85, 78, Surface.Road);
            Paint(43, 2, 45, 44, Surface.Road2);
            Paint(2, 26, 82, 27, Surface.Road2);
        }

        // ------------------------------------------------------------------ props

        /// <summary>Whether a prop with this footprint (cells around the centre) can stand here, keeping a free margin.</summary>
        private static bool Free(Ctx c, Vector3 center, float halfX, float halfZ, int margin, bool allowRoad, bool indoorOk = false)
        {
            var map = c.Map;
            int x0 = Mathf.FloorToInt(center.x - halfX) - margin, x1 = Mathf.FloorToInt(center.x + halfX) + margin;
            int z0 = Mathf.FloorToInt(center.z - halfZ) - margin, z1 = Mathf.FloorToInt(center.z + halfZ) + margin;
            if (x0 < 1 || z0 < 1 || x1 >= map.width - 1 || z1 >= map.height - 1) return false;
            if ((center - c.Spawn).magnitude < 5f) return false;
            for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
            {
                var t = map[x, z];
                if (t != Tile.Ground && t != Tile.Bush) return false;
                if (c.Taken[x, z]) return false;
                if (!indoorOk && c.Indoor[x, z]) return false;
                if (!allowRoad && (map.SurfaceAt(x, z) == Surface.Road)) return false;
            }
            return true;
        }

        private static void Take(Ctx c, Vector3 center, float halfX, float halfZ)
        {
            for (var x = Mathf.FloorToInt(center.x - halfX); x <= Mathf.FloorToInt(center.x + halfX); x++)
            for (var z = Mathf.FloorToInt(center.z - halfZ); z <= Mathf.FloorToInt(center.z + halfZ); z++)
                if (x >= 0 && z >= 0 && x < c.Map.width && z < c.Map.height) c.Taken[x, z] = true;
        }

        private static void Prop(Ctx c, string id, Vector3 at, float rotation, float scale = 1f) =>
            c.Map.props.Add(new MapDecoration { id = id, x = at.x, z = at.z, rotation = rotation, scale = scale });

        private static void PlaceOutdoorProps(Ctx c)
        {
            var map = c.Map;
            if (c.Flavor != null) RoadProps(c);
            else
            {
            // Wrecked cars parked along the concrete road, lamp posts every 12 m on its edge.
            for (var x = 8; x < map.width - 8; x += 5)
            {
                if (c.R() < 0.55f) continue;
                var at = new Vector3(x + 0.5f, 0f, c.R() < 0.5f ? 80.2f : 81.2f);
                if (!Free(c, at, 2f, 1f, 0, allowRoad: true)) continue;
                Prop(c, "car", at, 90f + c.R(-12f, 12f));
                Take(c, at, 2f, 1f);
                x += 4;
            }
            for (var x = 10; x < map.width - 6; x += 12)
            {
                var at = new Vector3(x + 0.5f, 0f, 83.5f);
                if (Free(c, at, 0.2f, 0.2f, 0, allowRoad: true)) { Prop(c, "lamp", at, 0f); Take(c, at, 0.2f, 0.2f); }
            }
            for (var z = 8; z < 78; z += 14)
            {
                var at = new Vector3(82.5f, 0f, z + 0.5f);
                if (Free(c, at, 0.2f, 0.2f, 0, allowRoad: true)) { Prop(c, "lamp", at, 0f); Take(c, at, 0.2f, 0.2f); }
            }
            // A barrier line where the road meets the Checkpoint.
            foreach (var at in new[] { new Vector3(84.5f, 0f, 75f), new Vector3(84.5f, 0f, 70f) })
                if (Free(c, at, 1.2f, 0.5f, 0, allowRoad: true)) { Prop(c, "barrier", at, 90f); Take(c, at, 0.5f, 1.2f); }
            }

            // Open ground: trees and rocks in the grass, bushes, clutter elsewhere. Picked on a jittered lattice.
            for (var gx = 4; gx < map.width - 4; gx += 6)
            for (var gz = 4; gz < map.height - 4; gz += 6)
            {
                var at = new Vector3(gx + c.R(-2f, 2f), 0f, gz + c.R(-2f, 2f));
                var cell = new Vector2Int(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.z));
                var surface = map.SurfaceAt(cell.x, cell.y);
                var roll = c.R();
                string id;
                float half;
                if (surface == Surface.Grass || surface == Surface.Grass2)
                {
                    var trees = c.Flavor == "reserve" ? 0.38f : 0.22f;
                    if (roll < trees) { id = "tree"; half = 0.6f; }
                    else if (roll < trees + 0.14f) { id = "bush"; half = 1f; }
                    else if (roll < trees + 0.26f) { id = "rock"; half = 0.8f; }
                    else continue;
                }
                else if (!Clutter(c.Flavor, roll, out id, out half)) continue;
                var bush = id.StartsWith("bush");
                if (!Free(c, at, half, half, bush ? 0 : 1, allowRoad: false)) continue;
                Prop(c, id, at, id == "tree" || id == "rock" || bush ? c.R(0f, 360f) : Mathf.Round(c.R(0f, 4f)) * 90f + c.R(-8f, 8f));
                if (!bush) Take(c, at, half, half);
            }
        }

        /// <summary>
        /// Clutter on open (non-grass) ground, by theme (2026-10-03): the checkpoint has barriers and sandbags, the labs
        /// tanks, generators and containers, the town crates and barrels, the reserve rocks.
        /// </summary>
        private static bool Clutter(string flavor, float roll, out string id, out float half)
        {
            (string id, float half, float weight)[] table;
            switch (flavor)
            {
                case "checkpoint":
                    table = new[] { ("barrier", 1.1f, 0.09f), ("sandbags", 1.1f, 0.09f), ("barrels", 0.6f, 0.05f), ("crate_stack", 1.1f, 0.05f), ("container", 0.7f, 0.04f), ("cones", 0.6f, 0.05f), ("tire_stack", 0.5f, 0.03f), ("generator", 0.9f, 0.03f) };
                    break;
                case "lab":
                    table = new[] { ("tank", 0.9f, 0.07f), ("generator", 0.9f, 0.06f), ("container", 0.7f, 0.07f), ("barrels", 0.6f, 0.07f), ("crate", 0.6f, 0.04f), ("cones", 0.6f, 0.04f), ("dumpster", 1f, 0.03f) };
                    break;
                case "reserve":
                    table = new[] { ("rock", 0.8f, 0.1f), ("tree", 0.6f, 0.08f), ("bush_small", 0.6f, 0.06f), ("log_pile", 1.1f, 0.04f), ("stump", 0.5f, 0.06f), ("crate", 0.6f, 0.02f) };
                    break;
                case "town":
                    table = new[] { ("crate_stack", 1.1f, 0.05f), ("barrels", 0.6f, 0.05f), ("crate", 0.6f, 0.04f), ("dumpster", 1f, 0.05f), ("tire_stack", 0.5f, 0.03f), ("cones", 0.6f, 0.03f), ("bush_small", 0.6f, 0.05f) };
                    break;
                default:
                    table = new[] { ("barrels", 0.6f, 0.12f), ("crate_stack", 1.1f, 0.08f), ("rock", 0.8f, 0.05f), ("generator", 0.9f, 0.04f), ("sandbags", 1.1f, 0.04f), ("tank", 0.9f, 0.03f), ("bush_small", 0.6f, 0.04f) };
                    break;
            }
            var acc = 0f;
            foreach (var entry in table)
            {
                acc += entry.weight;
                if (roll >= acc) continue;
                id = entry.id;
                half = entry.half;
                return true;
            }
            id = null;
            half = 0f;
            return false;
        }

        /// <summary>Generated stages: wrecked cars on the roads (along them), lamp posts on road edges.</summary>
        private static void RoadProps(Ctx c)
        {
            var map = c.Map;
            bool Road(int x, int z) => x >= 0 && z >= 0 && x < map.width && z < map.height && map.SurfaceAt(x, z) == Surface.Road;
            for (var x = 4; x < map.width - 4; x++)
            for (var z = 4; z < map.height - 4; z++)
            {
                if (!Road(x, z)) continue;
                // Cars: rarely, on the road's middle row, turned along it.
                if (c.R() < 0.012f)
                {
                    var eastWest = Road(x - 2, z) && Road(x + 2, z) && Road(x, z - 1) && Road(x, z + 1);
                    var northSouth = Road(x, z - 2) && Road(x, z + 2) && Road(x - 1, z) && Road(x + 1, z);
                    if (eastWest || northSouth)
                    {
                        var at = new Vector3(x + 0.5f, 0f, z + 0.5f);
                        float hx = eastWest ? 2f : 1f, hz = eastWest ? 1f : 2f;
                        if (Free(c, at, hx, hz, 0, allowRoad: true))
                        {
                            Prop(c, "car", at, (eastWest ? 90f : 0f) + c.R(-14f, 14f) + (c.R() < 0.5f ? 180f : 0f));
                            Take(c, at, hx, hz);
                        }
                    }
                }
            }
            // Lamps: on ground cells beside a road, kept 11 m apart.
            var lamps = new List<Vector2Int>();
            for (var x = 4; x < map.width - 4; x++)
            for (var z = 4; z < map.height - 4; z++)
            {
                if (Road(x, z) || map[x, z] != Tile.Ground || c.Indoor[x, z]) continue;
                if (!(Road(x + 1, z) || Road(x - 1, z) || Road(x, z + 1) || Road(x, z - 1))) continue;
                var near = false;
                foreach (var l in lamps)
                    if ((l - new Vector2Int(x, z)).sqrMagnitude < 121) { near = true; break; }
                if (near) continue;
                var at = new Vector3(x + 0.5f, 0f, z + 0.5f);
                if (!Free(c, at, 0.2f, 0.2f, 0, allowRoad: true)) continue;
                // The lamp's arm points south (-z) unturned; turn it over the road.
                Prop(c, "lamp", at, Road(x, z - 1) ? 0f : Road(x, z + 1) ? 180f : Road(x + 1, z) ? -90f : 90f);
                Take(c, at, 0.2f, 0.2f);
                lamps.Add(new Vector2Int(x, z));
            }
        }

        /// <summary>Lab furniture against the inside of north and east walls, never next to a doorway or a marker.</summary>
        private static void PlaceIndoorProps(Ctx c)
        {
            var map = c.Map;
            foreach (var b in map.buildings)
            {
                // Along the north wall (inside), every few metres.
                var z = b.z + b.d - 1;
                for (var x = b.x + 2; x <= b.x + b.w - 3; x += 4)
                {
                    // 2026-10-03: furniture that fits the building: homes get beds, shelves and tables; labs lockers, desks, racks.
                    var kind = Art.ThemeArt.KindOf(b.name);
                    string[] pool = kind == Art.ThemeArt.WallKind.House ? new[] { "bed", "shelf", "table", "shelf" }
                        : kind == Art.ThemeArt.WallKind.Cabin ? new[] { "table", "shelf", "bed" }
                        : kind == Art.ThemeArt.WallKind.Military ? new[] { "bed", "lockers", "bed" }
                        : new[] { "lockers", "desk", "server_rack", "desk" };
                    var id = pool[c.Rng.Next(pool.Length)];
                    var rotation = id == "bed" ? 90f : 0f;
                    var at = new Vector3(x + 1f, 0f, z + (id == "bed" ? 0.5f : id == "lockers" || id == "shelf" ? 0.7f : 0.55f));
                    if (!IndoorFree(c, x, z, 2)) continue;
                    Prop(c, id, at, rotation);
                    for (var dx = 0; dx < 2; dx++) c.Taken[x + dx, z] = true;
                }
                if (b.w * b.d > 60 && c.R() < 0.7f)
                {
                    var at = new Vector3(b.x + b.w * 0.5f, 0f, b.z + b.d * 0.5f);
                    var cx = Mathf.FloorToInt(at.x);
                    var cz = Mathf.FloorToInt(at.z);
                    var centre = Art.ThemeArt.KindOf(b.name) == Art.ThemeArt.WallKind.Facility ? "container" : "table";
                    if (IndoorFree(c, cx - 1, cz, 2) && IndoorFree(c, cx - 1, cz - 1, 2)) Prop(c, centre, at, 0f);
                }
            }
        }

        private static bool IndoorFree(Ctx c, int x, int z, int width)
        {
            var map = c.Map;
            for (var dx = -1; dx <= width; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                int cx = x + dx, cz = z + dz;
                var t = map[cx, cz];
                if (dz == 1 || (dx == -1 || dx == width) && dz != 0) continue;
                if (t != Tile.Ground || c.Taken[cx, cz]) return false;
            }
            // Not next to a doorway: the wall row behind must be solid along the whole width plus one.
            for (var dx = -1; dx <= width; dx++)
                if (!Tiles.IsSolid(map[x + dx, z + 1])) return false;
            return true;
        }

        // ------------------------------------------------------------------ decorations

        private static void Deco(Ctx c, string id, float x, float z, float scale = 1f) =>
            c.Map.decorations.Add(new MapDecoration { id = id, x = x, z = z, rotation = c.R(0f, 360f), scale = scale });

        private static void PlaceDecorations(Ctx c)
        {
            var map = c.Map;
            for (var x = 1; x < map.width - 1; x++)
            for (var z = 1; z < map.height - 1; z++)
            {
                var t = map[x, z];
                if (Tiles.IsSolid(t) || t == Tile.Bush) continue;
                var s = map.SurfaceAt(x, z);
                var roll = c.R();
                float px = x + c.R(0.15f, 0.85f), pz = z + c.R(0.15f, 0.85f);
                if (c.Indoor[x, z])
                {
                    if (roll < 0.035f) Deco(c, "papers", px, pz);
                    else if (roll < 0.05f) Deco(c, "bones_free_debris", px, pz);
                    else if (roll < 0.06f) Deco(c, "grate", Mathf.Floor(px) + 0.5f, Mathf.Floor(pz) + 0.5f);
                    else if (roll < 0.07f) Deco(c, "crack", px, pz);
                    continue;
                }
                switch (s)
                {
                    case Surface.Road:
                        if (roll < 0.05f) Deco(c, "crack", px, pz, c.R(1f, 1.6f));
                        else if (roll < 0.065f) Deco(c, "oil_stain", px, pz, c.R(0.45f, 0.8f));
                        else if (roll < 0.082f) Deco(c, "manhole", Mathf.Floor(px) + 0.5f, Mathf.Floor(pz) + 0.5f);
                        break;
                    case Surface.Road2:
                        if (roll < 0.04f) Deco(c, "rubble", px, pz);
                        else if (roll < 0.06f) Deco(c, "puddle", px, pz, c.R(0.7f, 1.2f));
                        break;
                    case Surface.Grass:
                    case Surface.Grass2:
                        if (roll < 0.09f) Deco(c, "grass_tuft", px, pz, c.R(0.8f, 1.3f));
                        else if (roll < 0.12f) Deco(c, "leaves", px, pz);
                        else if (roll < 0.135f) Deco(c, "mushrooms", px, pz);
                        break;
                    default:
                        if (roll < 0.03f) Deco(c, "crack", px, pz, c.R(0.8f, 1.5f));
                        else if (roll < 0.045f) Deco(c, "rubble", px, pz);
                        else if (roll < 0.05f) Deco(c, "toxic_puddle", px, pz, c.R(0.6f, 1f));
                        else if (roll < 0.065f) Deco(c, "papers", px, pz);
                        else if (roll < 0.0715f) Deco(c, "tire", px, pz);
                        else if (roll < 0.078f) Deco(c, "leaves", px, pz);
                        break;
                }
            }
            // Hazard stripes in front of every building door, arrows on the road.
            foreach (var b in map.buildings)
                for (var x = b.x; x <= b.x + b.w; x++)
                {
                    if (map[x, b.z] == Tile.Ground && map[x, b.z - 1] == Tile.Ground && Tiles.IsSolid(map[x - 1, b.z]))
                        map.decorations.Add(new MapDecoration { id = "hazard_stripes", x = x + 0.9f, z = b.z - 0.5f, rotation = 0f, scale = 0.9f });
                }
            if (c.Flavor == null)
            for (var x = 14; x < map.width - 10; x += 22)
                map.decorations.Add(new MapDecoration { id = "arrow", x = x, z = 80.5f, rotation = 90f, scale = 1.2f });
        }
    }
}
