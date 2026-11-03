using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// ⚠ Generates Claude's placeholder story stages (owner, 2026-10-03: a map per mission type, with layouts varied
    /// enough that replays don't feel like the same map). A stage is laid out as a 3 × 3 grid of districts split by
    /// roads, with the start in one corner and the objective in the opposite one. Each theme (checkpoint, town, lab,
    /// reserve) fills the districts its own way (houses, yards, parks, labs, forest, enclosures…), and the seed jitters
    /// every line and choice, so each recipe gives a different map. The result is an ordinary stage file: the owner edits
    /// it freely in the Map Editor afterwards, and nothing regenerates it unless asked.
    /// </summary>
    public static partial class StageForge
    {
        public class Recipe
        {
            public string Id;
            public string Name;
            /// <summary>"checkpoint", "town", "lab" or "reserve".</summary>
            public string Theme;
            /// <summary>"boss", "rescue", "bomb", "vaccine" or "endless" (no objective: Endless mode's map).</summary>
            public string Mission;
            public int Seed;
            public int Width = 112;
            public int Height = 112;
            /// <summary>Where the start is: 0 south-west, 1 south-east, 2 north-east, 3 north-west.</summary>
            public int Corner;
            public string Weather = "clear";
            public bool Dark;
            public float LootScale = 1f;
            public float Difficulty = 1f;
            public string BaseGround = ZombieGame.BaseGround.DefaultId;
            /// <summary>Vaccine stages: rival AI teams (owner, 2026-10-07: the final stage has two), one per corner beside the start.</summary>
            public int RivalTeams = 1;
        }

        private class Block
        {
            public RectInt R;
            public int Gx, Gz;
            public bool Merged;
            public string Kind;
            public string Name;
        }

        private class Ctx
        {
            public Recipe Recipe;
            public StageMap Map;
            public System.Random Rng;
            public Block[,] Blocks;
            public int[] RoadX, RoadZ;
            public int Nx, Nz;
            public List<Block> All;
            public Block Start, Objective;
            public int Houses;
            /// <summary>Cells under set-piece props (nothing else goes there).</summary>
            public bool[,] Occupied;
            /// <summary>Buildings waiting for their interior (furniture first, then loot and chest spots), 2026-10-04.</summary>
            public List<(MapBuilding building, bool incubator)> Later = new List<(MapBuilding building, bool incubator)>();

            public float R() => (float)Rng.NextDouble();
            public int R(int a, int b) => Rng.Next(a, b);
            public bool Chance(float p) => Rng.NextDouble() < p;
        }

        private const int RoadHalf = 1;

        public static StageMap Build(Recipe recipe)
        {
            var map = StageMap.Blank(recipe.Name, recipe.Width, recipe.Height);
            map.weather = recipe.Weather;
            map.dark = recipe.Dark;
            map.mission = recipe.Mission;
            map.lootScale = recipe.LootScale;
            map.difficulty = recipe.Difficulty;
            map.baseGround = recipe.BaseGround;
            map.theme = recipe.Theme;
            var c = new Ctx { Recipe = recipe, Map = map, Rng = new System.Random(recipe.Seed), Occupied = new bool[recipe.Width, recipe.Height] };

            Border(c);
            Grid(c);
            Roads(c);
            ChooseDistricts(c);
            foreach (var b in c.All) Fill(c, b);
            // 2026-10-04: each building is furnished for its type, then gets its loot and chest spots in the room left.
            foreach (var (building, incubator) in c.Later)
            {
                DesignInterior(c, building);
                Furnish(c, building, incubator);
            }
            // 2026-10-04: scenes with the owner's obstacle pack.
            foreach (var b in c.All) SetPieces(c, b);
            // Wrecked cars, barrier rows along the roads, checkpoint roadblocks (2026-10-04).
            Streets(c);
            foreach (var b in c.All) Patches(c, b);
            ClearDoorways(c.Map);
            // Owner, 2026-10-04: one fixed ground rule, grass round the outside and dirt inside (drawn by the ground
            // itself), so no grass patches are painted inside the map. Roads, dirt paths and floors stay.
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
            {
                var s = map.SurfaceAt(x, z);
                if (s == Surface.Grass || s == Surface.Grass2) map.SetSurface(x, z, Surface.None);
                // Loose dirt patches away from the roads were for variety on the old ground; dirt roads stay.
                // Owner, 2026-10-05 (connected asphalt roads): outside the reserve no dirt-path paint at all.
                if (s == Surface.Road2 && (c.Recipe.Theme != "reserve" || !NearRoadLine(c, x, z, 4))) map.SetSurface(x, z, Surface.None);
            }
            Route(c);
            Spawns(c);
            Loot(c);
            Areas(c);
            // Only the owner's obstacle pack, bushes and the building furniture stay as props (Claude's earlier decorative
            // props stay out, owner 2026-10-04); then the decorative details.
            map.props.RemoveAll(p => { var piece = Art.ArtLibrary.Get(p.id); return piece == null || (piece.Model == null && !piece.Bush && System.Array.IndexOf(Art.ArtLibrary.Interiors, p.id) < 0 && System.Array.IndexOf(Art.ArtLibrary.StreetFurniture, p.id) < 0 && p.id != "sandbags"); });
            // Interior decorations (rugs, the garage lifts) stay; the rest is redone by Details.
            map.decorations.RemoveAll(d => d.id != "rug" && d.id != "car_lift");
            Details(c);
            NoGrassOnRoadsOrFloors(map);
            // A prop must never cut a marker off: remove props around anything the start can't reach, and check again.
            for (var pass = 0; pass < 6; pass++)
            {
                var bad = Unreachable(map);
                if (bad.Count == 0) break;
                map.props.RemoveAll(p =>
                {
                    foreach (var cell in bad)
                        if ((new Vector2(p.x, p.z) - new Vector2(cell.x + 0.5f, cell.y + 0.5f)).magnitude < 4f + pass * 3f) return true;
                    return false;
                });
                if (pass == 5) foreach (var cell in Unreachable(map)) map[cell.x, cell.y] = Tile.Ground;
            }
            return map;
        }

        /// <summary>
        /// Owner, 2026-10-05: no hiding grass on roads (asphalt or dirt) or building floors. Grass cells there become
        /// ground, and grass props standing on such a cell go.
        /// </summary>
        public static void NoGrassOnRoadsOrFloors(StageMap map)
        {
            bool Paved(int x, int z)
            {
                if (x < 0 || z < 0 || x >= map.width || z >= map.height) return false;
                var s = map.SurfaceAt(x, z);
                return s == Surface.Road || s == Surface.Road2 || s == Surface.Floor;
            }
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == Tile.Bush && Paved(x, z)) map[x, z] = Tile.Ground;
            map.props.RemoveAll(p => (Art.ArtLibrary.Get(p.id)?.Bush ?? false) && Paved(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)));
        }

        // ------------------------------------------------------------------ frame: border, grid, roads

        /// <summary>D-075: natural terrain closes the edge, a little ragged so it doesn't look ruled.</summary>
        private static void Border(Ctx c)
        {
            var map = c.Map;
            var seed = c.Recipe.Seed * 0.37f;
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
            {
                var edge = Mathf.Min(Mathf.Min(x, map.width - 1 - x), Mathf.Min(z, map.height - 1 - z));
                var along = x + z;
                // A straight two-cell edge: the steel mesh fence stands on its inner side (owner, 2026-10-04).
                const int thickness = 2;
                if (edge < thickness) map[x, z] = Tile.Cliff;
            }
        }

        /// <summary>
        /// Three or four districts each way, split by roads at jittered positions; then one or two pairs of neighbouring
        /// districts merge into a large one. The start is one corner district, the objective the opposite one.
        /// </summary>
        private static void Grid(Ctx c)
        {
            var map = c.Map;
            // About one district per 30 m (2026-10-04: most maps grew 1.5×, the Vaccine map 2×).
            c.Nx = Mathf.Clamp(Mathf.RoundToInt(map.width / 30f) + (c.Chance(0.35f) ? 1 : 0), 3, 8);
            c.Nz = Mathf.Clamp(Mathf.RoundToInt(map.height / 30f) + (c.Chance(0.35f) ? 1 : 0), 3, 8);
            int[] Lines(int size, int n)
            {
                var lines = new int[n - 1];
                for (var k = 1; k < n; k++) lines[k - 1] = Mathf.RoundToInt(size * k / (float)n) + c.R(-4, 5);
                return lines;
            }
            c.RoadX = Lines(map.width, c.Nx);
            c.RoadZ = Lines(map.height, c.Nz);
            int Lo(int[] roads, int i) => i == 0 ? 5 : roads[i - 1] + RoadHalf + 1;
            int Hi(int[] roads, int i, int size) => i == roads.Length ? size - 6 : roads[i] - RoadHalf - 1;
            c.Blocks = new Block[c.Nx, c.Nz];
            for (var gx = 0; gx < c.Nx; gx++)
            for (var gz = 0; gz < c.Nz; gz++)
            {
                int x0 = Lo(c.RoadX, gx), x1 = Hi(c.RoadX, gx, map.width), z0 = Lo(c.RoadZ, gz), z1 = Hi(c.RoadZ, gz, map.height);
                c.Blocks[gx, gz] = new Block { R = new RectInt(x0, z0, x1 - x0, z1 - z0), Gx = gx, Gz = gz };
            }
            var corner = ((c.Recipe.Corner % 4) + 4) % 4;
            int sx = corner == 1 || corner == 2 ? c.Nx - 1 : 0, sz = corner >= 2 ? c.Nz - 1 : 0;
            c.Start = c.Blocks[sx, sz];
            c.Objective = c.Blocks[c.Nx - 1 - sx, c.Nz - 1 - sz];
            // Merge one or two neighbouring pairs into large districts (never the start or the objective).
            var merges = c.R(1, 3) + c.Nx * c.Nz / 12;
            for (var attempt = 0; attempt < 30 && merges > 0; attempt++)
            {
                var gx = c.R(0, c.Nx);
                var gz = c.R(0, c.Nz);
                var horizontal = c.Chance(0.5f);
                int ox = gx + (horizontal ? 1 : 0), oz = gz + (horizontal ? 0 : 1);
                if (ox >= c.Nx || oz >= c.Nz) continue;
                var a = c.Blocks[gx, gz];
                var b = c.Blocks[ox, oz];
                if (a == b || a.Merged || b.Merged || a == c.Start || b == c.Start || a == c.Objective || b == c.Objective) continue;
                var r = new RectInt(Mathf.Min(a.R.x, b.R.x), Mathf.Min(a.R.y, b.R.y), 0, 0);
                r.width = Mathf.Max(a.R.xMax, b.R.xMax) - r.x;
                r.height = Mathf.Max(a.R.yMax, b.R.yMax) - r.y;
                var merged = new Block { R = r, Gx = gx, Gz = gz, Merged = true };
                c.Blocks[gx, gz] = merged;
                c.Blocks[ox, oz] = merged;
                merges--;
            }
            c.All = new List<Block>();
            foreach (var b in c.Blocks)
                if (!c.All.Contains(b)) c.All.Add(b);
        }

        /// <summary>
        /// Roads are surfaces only (looks; layer 2), so they never block a route. Towns, labs and the checkpoint get one
        /// connected asphalt network; the reserve wobbling dirt paths. No
        /// road runs through a merged district.
        /// </summary>
        private static void Roads(Ctx c)
        {
            var map = c.Map;
            var reserve = c.Recipe.Theme == "reserve";
            bool InMerged(int x, int z)
            {
                foreach (var b in c.All)
                    if (b.Merged && b.R.Contains(new Vector2Int(x, z))) return true;
                return false;
            }
            void Segment(bool vertical, int line, int from, int to, Surface s, bool wobble)
            {
                var mid = (from + to) / 2;
                if (vertical ? InMerged(line, mid) : InMerged(mid, line)) return;
                for (var t = from; t <= to; t++)
                {
                    var offset = wobble ? Mathf.RoundToInt((Art.MeshKit.Noise2(t * 0.08f, line * 0.3f + c.Recipe.Seed) - 0.5f) * 5f) : 0;
                    for (var w = -RoadHalf; w <= RoadHalf; w++)
                    {
                        var x = vertical ? line + w + offset : t;
                        var z = vertical ? t : line + w + offset;
                        if (x < 2 || z < 2 || x >= map.width - 2 || z >= map.height - 2 || map[x, z] == Tile.Cliff) continue;
                        map.SetSurface(x, z, s);
                    }
                }
            }
            var cutsX = new List<int> { 3 };
            cutsX.AddRange(c.RoadX);
            cutsX.Add(map.width - 4);
            var cutsZ = new List<int> { 3 };
            cutsZ.AddRange(c.RoadZ);
            cutsZ.Add(map.height - 4);
            // Owner, 2026-10-05: road routes must connect. Every segment is laid (none dropped), all asphalt in towns, labs
            // and the checkpoint (no dirt stretches breaking an asphalt road), dirt paths in the reserve.
            var surface = reserve ? Surface.Road2 : Surface.Road;
            foreach (var x in c.RoadX)
                for (var s = 0; s + 1 < cutsZ.Count; s++)
                    Segment(true, x, cutsZ[s], cutsZ[s + 1], surface, reserve);
            foreach (var z in c.RoadZ)
                for (var s = 0; s + 1 < cutsX.Count; s++)
                    Segment(false, z, cutsX[s], cutsX[s + 1], surface, reserve);
        }

        // ------------------------------------------------------------------ districts

        private static readonly Dictionary<string, (string kind, float weight)[]> ThemeKinds = new Dictionary<string, (string, float)[]>
        {
            ["checkpoint"] = new[] { ("yard", 0.3f), ("barracks", 0.25f), ("compound", 0.2f), ("houses", 0.15f), ("park", 0.1f) },
            ["town"] = new[] { ("houses", 0.4f), ("park", 0.2f), ("shops", 0.18f), ("yard", 0.12f), ("plaza", 0.1f) },
            ["lab"] = new[] { ("labs", 0.42f), ("yard", 0.25f), ("park", 0.13f), ("compound", 0.2f) },
            ["reserve"] = new[] { ("forest", 0.45f), ("enclosure", 0.25f), ("cabins", 0.15f), ("meadow", 0.15f) },
        };

        private static readonly Dictionary<string, string> KindNames = new Dictionary<string, string>
        {
            ["yard"] = "Depot", ["barracks"] = "Barracks", ["compound"] = "Compound", ["houses"] = "Houses", ["park"] = "Park",
            ["shops"] = "Shops", ["plaza"] = "Plaza", ["labs"] = "Labs", ["forest"] = "Woods", ["enclosure"] = "Enclosure",
            ["cabins"] = "Cabins", ["meadow"] = "Meadow", ["arena"] = "Boss grounds", ["refuge"] = "Refuge", ["nest"] = "Hive",
            ["vault"] = "Vault",
        };

        private static void ChooseDistricts(Ctx c)
        {
            var kinds = ThemeKinds.TryGetValue(c.Recipe.Theme, out var k) ? k : ThemeKinds["town"];
            var used = new Dictionary<string, int>();
            foreach (var b in c.All)
            {
                if (b == c.Start) b.Kind = "start";
                else if (b == c.Objective && c.Recipe.Mission != "endless")
                    b.Kind = c.Recipe.Mission == "boss" ? "arena" : c.Recipe.Mission == "rescue" ? "refuge" : c.Recipe.Mission == "bomb" ? "nest" : "vault";
                else
                {
                    // Weighted, but no kind more than three times.
                    for (var attempt = 0; attempt < 10; attempt++)
                    {
                        var roll = c.R() * TotalWeight(kinds);
                        foreach (var (kind, weight) in kinds)
                        {
                            roll -= weight;
                            if (roll > 0f) continue;
                            b.Kind = kind;
                            break;
                        }
                        used.TryGetValue(b.Kind, out var n);
                        if (n < Mathf.Max(3, c.Nx * c.Nz / 4)) break;
                    }
                    used.TryGetValue(b.Kind, out var count);
                    used[b.Kind] = count + 1;
                }
                var compass = (b.Gz == c.Nz - 1 ? "North " : b.Gz == 0 ? "South " : "") + (b.Gx == c.Nx - 1 ? "East" : b.Gx == 0 ? "West" : b.Gz > 0 && b.Gz < c.Nz - 1 ? "Central" : "");
                if (b.Merged) compass = "Great " + compass;
                b.Name = b.Kind == "start" ? "Start" : $"{compass.Trim()} {(KindNames.TryGetValue(b.Kind, out var nm) ? nm : b.Kind)}".Trim();
            }
        }

        private static float TotalWeight((string, float)[] kinds)
        {
            var t = 0f;
            foreach (var (_, w) in kinds) t += w;
            return t;
        }

        private static void Fill(Ctx c, Block b)
        {
            switch (b.Kind)
            {
                case "start": StartArea(c, b); break;
                case "houses": Houses(c, b, 2, 2, 10, 13, "House"); break;
                case "barracks": Houses(c, b, 2, 1, 14, 17, "Military"); Pads(c, b, 1); break;
                case "cabins": Houses(c, b, 2, 2, 10, 12, "Cabin"); Greenery(c, b, 0.55f, 4); break;
                case "shops": Houses(c, b, 1, 2, 14, 18, "Shop"); break;
                case "labs": Labs(c, b); Pads(c, b, 1); break;
                case "yard": Pads(c, b, 2); Yard(c, b); break;
                case "compound": Compound(c, b); Pads(c, b, 1); break;
                case "park": Greenery(c, b, 0.62f, 6); Bushes(c, b, 4); break;
                case "plaza": Plaza(c, b); break;
                case "meadow": Greenery(c, b, 0.4f, 3); Bushes(c, b, 3); break;
                case "forest": Greenery(c, b, 0.25f, 2); Bushes(c, b, 7); break;
                case "enclosure": Enclosure(c, b); break;
                case "arena": Arena(c, b); break;
                case "refuge": Refuge(c, b); break;
                case "nest": Nest(c, b); Pads(c, b, 1); break;
                case "vault": Vault(c, b); break;
            }
        }

        // ---- building blocks of districts ----

        private static bool NearRoadLine(Ctx c, int x, int z, int within)
        {
            foreach (var rx in c.RoadX) if (Mathf.Abs(x - rx) <= within) return true;
            foreach (var rz in c.RoadZ) if (Mathf.Abs(z - rz) <= within) return true;
            return false;
        }

        private static bool Inside(Ctx c, int x, int z) => x > 1 && z > 1 && x < c.Map.width - 2 && z < c.Map.height - 2;

        private static bool Open(Ctx c, int x, int z) => Inside(c, x, z) && c.Map[x, z] == Tile.Ground && !c.Occupied[x, z];

        /// <summary>
        /// A building: walls on its outline, a 2-wide doorway in the south wall (facing the camera) and one more in
        /// another wall, a floor inside, and optionally a dividing wall with a gap. Returns the building record.
        /// </summary>
        private static MapBuilding Building(Ctx c, int x, int z, int w, int d, string name, bool divide = false, float height = 3.9f)
        {
            var map = c.Map;
            for (var i = x; i <= x + w; i++)
            for (var j = z; j <= z + d; j++)
            {
                if (!Inside(c, i, j)) continue;
                map.SetSurface(i, j, Surface.Floor);
                var edge = i == x || i == x + w || j == z || j == z + d;
                map[i, j] = edge ? Tile.Wall : Tile.Ground;
            }
            void Door(bool vertical, int line, int at)
            {
                for (var k = 0; k < 2; k++)
                {
                    var i = vertical ? line : at + k;
                    var j = vertical ? at + k : line;
                    if (Inside(c, i, j)) map[i, j] = Tile.Ground;
                }
            }
            Door(false, z, x + 1 + c.R(1, Mathf.Max(2, w - 3)));
            switch (c.R(0, 3))
            {
                case 0: Door(false, z + d, x + 1 + c.R(1, Mathf.Max(2, w - 3))); break;
                case 1: Door(true, x, z + 1 + c.R(1, Mathf.Max(2, d - 3))); break;
                default: Door(true, x + w, z + 1 + c.R(1, Mathf.Max(2, d - 3))); break;
            }
            if (divide && w >= 10)
            {
                var wall = x + w / 2 + c.R(-1, 2);
                var gap = z + 1 + c.R(1, Mathf.Max(2, d - 3));
                for (var j = z + 1; j < z + d; j++)
                    if (j != gap && j != gap + 1) map[wall, j] = Tile.Wall;
            }
            var b = new MapBuilding { name = name, x = x, z = z, w = w, d = d, height = height };
            map.buildings.Add(b);
            return b;
        }

        /// <summary>A free interior cell of a building (not next to a wall), for markers.</summary>
        /// <summary>
        /// Every doorway keeps two clear cells straight inside it (owner, 2026-10-04: some loot couldn't be reached).
        /// An inner wall right behind a 2-cell doorway left a 1-cell squeeze that zombies' pathing (and sometimes players)
        /// couldn't get through; such wall cells are removed. A building without any doorway gets one. Markers are never
        /// touched.
        /// </summary>
        public static void ClearDoorways(StageMap map)
        {
            foreach (var b in map.buildings)
            {
                int x0 = b.x, z0 = b.z, x1 = b.x + b.w, z1 = b.z + b.d;
                // A building with no doorway at all gets one in the middle of its south wall (towards the camera).
                var hasDoor = false;
                for (var x = x0 + 1; x < x1 && !hasDoor; x++) hasDoor = map[x, z0] != Tile.Wall || map[x, z1] != Tile.Wall;
                for (var z = z0 + 1; z < z1 && !hasDoor; z++) hasDoor = map[x0, z] != Tile.Wall || map[x1, z] != Tile.Wall;
                if (!hasDoor && x1 - x0 >= 4)
                {
                    var mid = (x0 + x1) / 2;
                    map[mid, z0] = Tile.Ground;
                    map[mid + 1, z0] = Tile.Ground;
                }
                void Door(int x, int z, int dx, int dz)
                {
                    if (map[x, z] == Tile.Wall) return;
                    for (var step = 1; step <= 2; step++)
                    {
                        var cx = x + dx * step;
                        var cz = z + dz * step;
                        if (cx <= x0 || cx >= x1 || cz <= z0 || cz >= z1) break;
                        if (map[cx, cz] == Tile.Wall) map[cx, cz] = Tile.Ground;
                    }
                }
                for (var x = x0 + 1; x < x1; x++)
                {
                    Door(x, z0, 0, 1);
                    Door(x, z1, 0, -1);
                }
                for (var z = z0 + 1; z < z1; z++)
                {
                    Door(x0, z, 1, 0);
                    Door(x1, z, -1, 0);
                }
            }
        }

        private static Vector2Int? InteriorCell(Ctx c, MapBuilding b)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var x = c.R(b.x + 2, Mathf.Max(b.x + 3, b.x + b.w - 1));
                var z = c.R(b.z + 2, Mathf.Max(b.z + 3, b.z + b.d - 1));
                if (!Open(c, x, z)) continue;
                var clear = true;
                for (var dx = -1; dx <= 1 && clear; dx++)
                for (var dz = -1; dz <= 1 && clear; dz++)
                    if (c.Map[x + dx, z + dz] == Tile.Wall) clear = false;
                if (clear) return new Vector2Int(x, z);
            }
            return null;
        }

        /// <summary>Loot, maybe a chest spot, maybe an incubator, inside a building.</summary>
        private static void Furnish(Ctx c, MapBuilding b, bool incubator)
        {
            var loot = 1 + (b.w * b.d > 90 ? 1 : 0) + c.R(0, 2);
            for (var i = 0; i < loot; i++)
            {
                var p = InteriorCell(c, b);
                if (p.HasValue) c.Map[p.Value.x, p.Value.y] = Tile.LootSpawn;
            }
            // 2026-10-04: most buildings may have a chest (owner: too few chests).
            if (c.Chance(0.85f))
            {
                var p = InteriorCell(c, b);
                if (p.HasValue) c.Map[p.Value.x, p.Value.y] = Tile.ChestSpawn;
            }
            if (incubator)
            {
                var p = InteriorCell(c, b);
                if (p.HasValue) c.Map[p.Value.x, p.Value.y] = Tile.Incubator;
            }
        }

        /// <summary>Smallest outline (walls included) each building type needs for its furniture.</summary>
        private static (int w, int d) MinSize(string type)
        {
            switch (type)
            {
                case "Garage": return (12, 10);
                case "Shop":
                case "Clinic":
                case "Security": return (14, 12);
                case "Military": return (13, 10);
                default: return (10, 9);
            }
        }

        private static readonly string[] ShopRow = { "Shop", "Clinic", "Security" };

        /// <summary>Widens the south doorway to <paramref name="cells"/> cells (a garage door a car fits through).</summary>
        private static void WideDoor(Ctx c, MapBuilding b, int cells)
        {
            var start = -1;
            for (var x = b.x + 1; x < b.x + b.w; x++)
                if (c.Map[x, b.z] != Tile.Wall) { start = x; break; }
            if (start < 0) start = b.x + 2;
            start = Mathf.Clamp(start - (cells - 2) / 2, b.x + 1, b.x + b.w - cells);
            for (var x = start; x < start + cells; x++) c.Map[x, b.z] = Tile.Ground;
        }

        private static void Houses(Ctx c, Block b, int cols, int rows, int min, int max, string name)
        {
            if (b.Merged)
            {
                cols = Mathf.Max(cols, b.R.width / 19);
                rows = Mathf.Max(rows, b.R.height / 18);
            }
            // 2026-10-04: buildings are 30 % bigger; only as many lots as fit at that size (at least 15 × 14 m each).
            cols = Mathf.Max(1, Mathf.Min(cols, b.R.width / 15));
            rows = Mathf.Max(1, Mathf.Min(rows, b.R.height / 14));
            var cellW = b.R.width / cols;
            var cellD = b.R.height / rows;
            // 2026-10-04: in a house district one row now and then is a single medium building (a shop, clinic or
            // security office), so every building type turns up in town.
            var mediumRow = -1;
            if (name == "House" && b.R.width >= 20 && b.R.height >= 18 && c.Chance(0.35f))
            {
                // The whole block is one medium building with a yard.
                var bw = Mathf.Clamp(c.R(14, 19), 14, b.R.width - 6);
                var bd = Mathf.Clamp(c.R(12, 15), 12, b.R.height - 6);
                var btype = ShopRow[c.R(0, ShopRow.Length)];
                c.Houses++;
                var block = Building(c, b.R.x + (b.R.width - bw) / 2, b.R.y + (b.R.height - bd) / 2, bw, bd, $"{btype} {c.Houses}", divide: btype != "Shop");
                c.Later.Add((block, false));
                Greenery(c, b, 0.72f, 3);
                Bushes(c, b, 2);
                return;
            }
            if (name == "House" && rows > 1 && b.R.width >= 20 && cellD >= 16 && c.Chance(0.6f))
            {
                mediumRow = c.R(0, rows);
                var mw = Mathf.Clamp(c.R(14, 19), 14, b.R.width - 6);
                var md = Mathf.Clamp(c.R(12, 15), 12, cellD - 4);
                var mx = b.R.x + (b.R.width - mw) / 2 + c.R(-1, 2);
                var mz = b.R.y + mediumRow * cellD + c.R(2, Mathf.Max(3, cellD - md - 1));
                var type = ShopRow[c.R(0, ShopRow.Length)];
                c.Houses++;
                var medium = Building(c, mx, mz, mw, md, $"{type} {c.Houses}", divide: type != "Shop" && mw >= 11);
                c.Later.Add((medium, false));
            }
            for (var i = 0; i < cols; i++)
            for (var j = 0; j < rows; j++)
            {
                if (j == mediumRow || c.Chance(0.15f)) continue; // the medium building's row, or an empty lot now and then
                // 2026-10-04: building types. Houses are small (some are car repair garages); the shops district mixes
                // shops, clinics and security offices (medium); barracks are military buildings (medium).
                var type = name;
                if (name == "House")
                {
                    var roll = c.R();
                    type = roll < 0.3f ? "Garage" : roll < 0.4f ? "Shop" : roll < 0.48f ? "Clinic" : roll < 0.56f ? "Security" : "House";
                }
                if (name == "Shop") type = ShopRow[c.Houses % ShopRow.Length];
                var medium = type == "Shop" || type == "Clinic" || type == "Security";
                var lo = type == "Garage" ? 12 : medium ? Mathf.Max(min, 14) : min;
                var hi = type == "Garage" ? 12 : medium ? Mathf.Max(max, 17) : max;
                var w = Mathf.Clamp(c.R(lo, hi + 1), 5, cellW - 4);
                var d = Mathf.Clamp(type == "Garage" ? 10 : type == "House" || type == "Cabin" ? c.R(9, 13) : c.R(12, 15), 5, cellD - 4);
                var x = b.R.x + i * cellW + c.R(2, Mathf.Max(3, cellW - w - 1));
                var z = b.R.y + j * cellD + c.R(2, Mathf.Max(3, cellD - d - 1));
                // Every type needs room for its furniture; a lot too small takes a house, or stays empty.
                var (minW, minD) = MinSize(type);
                if (w < minW || d < minD)
                {
                    if (w >= 10 && d >= 9) type = name == "Cabin" ? "Cabin" : "House";
                    else continue;
                }
                c.Houses++;
                var divide = type == "Clinic" || type == "Security" || type == "Military" ? w >= 11 : type == "House" && w >= 12;
                var house = Building(c, x, z, w, d, $"{type} {c.Houses}", divide: divide);
                if (type == "Garage") WideDoor(c, house, 5);
                c.Later.Add((house, type == "House" && c.Chance(0.15f)));
            }
            Greenery(c, b, 0.72f, 3);
            Bushes(c, b, 2);
        }

        private static void Labs(Ctx c, Block b)
        {
            var w = Mathf.Min(b.R.width - 6, c.R(23, 30));
            var d = Mathf.Min(b.R.height - 6, c.R(17, 22));
            var x = b.R.x + c.R(2, Mathf.Max(3, b.R.width - w - 2));
            var z = b.R.y + c.R(2, Mathf.Max(3, b.R.height - d - 2));
            c.Houses++;
            var lab = Building(c, x, z, w, d, $"Lab {c.Houses}", divide: true, height: 4.4f);
            c.Later.Add((lab, c.Chance(0.4f)));
            CoverLines(c, b, 2);
            Barrels(c, b, 2);
        }

        private static void Yard(Ctx c, Block b)
        {
            // 2026-10-04: a large warehouse when the yard has room (a truck parks at its door).
            var w = Mathf.Min(b.R.width - 8, c.R(23, 30));
            var d = Mathf.Min(b.R.height - 10, c.R(17, 22));
            if (w >= 18 && d >= 13)
            {
                var x = b.R.x + (b.R.width - w) / 2 + c.R(-1, 2);
                var z = b.R.y + (b.R.height - d) / 2 + c.R(0, 3);
                c.Houses++;
                var warehouse = Building(c, x, z, w, d, $"Warehouse {c.Houses}", height: 5.8f);
                WideDoor(c, warehouse, 5);
                c.Later.Add((warehouse, false));
            }
            else CoverLines(c, b, c.R(4, 7));
            Barrels(c, b, 2);
            Bushes(c, b, 1);
            Greenery(c, b, 0.78f, 2);
        }

        /// <summary>A fenced compound: a ring of cover with two gates, a building inside.</summary>
        private static void Compound(Ctx c, Block b)
        {
            var r = new RectInt(b.R.x + 2, b.R.y + 2, b.R.width - 4, b.R.height - 4);
            Fence(c, r, 3);
            var w = Mathf.Min(r.width - 8, c.R(16, 20));
            var d = Mathf.Min(r.height - 8, c.R(12, 16));
            c.Houses++;
            var house = Building(c, r.x + (r.width - w) / 2 + c.R(-2, 3), r.y + (r.height - d) / 2 + c.R(-2, 3), w, d, $"Military {c.Houses}", divide: w >= 11);
            c.Later.Add((house, false));
            Barrels(c, b, 2);
        }

        private static void Plaza(Ctx c, Block b)
        {
            var cx = b.R.center.x;
            var cz = b.R.center.y;
            for (var x = b.R.x + 3; x < b.R.xMax - 3; x++)
            for (var z = b.R.y + 3; z < b.R.yMax - 3; z++)
                if (Mathf.Abs(x - cx) + Mathf.Abs(z - cz) < 10) c.Map.SetSurface(x, z, Surface.Road);
            // A low wall ring with openings, and greenery around.
            for (var a = 0; a < 16; a++)
            {
                if (a % 4 == 0) continue;
                var ang = a / 16f * Mathf.PI * 2f;
                var x = Mathf.RoundToInt(cx + Mathf.Cos(ang) * 6f);
                var z = Mathf.RoundToInt(cz + Mathf.Sin(ang) * 6f);
                if (Open(c, x, z)) c.Map[x, z] = Tile.Cover;
            }
            Greenery(c, b, 0.6f, 4);
            Bushes(c, b, 2);
        }

        /// <summary>A fenced animal enclosure (reserve): a ring of cover with two gaps, grass and bushes inside.</summary>
        private static void Enclosure(Ctx c, Block b)
        {
            var r = new RectInt(b.R.x + 4, b.R.y + 4, b.R.width - 8, b.R.height - 8);
            if (r.width >= 10 && r.height >= 10)
            Fence(c, r, 2);
            Greenery(c, b, 0.3f, 2);
            Bushes(c, b, 4);
        }

        private static void Fence(Ctx c, RectInt r, int gates)
        {
            var cells = new List<Vector2Int>();
            for (var x = r.xMin; x < r.xMax; x++)
            {
                cells.Add(new Vector2Int(x, r.yMin));
                cells.Add(new Vector2Int(x, r.yMax - 1));
            }
            for (var z = r.yMin + 1; z < r.yMax - 1; z++)
            {
                cells.Add(new Vector2Int(r.xMin, z));
                cells.Add(new Vector2Int(r.xMax - 1, z));
            }
            // Gates: one always in the south side (towards the camera), the rest anywhere.
            var openings = new List<(Vector2Int at, bool horizontal)> { (new Vector2Int(r.x + r.width / 2 + c.R(-3, 4), r.yMin), true) };
            for (var g = 1; g < gates; g++)
            {
                var side = c.R(0, 3);
                openings.Add(side == 0 ? (new Vector2Int(r.x + c.R(3, r.width - 4), r.yMax - 1), true)
                    : side == 1 ? (new Vector2Int(r.xMin, r.y + c.R(3, r.height - 4)), false)
                    : (new Vector2Int(r.xMax - 1, r.y + c.R(3, r.height - 4)), false));
            }
            foreach (var cell in cells)
            {
                var gate = false;
                foreach (var (at, horizontal) in openings)
                    if (horizontal ? cell.y == at.y && Mathf.Abs(cell.x - at.x) <= 1 : cell.x == at.x && Mathf.Abs(cell.y - at.y) <= 1) gate = true;
                if (!gate && Open(c, cell.x, cell.y)) c.Map[cell.x, cell.y] = Tile.Cover;
            }
        }

        /// <summary>Short rows of cover (crates, barrels, containers) scattered in a block, never sealing anything.</summary>
        private static void CoverLines(Ctx c, Block b, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var horizontal = c.Chance(0.5f);
                var length = c.R(3, 7);
                var x = c.R(b.R.x + 3, b.R.xMax - 3 - (horizontal ? length : 0));
                var z = c.R(b.R.y + 3, b.R.yMax - 3 - (horizontal ? 0 : length));
                if (!AreaFree(c, x - 1, z - 1, (horizontal ? length : 1) + 2, (horizontal ? 1 : length) + 2)) continue;
                for (var k = 0; k < length; k++) c.Map[horizontal ? x + k : x, horizontal ? z : z + k] = Tile.Cover;
            }
        }

        private static bool AreaFree(Ctx c, int x, int z, int w, int d)
        {
            for (var i = x; i < x + w; i++)
            for (var j = z; j < z + d; j++)
                if (!Open(c, i, j)) return false;
            return true;
        }

        private static void Barrels(Ctx c, Block b, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var x = c.R(b.R.x + 2, b.R.xMax - 2);
                var z = c.R(b.R.y + 2, b.R.yMax - 2);
                if (AreaFree(c, x - 1, z - 1, 3, 3)) c.Map[x, z] = Tile.Barrel;
            }
        }

        private static void Bushes(Ctx c, Block b, int clusters)
        {
            for (var i = 0; i < clusters; i++)
            {
                var w = c.R(2, 5);
                var d = c.R(2, 4);
                var x = c.R(b.R.x + 2, Mathf.Max(b.R.x + 3, b.R.xMax - 2 - w));
                var z = c.R(b.R.y + 2, Mathf.Max(b.R.y + 3, b.R.yMax - 2 - d));
                if (!AreaFree(c, x - 1, z - 1, w + 2, d + 2)) continue;
                for (var k = x; k < x + w; k++)
                for (var j = z; j < z + d; j++)
                    if (!((k == x || k == x + w - 1) && (j == z || j == z + d - 1) && c.Chance(0.5f))) c.Map[k, j] = Tile.Bush;
            }
        }

        /// <summary>Concrete pads (yards, camps, depots): rectangles of road surface, so they get kerbs too.</summary>
        private static void Pads(Ctx c, Block b, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var w = c.R(8, Mathf.Max(9, b.R.width - 6));
                var d = c.R(6, Mathf.Max(7, b.R.height - 8));
                var x = c.R(b.R.x + 2, Mathf.Max(b.R.x + 3, b.R.xMax - 2 - w));
                var z = c.R(b.R.y + 2, Mathf.Max(b.R.y + 3, b.R.yMax - 2 - d));
                for (var k = x; k < x + w; k++)
                for (var j = z; j < z + d; j++)
                    if (Inside(c, k, j) && c.Map.SurfaceAt(k, j) == Surface.None && c.Map[k, j] != Tile.Wall) c.Map.SetSurface(k, j, Surface.Road);
            }
        }

        /// <summary>Everywhere: sparse grass tufts and dirt patches, so no stretch of ground is one flat colour.</summary>
        private static void Patches(Ctx c, Block b)
        {
            var map = c.Map;
            var seed = c.Recipe.Seed * 0.21f + b.Gx * 5.3f + b.Gz * 11.7f;
            for (var x = b.R.x; x < b.R.xMax; x++)
            for (var z = b.R.y; z < b.R.yMax; z++)
            {
                if (!Inside(c, x, z) || map.SurfaceAt(x, z) != Surface.None || map[x, z] == Tile.Wall) continue;
                var g = Art.MeshKit.Noise2(x / 4.5f + seed, z / 4.5f);
                var d = Art.MeshKit.Noise2(x / 5.5f + seed + 40f, z / 5.5f + 13f);
                if (g > 0.76f) map.SetSurface(x, z, g > 0.8f ? Surface.Grass : Surface.Grass2);
                else if (d > 0.74f) map.SetSurface(x, z, Surface.Road2);
            }
        }

        /// <summary>Grass over the block with natural edges (noise); the threshold sets how much is grass.</summary>
        private static void Greenery(Ctx c, Block b, float threshold, int patchiness)
        {
            var map = c.Map;
            var seed = c.Recipe.Seed * 0.13f + b.Gx * 7.1f + b.Gz * 3.3f;
            for (var x = b.R.x; x < b.R.xMax; x++)
            for (var z = b.R.y; z < b.R.yMax; z++)
            {
                if (!Inside(c, x, z) || map.SurfaceAt(x, z) != Surface.None || map[x, z] == Tile.Wall) continue;
                var n = Art.MeshKit.Noise2(x / (6f + patchiness) + seed, z / (6f + patchiness)) * 0.75f + Art.MeshKit.Noise2(x / 3f + seed * 2f, z / 3f) * 0.25f;
                if (n > threshold) map.SetSurface(x, z, Surface.Grass);
                else if (n > threshold - 0.07f) map.SetSurface(x, z, Surface.Grass2);
            }
        }

        // ---- start and objectives ----

        private static Vector2Int CornerPoint(Ctx c, Block b, int inset)
        {
            var west = b.Gx == 0;
            var south = b.Gz == 0;
            return new Vector2Int(west ? b.R.x + inset : b.R.xMax - 1 - inset, south ? b.R.y + inset : b.R.yMax - 1 - inset);
        }

        private static void StartArea(Ctx c, Block b)
        {
            var map = c.Map;
            const int size = 16;
            var west = b.Gx == 0;
            var south = b.Gz == 0;
            var x = west ? b.R.x : b.R.xMax - size;
            var z = south ? b.R.y : b.R.yMax - size;
            map.spawnArea = new MapRect { name = "Spawn Area", x = x, z = z, w = size, d = size };
            // 2026-10-04: the team's helicopter stands on the outer half of the pad; the players start on the inner half.
            var p = new Vector2Int(x + size / 2 + (west ? 5 : -5), z + size / 2);
            map[p.x, p.y] = Tile.PlayerSpawn;
            // A little sandbag cover and a crate pile so the start reads as the team's foothold.
            for (var k = -2; k <= 2; k++)
                if (Open(c, p.x + k, p.y + (south ? 5 : -5))) map[p.x + k, p.y + (south ? 5 : -5)] = Tile.Cover;
            Greenery(c, b, 0.7f, 3);
        }

        private static void Arena(Ctx c, Block b)
        {
            var map = c.Map;
            var r = new RectInt(b.R.x + 1, b.R.y + 1, b.R.width - 2, b.R.height - 2);
            map.bossArea = new MapRect { name = "Boss Area", x = r.x, z = r.y, w = r.width, d = r.height };
            var center = new Vector2Int(Mathf.RoundToInt(r.center.x), Mathf.RoundToInt(r.center.y));
            // Pillars to dodge around, and broken cover at the edges.
            var pillars = c.R(4, 7);
            for (var i = 0; i < pillars; i++)
            {
                var a = i / (float)pillars * Mathf.PI * 2f + c.R() * 0.4f;
                var px = Mathf.RoundToInt(center.x + Mathf.Cos(a) * r.width * 0.3f);
                var pz = Mathf.RoundToInt(center.y + Mathf.Sin(a) * r.height * 0.3f);
                if (!AreaFree(c, px - 1, pz - 1, 4, 4)) continue;
                for (var dx = 0; dx < 2; dx++)
                for (var dz = 0; dz < 2; dz++) map[px + dx, pz + dz] = Tile.Wall;
            }
            CoverLines(c, b, 3);
            map[center.x, center.y] = Tile.BossSpawn;
            for (var x = r.x; x < r.xMax; x++)
            for (var z = r.y; z < r.yMax; z++)
                if (map.SurfaceAt(x, z) == Surface.None && ((x + z) % 9 == 0 || Art.MeshKit.Noise2(x * 0.2f, z * 0.2f) > 0.66f)) map.SetSurface(x, z, Surface.Road2);
        }

        /// <summary>Rescue: the scientist hides deep in a building in the far district.</summary>
        private static void Refuge(Ctx c, Block b)
        {
            var w = Mathf.Min(b.R.width - 8, c.R(17, 22));
            var d = Mathf.Min(b.R.height - 8, c.R(13, 17));
            var x = b.R.x + (b.R.width - w) / 2 + c.R(-2, 3);
            var z = b.R.y + (b.R.height - d) / 2 + c.R(-2, 3);
            var house = Building(c, x, z, w, d, "Clinic", divide: true);
            // The hostage waits in the far half from the south door.
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var p = InteriorCell(c, house);
                if (!p.HasValue || p.Value.y < z + d / 2) continue;
                c.Map[p.Value.x, p.Value.y] = Tile.Hostage;
                break;
            }
            c.Later.Add((house, false));
            CoverLines(c, b, 3);
            Bushes(c, b, 2);
            Greenery(c, b, 0.7f, 3);
        }

        /// <summary>
        /// Bomb: the hive yard. The target incubator stands in the open with half walls around it, and many zombie spawn
        /// points ring it (owner, 2026-10-03: a high concentration of spawn points at the bomb target).
        /// </summary>
        private static void Nest(Ctx c, Block b)
        {
            var map = c.Map;
            var center = new Vector2Int(Mathf.RoundToInt(b.R.center.x) + c.R(-3, 4), Mathf.RoundToInt(b.R.center.y) + c.R(-3, 4));
            map[center.x, center.y] = Tile.BombTarget;
            for (var a = 0; a < 8; a++)
            {
                if (a % 2 == 1) continue;
                var ang = a / 8f * Mathf.PI * 2f;
                for (var k = -1; k <= 1; k++)
                {
                    var x = Mathf.RoundToInt(center.x + Mathf.Cos(ang) * 6f + Mathf.Sin(ang) * k);
                    var z = Mathf.RoundToInt(center.y + Mathf.Sin(ang) * 6f - Mathf.Cos(ang) * k);
                    if (Open(c, x, z)) map[x, z] = Tile.Cover;
                }
            }
            var placed = 0;
            for (var attempt = 0; attempt < 200 && placed < 18; attempt++)
            {
                var ang = c.R() * Mathf.PI * 2f;
                var dist = 9f + c.R() * 14f;
                var x = Mathf.RoundToInt(center.x + Mathf.Cos(ang) * dist);
                var z = Mathf.RoundToInt(center.y + Mathf.Sin(ang) * dist);
                if (!Open(c, x, z) || (new Vector2Int(x, z) - center).magnitude < 8f) continue;
                map[x, z] = Tile.ZombieSpawn;
                placed++;
            }
            Barrels(c, b, 3);
            CoverLines(c, b, 2);
            for (var x = b.R.x; x < b.R.xMax; x++)
            for (var z = b.R.y; z < b.R.yMax; z++)
                if (map.SurfaceAt(x, z) == Surface.None && Art.MeshKit.Noise2(x * 0.15f + 9f, z * 0.15f) > 0.55f) map.SetSurface(x, z, Surface.Road2);
        }

        /// <summary>Vaccine: a big lab with an inner room; the Vaccine lies in the far part.</summary>
        private static void Vault(Ctx c, Block b)
        {
            var w = Mathf.Min(b.R.width - 6, c.R(23, 30));
            var d = Mathf.Min(b.R.height - 6, c.R(18, 23));
            var x = b.R.x + (b.R.width - w) / 2;
            var z = b.R.y + (b.R.height - d) / 2;
            var lab = Building(c, x, z, w, d, "Vaccine Lab", divide: true, height: 4.7f);
            // An inner room in the north-east part, with its own doorway.
            var rx = x + w / 2 + 2;
            var rz = z + d / 2;
            for (var i = rx; i < x + w; i++) c.Map[i, rz] = Tile.Wall;
            c.Map[rx + 2, rz] = Tile.Ground;
            c.Map[rx + 3, rz] = Tile.Ground;
            var v = new Vector2Int(Mathf.Min(x + w - 3, rx + 4), Mathf.Min(z + d - 2, rz + 3));
            c.Map[v.x, v.y] = Tile.Vaccine;
            c.Later.Add((lab, false));
            CoverLines(c, b, 2);
            Barrels(c, b, 2);
        }

        // ------------------------------------------------------------------ set pieces (2026-10-03)

        /// <summary>
        /// Places one prop if its whole footprint (plus a cell of room) is open ground, and reserves the cells. Turns are
        /// multiples of 90° (a little jitter), so the footprint check stays exact.
        /// </summary>
        private static bool Piece(Ctx c, string id, float x, float z, float rotation)
        {
            var piece = Art.ArtLibrary.Get(id);
            if (piece == null) return false;
            var quarter = Mathf.RoundToInt(rotation / 90f) & 1;
            var hx = (quarter == 0 ? piece.Footprint.x : piece.Footprint.y) * 0.5f;
            var hz = (quarter == 0 ? piece.Footprint.y : piece.Footprint.x) * 0.5f;
            int x0 = Mathf.FloorToInt(x - hx) - 1, x1 = Mathf.FloorToInt(x + hx) + 1, z0 = Mathf.FloorToInt(z - hz) - 1, z1 = Mathf.FloorToInt(z + hz) + 1;
            for (var i = x0; i <= x1; i++)
            for (var j = z0; j <= z1; j++)
                // Outdoor pieces stay off roads and out of buildings (interiors are furnished separately).
                if (!Open(c, i, j) || c.Map.SurfaceAt(i, j) == Surface.Road || c.Map.SurfaceAt(i, j) == Surface.Floor) return false;
            for (var i = x0 + 1; i < x1; i++)
            for (var j = z0 + 1; j < z1; j++) c.Occupied[i, j] = true;
            // Vehicles and objects are mirrored half the time for variety; buildings never (owner, 2026-10-04).
            c.Map.props.Add(new MapDecoration { id = id, x = x, z = z, rotation = rotation + (c.R() - 0.5f) * 8f, scale = 1f, flip = !piece.NoFlip && c.Chance(0.5f) });
            return true;
        }

        /// <summary>
        /// The team's helicopter, centred on the helipad's "H" (owner, 2026-10-05). The model's middle isn't its pivot,
        /// so the model's own bounds are centred on the pad, nose along the pad (exactly 90°, no jitter). The players
        /// start beside it, clear of its body.
        /// </summary>
        private static void Helicopter(Ctx c, float cx, float cz)
        {
            const float yaw = 90f;
            var def = Art.PackProps.Get("helicopter");
            var mesh = def != null ? Art.PackProps.Mesh(def.Id, 0) : null;
            var offset = Vector3.zero;
            if (mesh != null) offset = Quaternion.Euler(0f, yaw, 0f) * (mesh.bounds.center * def.Size);
            var x = cx - offset.x;
            var z = cz - offset.z;
            c.Map.props.Add(new MapDecoration { id = "helicopter", x = x, z = z, rotation = yaw, scale = 1f });
            var piece = Art.ArtLibrary.Get("helicopter");
            if (piece == null) return;
            // Reserve the body's cells (its rotor overhangs freely).
            var half = new Vector2(piece.Footprint.y, piece.Footprint.x) * 0.5f;
            for (var i = Mathf.FloorToInt(cx - half.x); i <= Mathf.FloorToInt(cx + half.x); i++)
            for (var j = Mathf.FloorToInt(cz - half.y); j <= Mathf.FloorToInt(cz + half.y); j++)
                if (i >= 0 && j >= 0 && i < c.Map.width && j < c.Map.height) c.Occupied[i, j] = true;
        }

        /// <summary>Tries a few random spots in a block for a piece.</summary>
        private static bool PieceIn(Ctx c, Block b, string id, int margin = 3)
        {
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var x = c.R(b.R.x + margin, Mathf.Max(b.R.x + margin + 1, b.R.xMax - margin)) + 0.5f;
                var z = c.R(b.R.y + margin, Mathf.Max(b.R.y + margin + 1, b.R.yMax - margin)) + 0.5f;
                if (Piece(c, id, x, z, c.R(0, 4) * 90f)) return true;
            }
            return false;
        }

        private static void Decor(Ctx c, string id, float x, float z, float rotation = -1f) =>
            c.Map.decorations.Add(new MapDecoration { id = id, x = x, z = z, rotation = rotation < 0f ? c.R() * 360f : rotation, scale = 1f });

        /// <summary>
        /// Scenes per district with the owner's obstacle pack (2026-10-04): army posts with tents, jeeps and a control
        /// tower at the checkpoint; parked cars, dumpsters and hydrants by the houses; solid shops, a clinic and a gas
        /// station among the enterable buildings; benches and trees in the parks; trucks and vans
        /// around the labs; trees, dead trees, fences and enclosures in the reserve. Furniture inside buildings: couches
        /// and shelves in houses, workbenches, lab shelves and computers in labs. Everything keeps a free cell around it,
        /// and markers stay reachable (checked after the map is built).
        /// </summary>
        private static void SetPieces(Ctx c, Block b)
        {
            void Some(string id, int min, int max, int margin = 3)
            {
                var n = c.R(min, max + 1);
                for (var i = 0; i < n; i++) PieceIn(c, b, id, margin);
            }
            switch (b.Kind)
            {
                case "start":
                {
                    // The team's landing zone: their helicopter on the outer half of the pad (nose along it), a jeep and two
                    // tents beside the helipad (owner, 2026-10-04: the helicopter in the starting area).
                    var s = c.Map.spawnArea;
                    var cx = s.x + s.w / 2f;
                    var cz = s.z + s.d / 2f;
                    var west = b.Gx == 0;
                    var south = b.Gz == 0;
                    var side = s.w * 0.5f + 3.5f;
                    Helicopter(c, cx, cz);
                    Piece(c, "jeep", cx + (west ? side : -side), cz + (south ? -2f : 2f), 90f);
                    Piece(c, "military_tent", cx + (west ? -side : side), cz + (south ? 3f : -3f), 0f);
                    Piece(c, "military_tent", cx + (west ? -side : side), cz + (south ? -4f : 4f), 0f);
                    break;
                }
                case "yard":
                    Some("truck", 1, 2);
                    Some("van", 0, 1);
                    Some("trash_bin", 1, 2);
                    break;
                case "compound":
                case "barracks":
                    Some("military_tent", 2, 3, 4);
                    Some("jeep", 1, 1);
                    break;
                case "houses":
                case "shops":
                case "cabins":
                {
                    foreach (var house in c.Map.buildings)
                    {
                        if (!b.R.Contains(new Vector2Int(house.x, house.z))) continue;
                        if (c.Chance(0.5f)) Piece(c, b.Kind == "cabins" ? "wooden_fence" : "trash_bin", house.x + house.w + 2.5f, house.z + 1.5f, 90f);
                        // Parked beside the house (never in front of its door).
                        if (c.Chance(0.4f)) Piece(c, c.Chance(0.75f) ? "car" : "van", house.x - 2.5f, house.z + house.d * 0.5f, 90f);
                        if (c.Chance(0.3f)) Piece(c, "fire_hydrant", house.x - 1.5f, house.z - 1.5f, 0f);
                    }
                    if (c.Chance(0.4f)) PieceIn(c, b, "motorcycle", 2);
                    break;
                }
                case "park":
                case "meadow":
                    Some("tree", 3, 5);
                    Some("park_bench", 2, 3, 2);
                    Some("fire_hydrant", 0, 1, 2);
                    break;
                case "plaza":
                    PieceIn(c, b, "bus_stop", 2);
                    Some("park_bench", 2, 4, 2);
                    Some("tree", 1, 2);
                    Some("atm", 0, 1, 2);
                    break;
                case "forest":
                case "enclosure":
                    Some("tree", 4, 7, 2);
                    Some("dead_tree", 1, 3, 2);
                    if (b.Kind == "enclosure") Some("animal_enclosure", 1, 3);
                    Some("wooden_fence", 1, 3, 2);
                    Some("park_bench", 0, 1, 2);
                    break;
                case "labs":
                case "vault":
                    Some(c.Chance(0.5f) ? "truck" : "van", 1, 1);
                    break;
                case "nest":
                    Some("animal_enclosure", 1, 2);
                    Some("dead_tree", 1, 2, 2);
                    break;
                case "arena":
                    Some("truck", 1, 1);
                    Some("jeep", 0, 1);
                    break;
                case "refuge":
                    Some("military_tent", 1, 2, 3);
                    Some("truck", 1, 1);
                    Some("jeep", 0, 1);
                    break;
            }
        }


        /// <summary>
        /// Decorative details (owner, 2026-10-04): road markings (crosswalks at crossings, lane dashes, stop lines),
        /// blood stains and trails, scratch marks, trash and scraps of paper. Looks only; amounts grow with the map's area.
        /// </summary>
        private static void Details(Ctx c)
        {
            var map = c.Map;
            var area = map.width * map.height / (112f * 112f);
            var crossings = new HashSet<Vector2Int>();
            foreach (var x in c.RoadX)
            foreach (var z in c.RoadZ) crossings.Add(new Vector2Int(x, z));
            bool RoadAt(int x, int z) => x >= 0 && z >= 0 && x < map.width && z < map.height && map.SurfaceAt(x, z) == Surface.Road && !Tiles.IsSolid(map[x, z]);
            bool NearCrossing(int x, int z)
            {
                foreach (var k in crossings)
                    if (Mathf.Abs(k.x - x) <= 8 && Mathf.Abs(k.y - z) <= 8) return true;
                return false;
            }
            foreach (var k in crossings)
            {
                // A crosswalk on some arms of each crossing, a few metres out; sometimes a stop line behind it.
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (!c.Chance(0.5f)) continue;
                    int ax = k.x + dx * 5, az = k.y + dz * 5;
                    if (!RoadAt(ax, az)) continue;
                    // The stripes lie along the traffic: rotation 0 suits a road running north-south.
                    map.decorations.Add(new MapDecoration { id = "crosswalk", x = ax + 0.5f, z = az + 0.5f, rotation = dx != 0 ? 90f : 0f, scale = 0.7f });
                    if (c.Chance(0.4f) && RoadAt(k.x + dx * 7, k.y + dz * 7))
                        map.decorations.Add(new MapDecoration { id = "stop_line", x = k.x + dx * 7 + 0.5f, z = k.y + dz * 7 + 0.5f, rotation = dx != 0 ? 90f : 0f, scale = 0.9f });
                }
            }
            foreach (var x in c.RoadX)
                for (var z = 2; z < map.height - 2; z += 4)
                    if (RoadAt(x, z) && !NearCrossing(x, z)) Decor(c, "lane_dash", x + 0.5f, z + 0.5f, 90f);
            foreach (var z in c.RoadZ)
                for (var x = 2; x < map.width - 2; x += 4)
                    if (RoadAt(x, z) && !NearCrossing(x, z)) Decor(c, "lane_dash", x + 0.5f, z + 0.5f, 0f);

            void Scatter(string id, int count, System.Func<int, int, bool> where)
            {
                for (var i = 0; i < count; i++)
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        var x = c.R(2, map.width - 2);
                        var z = c.R(2, map.height - 2);
                        if (Tiles.IsSolid(map[x, z]) || !where(x, z)) continue;
                        Decor(c, id, x + c.R(), z + c.R());
                        break;
                    }
            }
            bool Anywhere(int x, int z) => true;
            bool Indoors(int x, int z) => map.SurfaceAt(x, z) == Surface.Floor;
            bool Street(int x, int z) => map.SurfaceAt(x, z) == Surface.Road;
            bool Outdoors(int x, int z) => map.SurfaceAt(x, z) != Surface.Floor && map[x, z] != Tile.Bush;
            bool Wet(int x, int z) => Outdoors(x, z) && (map.SurfaceAt(x, z) == Surface.Road || map.SurfaceAt(x, z) == Surface.Road2 || c.Chance(0.35f));
            Scatter("blood_stain", Mathf.RoundToInt(16 * area), Anywhere);
            Scatter("blood_trail", Mathf.RoundToInt(6 * area), Anywhere);
            // Owner, 2026-10-05: scratch marks and cracks only on buildings (drawn on the walls), none on the ground.
            Scatter("trash", Mathf.RoundToInt(14 * area), Street);
            Scatter("trash", Mathf.RoundToInt(8 * area), Anywhere);
            Scatter("papers", Mathf.RoundToInt(10 * area), Indoors);
            var normal = map.weather != "rain" && !map.dark;
            if (normal)
            {
                // Owner, 2026-10-05: leaves and loose paper that the wind blows now and then, in normal stages only.
                Scatter("wind_paper", Mathf.RoundToInt(12 * area), (x, z) => Outdoors(x, z) && (Street(x, z) || c.Chance(0.4f)));
                Scatter("wind_leaves", Mathf.RoundToInt(10 * area), Outdoors);
                foreach (var tree in map.props.FindAll(p => p.id == "tree" || p.id == "dead_tree"))
                {
                    if (!c.Chance(0.6f)) continue;
                    var a = c.R() * Mathf.PI * 2f;
                    var d = 1.5f + c.R() * 1.5f;
                    int x = Mathf.FloorToInt(tree.x + Mathf.Cos(a) * d), z = Mathf.FloorToInt(tree.z + Mathf.Sin(a) * d);
                    if (x > 2 && z > 2 && x < map.width - 2 && z < map.height - 2 && !Tiles.IsSolid(map[x, z]) && Outdoors(x, z))
                        Decor(c, "wind_leaves", x + c.R(), z + c.R());
                }
            }
            if (map.weather == "rain")
            {
                // Owner, 2026-10-05: brown puddles on the wet ground of rainy stages, mostly on the roads.
                for (var i = 0; i < Mathf.RoundToInt(34 * area); i++)
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        var x = c.R(3, map.width - 3);
                        var z = c.R(3, map.height - 3);
                        if (Tiles.IsSolid(map[x, z]) || Tiles.IsSolid(map[x + 1, z]) || Tiles.IsSolid(map[x - 1, z]) || !Wet(x, z)) continue;
                        map.decorations.Add(new MapDecoration { id = "rain_puddle", x = x + c.R(), z = z + c.R(), rotation = c.R() * 360f, scale = 0.7f + c.R() * 0.9f });
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ route, spawns, loot, areas

        /// <summary>
        /// Non-boss missions (owner, 2026-10-03): no Main Boss; secondary bosses guard the way, on the road crossings at
        /// about a third and two thirds of the way. Vaccine stages: the rival team starts in another corner.
        /// </summary>
        private static void Route(Ctx c)
        {
            var map = c.Map;
            if (c.Recipe.Mission != "boss" && c.Recipe.Mission != "endless")
            {
                var crossings = new List<Vector2Int>();
                foreach (var x in c.RoadX)
                foreach (var z in c.RoadZ) crossings.Add(new Vector2Int(x, z));
                var start = c.Start.R.center;
                var goal = c.Objective.R.center;
                foreach (var t in new[] { 0.33f, 0.68f })
                {
                    var target = Vector2.Lerp(start, goal, t);
                    crossings.Sort((a, b) => (a - target).sqrMagnitude.CompareTo((b - target).sqrMagnitude));
                    var p = crossings[0];
                    crossings.RemoveAt(0);
                    var cell = NearestOpen(c, p.x + 3, p.y + 3);
                    if (cell.HasValue) map[cell.Value.x, cell.Value.y] = Tile.SubBossSpawn;
                }
            }
            if (c.Recipe.Mission == "vaccine")
            {
                var sides = new List<Block> { c.Blocks[c.Start.Gx, c.Nz - 1 - c.Start.Gz], c.Blocks[c.Nx - 1 - c.Start.Gx, c.Start.Gz] };
                if (c.Chance(0.5f)) sides.Reverse();
                for (var i = 0; i < Mathf.Min(c.Recipe.RivalTeams, sides.Count); i++)
                {
                    var p = CornerPoint(c, sides[i], 6);
                    var cell = NearestOpen(c, p.x, p.y);
                    if (cell.HasValue) map[cell.Value.x, cell.Value.y] = Tile.RivalSpawn;
                }
            }
        }

        private static Vector2Int? NearestOpen(Ctx c, int x, int z)
        {
            for (var r = 0; r < 12; r++)
            for (var dx = -r; dx <= r; dx++)
            for (var dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                if (!AreaFree(c, x + dx - 1, z + dz - 1, 3, 3)) continue;
                return new Vector2Int(x + dx, z + dz);
            }
            return null;
        }

        /// <summary>Zombie spawn points: a handful per district on open ground, none in the start.</summary>
        private static void Spawns(Ctx c)
        {
            foreach (var b in c.All)
            {
                if (b == c.Start) continue;
                var count = b == c.Objective ? 6 : 4;
                for (var attempt = 0; attempt < 60 && count > 0; attempt++)
                {
                    var x = c.R(b.R.x + 2, b.R.xMax - 2);
                    var z = c.R(b.R.y + 2, b.R.yMax - 2);
                    if (!AreaFree(c, x - 1, z - 1, 3, 3)) continue;
                    c.Map[x, z] = Tile.ZombieSpawn;
                    count--;
                }
            }
        }

        /// <summary>Loot cells on open ground across the map, on top of the ones inside buildings (D-205).</summary>
        private static void Loot(Ctx c)
        {
            var map = c.Map;
            var items = GameTuning.Load().loot;
            var wanted = Mathf.RoundToInt((items.longGuns + items.ammoBoxes + items.healthItems + items.grenades) * StageRuntime.AreaScale(map.width, map.height)) + 14;
            var have = 0;
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == Tile.LootSpawn) have++;
            for (var attempt = 0; attempt < 2000 && have < wanted; attempt++)
            {
                var x = c.R(4, map.width - 4);
                var z = c.R(4, map.height - 4);
                if (!AreaFree(c, x - 1, z - 1, 3, 3)) continue;
                map[x, z] = Tile.LootSpawn;
                have++;
            }
        }

        /// <summary>
        /// One spawning area per district (D-079/D-080): zombies placed there (more further from the start and with
        /// difficulty), an entry group, some refill; the route and the objective are combat zones.
        /// </summary>
        private static void Areas(Ctx c)
        {
            foreach (var b in c.All)
            {
                if (b == c.Start) continue;
                var far = Mathf.Abs(b.Gx - c.Start.Gx) + Mathf.Abs(b.Gz - c.Start.Gz);
                var size = b.R.width * b.R.height / 900f;
                var basic = Mathf.RoundToInt((6 + far * 2) * size * c.Recipe.Difficulty) + c.R(0, 3);
                var area = new MapArea
                {
                    name = b.Name, x = b.R.x, z = b.R.y, w = b.R.width, d = b.R.height,
                    entryGroup = 2 + far + c.R(0, 2),
                    refills = c.Chance(0.5f),
                    combatZone = b == c.Objective || far >= 3,
                };
                area.placed.Add(new MapPlaced { type = "Basic", count = basic });
                if (far >= 2) area.placed.Add(new MapPlaced { type = "Fast", count = 1 + c.R(0, far) });
                if (far >= 3 && c.Chance(0.6f)) area.placed.Add(new MapPlaced { type = "Tank", count = 1 });
                c.Map.areas.Add(area);
            }
        }

        // ------------------------------------------------------------------ checks

        /// <summary>Cells a survivor can't reach from the player spawn that hold a marker (empty = all fine).</summary>
        public static List<Vector2Int> Unreachable(StageMap map)
        {
            var blocked = new bool[map.width, map.height];
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++) blocked[x, z] = Tiles.BlocksMovement(map[x, z]);
            foreach (var p in map.props)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                if (piece == null || piece.Bush || piece.Walkable) continue;
                // The prop's real footprint at its turn (2026-10-04: a square of its longest side sealed off rooms that
                // were open, and the clean-up then deleted furniture). A cell counts as blocked when its centre is
                // inside the footprint shrunk a little, so a thin gap that people fit through stays open.
                var scale = p.scale > 0f ? p.scale : 1f;
                var footprint = Art.PackProps.Footprint(piece, p);
                var hx = footprint.x * 0.5f * scale - 0.25f;
                var hz = footprint.y * 0.5f * scale - 0.25f;
                if (piece.Round) hx = hz = piece.Footprint.x * 0.5f * scale - 0.25f;
                var turn = -p.rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                var reach = Mathf.CeilToInt(Mathf.Max(hx, hz) + 1f);
                for (var x = Mathf.FloorToInt(p.x) - reach; x <= Mathf.FloorToInt(p.x) + reach; x++)
                for (var z = Mathf.FloorToInt(p.z) - reach; z <= Mathf.FloorToInt(p.z) + reach; z++)
                {
                    if (x < 0 || z < 0 || x >= map.width || z >= map.height) continue;
                    var dx = x + 0.5f - p.x;
                    var dz = z + 0.5f - p.z;
                    // Into the prop's own frame (Unity's yaw turns +z towards +x).
                    var lx = dx * cos + dz * sin;
                    var lz = -dx * sin + dz * cos;
                    if (Mathf.Abs(lx) <= hx && Mathf.Abs(lz) <= hz) blocked[x, z] = true;
                }
            }
            var seen = new bool[map.width, map.height];
            var queue = new Queue<Vector2Int>();
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == Tile.PlayerSpawn)
                {
                    seen[x, z] = true;
                    queue.Enqueue(new Vector2Int(x, z));
                }
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var n = p + d;
                    if (n.x < 0 || n.y < 0 || n.x >= map.width || n.y >= map.height || seen[n.x, n.y] || blocked[n.x, n.y]) continue;
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            var bad = new List<Vector2Int>();
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (Tiles.IsMarker(map[x, z]) && map[x, z] != Tile.Barrel && !seen[x, z]) bad.Add(new Vector2Int(x, z));
            return bad;
        }
    }
}
