using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Streets (owner, 2026-10-04):
    /// - Abandoned cars: wrecks along the roads at any angle, some leaning, on their side, upside down or nose-down, mostly
    ///   rusted, burnt or blood-stained; and pile-ups of two or three cars by some crossings.
    /// (Barrier rows and checkpoint roadblocks were removed, owner 2026-10-05; barriers stay placeable by hand.)
    /// Everything is checked cell by cell under its real (turned, posed) footprint, and the markers stay reachable (the
    /// generator's final check removes anything that cuts one off).
    /// </summary>
    public static partial class StageForge
    {
        private static void Streets(Ctx c)
        {
            var theme = c.Recipe.Theme;
            // Lights first, so the wrecks keep clear of them.
            Lights(c);
            var area = c.Map.width * c.Map.height / (112f * 112f);
            var wrecks = Mathf.RoundToInt((theme == "reserve" ? 4f : 9f) * area);
            for (var i = 0; i < wrecks; i++) Wreck(c);
            if (theme != "reserve") Pileups(c, 0.25f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A road line's surface at a point along it (asphalt or dirt), or none.</summary>
        private static bool RoadAlong(Ctx c, bool vertical, int line, int t)
        {
            var x = vertical ? line : t;
            var z = vertical ? t : line;
            if (x < 0 || z < 0 || x >= c.Map.width || z >= c.Map.height) return false;
            var s = c.Map.SurfaceAt(x, z);
            return s == Surface.Road || s == Surface.Road2;
        }

        /// <summary>The cells whose centres lie under a footprint at a turn.</summary>
        private static List<Vector2Int> Covered(Vector2 centre, Vector2 footprint, float yaw)
        {
            var cells = new List<Vector2Int>();
            var hx = footprint.x * 0.5f;
            var hz = footprint.y * 0.5f;
            var a = -yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            var reach = Mathf.CeilToInt(Mathf.Max(hx, hz) + 1f);
            for (var x = Mathf.FloorToInt(centre.x) - reach; x <= Mathf.FloorToInt(centre.x) + reach; x++)
            for (var z = Mathf.FloorToInt(centre.y) - reach; z <= Mathf.FloorToInt(centre.y) + reach; z++)
            {
                var dx = x + 0.5f - centre.x;
                var dz = z + 0.5f - centre.y;
                var lx = dx * cos + dz * sin;
                var lz = -dx * sin + dz * cos;
                if (Mathf.Abs(lx) <= hx && Mathf.Abs(lz) <= hz) cells.Add(new Vector2Int(x, z));
            }
            return cells;
        }

        private static bool OnRoadCentre(Ctx c, Vector2Int cell)
        {
            foreach (var x in c.RoadX) if (cell.x == x) return true;
            foreach (var z in c.RoadZ) if (cell.y == z) return true;
            return false;
        }

        /// <summary>
        /// Places a piece if every cell under it is open ground outside buildings. Roads: only their edge lanes, unless
        /// <paramref name="road"/> allows any road cell (roadblocks). The middle lane of a road always stays open for
        /// wrecks and rows, so traffic (and zombies) can pass.
        /// </summary>
        private static MapDecoration PlaceAt(Ctx c, string id, Vector2 at, float yaw, int variant = 0, float roll = 0f, float pitch = 0f, bool road = false)
        {
            var piece = Art.ArtLibrary.Get(id);
            if (piece == null) return null;
            var d = new MapDecoration { id = id, x = at.x, z = at.y, rotation = yaw, scale = 1f, variant = variant, roll = roll, pitch = pitch };
            var cells = Covered(at, Art.PackProps.Footprint(piece, d), yaw);
            if (cells.Count == 0) return null;
            foreach (var cell in cells)
            {
                if (!Inside(c, cell.x, cell.y) || c.Map[cell.x, cell.y] != Tile.Ground || c.Occupied[cell.x, cell.y]) return null;
                var s = c.Map.SurfaceAt(cell.x, cell.y);
                if (s == Surface.Floor) return null;
                if (!road && (s == Surface.Road || s == Surface.Road2) && OnRoadCentre(c, cell)) return null;
            }
            foreach (var cell in cells) c.Occupied[cell.x, cell.y] = true;
            d.flip = !piece.NoFlip && c.Chance(0.5f);
            c.Map.props.Add(d);
            return d;
        }

        // ------------------------------------------------------------------ lights (owner, 2026-10-05)

        /// <summary>
        /// Street lights about every 14 m along each asphalt road, alternating sides, on the verge just off the road with
        /// the lamp out over it; traffic lights on the corners of most crossings, each signal over one of the roads. None
        /// in the reserve (dirt paths through the woods).
        /// </summary>
        private static void Lights(Ctx c)
        {
            if (c.Recipe.Theme == "reserve") return;
            var map = c.Map;
            bool Asphalt(int x, int z) => x >= 0 && z >= 0 && x < map.width && z < map.height && map.SurfaceAt(x, z) == Surface.Road;
            bool NearCrossing(int x, int z, int d)
            {
                foreach (var rx in c.RoadX)
                foreach (var rz in c.RoadZ)
                    if (Mathf.Abs(x - rx) <= d && Mathf.Abs(z - rz) <= d) return true;
                return false;
            }
            // The road spans cells line - 1 .. line + 1; the verge cells are line + 2 and line - 2.
            const float east = RoadHalf + 1.4f, west = -RoadHalf - 0.4f;
            foreach (var x in c.RoadX)
            {
                var side = c.Chance(0.5f) ? 1 : -1;
                for (var z = 8; z < map.height - 8; z += 14)
                {
                    side = -side;
                    var t = z + c.R(-2, 3);
                    if (!Asphalt(x, t) || NearCrossing(x, t, 6)) continue;
                    PlaceAt(c, "street_light", new Vector2(x + (side > 0 ? east : west), t + 0.5f), side > 0 ? -90f : 90f);
                }
            }
            foreach (var z in c.RoadZ)
            {
                var side = c.Chance(0.5f) ? 1 : -1;
                for (var x = 8; x < map.width - 8; x += 14)
                {
                    side = -side;
                    var t = x + c.R(-2, 3);
                    if (!Asphalt(t, z) || NearCrossing(t, z, 6)) continue;
                    PlaceAt(c, "street_light", new Vector2(t + 0.5f, z + (side > 0 ? east : west)), side > 0 ? 180f : 0f);
                }
            }
            // Traffic lights: on the four corners of a crossing, the arm reaching over the road beside it.
            foreach (var rx in c.RoadX)
            foreach (var rz in c.RoadZ)
            {
                if (!Asphalt(rx, rz) || !Asphalt(rx, rz + 4) || !Asphalt(rx + 4, rz) || !c.Chance(0.8f)) continue;
                var corners = new[]
                {
                    (new Vector2(rx + east, rz + east), -90f),   // north-east: over the north arm
                    (new Vector2(rx + west, rz + west), 90f),    // south-west: over the south arm
                    (new Vector2(rx + west, rz + east), 180f),   // north-west: over the west arm
                    (new Vector2(rx + east, rz + west), 0f),     // south-east: over the east arm
                };
                foreach (var (at, yaw) in corners)
                    if (c.Chance(0.65f)) PlaceAt(c, "traffic_light", at, yaw);
            }
        }

        // ------------------------------------------------------------------ wrecks

        /// <summary>Car variants: 1–6 painted, 7 rusted, 8 burnt, 9 blood-stained. Van: 1–3 painted, 4 burnt, 5 blood-stained.</summary>
        private static (string id, int variant) WreckModel(Ctx c, bool wrecked)
        {
            if (c.Chance(0.75f))
                return ("car", wrecked ? new[] { 7, 8, 9, 7, 8 }[c.R(0, 5)] : c.R(1, 7));
            return ("van", wrecked ? new[] { 4, 5, 4 }[c.R(0, 3)] : c.R(1, 4));
        }

        /// <summary>How a wreck lies: upright, leaning, on its side, upside down or nose-down (roll, pitch).</summary>
        private static (float roll, float pitch, bool wrecked) WreckPose(Ctx c)
        {
            var r = c.R();
            var side = c.Chance(0.5f) ? 1f : -1f;
            if (r < 0.38f) return (0f, 0f, c.Chance(0.45f));                                  // abandoned
            if (r < 0.58f) return (side * (12f + c.R() * 14f), 0f, c.Chance(0.6f));           // leaning (a flat tyre, a kerb)
            if (r < 0.74f) return (side * 90f, 0f, c.Chance(0.85f));                          // on its side
            if (r < 0.9f) return (180f, 0f, c.Chance(0.9f));                                  // upside down
            return (side * 6f, -12f - c.R() * 8f, c.Chance(0.7f));                            // nose-down in a ditch
        }

        /// <summary>One abandoned car beside a road, at a random angle and pose.</summary>
        private static void Wreck(Ctx c)
        {
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var vertical = c.Chance(0.5f);
                var lines = vertical ? c.RoadX : c.RoadZ;
                if (lines.Length == 0) return;
                var line = lines[c.R(0, lines.Length)];
                var length = vertical ? c.Map.height : c.Map.width;
                var t = c.R(6, length - 6);
                if (!RoadAlong(c, vertical, line, t)) continue;
                // Half on the road edge, half on the shoulder, or out on the verge.
                var off = (c.Chance(0.5f) ? 1f : -1f) * (2.2f + c.R() * 2f) + 0.5f;
                var at = vertical ? new Vector2(line + off, t + 0.5f) : new Vector2(t + 0.5f, line + off);
                var along = vertical ? 90f : 0f;
                var yaw = c.Chance(0.3f) ? c.R() * 360f : along + (c.Chance(0.5f) ? 0f : 180f) + (c.R() - 0.5f) * 70f;
                var (roll, pitch, wrecked) = WreckPose(c);
                var (id, variant) = WreckModel(c, wrecked);
                if (PlaceAt(c, id, at, yaw, variant, roll, pitch) != null) return;
            }
        }

        /// <summary>A pile-up: two or three cars crashed together by a crossing.</summary>
        private static void Pileups(Ctx c, float chance)
        {
            foreach (var x in c.RoadX)
            foreach (var z in c.RoadZ)
            {
                if (!c.Chance(chance)) continue;
                if (!RoadAlong(c, true, x, z)) continue;
                // Off one corner of the crossing.
                var corner = new Vector2(x + 0.5f + (c.Chance(0.5f) ? 4.5f : -4f), z + 0.5f + (c.Chance(0.5f) ? 4.5f : -4f));
                var heading = c.R() * 360f;
                var n = c.R(2, 4);
                for (var i = 0; i < n; i++)
                {
                    var spot = corner + new Vector2(Mathf.Cos(heading * Mathf.Deg2Rad), Mathf.Sin(heading * Mathf.Deg2Rad)) * (i * 3.6f);
                    var (roll, pitch, _) = i == 0 ? (0f, 0f, true) : WreckPose(c);
                    var (id, variant) = WreckModel(c, true);
                    PlaceAt(c, id, spot, heading + (c.R() - 0.5f) * 80f, variant, roll, pitch);
                }
            }
        }
    }
}
