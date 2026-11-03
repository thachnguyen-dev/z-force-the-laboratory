using UnityEngine;
using P = ZombieGame.Art.ArtPalette;

namespace ZombieGame.Art
{
    /// <summary>
    /// A building's roof (owner, 2026-09-30: roofs hide the inside and lift when a player comes in): a dark tar roof with a
    /// raised parapet, air-conditioning units, vents, a glowing skylight strip and pipes. Details depend on the building's
    /// position, so each roof looks different but always the same.
    /// </summary>
    public static class RoofArt
    {
        private static readonly Color[] HouseRoofs =
        {
            new Color(0.72f, 0.3f, 0.24f), new Color(0.24f, 0.5f, 0.56f), new Color(0.86f, 0.52f, 0.24f),
            new Color(0.34f, 0.42f, 0.64f), new Color(0.52f, 0.36f, 0.56f),
        };

        /// <summary>
        /// Houses, shops, the clinic and cabins get a pitched roof (2026-10-03): two tiled slopes meeting at a ridge along the
        /// long side, a coloured trim, a chimney; the clinic white with a red cross. Military posts an olive flat roof.
        /// </summary>
        public static Mesh Build(Rect bounds, float height, string name, string buildingName)
        {
            var kind = ThemeArt.KindOf(buildingName);
            if (kind != ThemeArt.WallKind.House && kind != ThemeArt.WallKind.Cabin) return Build(bounds, height, name, kind);
            var k = new MeshKit();
            const float over = 0.3f;
            float x0 = bounds.xMin - over, x1 = bounds.xMax + over, z0 = bounds.yMin - over, z1 = bounds.yMax + over;
            var y = height + 0.04f;
            var h = Mathf.Abs((buildingName ?? "").GetHashCode());
            var clinic = (buildingName ?? "").StartsWith("Clinic");
            var colour = clinic ? new Color(0.92f, 0.92f, 0.9f) : kind == ThemeArt.WallKind.Cabin ? new Color(0.5f, 0.32f, 0.2f) : HouseRoofs[h % HouseRoofs.Length];
            var alongX = x1 - x0 >= z1 - z0;
            var ridge = Mathf.Min(1.6f, (alongX ? z1 - z0 : x1 - x0) * 0.22f);
            var rows = 6;
            if (alongX)
            {
                var zm = (z0 + z1) * 0.5f;
                for (var i = 0; i < rows; i++)
                {
                    float t0 = i / (float)rows, t1 = (i + 1f) / rows;
                    var shade = i % 2 == 0 ? 1f : 0.9f;
                    // South slope (towards the camera), then the north one.
                    k.Quad(new Vector3(x0, y + ridge * t0, Mathf.Lerp(z0, zm, t0)), new Vector3(x0, y + ridge * t1, Mathf.Lerp(z0, zm, t1)),
                        new Vector3(x1, y + ridge * t1, Mathf.Lerp(z0, zm, t1)), new Vector3(x1, y + ridge * t0, Mathf.Lerp(z0, zm, t0)), MeshKit.Shade(colour, shade));
                    k.Quad(new Vector3(x0, y + ridge * t1, Mathf.Lerp(z1, zm, t1)), new Vector3(x0, y + ridge * t0, Mathf.Lerp(z1, zm, t0)),
                        new Vector3(x1, y + ridge * t0, Mathf.Lerp(z1, zm, t0)), new Vector3(x1, y + ridge * t1, Mathf.Lerp(z1, zm, t1)), MeshKit.Shade(colour, shade * 0.8f));
                }
                // Gable ends.
                k.Tri(new Vector3(x0, y, z0), new Vector3(x0, y + ridge, zm), new Vector3(x0, y, z1), MeshKit.Shade(colour, 0.7f));
                k.Tri(new Vector3(x1, y, z1), new Vector3(x1, y + ridge, zm), new Vector3(x1, y, z0), MeshKit.Shade(colour, 0.7f));
                k.Box(new Vector3((x0 + x1) * 0.5f, y + ridge - 0.04f, zm), new Vector3(x1 - x0 + 0.05f, 0.1f, 0.18f), MeshKit.Shade(colour, 0.6f), Faces.Bottom);
                k.Quad(new Vector3(x0, y - 0.12f, z0 - 0.01f), new Vector3(x0, y + 0.02f, z0 - 0.01f), new Vector3(x1, y + 0.02f, z0 - 0.01f), new Vector3(x1, y - 0.12f, z0 - 0.01f), new Color(0.95f, 0.94f, 0.9f));
                if (clinic)
                {
                    var c = new Vector3((x0 + x1) * 0.5f, 0f, Mathf.Lerp(z0, zm, 0.5f));
                    var cy = y + ridge * 0.5f + 0.02f;
                    var red = new Color(0.86f, 0.18f, 0.16f);
                    k.Quad(new Vector3(c.x - 0.25f, cy - 0.15f, c.z - 0.8f * 0.55f), new Vector3(c.x - 0.25f, cy + 0.15f, c.z + 0.8f * 0.55f), new Vector3(c.x + 0.25f, cy + 0.15f, c.z + 0.8f * 0.55f), new Vector3(c.x + 0.25f, cy - 0.15f, c.z - 0.8f * 0.55f), red);
                    k.Quad(new Vector3(c.x - 0.8f, cy - 0.05f, c.z - 0.25f * 0.55f), new Vector3(c.x - 0.8f, cy + 0.05f, c.z + 0.25f * 0.55f), new Vector3(c.x + 0.8f, cy + 0.05f, c.z + 0.25f * 0.55f), new Vector3(c.x + 0.8f, cy - 0.05f, c.z - 0.25f * 0.55f), red);
                }
                else
                {
                    var cx = Mathf.Lerp(x0, x1, 0.25f + (h % 50) / 100f);
                    k.Box(new Vector3(cx, y + ridge * 0.5f, Mathf.Lerp(zm, z1, 0.4f)), new Vector3(0.45f, ridge * 0.5f + 0.7f, 0.45f), new Color(0.55f, 0.36f, 0.3f), Faces.Bottom, new Color(0.25f, 0.2f, 0.2f));
                }
            }
            else
            {
                var xm = (x0 + x1) * 0.5f;
                for (var i = 0; i < rows; i++)
                {
                    float t0 = i / (float)rows, t1 = (i + 1f) / rows;
                    var shade = i % 2 == 0 ? 1f : 0.9f;
                    k.Quad(new Vector3(Mathf.Lerp(x0, xm, t0), y + ridge * t0, z0), new Vector3(Mathf.Lerp(x0, xm, t0), y + ridge * t0, z1),
                        new Vector3(Mathf.Lerp(x0, xm, t1), y + ridge * t1, z1), new Vector3(Mathf.Lerp(x0, xm, t1), y + ridge * t1, z0), MeshKit.Shade(colour, shade * 0.88f));
                    k.Quad(new Vector3(Mathf.Lerp(x1, xm, t1), y + ridge * t1, z0), new Vector3(Mathf.Lerp(x1, xm, t1), y + ridge * t1, z1),
                        new Vector3(Mathf.Lerp(x1, xm, t0), y + ridge * t0, z1), new Vector3(Mathf.Lerp(x1, xm, t0), y + ridge * t0, z0), MeshKit.Shade(colour, shade));
                }
                k.Tri(new Vector3(x0, y, z0), new Vector3(xm, y + ridge, z0), new Vector3(x1, y, z0), MeshKit.Shade(colour, 0.75f));
                k.Box(new Vector3(xm, y + ridge - 0.04f, (z0 + z1) * 0.5f), new Vector3(0.18f, 0.1f, z1 - z0 + 0.05f), MeshKit.Shade(colour, 0.6f), Faces.Bottom);
            }
            k.RemoveHiddenFaces();
            return k.ToMesh(name);
        }

        /// <summary>Flat-roof colours by building type (surface, lighter strips, parapet), 2026-10-04.</summary>
        private static (Color tar, Color light, Color rim) RoofColours(ThemeArt.WallKind kind)
        {
            switch (kind)
            {
                case ThemeArt.WallKind.Military: return (new Color(0.33f, 0.37f, 0.24f), new Color(0.38f, 0.42f, 0.27f), new Color(0.4f, 0.44f, 0.29f));
                case ThemeArt.WallKind.Clinic: return (new Color(0.68f, 0.69f, 0.68f), new Color(0.74f, 0.75f, 0.74f), new Color(0.86f, 0.86f, 0.83f));
                case ThemeArt.WallKind.Shop: return (new Color(0.4f, 0.4f, 0.41f), new Color(0.45f, 0.45f, 0.46f), new Color(0.6f, 0.58f, 0.55f));
                case ThemeArt.WallKind.Garage: return (new Color(0.3f, 0.31f, 0.33f), new Color(0.35f, 0.36f, 0.38f), new Color(0.45f, 0.5f, 0.55f));
                case ThemeArt.WallKind.Warehouse: return (new Color(0.46f, 0.47f, 0.47f), new Color(0.52f, 0.53f, 0.53f), new Color(0.42f, 0.3f, 0.25f));
                case ThemeArt.WallKind.Security: return (new Color(0.25f, 0.28f, 0.34f), new Color(0.3f, 0.33f, 0.4f), new Color(0.33f, 0.4f, 0.5f));
                default: return (new Color(0.4f, 0.44f, 0.47f), new Color(0.46f, 0.5f, 0.53f), new Color(0.55f, 0.6f, 0.6f));
            }
        }

        public static Mesh Build(Rect bounds, float height, string name, ThemeArt.WallKind kind = ThemeArt.WallKind.Facility)
        {
            var k = new MeshKit();
            const float over = 0.15f;
            float x0 = bounds.xMin - over, x1 = bounds.xMax + over, z0 = bounds.yMin - over, z1 = bounds.yMax + over;
            var y = height + 0.04f;
            var (tar, tarLight, wall) = RoofColours(kind);
            // The roof surface in strips, a little uneven in tone.
            var strips = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 2f));
            for (var i = 0; i < strips; i++)
            {
                var a = Mathf.Lerp(z0, z1, i / (float)strips);
                var b = Mathf.Lerp(z0, z1, (i + 1) / (float)strips);
                var tone = Color.Lerp(tar, tarLight, MeshKit.Hash01(Mathf.RoundToInt(bounds.x), i, Mathf.RoundToInt(bounds.y)));
                k.Quad(new Vector3(x0, y, a), new Vector3(x0, y, b), new Vector3(x1, y, b), new Vector3(x1, y, a), tone);
            }
            // Parapet: a raised rim with a lit top and a front face.
            const float rim = 0.3f, rimH = 0.32f;
            var cap = new Color(0.82f, 0.85f, 0.8f);
            k.Box(new Vector3((x0 + x1) * 0.5f, y, z0 + rim * 0.5f), new Vector3(x1 - x0, rimH, rim), wall, Faces.Bottom | Faces.North, cap);
            k.Box(new Vector3((x0 + x1) * 0.5f, y, z1 - rim * 0.5f), new Vector3(x1 - x0, rimH, rim), wall, Faces.Bottom | Faces.North, cap);
            k.Box(new Vector3(x0 + rim * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(rim, rimH, z1 - z0 - rim * 2f), wall, Faces.Bottom, cap);
            k.Box(new Vector3(x1 - rim * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(rim, rimH, z1 - z0 - rim * 2f), wall, Faces.Bottom, cap);
            // A thin green light line along the front parapet (labs only).
            if (kind == ThemeArt.WallKind.Facility)
            k.Quad(new Vector3(x0, y + 0.12f, z0 - 0.003f), new Vector3(x0, y + 0.17f, z0 - 0.003f), new Vector3(x1, y + 0.17f, z0 - 0.003f), new Vector3(x1, y + 0.12f, z0 - 0.003f), MeshKit.Glow(P.Toxic, 0.6f));

            // The clinic: a big red cross on the roof, seen from far away.
            if (kind == ThemeArt.WallKind.Clinic)
            {
                var c = new Vector3((x0 + x1) * 0.5f, y + 0.01f, (z0 + z1) * 0.5f);
                var s = Mathf.Min(x1 - x0, z1 - z0) * 0.3f;
                var red = new Color(0.66f, 0.13f, 0.12f);
                k.Quad(c + new Vector3(-s * 0.3f, 0f, -s), c + new Vector3(-s * 0.3f, 0f, s), c + new Vector3(s * 0.3f, 0f, s), c + new Vector3(s * 0.3f, 0f, -s), red);
                k.Quad(c + new Vector3(-s, 0.002f, -s * 0.3f), c + new Vector3(-s, 0.002f, s * 0.3f), c + new Vector3(s, 0.002f, s * 0.3f), c + new Vector3(s, 0.002f, -s * 0.3f), red);
            }
            var seed = Mathf.RoundToInt(bounds.x * 31f + bounds.y * 17f);
            var r = new System.Random(seed);
            float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var innerX0 = x0 + 0.8f;
            var innerX1 = x1 - 0.8f;
            var innerZ0 = z0 + 0.8f;
            var innerZ1 = z1 - 0.8f;
            if (innerX1 <= innerX0 || innerZ1 <= innerZ0) return Finish(k, name);
            var area = (innerX1 - innerX0) * (innerZ1 - innerZ0);
            // Air-conditioning units with a fan grille.
            var units = Mathf.Clamp(Mathf.RoundToInt(area / 20f), 1, 8);
            for (var i = 0; i < units; i++)
            {
                var p = new Vector3(R(innerX0, innerX1), y, R(innerZ0, innerZ1));
                k.BevelBox(p, new Vector3(1.1f, 0.6f, 0.9f), 0.06f, P.Metal, P.MetalLight);
                k.Cylinder(p + Vector3.up * 0.6f, 0.32f, 0.02f, 10, P.Black, true, P.MetalDark);
                k.Quad(p + new Vector3(-0.4f, 0.12f, -0.452f), p + new Vector3(-0.4f, 0.45f, -0.452f), p + new Vector3(0.4f, 0.45f, -0.452f), p + new Vector3(0.4f, 0.12f, -0.452f), P.MetalDark);
            }
            // Vents and pipes.
            var vents = Mathf.Clamp(Mathf.RoundToInt(area / 10f), 2, 14);
            for (var i = 0; i < vents; i++)
            {
                var p = new Vector3(R(innerX0, innerX1), y, R(innerZ0, innerZ1));
                k.Cylinder(p, 0.16f, 0.45f, 8, P.MetalDark, true, P.Black);
                k.Cylinder(p + Vector3.up * 0.45f, 0.24f, 0.05f, 8, P.Metal, true, P.MetalLight);
            }
            // Skylights on larger roofs: a row of framed dark-glass panes with a faint green glow from inside.
            if (area > 40f)
            {
                var sz = R(innerZ0, innerZ1 - 1.2f);
                var count = Mathf.Max(2, Mathf.FloorToInt((innerX1 - innerX0) * 0.5f / 1.3f));
                var startX = (innerX0 + innerX1) * 0.5f - count * 0.65f;
                for (var i = 0; i < count; i++)
                {
                    var cx = startX + i * 1.3f + 0.65f;
                    k.Box(new Vector3(cx, y, sz + 0.55f), new Vector3(1.1f, 0.14f, 1.1f), P.MetalDark, Faces.Bottom, P.Metal);
                    k.Quad(new Vector3(cx - 0.45f, y + 0.145f, sz + 0.1f), new Vector3(cx - 0.45f, y + 0.145f, sz + 1f),
                        new Vector3(cx + 0.45f, y + 0.145f, sz + 1f), new Vector3(cx + 0.45f, y + 0.145f, sz + 0.1f), MeshKit.Glow(new Color(0.1f, 0.2f, 0.16f), 0.18f));
                    k.Quad(new Vector3(cx - 0.02f, y + 0.15f, sz + 0.1f), new Vector3(cx - 0.02f, y + 0.15f, sz + 1f),
                        new Vector3(cx + 0.02f, y + 0.15f, sz + 1f), new Vector3(cx + 0.02f, y + 0.15f, sz + 0.1f), P.Metal);
                }
            }
            // Panel seams across the roof every two metres, so large roofs are not one flat sheet.
            var seam = new Color(0.3f, 0.33f, 0.35f);
            for (var sx = Mathf.Ceil(x0 + rim); sx < x1 - rim; sx += 2f)
                k.Quad(new Vector3(sx, y + 0.002f, z0 + rim), new Vector3(sx, y + 0.002f, z1 - rim), new Vector3(sx + 0.05f, y + 0.002f, z1 - rim), new Vector3(sx + 0.05f, y + 0.002f, z0 + rim), seam);
            for (var sz2 = Mathf.Ceil(z0 + rim); sz2 < z1 - rim; sz2 += 2f)
                k.Quad(new Vector3(x0 + rim, y + 0.002f, sz2), new Vector3(x0 + rim, y + 0.002f, sz2 + 0.05f), new Vector3(x1 - rim, y + 0.002f, sz2 + 0.05f), new Vector3(x1 - rim, y + 0.002f, sz2), seam);
            // A roof hatch and an antenna with a blinking-green tip.
            var hatch = new Vector3(R(innerX0, innerX1), y, R(innerZ0, innerZ1));
            k.BevelBox(hatch, new Vector3(0.9f, 0.18f, 0.9f), 0.05f, P.Metal, P.MetalLight);
            k.Quad(hatch + new Vector3(-0.3f, 0.185f, -0.05f), hatch + new Vector3(-0.3f, 0.185f, 0.05f), hatch + new Vector3(0.3f, 0.185f, 0.05f), hatch + new Vector3(0.3f, 0.185f, -0.05f), P.Hazard);
            var mast = new Vector3(R(innerX0, innerX1), y, R(innerZ0, innerZ1));
            k.Cylinder(mast, 0.05f, 1.4f, 6, P.MetalDark, true, P.Metal);
            k.Box(mast + Vector3.up * 1.4f, new Vector3(0.12f, 0.12f, 0.12f), MeshKit.Glow(P.Toxic, 0.9f), Faces.Bottom, MeshKit.Glow(P.Toxic, 0.9f));
            // A run of pipe along the back.
            var before = k.Transform;
            k.Transform = Matrix4x4.TRS(new Vector3(innerX0, y + 0.12f, innerZ1 - 0.2f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            k.Cylinder(Vector3.zero, 0.1f, innerX1 - innerX0, 8, P.Metal, true, P.MetalLight);
            k.Transform = before;
            return Finish(k, name);
        }

        private static Mesh Finish(MeshKit k, string name)
        {
            k.RemoveHiddenFaces();
            return k.ToMesh(name);
        }
    }
}
