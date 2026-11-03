using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Gameplay queries answered from the stage grid instead of physics (owner, 2026-09-30: simplified, grid-based
    /// collision). Sight lines step through cells, then test free-placed props (2026-10-02); bushes are cells or bush props.
    /// </summary>
    public static class StageGrid
    {
        /// <summary>The running stage's map (null before a stage is built; queries then fall back to physics).</summary>
        public static StageMap Map;

        /// <summary>Within this distance you see into a bush (PLACEHOLDER number; the rule is approved).</summary>
        public const float BushRevealDistance = 2.5f;

        /// <summary>D-051: walls and cover block sight. Bushes don't block the line itself.</summary>
        public static bool SightBlocked(Vector3 from, Vector3 to)
        {
            var map = Map;
            if (map == null) return Physics.Linecast(from, to, Layers.ObstacleMask);
            // Grid traversal (Amanatides & Woo) over the cells the line crosses.
            float x = from.x, z = from.z;
            float dx = to.x - from.x, dz = to.z - from.z;
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            int ex = Mathf.FloorToInt(to.x), ez = Mathf.FloorToInt(to.z);
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dx) > 1e-6f ? Mathf.Abs(1f / dx) : float.MaxValue;
            float tdz = Mathf.Abs(dz) > 1e-6f ? Mathf.Abs(1f / dz) : float.MaxValue;
            float tx = Mathf.Abs(dx) > 1e-6f ? (dx > 0 ? cx + 1 - x : x - cx) * tdx : float.MaxValue;
            float tz = Mathf.Abs(dz) > 1e-6f ? (dz > 0 ? cz + 1 - z : z - cz) * tdz : float.MaxValue;
            for (var guard = 0; guard < 512; guard++)
            {
                if (Tiles.BlocksSight(map[cx, cz]) && !(cx == Mathf.FloorToInt(from.x) && cz == Mathf.FloorToInt(from.z))) return true;
                if (cx == ex && cz == ez) return PropsBlock(from, to);
                if (tx < tz)
                {
                    if (tx > 1f) return PropsBlock(from, to);
                    tx += tdx;
                    cx += sx;
                }
                else
                {
                    if (tz > 1f) return PropsBlock(from, to);
                    tz += tdz;
                    cz += sz;
                }
            }
            return false;
        }

        /// <summary>Free-placed props (owner, 2026-10-02): whether any solid ones exist, so sight lines also check them.</summary>
        public static bool HasSolidProps;

        /// <summary>Bush props, rasterised at <see cref="BushMaskResolution"/> metres (null: none).</summary>
        public static bool[,] BushMask;
        public const float BushMaskResolution = 0.5f;

        public static bool InBush(Vector3 p)
        {
            if (Map == null) return false;
            if (Map.At(p) == Tile.Bush) return true;
            if (BushMask == null) return false;
            int bx = Mathf.FloorToInt(p.x / BushMaskResolution), bz = Mathf.FloorToInt(p.z / BushMaskResolution);
            return bx >= 0 && bz >= 0 && bx < BushMask.GetLength(0) && bz < BushMask.GetLength(1) && BushMask[bx, bz];
        }


        /// <summary>
        /// Bush rule (owner-approved 2026-09-30, Brawl Stars style): someone in a bush can't be seen from further than
        /// <see cref="BushRevealDistance"/> — unless the viewer is in the same bush patch area right next to them.
        /// </summary>
        /// <summary>Props block sight whatever their height, like cover cells: the line is tested low over the ground.</summary>
        private static bool PropsBlock(Vector3 from, Vector3 to)
        {
            if (!HasSolidProps) return false;
            return Physics.Linecast(new Vector3(from.x, 0.6f, from.z), new Vector3(to.x, 0.6f, to.z), Layers.ObstacleMask, QueryTriggerInteraction.Ignore);
        }

        public static bool HiddenByBush(Vector3 viewer, Vector3 target)
        {
            if (!InBush(target)) return false;
            var d = new Vector2(target.x - viewer.x, target.z - viewer.z).magnitude;
            return d > BushRevealDistance;
        }
    }
}
