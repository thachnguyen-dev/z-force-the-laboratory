using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// The running stage's conditions (owner, 2026-09-30): rain shortens how far sounds carry and slows the players;
    /// darkness shortens everyone's sight. Gameplay code multiplies by these.
    /// </summary>
    public static class StageConditions
    {
        public static bool Rain { get; private set; }
        public static bool Dark { get; private set; }

        /// <summary>Multiplies every sound's range (D-149–D-156).</summary>
        public static float NoiseMultiplier { get; private set; } = 1f;
        /// <summary>Multiplies the players' (and the AI teammate's) movement speed.</summary>
        public static float PlayerSpeedMultiplier { get; private set; } = 1f;
        /// <summary>Multiplies zombie and AI sight range.</summary>
        public static float VisionMultiplier { get; private set; } = 1f;
        /// <summary>
        /// Owner, 2026-10-03: once a player has approached the objective of a C4, rescue or Vaccine stage, zombies detect
        /// players 3× as well, by sight and by hearing, for the rest of the stage.
        /// </summary>
        public static float ZombieDetectionMultiplier { get; set; } = 1f;

        public static void Apply(StageLayout layout, GameTuning tuning)
        {
            Set(layout.Rain, layout.Dark, tuning);
            ZombieDetectionMultiplier = 1f;
        }

        /// <summary>Endless mode (owner, 2026-10-07): the weather changes as the run goes on.</summary>
        public static void Set(bool rain, bool dark, GameTuning tuning)
        {
            Rain = rain;
            Dark = dark;
            var c = tuning.conditions;
            // Owner, 2026-10-06: zombies hear every sound 30 % less far (rain shortens it further).
            NoiseMultiplier = (Rain ? c.rainNoiseMultiplier : 1f) * tuning.perception.hearingScale;
            PlayerSpeedMultiplier = Rain ? c.rainSpeedMultiplier : 1f;
            VisionMultiplier = Dark ? c.darkVisionMultiplier : 1f;
        }

        public static void Clear()
        {
            Rain = Dark = false;
            NoiseMultiplier = PlayerSpeedMultiplier = VisionMultiplier = ZombieDetectionMultiplier = 1f;
        }
    }

    /// <summary>NavMesh areas (owner, 2026-10-01: no water cells any more, so everything walkable is one area).</summary>
    public static class NavAreas
    {
        public const int WalkMask = NavMesh.AllAreas;

        public static int MaskFor(ZombieDef def) => WalkMask;
    }
}
