using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Saved settings (D-246). No game progress is saved — every stage is always open. Versioned JSON in
    /// persistentDataPath, written atomically.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        public int version = 2;
        public bool minimap = true;
        public float volume = 1f;
        public bool haptics = true;
        /// <summary>Owner, 2026-10-07: the stats display (fps, zombies, shots) can be turned off; off by default.</summary>
        public bool statsDisplay;
        /// <summary>Owner, 2026-10-09: the introduction has been shown (it plays the first time the game starts).</summary>
        public bool introSeen;
        public int quality = -1;
        /// <summary>D-009/D-017: where the Android buttons sit (empty = defaults).</summary>
        public List<TouchButton> touch = new List<TouchButton>();
        /// <summary>PC controls, rebindable (owner, 2026-09-29). Empty = defaults.</summary>
        public List<KeyBinding> keys = new List<KeyBinding>();
        /// <summary>Story missions won at least once (owner, 2026-10-03: shown as completed; all stay open).</summary>
        public List<string> completedMissions = new List<string>();
        /// <summary>Owner, 2026-10-04: a button that opens every stage, for testing (Solo normally unlocks one by one).</summary>
        public bool unlockAllForTesting;
        /// <summary>Owner, 2026-10-03: Solo unlocks missions one by one; Co-op has everything open and keeps its own record.</summary>
        public List<string> completedCoop = new List<string>();
        /// <summary>Endless mode's best times survived, in seconds (owner, 2026-10-07).</summary>
        public float endlessBestSolo;
        public float endlessBestCoop;
        /// <summary>"missionId=stageId": the map each mission was last played on, so replays rotate maps.</summary>
        public List<string> lastPlayed = new List<string>();

        public string LastPlayed(string missionId)
        {
            foreach (var entry in lastPlayed)
                if (entry.StartsWith(missionId + "=")) return entry.Substring(missionId.Length + 1);
            return null;
        }

        public void SetLastPlayed(string missionId, string stageId)
        {
            lastPlayed.RemoveAll(e => e.StartsWith(missionId + "="));
            lastPlayed.Add($"{missionId}={stageId}");
        }
    }

    /// <summary>One on-screen button: measured from the left or right screen edge (virtual units, 720 tall).</summary>
    [Serializable]
    public class TouchButton
    {
        public string id;
        public bool right;
        public float x;
        public float y;
        public float size = 1f;

        public TouchButton Copy() => (TouchButton)MemberwiseClone();
    }

    [Serializable]
    public class KeyBinding
    {
        public string action;
        /// <summary>A <see cref="UnityEngine.InputSystem.Key"/> name, or Mouse0 / Mouse1 / Mouse2.</summary>
        public string key;
    }

    public static class SettingsStore
    {
        private static string PathOnDisk => Path.Combine(Application.persistentDataPath, "settings.json");

        public static SettingsData Load()
        {
            try
            {
                if (File.Exists(PathOnDisk)) return JsonUtility.FromJson<SettingsData>(File.ReadAllText(PathOnDisk)) ?? new SettingsData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZOMBIE] Settings unreadable, using defaults: {e.Message}");
            }
            return new SettingsData();
        }

        public static void Save(SettingsData data)
        {
            try
            {
                var temp = PathOnDisk + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(PathOnDisk)) File.Delete(PathOnDisk);
                File.Move(temp, PathOnDisk);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZOMBIE] Settings not saved: {e.Message}");
            }
        }
    }

    /// <summary>D-003: orthographic, tilted 60°, following the local player.</summary>
    public class CameraRig : MonoBehaviour
    {
        public const float Tilt = 60f;
        public Transform Target;
        public float Distance = 40f;
        /// <summary>
        /// Half the visible height in metres (orthographic). Owner, 2026-10-04: framed like Brawl Stars, where a character
        /// is about one tile wide and the screen shows about 23 tiles across: 6 shows about 27 m across on a 20:9 phone (21 m
        /// at 16:9) with 1 m-wide characters. PLACEHOLDER.
        /// </summary>
        public const float ViewSize = 6f;

        public static CameraRig Create()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            // Owner, 2026-10-03: closer to the action (was 11): characters, zombies and props read more clearly.
            camera.orthographicSize = ViewSize;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.1f);
            go.AddComponent<AudioListener>();
            go.transform.rotation = Quaternion.Euler(Tilt, 0f, 0f);
            // The screen look: bloom on the neon, tonemapping, grading (2026-10-02).
            Art.PostFx.Setup(camera, lowQuality: false);
            return go.AddComponent<CameraRig>();
        }

        private void LateUpdate()
        {
            if (Target == null) return;
            var focus = Target.position + Vector3.up;
            var wanted = focus - transform.forward * Distance;
            transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        public void Snap()
        {
            if (Target != null) transform.position = Target.position + Vector3.up - transform.forward * Distance;
        }
    }
}
