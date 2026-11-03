using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Owner, 2026-10-09: zombies don't appear where a player can see. Every player's camera shows the same patch of
    /// ground round them (orthographic, tilted <see cref="CameraRig.Tilt"/>°, <see cref="CameraRig.ViewSize"/> m half
    /// height), so a point is in view when it lies inside that patch round any living player, sized for the widest phone
    /// screens and with a margin for tall zombies and the screen's edge.
    /// </summary>
    public static class SpawnView
    {
        /// <summary>The widest screen allowed for (21.6:9 phones). PLACEHOLDER.</summary>
        public const float WidestAspect = 2.4f;
        /// <summary>Kept clear beyond the screen's edge (m): a boss's head shows above its feet. PLACEHOLDER.</summary>
        public const float Margin = 3f;

        /// <summary>Half the patch of ground a player's camera shows: across (x) and along (z), in metres.</summary>
        public static Vector2 HalfView => new Vector2(
            CameraRig.ViewSize * WidestAspect + Margin,
            CameraRig.ViewSize / Mathf.Sin(CameraRig.Tilt * Mathf.Deg2Rad) + Margin);

        /// <summary>True when <paramref name="position"/> is on a living player's screen (or just off its edge).</summary>
        public static bool Seen(Vector3 position)
        {
            var half = HalfView;
            foreach (var s in Survivor.All)
            {
                if (s == null || s.IsDead || !s.IsHuman) continue;
                var d = position - s.transform.position;
                if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.z) <= half.y) return true;
            }
            return false;
        }
    }
}
