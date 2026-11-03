using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// ⚠ Placeholder tile prefabs (simple boxes) and the tile set that maps cell types to them. Replace any prefab — or
    /// point the tile set at new ones — to reskin every stage without touching the maps.
    /// </summary>
    public static class TileSetup
    {
        private const string Folder = "Assets/Zombie/Placeholder/Tiles";
        private const string TileSetPath = "Assets/Zombie/Resources/" + TileSet.ResourceName + ".asset";
        public const string StagesFolder = "Assets/Zombie/Resources/Stages";

        /// <summary>
        /// The map art (owner, 2026-10-02: Claude makes the map graphics): the two materials, the painted ground textures,
        /// and an empty tile set (empty slots use the built-in block art; put prefabs in them to override).
        /// </summary>
        public static void BuildTiles()
        {
            var set = AssetDatabase.LoadAssetAtPath<TileSet>(TileSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<TileSet>();
                AssetDatabase.CreateAsset(set, TileSetPath);
            }
            // The old greybox boxes are no longer used: their slots go back to the built-in art.
            GameObject Keep(GameObject prefab) => prefab != null && AssetDatabase.GetAssetPath(prefab).StartsWith(Folder) ? null : prefab;
            set.wall = Keep(set.wall);
            set.cliff = Keep(set.cliff);
            set.cover = Keep(set.cover);
            set.bush = Keep(set.bush);
            EditorUtility.SetDirty(set);
            Material(Art.ArtMaterials.StylizedResource, "Zombie/Stylized");
            Material(Art.ArtMaterials.GroundResource, "Zombie/Ground");
            TextureForge.PaintAll();
            // The Stylized material's wear texture (cracks, rust, grime, chips; 2026-10-04).
            var stylized = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Zombie/Resources/{Art.ArtMaterials.StylizedResource}.mat");
            var wear = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureForge.Folder}wear.png");
            var camo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureForge.Folder}camo.png");
            if (stylized != null && camo != null && stylized.HasProperty("_CamoTex") && stylized.GetTexture("_CamoTex") != camo)
            {
                stylized.SetTexture("_CamoTex", camo);
                EditorUtility.SetDirty(stylized);
            }
            if (stylized != null && wear != null && stylized.HasProperty("_WearTex") && stylized.GetTexture("_WearTex") != wear)
            {
                stylized.SetTexture("_WearTex", wear);
                EditorUtility.SetDirty(stylized);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Material(string resource, string shaderName)
        {
            var path = $"Assets/Zombie/Resources/{resource}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader {shaderName} not found (compile error?)");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) AssetDatabase.CreateAsset(new Material(shader) { name = resource }, path);
            else if (material.shader != shader) material.shader = shader;
        }

        /// <summary>The test stage's map file, generated once from its code source. After that the file is the source.</summary>
        public static void EnsureTestStageMap()
        {
            var path = $"{StagesFolder}/{StageLibrary.TestStageId}.json";
            if (File.Exists(path))
            {
                // Writes only the variants that are missing; existing files are never overwritten here.
                WriteVariants(StageMap.FromJson(File.ReadAllText(path)), overwrite: false);
                return;
            }
            WriteTestStageMap(path);
        }

        [MenuItem("Zombie/Developer (Claude)/Regenerate Test Stage Map (overwrites edits)")]
        public static void RegenerateTestStageMap() => WriteTestStageMap($"{StagesFolder}/{StageLibrary.TestStageId}.json");


        /// <summary>
        /// ⚠ Placeholder variants of the test stage, one per stage condition (owner, 2026-09-30): rain, darkness, and a
        /// rescue mission with the hostage waiting in the Checkpoint barracks.
        /// </summary>
        private static void WriteVariants(StageMap baseMap, bool overwrite = true)
        {
            void Variant(string id, string name, System.Action<StageMap> change)
            {
                var path = $"{StagesFolder}/{id}.json";
                if (!overwrite && File.Exists(path)) return;
                var map = StageMap.FromJson(baseMap.ToJson());
                map.name = name;
                change(map);
                // Owner, 2026-10-03: only boss missions keep the Main Boss and its area.
                if (map.mission != "boss")
                {
                    for (var x = 0; x < map.width; x++)
                    for (var z = 0; z < map.height; z++)
                        if (map[x, z] == Tile.BossSpawn) map[x, z] = Tile.Ground;
                    map.bossArea = new MapRect { name = "Boss Area" };
                    map[47, 40] = Tile.SubBossSpawn;
                    map[86, 62] = Tile.SubBossSpawn;
                }
                map.Pack();
                File.WriteAllText(path, map.ToJson());
                AssetDatabase.ImportAsset(path);
                Debug.Log($"[ZOMBIE-SETUP] Wrote {path}");
                // A new stage joins the stage list (an existing list is otherwise left as the owner arranged it).
                var index = StageFiles.ReadIndex();
                if (index.Count > 0 && !index.Contains(id))
                {
                    index.Add(id);
                    StageFiles.WriteIndex(index);
                }
            }
            Variant("test_rain", "Test Stage - Rain", m =>
            {
                m.weather = "rain";
                // Owner, 2026-09-30: the four-legged Dog appears in Rain stages.
                foreach (var area in m.areas)
                    if (area.name == "Warehouse" || area.name == "Town") area.placed.Add(new MapPlaced { type = "Dog", count = 2 });
            });
            Variant("test_dark", "Test Stage - Darkness", m => m.dark = true);
            Variant("test_rescue", "Test Stage - Rescue", m =>
            {
                m.mission = "rescue";
                m[94, 65] = Tile.Hostage;
            });
            // ⚠ Placeholder stages for the owner's two new missions (2026-10-02).
            Variant("test_bomb", "Test Stage - Bomb", m =>
            {
                m.mission = "bomb";
                // The target incubator stands in the Checkpoint barracks, far from the start.
                m[97, 65] = Tile.BombTarget;
            });
            Variant("test_vaccine", "Test Stage - Vaccine", m =>
            {
                m.mission = "vaccine";
                // Owner: a night stage with 30 % of the items.
                m.dark = true;
                m.lootScale = 0.3f;
                // The Vaccine lies at the back of the Warehouse.
                m[30, 72] = Tile.Vaccine;
                // Owner, 2026-10-03: the rival AI team starts in the Checkpoint, far from the players.
                m[104, 38] = Tile.RivalSpawn;
            });
        }
        private static void WriteTestStageMap(string path)
        {
            Directory.CreateDirectory(StagesFolder);
            var map = StageMapBuilder.FromLayout(TestStage.Build());
            File.WriteAllText(path, map.ToJson());
            WriteVariants(map);
            AssetDatabase.ImportAsset(path);
            Debug.Log($"[ZOMBIE-SETUP] Wrote {path}");
        }
    }
}
