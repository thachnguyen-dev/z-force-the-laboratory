using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>A box of terrain or obstacle. Centre and size on the ground plane (x, z), height in metres.</summary>
    public struct Block
    {
        public Vector2 Center;
        public Vector2 Size;
        public float Height;
        public Color Color;

        public Block(float x, float z, float width, float depth, float height, Color color)
        {
            Center = new Vector2(x, z);
            Size = new Vector2(width, depth);
            Height = height;
            Color = color;
        }
    }

    public class AreaDef
    {
        public string Name;
        public Rect Bounds;
        public bool CombatZone;
        public bool Refills;
        /// <summary>Zombies spawned the first time a survivor enters (D-080).</summary>
        public int EntryGroup;
        /// <summary>Pre-placed zombies (D-062). Positions are fixed per stage (chosen by the builder with a fixed seed).</summary>
        public readonly List<(ZombieType type, int count)> Placed = new List<(ZombieType, int)>();
        /// <summary>Filled by the builder: free ground points inside the area.</summary>
        public readonly List<Vector3> FreePoints = new List<Vector3>();
    }

    /// <summary>A hand-designed stage (D-070): terrain, areas, loot points, zombies and the Boss Area.</summary>
    public class StageLayout
    {
        public string Name;
        public Vector2 Size;
        /// <summary>D-186: difficulty rises by stage — multiplies spawn counts only (D-187).</summary>
        public float Difficulty = 1f;
        public Vector3 PlayerSpawn;
        public Rect SpawnArea;
        public Rect BossArea;
        public Vector3 BossPosition;
        public readonly List<Block> Blocks = new List<Block>();
        public readonly List<AreaDef> Areas = new List<AreaDef>();
        public readonly List<Vector3> Barrels = new List<Vector3>();
        public readonly List<BuildingDef> Buildings = new List<BuildingDef>();
        /// <summary>The map this layout was built from (tile stages); null for a code-built layout.</summary>
        public StageMap Map;
        /// <summary>Loot spawn cells (D-205: random placement among valid locations).</summary>
        public readonly List<Vector3> LootPoints = new List<Vector3>();
        /// <summary>Enemy spawn cells for dynamic spawning (D-080).</summary>
        public readonly List<Vector3> ZombieSpawnPoints = new List<Vector3>();
        /// <summary>The stage file id (e.g. "test_stage"): how every device loads the same stage.</summary>
        public string Id;
        /// <summary>Stage conditions (owner, 2026-09-30).</summary>
        public bool Rain;
        public bool Dark;
        public bool Rescue;
        /// <summary>The mission: "boss", "rescue", "bomb" or "vaccine".</summary>
        public string Mission = "boss";
        /// <summary>The tutorial stage (Tutorial): no random spawning; each step brings its own zombies.</summary>
        public bool Tutorial;
        /// <summary>Endless mode (Endless): waves through the fence's gaps instead of the usual spawning.</summary>
        public bool Endless;
        /// <summary>The Main Boss's and the secondary bosses' zombie types (owner, 2026-10-07).</summary>
        public ZombieType BossType = ZombieType.Boss;
        public ZombieType SubBossType = ZombieType.SubBoss;
        public bool Bomb => Mission == "bomb";
        public bool VaccineMission => Mission == "vaccine";
        public Vector3 BombTargetPosition;
        public Vector3 VaccinePosition;
        /// <summary>Vaccine stages: one rival AI team starts on each of these (owner, 2026-10-07: the final stage has two).</summary>
        public readonly List<Vector3> RivalSpawns = new List<Vector3>();
        /// <summary>The map has a Main Boss cell (X).</summary>
        public bool HasBoss;
        public readonly List<Vector3> SubBossPositions = new List<Vector3>();
        public float LootScale = 1f;
        public Vector3 HostagePosition;
        /// <summary>Zombies placed on exact cells by the map.</summary>
        public readonly List<(ZombieType type, Vector3 position)> PlacedZombies = new List<(ZombieType, Vector3)>();
        /// <summary>The map's own tile set name, or empty for the default.</summary>
        public string TileSetName;
        /// <summary>The base ground preset (<see cref="BaseGround"/>).</summary>
        public string BaseGroundId = BaseGround.DefaultId;

        /// <summary>Adds an enterable building; its walls become obstacle blocks.</summary>
        public BuildingDef AddBuilding(BuildingDef building)
        {
            Buildings.Add(building);
            Blocks.AddRange(building.WallBlocks());
            return building;
        }

        public AreaDef AreaAt(Vector3 position)
        {
            var p = new Vector2(position.x, position.z);
            foreach (var area in Areas) if (area.Bounds.Contains(p)) return area;
            return null;
        }
    }
}
