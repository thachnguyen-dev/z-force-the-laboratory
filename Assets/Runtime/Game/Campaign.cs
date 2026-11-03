using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>One story mission (owner, 2026-10-03): its place in the story and the maps it can be played on.</summary>
    [Serializable]
    public class CampaignMission
    {
        public string id;
        public string title;
        /// <summary>A short line for the stage list ("Boss", "Rescue", "Bomb", "Vaccine", plus conditions).</summary>
        public string tag;
        public string story;
        /// <summary>Stage ids; replays rotate through them (owner: replays may use different maps).</summary>
        public List<string> stages = new List<string>();
    }

    [Serializable]
    public class CampaignData
    {
        public List<CampaignMission> missions = new List<CampaignMission>();
    }

    /// <summary>
    /// The main story (owner, 2026-10-03): an ordered list of missions, the Vaccine mission last. Every mission is open
    /// (owner: all unlocked, shown in story order) and replayable; each replay takes the next of its maps, and every run
    /// rolls its zombies differently. Stored in <c>Resources/Stages/campaign.json</c> (editable; Claude's six missions
    /// are placeholders until the owner's stage list).
    /// </summary>
    public static class Campaign
    {
        public const string File = "campaign";
        private static CampaignData _data;

        public static IReadOnlyList<CampaignMission> Missions
        {
            get
            {
                if (_data != null) return _data.missions;
                var asset = Resources.Load<TextAsset>($"Stages/{File}");
                _data = asset != null ? JsonUtility.FromJson<CampaignData>(asset.text) : new CampaignData();
                return _data.missions;
            }
        }

        public static void Invalidate() => _data = null;

        /// <summary>The tutorial mission: Solo only (owner, 2026-10-07: Co-op has no tutorial).</summary>
        public const string TutorialId = "m0_tutorial";

        /// <summary>The missions listed for play, by index: all of them in Solo, all but the tutorial in Co-op.</summary>
        public static List<int> Listed(bool coop)
        {
            var list = new List<int>();
            for (var i = 0; i < Missions.Count; i++)
                if (!coop || Missions[i].id != TutorialId) list.Add(i);
            return list;
        }

        public static CampaignMission MissionOf(string stageId)
        {
            foreach (var m in Missions)
                if (m.stages.Contains(stageId)) return m;
            return null;
        }

        /// <summary>
        /// "Unlock all (test)" is on, in a development build only (owner, 2026-10-10: it showed in the store build; a
        /// release build neither shows the switch nor honours a setting left on).
        /// </summary>
        public static bool TestUnlock(SettingsData settings) => Gui.DevTools && settings.unlockAllForTesting;

        /// <summary>Owner, 2026-10-03: in Solo a mission opens once the one before it was won in Solo; in Co-op all are open.</summary>
        public static bool Unlocked(int index, SettingsData settings, bool coop) =>
            coop || TestUnlock(settings) || index <= 0 || index >= Missions.Count || settings.completedMissions.Contains(Missions[index - 1].id);

        /// <summary>The map to play next: the one after the last played (first time: the first).</summary>
        public static string NextStage(CampaignMission mission, SettingsData settings)
        {
            var stages = mission.stages.FindAll(StageLibrary.Exists);
            if (stages.Count == 0) return null;
            var last = settings.LastPlayed(mission.id);
            var i = stages.IndexOf(last);
            return stages[(i + 1) % stages.Count];
        }
    }
}
