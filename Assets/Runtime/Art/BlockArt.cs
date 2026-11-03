using UnityEngine;
using P = ZombieGame.Art.ArtPalette;

namespace ZombieGame.Art
{
    /// <summary>
    /// Layer 3's grid blocks drawn as continuous pieces (owner, 2026-10-02): neighbouring wall cells make one wall,
    /// neighbouring cliff cells one cliff face, bush cells a hedge. The camera never turns, so only tops and south faces
    /// are built, and a south face only where nothing as tall stands in front of it. Details depend on world position, so
    /// neighbours always line up.
    /// </summary>
    public static class BlockArt
    {
        public static void Build(MeshKit k, StageMap map, int x, int z)
        {
            var theme = string.IsNullOrEmpty(map.theme) ? "lab" : map.theme;
            switch (map[x, z])
            {
                case Tile.Wall:
                    var kind = ThemeArt.KindOf(map, x, z, out var building);
                    // Owner, 2026-10-05: cracks and scratch marks belong on buildings (not the ground): every wall
                    // carries cracks, grime and chipped paint from the wear texture, and some show claw marks.
                    var before = k.Wear;
                    k.Wear = ThemeArt.WallWear(x, z);
                    if (kind == ThemeArt.WallKind.Facility) Wall(k, map, x, z);
                    else ThemeArt.HouseWall(k, map, x, z, kind, building);
                    k.Wear = before;
                    if (!TileAssembler.FrontHidden(map, x, z, Tile.Wall)) ThemeArt.ScratchMarks(k, x, z);
                    break;
                case Tile.Cliff:
                    // Owner, 2026-10-04: the map is surrounded by the steel mesh fence (the owner's fence model).
                    if (ThemeArt.FenceBorder(k, map, x, z)) break;
                    if (theme == "town" || theme == "reserve") ThemeArt.TreeLine(k, map, x, z);
                    else if (theme == "lab" || theme == "checkpoint") ThemeArt.PerimeterWall(k, map, x, z);
                    else Cliff(k, map, x, z);
                    break;
                // Owner, 2026-10-04: the owner's grass model replaces these bushes (Art.GrassLayer).
                case Tile.Bush: if (!GrassLayer.Available) BushCell(k, x, z); break;
                case Tile.Cover:
                    if (ThemeArt.FenceRun(map, x, z, out var horizontal)) ThemeArt.Fence(k, map, x, z, horizontal, theme);
                    else CoverCell(k, x, z);
                    break;
            }
        }

        private static bool Solid(StageMap map, int x, int z, Tile tile) => map[x, z] == tile;

        // ------------------------------------------------------------------ walls (3 m)

        /// <summary>
        /// Facility wall: dark metal panels on a darker plinth, a lighter cap with a lit edge where the wall ends, a
        /// glowing green strip, panel seams every two metres, the odd status light and vent, grime towards the bottom.
        /// </summary>
        private static void Wall(MeshKit k, StageMap map, int x, int z)
        {
            var h = StageMapBuilder.WallHeight;
            float x0 = x, x1 = x + 1f, z0 = z, z1 = z + 1f;
            var capTop = new Color(0.62f, 0.66f, 0.64f);
            var capEdge = new Color(0.86f, 0.89f, 0.85f);
            // Top cap with a lighter rim on every open side, so a wall's outline reads from above.
            k.Quad(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), capTop);
            const float rim = 0.09f;
            const float lift = 0.003f;
            if (!Solid(map, x, z - 1, Tile.Wall)) k.Quad(new Vector3(x0, h + lift, z0), new Vector3(x0, h + lift, z0 + rim), new Vector3(x1, h + lift, z0 + rim), new Vector3(x1, h + lift, z0), capEdge);
            if (!Solid(map, x, z + 1, Tile.Wall)) k.Quad(new Vector3(x0, h + lift, z1 - rim), new Vector3(x0, h + lift, z1), new Vector3(x1, h + lift, z1), new Vector3(x1, h + lift, z1 - rim), MeshKit.Shade(capEdge, 0.85f));
            if (!Solid(map, x - 1, z, Tile.Wall)) k.Quad(new Vector3(x0, h + lift, z0), new Vector3(x0, h + lift, z1), new Vector3(x0 + rim, h + lift, z1), new Vector3(x0 + rim, h + lift, z0), MeshKit.Shade(capEdge, 0.92f));
            if (!Solid(map, x + 1, z, Tile.Wall)) k.Quad(new Vector3(x1 - rim, h + lift, z0), new Vector3(x1 - rim, h + lift, z1), new Vector3(x1, h + lift, z1), new Vector3(x1, h + lift, z0), MeshKit.Shade(capEdge, 0.92f));
            // A thin glowing line along the middle of long runs' tops, now and then.
            if (Solid(map, x - 1, z, Tile.Wall) && Solid(map, x + 1, z, Tile.Wall) && MeshKit.Hash01(x / 4, 3, z) < 0.35f)
                k.Quad(new Vector3(x0, h + lift * 2f, z0 + 0.47f), new Vector3(x0, h + lift * 2f, z0 + 0.53f), new Vector3(x1, h + lift * 2f, z0 + 0.53f), new Vector3(x1, h + lift * 2f, z0 + 0.47f), MeshKit.Glow(P.ToxicSoft, 0.55f));

            if (TileAssembler.FrontHidden(map, x, z, Tile.Wall)) return;
            // ---- South face ----
            var f = z0;
            var plinth = new Color(0.3f, 0.33f, 0.36f);
            var panelLow = new Color(0.44f, 0.5f, 0.52f);
            var panelHigh = new Color(0.6f, 0.67f, 0.68f);
            // Plinth (protrudes a little, with its own small top).
            const float out1 = 0.05f;
            k.Quad(new Vector3(x0, 0f, f - out1), new Vector3(x0, 0.32f, f - out1), new Vector3(x1, 0.32f, f - out1), new Vector3(x1, 0f, f - out1), plinth);
            k.Quad(new Vector3(x0, 0.32f, f - out1), new Vector3(x0, 0.32f, f), new Vector3(x1, 0.32f, f), new Vector3(x1, 0.32f, f - out1), MeshKit.Shade(plinth, 1.6f));
            // Panels: grime gradient from the bottom.
            k.Quad(new Vector3(x0, 0.32f, f), new Vector3(x0, h - 0.22f, f), new Vector3(x1, h - 0.22f, f), new Vector3(x1, 0.32f, f), panelLow, panelHigh, panelHigh, panelLow);
            // Cap lip (protrudes, catches the light).
            const float out2 = 0.06f;
            k.Quad(new Vector3(x0, h - 0.22f, f - out2), new Vector3(x0, h, f - out2), new Vector3(x1, h, f - out2), new Vector3(x1, h - 0.22f, f - out2), new Color(0.72f, 0.76f, 0.73f));
            k.Quad(new Vector3(x0, h - 0.22f, f), new Vector3(x0, h - 0.22f, f - out2), new Vector3(x1, h - 0.22f, f - out2), new Vector3(x1, h - 0.22f, f), MeshKit.Shade(plinth, 0.8f));
            k.Quad(new Vector3(x0, h, f - out2), new Vector3(x0, h, f), new Vector3(x1, h, f), new Vector3(x1, h, f - out2), capEdge);
            // Glowing green strip at chest height, and its dark backing band.
            const float d1 = 0.004f;
            k.Quad(new Vector3(x0, 1.05f, f - d1), new Vector3(x0, 1.31f, f - d1), new Vector3(x1, 1.31f, f - d1), new Vector3(x1, 1.05f, f - d1), new Color(0.16f, 0.2f, 0.18f));
            k.Quad(new Vector3(x0, 1.14f, f - d1 * 2f), new Vector3(x0, 1.22f, f - d1 * 2f), new Vector3(x1, 1.22f, f - d1 * 2f), new Vector3(x1, 1.14f, f - d1 * 2f), MeshKit.Glow(P.Toxic, 0.7f));
            // Seams every two metres (world-aligned), plus rivets.
            if ((x & 1) == 0)
            {
                k.Quad(new Vector3(x0, 0.32f, f - d1), new Vector3(x0, h - 0.22f, f - d1), new Vector3(x0 + 0.035f, h - 0.22f, f - d1), new Vector3(x0 + 0.035f, 0.32f, f - d1), new Color(0.3f, 0.35f, 0.37f));
                foreach (var y in new[] { 0.6f, 1.7f, 2.45f })
                    k.Quad(new Vector3(x0 + 0.08f, y, f - d1), new Vector3(x0 + 0.08f, y + 0.05f, f - d1), new Vector3(x0 + 0.13f, y + 0.05f, f - d1), new Vector3(x0 + 0.13f, y, f - d1), MeshKit.Shade(panelHigh, 1.4f));
            }
            // Details by position: a status light, a vent, or a hazard plate.
            var roll = MeshKit.Hash01(x, 7, z);
            if (roll < 0.12f)
                k.Quad(new Vector3(x0 + 0.42f, 1.9f, f - d1), new Vector3(x0 + 0.42f, 2.06f, f - d1), new Vector3(x0 + 0.58f, 2.06f, f - d1), new Vector3(x0 + 0.58f, 1.9f, f - d1), MeshKit.Glow(P.Toxic, 0.95f));
            else if (roll < 0.24f)
            {
                k.Quad(new Vector3(x0 + 0.2f, 1.75f, f - d1), new Vector3(x0 + 0.2f, 2.3f, f - d1), new Vector3(x0 + 0.8f, 2.3f, f - d1), new Vector3(x0 + 0.8f, 1.75f, f - d1), P.MetalDark);
                for (var i = 0; i < 4; i++)
                {
                    var y = 1.82f + i * 0.12f;
                    k.Quad(new Vector3(x0 + 0.26f, y, f - d1 * 2f), new Vector3(x0 + 0.26f, y + 0.05f, f - d1 * 2f), new Vector3(x0 + 0.74f, y + 0.05f, f - d1 * 2f), new Vector3(x0 + 0.74f, y, f - d1 * 2f), P.Black);
                }
            }
            else if (roll < 0.3f)
            {
                for (var i = 0; i < 3; i++)
                {
                    var sx = x0 + 0.15f + i * 0.25f;
                    k.Quad(new Vector3(sx, 0.4f, f - d1), new Vector3(sx + 0.12f, 0.75f, f - d1), new Vector3(sx + 0.24f, 0.75f, f - d1), new Vector3(sx + 0.12f, 0.4f, f - d1), i % 2 == 0 ? P.Hazard : P.Black);
                }
            }
        }

        // ------------------------------------------------------------------ cliffs (5 m, the map edge)

        /// <summary>Layered dark rock with a jagged, mossy top. Heights come from world-space noise, so cells join.</summary>
        private static float CliffTop(float wx, float wz) =>
            StageMapBuilder.CliffHeight + (MeshKit.Noise2(wx * 0.7f, wz * 0.7f) - 0.5f) * 0.9f + (MeshKit.Noise2(wx * 2.3f, wz * 2.3f) - 0.5f) * 0.3f;

        private static float CliffBulge(float wx, float y) =>
            (MeshKit.Noise2(wx * 1.3f + 11f, y * 0.9f) - 0.5f) * 0.35f + (MeshKit.Noise2(wx * 3.1f, y * 2.7f + 5f) - 0.5f) * 0.12f;

        private static void Cliff(MeshKit k, StageMap map, int x, int z)
        {
            const int steps = 3;
            var rockDark = new Color(0.09f, 0.11f, 0.19f);
            var rock = new Color(0.19f, 0.23f, 0.34f);
            var moss = P.Moss;
            // The map's south edge faces off the map, so its front is drawn too.
            var frontOpen = z == 0 || map[x, z - 1] != Tile.Cliff;
            float Front(float px) => Mathf.Max(0f, CliffBulge(px, CliffTop(px, z)) + 0.12f);
            // Jagged top: a 3 × 3 grid of quads with mossy colour by height.
            for (var i = 0; i < steps; i++)
            for (var j = 0; j < steps; j++)
            {
                float ax = x + i / (float)steps, bx = x + (i + 1f) / steps, az = z + j / (float)steps, bz = z + (j + 1f) / steps;
                // Where the front is open, the top's south edge leans out with the face below it, so they meet.
                Vector3 V(float px, float pz) => new Vector3(px, CliffTop(px, pz), frontOpen && pz <= z + 0.001f ? z - Front(px) : pz);
                var c = Color.Lerp(rock, moss, Mathf.Clamp01(MeshKit.Noise2(ax * 1.7f, az * 1.7f) * 1.6f - 0.4f));
                k.Quad(V(ax, az), V(ax, bz), V(bx, bz), V(bx, az), c);
            }
            if (!frontOpen) return;
            // South face: a grid bulging towards the camera, rock layers getting lighter towards the top.
            const int rows = 6;
            for (var i = 0; i < steps; i++)
            for (var row = 0; row < rows; row++)
            {
                float ax = x + i / (float)steps, bx = x + (i + 1f) / steps;
                float Ya(float px, int r) => r == rows ? CliffTop(px, z) : r * StageMapBuilder.CliffHeight / rows;
                Vector3 V(float px, int r)
                {
                    var y = Ya(px, r);
                    return new Vector3(px, y, z - Mathf.Max(0f, CliffBulge(px, y) + 0.12f));
                }
                var t = (row + 0.5f) / rows;
                var layer = MeshKit.Noise2(x * 0.3f, row * 0.9f);
                var c = Color.Lerp(rockDark, rock, t * 0.8f + layer * 0.3f);
                if (row == rows - 1) c = Color.Lerp(c, moss, 0.45f);
                k.Quad(V(ax, row), V(ax, row + 1), V(bx, row + 1), V(bx, row), c);
            }
            // Moss hanging over the edge and the odd glowing fungus.
            if (MeshKit.Hash01(x, 11, z) < 0.25f)
                ArtLibrary.Crystal(k, new Vector3(x + 0.5f, 0f, z - 0.25f), 0.3f);
        }

        // ------------------------------------------------------------------ bush cells

        private static void BushCell(MeshKit k, int x, int z)
        {
            var seed = x * 7349 + z * 3571;
            var jitter = new Vector3(MeshKit.Hash01(x, 1, z) - 0.5f, 0f, MeshKit.Hash01(x, 2, z) - 0.5f) * 0.25f;
            ArtLibrary.Bush(k, new Vector3(x + 0.5f, 0f, z + 0.5f) + jitter, 0.62f, seed);
        }

        // ------------------------------------------------------------------ cover cells (one-cell obstacles)

        private static readonly string[] CoverLooks = { "crate", "barrels", "container", "crate", "barrels" };

        private static void CoverCell(MeshKit k, int x, int z)
        {
            var seed = x * 92821 + z * 68917;
            var look = CoverLooks[(int)(MeshKit.Hash01(x, 5, z) * CoverLooks.Length) % CoverLooks.Length];
            var turn = (int)(MeshKit.Hash01(x, 6, z) * 4f) * 90f;
            var before = k.Transform;
            // Containers are a little bigger than a cell; shrink them to fit.
            var scale = look == "container" ? 0.85f : 1f;
            k.Transform = before * Matrix4x4.TRS(new Vector3(x + 0.5f, 0f, z + 0.5f), Quaternion.Euler(0f, look == "container" ? 0f : turn, 0f), Vector3.one * scale);
            ArtLibrary.Get(look).Build(k, seed);
            k.Transform = before;
        }
    }
}
