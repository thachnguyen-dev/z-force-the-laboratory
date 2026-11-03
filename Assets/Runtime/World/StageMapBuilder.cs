using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>Turns map data into the runtime <see cref="StageLayout"/>, and the old code-built layout into map data.</summary>
    public static class StageMapBuilder
    {
        public const float WallHeight = 3f;
        public const float CliffHeight = 5f;
        public const float CoverHeight = 1.3f;

        public static readonly Color WallColor = new Color(0.52f, 0.5f, 0.47f);
        public static readonly Color CliffColor = new Color(0.42f, 0.36f, 0.3f);
        public static readonly Color CoverColor = new Color(0.45f, 0.38f, 0.28f);

        // ------------------------------------------------------------------ map → layout

        public static StageLayout ToLayout(StageMap map)
        {
            var s = new StageLayout
            {
                Name = map.name,
                Size = new Vector2(map.width, map.height),
                Difficulty = map.difficulty,
                SpawnArea = map.spawnArea.ToRect(),
                BossArea = map.bossArea.ToRect(),
                Map = map,
                Rain = map.weather == "rain",
                Dark = map.dark,
                Rescue = map.mission == "rescue",
                Mission = string.IsNullOrEmpty(map.mission) ? "boss" : map.mission,
                Tutorial = map.tutorial,
                Endless = map.endless,
                BossType = System.Enum.TryParse<ZombieType>(map.bossType, out var bt) && ZombieTypes.IsBoss(bt) ? bt : ZombieType.Boss,
                SubBossType = System.Enum.TryParse<ZombieType>(map.subBossType, out var sbt) && ZombieTypes.IsBoss(sbt) ? sbt : ZombieType.SubBoss,
                LootScale = map.lootScale <= 0f ? 1f : map.lootScale,
                TileSetName = map.tileSet,
                BaseGroundId = string.IsNullOrEmpty(map.baseGround) ? BaseGround.DefaultId : map.baseGround,
            };

            // Colliders: merged rectangles of solid cells, one set per kind (few boxes instead of one per cell).
            AddMerged(map, Tile.Wall, r => s.Blocks.Add(new Block(r.center.x, r.center.y, r.width, r.height, WallHeight, WallColor)));
            AddMerged(map, Tile.Cliff, r => s.Blocks.Add(new Block(r.center.x, r.center.y, r.width, r.height, CliffHeight, CliffColor)));
            AddMerged(map, Tile.Cover, r => s.Blocks.Add(new Block(r.center.x, r.center.y, r.width, r.height, CoverHeight, CoverColor)));

            for (var z = 0; z < map.height; z++)
            for (var x = 0; x < map.width; x++)
            {
                var p = StageMap.CellCenter(x, z);
                switch (map[x, z])
                {
                    case Tile.PlayerSpawn: s.PlayerSpawn = p; break;
                    case Tile.BossSpawn: s.BossPosition = p; s.HasBoss = true; break;
                    case Tile.SubBossSpawn: s.SubBossPositions.Add(p); break;
                    case Tile.Barrel: s.Barrels.Add(p); break;
                    case Tile.LootSpawn: s.LootPoints.Add(p); break;
                    case Tile.ZombieSpawn: s.ZombieSpawnPoints.Add(p); break;
                    case Tile.Hostage: s.HostagePosition = p; break;
                    case Tile.BombTarget: s.BombTargetPosition = p; break;
                    case Tile.Vaccine: s.VaccinePosition = p; break;
                    case Tile.RivalSpawn: s.RivalSpawns.Add(p); break;
                }
            }

            foreach (var b in map.buildings)
            {
                // The footprint includes the wall cells on the far sides, so the roof covers the walls too.
                var def = new BuildingDef { Name = b.name, Bounds = new Rect(b.x, b.z, b.w + 1, b.d + 1), Height = b.height, Wall = 0f, MayHaveChest = false };
                // Doorways are the walkable cells on the building's outline.
                for (var x = b.x; x <= b.x + b.w; x++)
                for (var z = b.z; z <= b.z + b.d; z++)
                {
                    var edge = x == b.x || x == b.x + b.w || z == b.z || z == b.z + b.d;
                    var cell = map[x, z];
                    if (edge && !Tiles.BlocksMovement(cell)) def.DoorPoints.Add(StageMap.CellCenter(x, z));
                    if (cell == Tile.ChestSpawn && !def.MayHaveChest)
                    {
                        def.MayHaveChest = true;
                        def.ChestPoint = StageMap.CellCenter(x, z);
                    }
                    if (cell == Tile.Incubator && !def.Incubator)
                    {
                        def.Incubator = true;
                        def.IncubatorPoint = StageMap.CellCenter(x, z);
                    }
                }
                s.Buildings.Add(def);
            }

            foreach (var zombie in map.zombies)
                if (System.Enum.TryParse<ZombieType>(zombie.type, out var zt) && !ZombieTypes.IsBoss(zt))
                    s.PlacedZombies.Add((zt, StageMap.CellCenter(zombie.x, zombie.z)));

            foreach (var a in map.areas)
            {
                var area = new AreaDef { Name = a.name, Bounds = a.ToRect(), EntryGroup = a.entryGroup, Refills = a.refills, CombatZone = a.combatZone };
                foreach (var placed in a.placed)
                    if (System.Enum.TryParse<ZombieType>(placed.type, out var type)) area.Placed.Add((type, placed.count));
                s.Areas.Add(area);
            }
            return s;
        }

        /// <summary>Greedy rectangle merge of every cell of one kind.</summary>
        private static void AddMerged(StageMap map, Tile tile, System.Action<Rect> add)
        {
            var used = new bool[map.width, map.height];
            for (var z = 0; z < map.height; z++)
            for (var x = 0; x < map.width; x++)
            {
                if (used[x, z] || map[x, z] != tile) continue;
                var w = 1;
                while (x + w < map.width && !used[x + w, z] && map[x + w, z] == tile) w++;
                var d = 1;
                while (z + d < map.height)
                {
                    var full = true;
                    for (var i = 0; i < w && full; i++) full = !used[x + i, z + d] && map[x + i, z + d] == tile;
                    if (!full) break;
                    d++;
                }
                for (var i = 0; i < w; i++)
                for (var j = 0; j < d; j++) used[x + i, z + j] = true;
                add(new Rect(x, z, w, d));
            }
        }


        // ------------------------------------------------------------------ validation (Map Editor warnings, tests)

        /// <summary>Problems in a map, most serious first. Errors start with "Error:" — the stage won't play properly.</summary>
        public static List<string> Validate(StageMap map)
        {
            var problems = new List<string>();
            int players = 0, bosses = 0, hostages = 0, loot = 0, spawns = 0, targets = 0, vaccines = 0;
            var markersOutside = 0;
            for (var z = 0; z < map.height; z++)
            for (var x = 0; x < map.width; x++)
            {
                var t = map[x, z];
                switch (t)
                {
                    case Tile.PlayerSpawn: players++; break;
                    case Tile.BossSpawn: bosses++; break;
                    case Tile.Hostage: hostages++; break;
                    case Tile.BombTarget: targets++; break;
                    case Tile.Vaccine: vaccines++; break;
                    case Tile.LootSpawn: loot++; break;
                    case Tile.ZombieSpawn: spawns++; break;
                    case Tile.ChestSpawn:
                    case Tile.Incubator:
                        if (!InsideBuilding(map, x, z)) markersOutside++;
                        break;
                }
            }
            if (players == 0) problems.Add("Error: no player spawn cell (P).");
            if (map.mission == "boss" && bosses == 0) problems.Add("Error: a boss stage needs a Main Boss cell (X).");
            if (map.mission == "rescue" && hostages == 0) problems.Add("Error: a rescue stage needs a hostage cell (H).");
            if (map.mission == "bomb" && targets == 0) problems.Add("Error: a bomb stage needs a target incubator cell (B).");
            if (map.mission == "vaccine" && vaccines == 0) problems.Add("Error: a vaccine stage needs a Vaccine cell (V).");
            if (map.mission == "vaccine" && !HasCell(map, Tile.RivalSpawn)) problems.Add("Warning: no rival team start cell (E): the rival team won't appear.");
            if (targets > 1) problems.Add($"Warning: {targets} target incubator cells; only one is used.");
            if (vaccines > 1) problems.Add($"Warning: {vaccines} Vaccine cells; only one is used.");
            if (map.spawnArea == null || map.spawnArea.w <= 0 || map.spawnArea.d <= 0) problems.Add("Error: no Spawn Area rectangle (the start / rescue goal).");
            if (players > 1) problems.Add($"Warning: {players} player spawn cells; only one is used.");
            if (bosses > 1) problems.Add($"Warning: {bosses} Main Boss cells; only one is used.");
            if (hostages > 1) problems.Add($"Warning: {hostages} hostage cells; only one is used.");
            if (loot == 0) problems.Add("Warning: no loot spawn cells (l) — no loot will appear.");
            var items = GameTuning.Load().loot;
            var wanted = items.longGuns + items.ammoBoxes + items.healthItems + items.grenades;
            if (loot > 0 && loot < wanted) problems.Add($"Warning: {loot} loot spawn cells for {wanted} items — {wanted - loot} items won't appear.");
            if (spawns == 0) problems.Add("Warning: no zombie spawn cells (z) — dynamic spawns use any free ground.");
            if (map.mission == "boss" && (map.bossArea == null || map.bossArea.w <= 0)) problems.Add("Warning: no Boss Area rectangle.");
            if (map.mission != "boss" && bosses > 0) problems.Add("Warning: a Main Boss cell (X) in a stage whose mission isn't the boss: it is ignored; use sub-boss cells (S).");
            if (markersOutside > 0) problems.Add($"Warning: {markersOutside} chest/incubator cells are outside every building rectangle and will be ignored.");
            foreach (var b in map.buildings)
            {
                // A solid block (a guard post, no room inside) needs no doorway.
                if (b.w < 2 || b.d < 2) continue;
                var doors = 0;
                for (var x = b.x; x <= b.x + b.w; x++)
                for (var z = b.z; z <= b.z + b.d; z++)
                    if ((x == b.x || x == b.x + b.w || z == b.z || z == b.z + b.d) && !Tiles.BlocksMovement(map[x, z])) doors++;
                if (doors == 0) problems.Add($"Warning: building '{b.name}' has no doorway in its outline.");
            }
            foreach (var zombie in map.zombies)
            {
                if (!System.Enum.TryParse<ZombieType>(zombie.type, out var type) || ZombieTypes.IsBoss(type))
                    problems.Add($"Error: placed zombie at {zombie.x},{zombie.z} has an unknown type '{zombie.type}'.");
                else if (Tiles.BlocksMovement(map[zombie.x, zombie.z]))
                    problems.Add($"Warning: placed zombie at {zombie.x},{zombie.z} stands on a blocked cell.");
            }
            foreach (var a in map.areas)
            foreach (var placed in a.placed)
                if (!System.Enum.TryParse<ZombieType>(placed.type, out var type) || ZombieTypes.IsBoss(type))
                    problems.Add($"Error: area '{a.name}' places an unknown zombie type '{placed.type}'.");
            if (!string.IsNullOrEmpty(map.baseGround) && !BaseGround.Exists(map.baseGround))
                problems.Add($"Warning: base ground '{map.baseGround}' is unknown; the default is used.");
            void CheckPieces(List<MapDecoration> pieces, Art.ArtLibrary.Kind kind, string what)
            {
                if (pieces == null) return;
                var unknown = new HashSet<string>();
                var outside = 0;
                foreach (var d in pieces)
                {
                    if (!Art.PropLayer.Exists(d.id, kind)) unknown.Add(d.id);
                    if (d.x < 0f || d.z < 0f || d.x > map.width || d.z > map.height) outside++;
                }
                foreach (var id in unknown) problems.Add($"Warning: {what} '{id}' is not a built-in piece or a prefab, so it won't show.");
                if (outside > 0) problems.Add($"Warning: {outside} {what}(s) outside the map.");
            }
            CheckPieces(map.props, Art.ArtLibrary.Kind.Prop, "prop");
            CheckPieces(map.decorations, Art.ArtLibrary.Kind.Decoration, "decoration");
            // A prop on a marker hides it or makes it unreachable.
            if (map.props != null)
                foreach (var p in map.props)
                {
                    var piece = Art.ArtLibrary.Get(p.id);
                    if (piece == null || piece.Bush) continue;
                    var cell = map[Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)];
                    if (Tiles.IsMarker(cell)) problems.Add($"Warning: prop '{p.id}' at {p.x:0.#},{p.z:0.#} stands on a {cell} cell.");
                }
            problems.Sort((p, q) => q.StartsWith("Error").CompareTo(p.StartsWith("Error")));
            return problems;
        }

        private static bool HasCell(StageMap map, Tile tile)
        {
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == tile) return true;
            return false;
        }

        private static bool InsideBuilding(StageMap map, int x, int z)
        {
            foreach (var b in map.buildings)
                if (x >= b.x && x <= b.x + b.w && z >= b.z && z <= b.z + b.d) return true;
            return false;
        }
        // ------------------------------------------------------------------ old layout → map (one-off conversion)

        /// <summary>
        /// Rasterises a code-built layout into 1 m cells: thin walls become one cell wide, doorways stay open. Adds the
        /// markers the tile system uses (loot and zombie spawn points on a lattice) and some bushes and water
        /// (⚠ placeholder content).
        /// </summary>
        public static StageMap FromLayout(StageLayout s)
        {
            var map = StageMap.Blank(s.Name, (int)s.Size.x, (int)s.Size.y);
            map.difficulty = s.Difficulty;
            map.spawnArea = ToMapRect("Spawn Area", s.SpawnArea);
            map.bossArea = ToMapRect("Boss Area", s.BossArea);

            foreach (var b in s.Buildings)
            {
                map.buildings.Add(new MapBuilding
                {
                    name = b.Name, x = Mathf.RoundToInt(b.Bounds.x), z = Mathf.RoundToInt(b.Bounds.y),
                    w = Mathf.RoundToInt(b.Bounds.width), d = Mathf.RoundToInt(b.Bounds.height), height = b.Height,
                });
                for (var x = Mathf.RoundToInt(b.Bounds.xMin); x < Mathf.RoundToInt(b.Bounds.xMax); x++)
                for (var z = Mathf.RoundToInt(b.Bounds.yMin); z < Mathf.RoundToInt(b.Bounds.yMax); z++) map.SetSurface(x, z, Surface.Floor);
            }
            foreach (var block in s.Blocks)
            {
                var tile = block.Height >= 4.5f ? Tile.Cliff : block.Height >= 2.4f ? Tile.Wall : Tile.Cover;
                Rasterise(map, block.Center, block.Size, tile);
            }
            // D-075: natural terrain closes the edge.
            for (var x = 0; x < map.width; x++) map[x, 0] = map[x, map.height - 1] = Tile.Cliff;
            for (var z = 0; z < map.height; z++) map[0, z] = map[map.width - 1, z] = Tile.Cliff;

            PlaceholderNature(map);

            // Markers.
            Mark(map, s.PlayerSpawn, Tile.PlayerSpawn);
            Mark(map, s.BossPosition, Tile.BossSpawn);
            foreach (var p in s.Barrels) Mark(map, p, Tile.Barrel);
            foreach (var b in s.Buildings)
            {
                var c = b.Bounds.center;
                if (b.MayHaveChest) MarkNear(map, new Vector3(c.x, 0f, c.y), Tile.ChestSpawn, b.Bounds);
                if (b.Incubator) MarkNear(map, new Vector3(b.Bounds.x + b.Bounds.width * 0.3f, 0f, b.Bounds.y + b.Bounds.height * 0.7f), Tile.Incubator, b.Bounds);
            }
            var boss = s.BossArea;
            for (var z = 3; z < map.height - 2; z += 5)
            for (var x = 3; x < map.width - 2; x += 5)
            {
                var inBoss = boss.Contains(new Vector2(x + 0.5f, z + 0.5f));
                var t = map[x, z];
                if (inBoss || t != Tile.Ground) continue;
                map[x, z] = Tile.LootSpawn;
            }
            for (var z = 5; z < map.height - 2; z += 6)
            for (var x = 5; x < map.width - 2; x += 6)
            {
                var p = new Vector2(x + 0.5f, z + 0.5f);
                if (boss.Contains(p) || s.SpawnArea.Contains(p) || map[x, z] != Tile.Ground) continue;
                map[x, z] = Tile.ZombieSpawn;
            }

            foreach (var a in s.Areas)
            {
                if (a.Name == "Spawn Area" || a.Name == "Boss Area") continue;
                var area = new MapArea
                {
                    name = a.Name, x = Mathf.RoundToInt(a.Bounds.x), z = Mathf.RoundToInt(a.Bounds.y), w = Mathf.RoundToInt(a.Bounds.width),
                    d = Mathf.RoundToInt(a.Bounds.height), entryGroup = a.EntryGroup, refills = a.Refills, combatZone = a.CombatZone,
                };
                foreach (var (type, count) in a.Placed) area.placed.Add(new MapPlaced { type = type.ToString(), count = count });
                map.areas.Add(area);
            }
            map.Pack();
            return map;
        }

        /// <summary>⚠ Placeholder content made by Claude: a pond and some bush patches, away from the test areas.</summary>
        private static void PlaceholderNature(StageMap map)
        {
            FillIfGround(map, new Rect(35, 88, 3, 5), Tile.Bush);
            FillIfGround(map, new Rect(60, 22, 4, 3), Tile.Bush);
            FillIfGround(map, new Rect(83, 58, 2, 6), Tile.Bush);
            FillIfGround(map, new Rect(20, 30, 4, 3), Tile.Bush);
            FillIfGround(map, new Rect(74, 84, 5, 4), Tile.Bush);
            FillIfGround(map, new Rect(46, 96, 6, 3), Tile.Bush);
        }

        private static MapRect ToMapRect(string name, Rect r) => new MapRect
        {
            name = name, x = Mathf.RoundToInt(r.x), z = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), d = Mathf.RoundToInt(r.height),
        };

        private static void Fill(StageMap map, Rect r, Tile tile)
        {
            for (var x = Mathf.RoundToInt(r.xMin); x < Mathf.RoundToInt(r.xMax); x++)
            for (var z = Mathf.RoundToInt(r.yMin); z < Mathf.RoundToInt(r.yMax); z++) map[x, z] = tile;
        }

        private static void FillIfGround(StageMap map, Rect r, Tile tile)
        {
            for (var x = (int)r.xMin; x < (int)r.xMax; x++)
            for (var z = (int)r.yMin; z < (int)r.yMax; z++)
                if (map[x, z] == Tile.Ground) map[x, z] = tile;
        }

        /// <summary>Thin (under 1 m) sides snap to the one cell they sit in; longer sides take the cells whose centres they cover.</summary>
        private static void Rasterise(StageMap map, Vector2 center, Vector2 size, Tile tile)
        {
            Span(center.x, size.x, out var x0, out var x1);
            Span(center.y, size.y, out var z0, out var z1);
            for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++) map[x, z] = tile;
        }

        private static void Span(float center, float size, out int from, out int to)
        {
            if (size < 1f)
            {
                from = to = Mathf.FloorToInt(center);
                return;
            }
            from = Mathf.CeilToInt(center - size * 0.5f - 0.5f);
            to = Mathf.FloorToInt(center + size * 0.5f - 0.5f);
            if (to < from) to = from;
        }

        private static void Mark(StageMap map, Vector3 p, Tile tile) => map[Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z)] = tile;

        /// <summary>The nearest ground cell to <paramref name="p"/> inside <paramref name="bounds"/>.</summary>
        private static void MarkNear(StageMap map, Vector3 p, Tile tile, Rect bounds)
        {
            var best = float.MaxValue;
            int bx = -1, bz = -1;
            for (var x = (int)bounds.xMin + 1; x < (int)bounds.xMax; x++)
            for (var z = (int)bounds.yMin + 1; z < (int)bounds.yMax; z++)
            {
                var t = map[x, z];
                if (t != Tile.Ground) continue;
                var d = (StageMap.CellCenter(x, z) - p).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                bx = x;
                bz = z;
            }
            if (bx >= 0) map[bx, bz] = tile;
        }
    }

    /// <summary>The stage list file.</summary>
    [System.Serializable]
    public class StageIndex
    {
        public List<string> stages = new List<string>();
    }

    /// <summary>Where stages come from: JSON map files in <c>Resources/Stages</c>.</summary>
    public static class StageLibrary
    {
        public const string TestStageId = "test_stage";

        public const string IndexFile = "index";
        private static readonly string[] BuiltIn = { TestStageId, "test_rain", "test_dark", "test_rescue" };
        private static List<string> _ids;

        /// <summary>
        /// The stages offered in stage selection, in order (every stage open, D-184 replaced): read from
        /// <c>Resources/Stages/index.json</c>, which the Map Editor maintains.
        /// </summary>
        public static IReadOnlyList<string> Ids
        {
            get
            {
                if (_ids != null) return _ids;
                var asset = Resources.Load<TextAsset>($"Stages/{IndexFile}");
                _ids = asset != null ? JsonUtility.FromJson<StageIndex>(asset.text).stages : new List<string>(BuiltIn);
                return _ids;
            }
        }

        /// <summary>After the Map Editor changes stages or the list.</summary>
        public static void Invalidate()
        {
            _ids = null;
            Names.Clear();
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> Names = new System.Collections.Generic.Dictionary<string, string>();

        public static bool Exists(string id) => id == TestStageId || Resources.Load<TextAsset>($"Stages/{id}") != null;

        public static string DisplayName(string id)
        {
            if (Names.TryGetValue(id, out var name)) return name;
            var asset = Resources.Load<TextAsset>($"Stages/{id}");
            name = asset != null ? JsonUtility.FromJson<StageMap>(asset.text).name : id;
            Names[id] = name;
            return name;
        }

        public static StageLayout Load(string id)
        {
            var asset = Resources.Load<TextAsset>($"Stages/{id}");
            // The test stage can always be regenerated from its code source if the file is missing (e.g. in tests).
            var map = asset != null ? StageMap.FromJson(asset.text) : id == TestStageId ? StageMapBuilder.FromLayout(TestStage.Build()) : null;
            if (map == null) throw new System.ArgumentException($"No stage '{id}'");
            var layout = StageMapBuilder.ToLayout(map);
            layout.Id = id;
            return layout;
        }
    }
}
