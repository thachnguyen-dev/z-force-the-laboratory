using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>Stage files and the stage list (<c>Resources/Stages/index.json</c>) for the Map Editor.</summary>
    public static class StageFiles
    {
        public static string PathOf(string id) => $"{TileSetup.StagesFolder}/{id}.json";
        private static string IndexPath => $"{TileSetup.StagesFolder}/{StageLibrary.IndexFile}.json";

        public static bool Exists(string id) => File.Exists(PathOf(id));

        public static StageMap Load(string id) => StageMap.FromJson(File.ReadAllText(PathOf(id)));

        public static void Save(string id, StageMap map)
        {
            Directory.CreateDirectory(TileSetup.StagesFolder);
            map.Pack();
            File.WriteAllText(PathOf(id), map.ToJson());
            AssetDatabase.ImportAsset(PathOf(id));
            StageLibrary.Invalidate();
        }

        /// <summary>Every stage file in the folder (the list file excluded).</summary>
        /// <summary>
        /// Rewrites every stage file in the current format (owner, 2026-10-01: water removed, surfaces on their own layer).
        /// Only the format changes: every wall, marker, area and setting is kept.
        /// </summary>
        [UnityEditor.MenuItem("Zombie/Developer (Claude)/Convert Stage Files To The Current Format")]
        public static void ConvertAll()
        {
            foreach (var id in AllFiles())
            {
                var before = File.ReadAllText(PathOf(id));
                var map = StageMap.FromJson(before);
                map.Pack();
                if (map.ToJson() == before) continue;
                Save(id, map);
                UnityEngine.Debug.Log($"[ZOMBIE-SETUP] Converted {id}");
            }
        }

        public static List<string> AllFiles()
        {
            var ids = new List<string>();
            if (!Directory.Exists(TileSetup.StagesFolder)) return ids;
            foreach (var file in Directory.GetFiles(TileSetup.StagesFolder, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (id != StageLibrary.IndexFile) ids.Add(id);
            }
            ids.Sort();
            return ids;
        }

        public static List<string> ReadIndex()
        {
            if (!File.Exists(IndexPath)) return new List<string>();
            return JsonUtility.FromJson<StageIndex>(File.ReadAllText(IndexPath)).stages ?? new List<string>();
        }

        public static void WriteIndex(List<string> ids)
        {
            Directory.CreateDirectory(TileSetup.StagesFolder);
            File.WriteAllText(IndexPath, JsonUtility.ToJson(new StageIndex { stages = ids }, true));
            AssetDatabase.ImportAsset(IndexPath);
            StageLibrary.Invalidate();
        }

        /// <summary>Creates the stage list once, from the files present (test stages first). Never rewrites an existing list.</summary>
        public static void EnsureIndex()
        {
            if (File.Exists(IndexPath)) return;
            var ids = new List<string>();
            foreach (var id in new[] { StageLibrary.TestStageId, "test_rain", "test_dark", "test_rescue" })
                if (Exists(id)) ids.Add(id);
            foreach (var id in AllFiles())
                if (!ids.Contains(id)) ids.Add(id);
            WriteIndex(ids);
        }

        /// <summary>A file-name-safe id from a stage name.</summary>
        public static string IdFrom(string name)
        {
            var chars = new System.Text.StringBuilder();
            foreach (var c in name.ToLowerInvariant())
                chars.Append(char.IsLetterOrDigit(c) ? c : '_');
            var id = chars.ToString().Trim('_');
            if (id.Length == 0) id = "stage";
            var unique = id;
            for (var n = 2; Exists(unique) || unique == StageLibrary.IndexFile; n++) unique = $"{id}_{n}";
            return unique;
        }
    }
}
