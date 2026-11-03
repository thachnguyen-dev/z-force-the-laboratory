using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// ⚠ Claude's placeholder story (owner, 2026-10-03: the main story, the Vaccine mission last; six missions for now,
    /// more later), each mission with two generated maps. Writes the stage files, <c>campaign.json</c> and puts the story
    /// stages first in the stage list. It only ever writes the story stage ids below, never other stages.
    /// </summary>
    public static class StoryStages
    {
        private class Def
        {
            public string Id, Title, Tag, Story, Theme, Mission, Weather = "clear", Ground = BaseGround.DefaultId;
            public bool Dark;
            public float Loot = 1f, Difficulty = 1f;
            public int RivalTeams = 1;
            public (string name, int seed, int corner, int w, int h)[] Maps;
        }

        /// <summary>
        /// The final stage (owner, 2026-10-07): Vaccine Collection, on a map larger than every earlier one, at night, with many
        /// zombies and two rival AI teams besides the players. "6. The Vaccine" moved to just before it.
        /// </summary>
        private static readonly Def Finale = new Def
        {
            Id = "m7_collection", Title = "7. Vaccine Collection", Tag = "Vaccine · Town · Night rain · 2 rival teams · Final", Theme = "town",
            Mission = "vaccine", Dark = true, Weather = "rain", Loot = 0.4f, Difficulty = 1.7f, Ground = "ash", RivalTeams = 2,
            Story = "The last vaccine stock is in a lab at the far side of the city. A storm has broken over it, and the night is " +
                    "crawling with the infected. Two " +
                    "rival teams are racing you for it, and they'll fight each other as readily as they fight you. Get the vaccine " +
                    "and bring it back to the start - if a rival team takes it, you have 60 seconds to win it back.",
            Maps = new[] { ("Vaccine Collection - Old City", 7101, 0, 272, 272), ("Vaccine Collection - Harbour District", 7102, 2, 264, 280) },
        };

        private static readonly Def[] Missions =
        {
            new Def
            {
                Id = "m1_gate", Title = "1. Quarantine Gate", Tag = "Boss · Checkpoint", Theme = "checkpoint", Mission = "boss", Difficulty = 1f,
                Story = "The government has sealed the town. Your special forces team goes in through the military checkpoint, where the " +
                        "first security troops fell to the virus. Something huge now leads them. Kill it and open the way into town.",
                Maps = new[] { ("Quarantine Gate - North Post", 1101, 0, 112, 112), ("Quarantine Gate - Supply Road", 1102, 1, 120, 104) },
            },
            new Def
            {
                Id = "m2_scientist", Title = "2. The First Scientist", Tag = "Rescue · Town", Theme = "town", Mission = "rescue", Difficulty = 1.05f, Ground = "ash",
                Story = "A scientist hiding in the town clinic is still answering the radio. Reach her, get her moving, and bring her back " +
                        "to your starting point. Once she follows you, every zombie in town will come for you.",
                Maps = new[] { ("The First Scientist - Old Town", 2101, 3, 168, 168), ("The First Scientist - Market Streets", 2102, 2, 174, 168) },
            },
            new Def
            {
                Id = "m3_breeding", Title = "3. Breeding Lab", Tag = "Bomb · Lab compound", Theme = "lab", Mission = "bomb", Difficulty = 1.15f,
                Story = "The virus first spread among the lab animals - rats and rabbits. Their breeding hive still pours out the infected. " +
                        "Plant the C4 on it, hold out against the swarm until it blows, then get back to the start.",
                Maps = new[] { ("Breeding Lab - Animal Wing", 3101, 1, 168, 168), ("Breeding Lab - Holding Pens", 3102, 0, 168, 180) },
            },
            new Def
            {
                Id = "m4_reserve", Title = "4. Wildlife Reserve", Tag = "Boss · Reserve · Rain", Theme = "reserve", Mission = "boss", Weather = "rain",
                Difficulty = 1.2f, Ground = "moss",
                Story = "The infection reached the large animals of the wildlife reserve. In the rain the four-legged ones hunt in packs and " +
                        "call the others. Find what leads them and put it down.",
                Maps = new[] { ("Wildlife Reserve - Enclosures", 4101, 2, 186, 168), ("Wildlife Reserve - Deep Woods", 4102, 3, 168, 186) },
            },
            new Def
            {
                Id = "m5_wing", Title = "5. Research Wing", Tag = "Rescue · Lab · Night", Theme = "lab", Mission = "rescue", Dark = true, Difficulty = 1.3f,
                Story = "Night. The last scientists are trapped in the research wing, where the virus was made. Get one of them out alive - " +
                        "they know where the vaccine is kept.",
                Maps = new[] { ("Research Wing - East Labs", 5101, 0, 168, 168), ("Research Wing - Cold Storage", 5102, 2, 174, 168) },
            },
            new Def
            {
                Id = "m6_vaccine", Title = "6. The Vaccine", Tag = "Vaccine · Lab · Night", Theme = "lab", Mission = "vaccine", Dark = true,
                Loot = 0.3f, Difficulty = 1.4f, Ground = "ash",
                Story = "The vaccine is in the central lab. Supplies are scarce, and a rival team is after it too. Get the vaccine and " +
                        "bring it back to the start - if they take it, you have 60 seconds to win it back.",
                Maps = new[] { ("The Vaccine - Central Lab", 6101, 1, 232, 232), ("The Vaccine - Sub-Level", 6102, 3, 224, 240) },
            },
            Finale,
        };

        /// <summary>Endless mode's map (owner, 2026-10-07): as large as the largest, fenced, with gaps for the zombies.</summary>
        private const int EndlessSize = 272;
        /// <summary>Gaps in the fence on each side, and how wide each is (cells).</summary>
        private const int GapsPerSide = 3, GapWidth = 4;

        [MenuItem("Zombie/Developer (Claude)/Generate Story Stages (overwrites them)")]
        public static void GenerateAll()
        {
            var data = new CampaignData();
            var ids = new List<string>();
            foreach (var m in Missions)
            {
                var mission = new CampaignMission { id = m.Id, title = m.Title, tag = m.Tag, story = m.Story };
                for (var i = 0; i < m.Maps.Length; i++)
                {
                    var (name, seed, corner, w, h) = m.Maps[i];
                    var id = $"{m.Id}_{(char)('a' + i)}";
                    var map = StageForge.Build(new StageForge.Recipe
                    {
                        Id = id, Name = name, Theme = m.Theme, Mission = m.Mission, Seed = seed, Corner = corner, Width = w, Height = h,
                        Weather = m.Weather, Dark = m.Dark, LootScale = m.Loot, Difficulty = m.Difficulty, BaseGround = m.Ground,
                        RivalTeams = m.RivalTeams,
                    });
                    StageFiles.Save(id, map);
                    var problems = StageMapBuilder.Validate(map);
                    var unreachable = StageForge.Unreachable(map);
                    Debug.Log($"[ZOMBIE-STORY] {id} '{name}': {map.props.Count} props, {map.decorations.Count} decorations, " +
                              $"{problems.Count} checks{(problems.Count > 0 ? ": " + string.Join(" | ", problems) : "")}, unreachable {unreachable.Count}");
                    Preview(map, $"{id}.png");
                    mission.stages.Add(id);
                    ids.Add(id);
                }
                data.missions.Add(mission);
            }
            var path = $"{TileSetup.StagesFolder}/{Campaign.File}.json";
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(path);
            // Story stages first in the stage list; everything else keeps its order after them.
            var index = StageFiles.ReadIndex();
            index.RemoveAll(ids.Contains);
            index.InsertRange(0, ids);
            StageFiles.WriteIndex(index);
            Campaign.Invalidate();
        }

        /// <summary>
        /// Only the final mission and Endless mode's map (the earlier stages have hand edits a full regeneration would undo):
        /// the final mission goes last in the campaign, after "6. The Vaccine".
        /// </summary>
        [MenuItem("Zombie/Developer (Claude)/Generate Final Stage and Endless Map")]
        public static void GenerateFinale()
        {
            var ids = new List<string>();
            var mission = new CampaignMission { id = Finale.Id, title = Finale.Title, tag = Finale.Tag, story = Finale.Story };
            for (var i = 0; i < Finale.Maps.Length; i++)
            {
                var (name, seed, corner, w, h) = Finale.Maps[i];
                var id = $"{Finale.Id}_{(char)('a' + i)}";
                var map = StageForge.Build(new StageForge.Recipe
                {
                    Id = id, Name = name, Theme = Finale.Theme, Mission = Finale.Mission, Seed = seed, Corner = corner, Width = w, Height = h,
                    Weather = Finale.Weather, Dark = Finale.Dark, LootScale = Finale.Loot, Difficulty = Finale.Difficulty, BaseGround = Finale.Ground,
                    RivalTeams = Finale.RivalTeams,
                });
                Save(id, map, name);
                mission.stages.Add(id);
                ids.Add(id);
            }
            var endless = BuildEndless();
            Save(Endless.StageId, endless, endless.name);
            ids.Add(Endless.StageId);

            var path = $"{TileSetup.StagesFolder}/{Campaign.File}.json";
            var data = JsonUtility.FromJson<CampaignData>(File.ReadAllText(path));
            data.missions.RemoveAll(m => m.id == Finale.Id);
            var old = data.missions.Find(m => m.id == "m6_vaccine");
            if (old != null) old.tag = "Vaccine · Lab · Night";
            data.missions.Add(mission);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(path);
            // After the story stages already in the list.
            var index = StageFiles.ReadIndex();
            index.RemoveAll(ids.Contains);
            var after = index.FindLastIndex(id => id.StartsWith("m6_"));
            index.InsertRange(after + 1, ids);
            StageFiles.WriteIndex(index);
            Campaign.Invalidate();
        }

        private static void Save(string id, StageMap map, string name)
        {
            StageFiles.Save(id, map);
            var problems = StageMapBuilder.Validate(map);
            var unreachable = StageForge.Unreachable(map);
            Debug.Log($"[ZOMBIE-STORY] {id} '{name}' {map.width}×{map.height}: {map.props.Count} props, {map.decorations.Count} decorations, " +
                      $"{problems.Count} checks{(problems.Count > 0 ? ": " + string.Join(" | ", problems) : "")}, unreachable {unreachable.Count}");
            Preview(map, $"{id}.png");
        }

        /// <summary>
        /// Endless mode's map (owner, 2026-10-07): a town as large as the largest map, full of equipment; no zombies placed and
        /// no objective. The fence round the edge has gaps, a few per side, and the waves come in through them (the 'z' cells
        /// in each gap). The ground just inside each gap is cleared so nothing blocks the way in.
        /// </summary>
        private static StageMap BuildEndless()
        {
            var map = StageForge.Build(new StageForge.Recipe
            {
                Id = Endless.StageId, Name = "Endless - Fenced City", Theme = "town", Mission = "endless", Seed = 9101, Corner = 0,
                Width = EndlessSize, Height = EndlessSize, LootScale = 1f, Difficulty = 1f,
            });
            map.endless = true;
            map.areas.Clear();
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
            {
                var t = map[x, z];
                if (t == Tile.ZombieSpawn || t == Tile.BossSpawn || t == Tile.SubBossSpawn) map[x, z] = Tile.Ground;
            }
            const int clear = 6;
            void Gap(int x0, int z0, int dx, int dz, int inX, int inZ)
            {
                // Along the edge (dx, dz) for GapWidth cells; inward (inX, inZ) for the cleared depth.
                for (var a = 0; a < GapWidth; a++)
                for (var d = 0; d < clear; d++)
                {
                    var x = x0 + dx * a + inX * d;
                    var z = z0 + dz * a + inZ * d;
                    if (x < 0 || z < 0 || x >= map.width || z >= map.height) continue;
                    var t = map[x, z];
                    if (t == Tile.Cliff || t == Tile.Wall || t == Tile.Cover || t == Tile.Barrel) map[x, z] = Tile.Ground;
                    if (d == 0 && (a == 1 || a == 2)) map[x, z] = Tile.ZombieSpawn;
                }
                map.props.RemoveAll(p =>
                {
                    var cx = x0 + dx * GapWidth * 0.5f + inX * clear * 0.5f;
                    var cz = z0 + dz * GapWidth * 0.5f + inZ * clear * 0.5f;
                    return Mathf.Abs(p.x - cx) < GapWidth + 1f && Mathf.Abs(p.z - cz) < clear + 1f || Mathf.Abs(p.x - cx) < clear + 1f && Mathf.Abs(p.z - cz) < GapWidth + 1f;
                });
            }
            for (var i = 0; i < GapsPerSide; i++)
            {
                // Evenly along each side, clear of the corners.
                var at = Mathf.RoundToInt(map.width * (i + 1f) / (GapsPerSide + 1f)) - GapWidth / 2;
                Gap(at, 0, 1, 0, 0, 1);
                Gap(at, map.height - 1, 1, 0, 0, -1);
                Gap(0, at, 0, 1, 1, 0);
                Gap(map.width - 1, at, 0, 1, -1, 0);
            }
            return map;
        }

        /// <summary>A picture of a map (one pixel per cell, 4× scaled) for checking layouts: logs/maps/&lt;id&gt;.png.</summary>
        public static void Preview(StageMap map, string file)
        {
            const int k = 4;
            var t = new Texture2D(map.width * k, map.height * k, TextureFormat.RGBA32, false);
            var px = new Color32[t.width * t.height];
            Color32 ColourOf(int x, int z)
            {
                switch (map[x, z])
                {
                    case Tile.Wall: return new Color32(235, 235, 235, 255);
                    case Tile.Cliff: return new Color32(90, 70, 55, 255);
                    case Tile.Cover: return new Color32(190, 150, 90, 255);
                    case Tile.Bush: return new Color32(40, 150, 50, 255);
                    case Tile.PlayerSpawn: return new Color32(60, 140, 255, 255);
                    case Tile.BossSpawn: return new Color32(255, 30, 80, 255);
                    case Tile.SubBossSpawn: return new Color32(255, 120, 20, 255);
                    case Tile.Hostage: return new Color32(255, 255, 255, 255);
                    case Tile.BombTarget: return new Color32(255, 40, 20, 255);
                    case Tile.Vaccine: return new Color32(80, 255, 120, 255);
                    case Tile.RivalSpawn: return new Color32(220, 40, 160, 255);
                    case Tile.ZombieSpawn: return new Color32(150, 40, 40, 255);
                    case Tile.LootSpawn: return new Color32(255, 220, 60, 255);
                    case Tile.ChestSpawn: return new Color32(150, 230, 100, 255);
                    case Tile.Incubator: return new Color32(170, 70, 200, 255);
                    case Tile.Barrel: return new Color32(255, 100, 30, 255);
                }
                switch (map.SurfaceAt(x, z))
                {
                    case Surface.Road: return new Color32(120, 120, 115, 255);
                    case Surface.Road2: return new Color32(130, 100, 70, 255);
                    case Surface.Grass: return new Color32(70, 120, 55, 255);
                    case Surface.Grass2: return new Color32(95, 115, 60, 255);
                    case Surface.Floor: return new Color32(80, 80, 95, 255);
                }
                return new Color32(55, 58, 52, 255);
            }
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
            {
                var col = ColourOf(x, z);
                for (var i = 0; i < k; i++)
                for (var j = 0; j < k; j++) px[(z * k + j) * t.width + x * k + i] = col;
            }
            foreach (var p in map.props)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                var col = piece != null && piece.Bush ? new Color32(30, 200, 60, 255) : new Color32(20, 20, 20, 255);
                var cx = Mathf.RoundToInt(p.x * k);
                var cz = Mathf.RoundToInt(p.z * k);
                for (var i = -2; i <= 2; i++)
                for (var j = -2; j <= 2; j++)
                {
                    var x = cx + i;
                    var z = cz + j;
                    if (x >= 0 && z >= 0 && x < t.width && z < t.height) px[z * t.width + x] = col;
                }
            }
            t.SetPixels32(px);
            t.Apply();
            var folder = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "logs", "maps");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, file), t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
