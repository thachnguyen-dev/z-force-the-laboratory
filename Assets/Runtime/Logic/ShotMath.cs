using UnityEngine;

namespace ZombieGame
{
    /// <summary>Directions for shots on the ground plane (XZ).</summary>
    public static class ShotMath
    {
        public static Vector3 Rotate(Vector3 direction, float degrees) => Quaternion.AngleAxis(degrees, Vector3.up) * direction;

        /// <summary>
        /// The fixed pellet offsets in degrees (D-133): evenly across the pattern width, never random.
        /// </summary>
        public static float PelletOffset(int index, int pellets, float widthDeg)
        {
            if (pellets <= 1) return 0f;
            return -widthDeg * 0.5f + widthDeg * index / (pellets - 1);
        }

        /// <summary>
        /// Soft aim assist (D-013): pulls the shot part of the way toward the zombie closest to the aim line,
        /// if one is within <paramref name="assistDeg"/>. Never snaps fully.
        /// </summary>
        public static Vector3 Assist(Vector3 aim, Vector3 origin, float range, float assistDeg, float strength)
        {
            var best = assistDeg;
            var bestDir = Vector3.zero;
            foreach (var zombie in Zombie.Alive)
            {
                var to = zombie.transform.position - origin;
                to.y = 0f;
                var distance = to.magnitude;
                if (distance < 0.01f || distance > range) continue;
                var angle = Vector3.Angle(aim, to);
                if (angle >= best) continue;
                best = angle;
                bestDir = to / distance;
            }
            if (bestDir == Vector3.zero) return aim;
            return Vector3.Slerp(aim, bestDir, strength).normalized;
        }
    }
}
