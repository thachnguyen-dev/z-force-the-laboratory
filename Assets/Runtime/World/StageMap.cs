using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// What one grid cell means for gameplay (owner, 2026-09-30: tile-based maps in the style of Brawl Stars).
    /// Markers are walkable ground with a special meaning.
    /// </summary>
    public enum Tile : byte
    {
        Ground,
        /// <summary>Tall wall: blocks movement, sight and bullets.</summary>
        Wall,
        /// <summary>Map-edge terrain (D-075): like a wall, taller.</summary>
        Cliff,
        /// <summary>Low cover (crates, sandbags, cars): blocks movement, sight and bullets.</summary>
        Cover,
        /// <summary>Bush: walkable, hides who is inside (owner-approved rules, see <see cref="StageGrid"/>).</summary>
        Bush,
        LootSpawn,
        ZombieSpawn,
        PlayerSpawn,
        BossSpawn,
        Barrel,
        ChestSpawn,
        Incubator,
        /// <summary>Rescue stages: where the hostage waits (owner, 2026-09-30).</summary>
        Hostage,
        /// <summary>Bomb stages (owner, 2026-10-02): the incubator the C4 must be planted on.</summary>
        BombTarget,
        /// <summary>Vaccine stages (owner, 2026-10-02): where the Vaccine lies.</summary>
        Vaccine,
        /// <summary>Vaccine stages (owner, 2026-10-03): where the rival AI team starts.</summary>
        RivalSpawn,
        /// <summary>Owner, 2026-10-03: a secondary boss on the route (missions that aren't about the Main Boss).</summary>
        SubBossSpawn,
    }

    public static class Tiles
    {
        /// <summary>The characters used in a map file. One character per cell.</summary>
        public static readonly Dictionary<char, Tile> FromChar = new Dictionary<char, Tile>
        {
            ['.'] = Tile.Ground, ['#'] = Tile.Wall, ['^'] = Tile.Cliff, ['o'] = Tile.Cover, ['*'] = Tile.Bush,
            ['l'] = Tile.LootSpawn, ['z'] = Tile.ZombieSpawn, ['P'] = Tile.PlayerSpawn,
            ['X'] = Tile.BossSpawn, ['r'] = Tile.Barrel, ['c'] = Tile.ChestSpawn, ['n'] = Tile.Incubator, ['H'] = Tile.Hostage, ['B'] = Tile.BombTarget, ['V'] = Tile.Vaccine, ['E'] = Tile.RivalSpawn, ['S'] = Tile.SubBossSpawn,
        };

        private static Dictionary<Tile, char> _toChar;

        public static char ToChar(Tile tile)
        {
            if (_toChar == null)
            {
                _toChar = new Dictionary<Tile, char>();
                foreach (var pair in FromChar) _toChar[pair.Value] = pair.Key;
            }
            return _toChar[tile];
        }

        /// <summary>Walls, cliffs and cover stop characters, bullets and sight.</summary>
        public static bool IsSolid(Tile t) => t == Tile.Wall || t == Tile.Cliff || t == Tile.Cover;

        /// <summary>What characters can't walk into.</summary>
        public static bool BlocksMovement(Tile t) => IsSolid(t);

        public static bool BlocksSight(Tile t) => IsSolid(t);

        public static bool IsMarker(Tile t) => t >= Tile.LootSpawn;

        /// <summary>The 3D blocks of layer 3 (everything with a gameplay effect that has a body).</summary>
        public static bool IsBlock(Tile t) => t == Tile.Wall || t == Tile.Cliff || t == Tile.Cover || t == Tile.Bush;
    }

    /// <summary>
    /// Layer 2 (owner, 2026-10-01): flat 2D surfaces that make visual areas (grass, roads, building floors). They are
    /// decoration: no gameplay effect at all. Painted on their own grid, under the gameplay cells.
    /// </summary>
    public enum Surface : byte
    {
        None,
        Floor,
        Grass,
        Grass2,
        Road,
        Road2,
    }

    public static class Surfaces
    {
        public static readonly Dictionary<char, Surface> FromChar = new Dictionary<char, Surface>
        {
            ['.'] = Surface.None, ['_'] = Surface.Floor, [','] = Surface.Grass, [';'] = Surface.Grass2, ['='] = Surface.Road, [':'] = Surface.Road2,
        };

        private static Dictionary<Surface, char> _toChar;

        public static char ToChar(Surface surface)
        {
            if (_toChar == null)
            {
                _toChar = new Dictionary<Surface, char>();
                foreach (var pair in FromChar) _toChar[pair.Value] = pair.Key;
            }
            return _toChar[surface];
        }
    }

    [Serializable]
    public class MapRect
    {
        public string name;
        public int x, z, w, d;
        public Rect ToRect() => new Rect(x, z, w, d);
    }

    [Serializable]
    public class MapBuilding : MapRect
    {
        public float height = 3f;
    }

    [Serializable]
    public class MapPlaced
    {
        public string type;
        public int count;
    }

    /// <summary>One zombie placed on a specific cell (pre-placed, D-062).</summary>
    [Serializable]
    public class MapZombie
    {
        public string type = "Basic";
        public int x, z;
    }

    [Serializable]
    /// <summary>A free-placed piece: a prop (physical) or a decoration (looks only).</summary>
    public class MapDecoration
    {
        /// <summary>A built-in piece id (see ZombieGame.Art.ArtLibrary) or a prefab name in Resources/Props or Resources/Decorations.</summary>
        public string id;
        /// <summary>World position in metres (not snapped to cells).</summary>
        public float x, z;
        /// <summary>Turn around the vertical axis, degrees.</summary>
        public float rotation;
        public float scale = 1f;
        /// <summary>Mirrored (vehicles and objects only; buildings are never mirrored).</summary>
        public bool flip;
        /// <summary>Colour variant of a pack model: 0 = chosen from the position, 1 = base colours, 2 = "_v1"…</summary>
        public int variant;
        /// <summary>
        /// Pose of a pack model (2026-10-04: overturned, tilted, crashed cars): roll tips it over sideways around its
        /// length (90 = on its side, 180 = upside down), pitch tips the nose up or down. It always rests on the ground.
        /// </summary>
        public float roll, pitch;
    }

    [Serializable]
    public class MapArea : MapRect
    {
        public int entryGroup;
        public bool refills;
        public bool combatZone;
        public List<MapPlaced> placed = new List<MapPlaced>();
    }

    /// <summary>
    /// A stage as lightweight data (owner, 2026-09-30): a character grid plus a few rectangles. Stored as JSON in
    /// <c>Resources/Stages</c>; the loader assembles tile prefabs at each cell's X, Z. Row 0 is the NORTH edge (the top,
    /// as you read the file); column 0 is the west edge. Cell (col, row) covers x = col..col+1, z = (height-1-row)..
    /// </summary>
    [Serializable]
    public class StageMap
    {
        public int version = 1;
        public string name = "Stage";
        public int width;
        public int height;
        public float difficulty = 1f;
        /// <summary>Stage conditions (owner, 2026-09-30): "clear" or "rain"; darkness; the mission: "boss", "rescue", "bomb" or "vaccine".</summary>
        public string weather = "clear";
        public bool dark;
        public string mission = "boss";
        /// <summary>The tutorial stage (owner, 2026-10-07): instructions step by step, only the zombies each step brings.</summary>
        public bool tutorial;
        /// <summary>Endless mode's map (owner, 2026-10-07): no zombies at the start; they come in waves through the fence's gaps (the 'z' cells).</summary>
        public bool endless;
        /// <summary>
        /// Owner, 2026-10-07: which zombie type the stage's Main Boss and its secondary bosses are ("Boss" = boss_1,
        /// "Bear", "Tiger"; "SubBoss" = the placeholder secondary boss).
        /// </summary>
        public string bossType = "Boss";
        public string subBossType = "SubBoss";
        /// <summary>How much loot appears (owner, 2026-10-02: the Vaccine stage has 30 %). 1 = normal.</summary>
        public float lootScale = 1f;
        public List<string> rows = new List<string>();
        /// <summary>
        /// Layer 2, the surfaces (owner, 2026-10-01): one row per map row like <see cref="rows"/>, using the
        /// <see cref="Surfaces"/> characters. Looks only.
        /// </summary>
        public List<string> surface = new List<string>();
        public List<MapBuilding> buildings = new List<MapBuilding>();
        public List<MapArea> areas = new List<MapArea>();
        public MapRect spawnArea = new MapRect();
        public MapRect bossArea = new MapRect();
        /// <summary>Zombies placed on exact cells (in addition to each area's placed counts).</summary>
        public List<MapZombie> zombies = new List<MapZombie>();
        /// <summary>Optional tile set for this stage (Resources/TileSets/&lt;name&gt;); empty = the default tile set.</summary>
        public string tileSet = "";
        /// <summary>
        /// The base ground (owner, 2026-10-01: bottom layer, simple and mostly uniform, may differ per stage): a
        /// <see cref="BaseGround"/> preset id. Everything else sits on top of it.
        /// </summary>
        public string baseGround = BaseGround.DefaultId;
        /// <summary>
        /// The look of the map's walls, fences and edge (2026-10-03): "town", "lab", "checkpoint" or "reserve" (empty =
        /// lab). Looks only.
        /// </summary>
        public string theme = "";
        /// <summary>
        /// Props (owner, 2026-10-02: maps no longer have to follow the grid): physical objects placed freely, at any spot,
        /// turn and size, that block movement, bullets and sight (bush props hide whoever is inside instead).
        /// </summary>
        public List<MapDecoration> props = new List<MapDecoration>();
        /// <summary>Decorative details (owner, 2026-10-01): free-placed, no gameplay effect.</summary>
        public List<MapDecoration> decorations = new List<MapDecoration>();

        [NonSerialized] private Tile[,] _cells;
        [NonSerialized] private Surface[,] _surface;

        public static StageMap FromJson(string json)
        {
            var map = JsonUtility.FromJson<StageMap>(json);
            map.Parse();
            return map;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static StageMap Blank(string name, int width, int height)
        {
            var map = new StageMap { name = name, width = width, height = height };
            for (var r = 0; r < height; r++) map.rows.Add(new string('.', width));
            map.Parse();
            return map;
        }

        public void Parse()
        {
            _cells = new Tile[width, height];
            _surface = new Surface[width, height];
            var converted = false;
            for (var r = 0; r < height; r++)
            {
                var row = r < rows.Count ? rows[r] : "";
                var surfaceRow = surface != null && r < surface.Count ? surface[r] : "";
                for (var c = 0; c < width; c++)
                {
                    int x = c, z = height - 1 - r;
                    var ch = c < row.Length ? row[c] : '.';
                    if (Tiles.FromChar.TryGetValue(ch, out var t)) _cells[x, z] = t;
                    else
                    {
                        // Older files (before 2026-10-01): floors, grass and roads were cells, and '~' was water (removed).
                        _cells[x, z] = Tile.Ground;
                        if (Surfaces.FromChar.TryGetValue(ch, out var old) && old != Surface.None)
                        {
                            _surface[x, z] = old;
                            converted = true;
                        }
                    }
                    if (c < surfaceRow.Length && Surfaces.FromChar.TryGetValue(surfaceRow[c], out var s) && s != Surface.None) _surface[x, z] = s;
                }
            }
            if (converted) FillMarkerSurfaces();
        }

        /// <summary>Converted files: a marker cell takes the surface most of its neighbours have, so floors stay whole.</summary>
        private void FillMarkerSurfaces()
        {
            var counts = new int[6];
            for (var z = 0; z < height; z++)
            for (var x = 0; x < width; x++)
            {
                if (!Tiles.IsMarker(_cells[x, z]) || _surface[x, z] != Surface.None) continue;
                System.Array.Clear(counts, 0, counts.Length);
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= width || nz >= height || Tiles.IsMarker(_cells[nx, nz])) continue;
                    counts[(int)_surface[nx, nz]]++;
                }
                var best = 0;
                for (var i = 1; i < counts.Length; i++)
                    if (counts[i] > counts[best]) best = i;
                _surface[x, z] = (Surface)best;
            }
        }

        /// <summary>Writes the cells back into <see cref="rows"/> and <see cref="surface"/> (before saving).</summary>
        public void Pack()
        {
            rows.Clear();
            surface = new List<string>();
            var chars = new char[width];
            var surfaceChars = new char[width];
            var anySurface = false;
            for (var r = 0; r < height; r++)
            {
                for (var c = 0; c < width; c++)
                {
                    chars[c] = Tiles.ToChar(_cells[c, height - 1 - r]);
                    surfaceChars[c] = Surfaces.ToChar(_surface[c, height - 1 - r]);
                    anySurface |= _surface[c, height - 1 - r] != Surface.None;
                }
                rows.Add(new string(chars));
                surface.Add(new string(surfaceChars));
            }
            if (!anySurface) surface.Clear();
        }

        /// <summary>Layer 2 at a cell (looks only). Outside = none.</summary>
        public Surface SurfaceAt(int x, int z) => x < 0 || z < 0 || x >= width || z >= height ? Surface.None : _surface[x, z];

        public void SetSurface(int x, int z, Surface value)
        {
            if (x >= 0 && z >= 0 && x < width && z < height) _surface[x, z] = value;
        }

        /// <summary>Cell at column <paramref name="x"/>, world-row <paramref name="z"/> (z grows north). Outside = cliff.</summary>
        public Tile this[int x, int z]
        {
            get => x < 0 || z < 0 || x >= width || z >= height ? Tile.Cliff : _cells[x, z];
            set
            {
                if (x >= 0 && z >= 0 && x < width && z < height) _cells[x, z] = value;
            }
        }

        public Tile At(Vector3 world) => this[Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z)];

        public static Vector3 CellCenter(int x, int z) => new Vector3(x + 0.5f, 0f, z + 0.5f);
    }
}
