using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Paints the ground textures in code (owner, 2026-10-02: Claude makes the map graphics). Every texture repeats
    /// seamlessly: all noise wraps around, and every stamp is drawn wrapped across the edges. Style: chunky hand-painted
    /// cartoon, dark, with toxic-green accents. Written once to Resources/Art/Ground; delete a file to repaint it.
    /// </summary>
    public static class TextureForge
    {
        public const string Folder = "Assets/Zombie/Resources/" + Art.ArtMaterials.GroundTextures;
        private const int Size = 512;

        [MenuItem("Zombie/Developer (Claude)/Repaint Ground Textures")]
        public static void RepaintAll()
        {
            foreach (var name in new[] { "dirt", "lawn", "road", "road2", "floor", "noise", "wear", "camo" })
                File.Delete($"{Folder}{name}.png");
            PaintAll();
        }

        public static void PaintAll()
        {
            Directory.CreateDirectory(Folder);
            // 2026-10-04: new ground (owner: replace the old ground assets): dirt inside, lawn round the outside.
            Write("dirt", DirtGround);
            Write("lawn", LawnGround);
            Write("road", Asphalt);
            Write("road2", Dirt);
            Write("floor", LabFloor);
            Write("noise", NoiseTexture, 256, linear: true);
            Write("wear", WearTexture, 512, linear: true);
            Write("camo", CamoTexture, 512);
        }

        private delegate void Painter(Canvas c);

        private static void Write(string name, Painter paint, int size = Size, bool linear = false)
        {
            var path = $"{Folder}{name}.png";
            if (File.Exists(path)) return;
            var canvas = new Canvas(size);
            paint(canvas);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = !linear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            Debug.Log($"[ZOMBIE-ART] Painted {path}");
        }

        // ------------------------------------------------------------------ canvas and tiling noise

        private class Canvas
        {
            public readonly int N;
            public readonly Color[] Pixels;
            public readonly System.Random Rng;

            public Canvas(int n, int seed = 1)
            {
                N = n;
                Pixels = new Color[n * n];
                Rng = new System.Random(seed + n);
            }

            public float R() => (float)Rng.NextDouble();
            public float R(float a, float b) => a + R() * (b - a);

            public Color this[int x, int y]
            {
                get => Pixels[Wrap(y) * N + Wrap(x)];
                set => Pixels[Wrap(y) * N + Wrap(x)] = value;
            }

            public int Wrap(int v) => ((v % N) + N) % N;

            public void Fill(System.Func<float, float, Color> f)
            {
                for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++) Pixels[y * N + x] = f(x / (float)N, y / (float)N);
            }

            /// <summary>Blends a colour into a soft disc (wrapped).</summary>
            public void Dot(float cx, float cy, float radius, Color color, float alpha, float hardness = 0.6f)
            {
                var r = Mathf.CeilToInt(radius) + 1;
                for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    var d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.5f, radius);
                    if (d > 1f) continue;
                    var a = alpha * Mathf.Clamp01((1f - d) / Mathf.Max(0.01f, 1f - hardness));
                    var x = Mathf.RoundToInt(cx) + dx;
                    var y = Mathf.RoundToInt(cy) + dy;
                    this[x, y] = Color.Lerp(this[x, y], color, a);
                }
            }

            /// <summary>A tapered stroke (grass blades, scratches), wrapped.</summary>
            public void Stroke(float x0, float y0, float x1, float y1, float width0, float width1, Color color, float alpha)
            {
                var length = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                var steps = Mathf.Max(2, Mathf.CeilToInt(length * 1.5f));
                for (var i = 0; i <= steps; i++)
                {
                    var t = i / (float)steps;
                    Dot(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), Mathf.Lerp(width0, width1, t) * 0.5f, color, alpha, 0.75f);
                }
            }
        }

        /// <summary>Wrapping value noise on a lattice of <paramref name="period"/> cells over 0..1.</summary>
        private static float Noise(float u, float v, int period, int seed)
        {
            var x = u * period;
            var y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float L(int i, int j) => Art.MeshKit.Hash01(((i % period) + period) % period, seed, ((j % period) + period) % period);
            return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
        }

        private static float Fbm(float u, float v, int period, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (var o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, period << o, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>Wrapping cellular noise: distance to the nearest and second-nearest feature point (0..~1).</summary>
        private static Vector2 Cells(float u, float v, int period, int seed)
        {
            var x = u * period;
            var y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = 9f, f2 = 9f;
            for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = ((gx % period) + period) % period, wy = ((gy % period) + period) % period;
                var px = gx + Art.MeshKit.Hash01(wx, seed, wy);
                var py = gy + Art.MeshKit.Hash01(wx, seed + 1, wy);
                var d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                }
                else if (d < f2) f2 = d;
            }
            return new Vector2(f1, f2);
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // ------------------------------------------------------------------ the textures

        /// <summary>
        /// Draws one chunky grass blade with a dark shadow under it, so blades read at play distance (the camera shows
        /// about 50 pixels per metre and the texture has 128, so details are drawn about three times bigger than real).
        /// </summary>
        private static void Blade(Canvas c, float x, float y, float angle, float length, float width, Color color)
        {
            var dx = Mathf.Cos(angle) * length;
            var dy = Mathf.Sin(angle) * length;
            c.Stroke(x + 1.5f, y + 2f, x + dx + 1.5f, y + dy + 2f, width * 1.1f, 1.2f, Art.MeshKit.Shade(color, 0.45f), 0.55f);
            c.Stroke(x, y, x + dx, y + dy, width, 1f, color, 0.95f);
        }

        /// <summary>
        /// Concrete pavement (the "road" look): mid-grey slabs a metre wide, dark joints with moss in places, cracks and
        /// stains. Clearly lighter than the dark base ground, so roads read. 4 m.
        /// </summary>
        private static void Asphalt(Canvas c)
        {
            // 2026-10-04: roads are dark grey asphalt: fine grain and faded patches.
            var dark = C(0.2f, 0.21f, 0.23f);
            var light = C(0.27f, 0.28f, 0.3f);
            c.Fill((u, v) =>
            {
                var big = Fbm(u, v, 3, 31);
                var grain = Noise(u, v, 256, 32);
                var col = Color.Lerp(dark, light, Mathf.Clamp01(big * 0.8f + (grain - 0.5f) * 0.6f));
                if (Fbm(u, v, 2, 34) > 0.64f) col = Color.Lerp(col, C(0.17f, 0.18f, 0.2f), 0.55f);
                return col;
            });
            for (var i = 0; i < 2200; i++)
                c.Dot(c.R(0f, c.N), c.R(0f, c.N), c.R(0.6f, 1.3f), c.R() < 0.5f ? C(0.32f, 0.33f, 0.35f) : C(0.15f, 0.16f, 0.17f), 0.6f);
            // Owner, 2026-10-05: no cracks on the ground any more (they belong on buildings).
        }

        /// <summary>Packed dark dirt: mottled brown, a few chunky stones with a shadow and a lit top, weeds. 4 m.</summary>
        private static void Dirt(Canvas c)
        {
            var dark = C(0.55f, 0.42f, 0.27f);
            var light = C(0.65f, 0.51f, 0.34f);
            c.Fill((u, v) => Color.Lerp(dark, light, Mathf.Clamp01(Fbm(u, v, 4, 41) * 1.3f - 0.15f + (Fbm(u, v, 32, 42, 2) - 0.5f) * 0.2f)));
            for (var i = 0; i < 120; i++)
            {
                var x = c.R(0f, c.N);
                var y = c.R(0f, c.N);
                var r = c.R(3.5f, 8f);
                var tone = c.R(0.5f, 0.68f);
                c.Dot(x + 2f, y + 2.5f, r * 1.05f, C(0.36f, 0.27f, 0.17f), 0.65f, 0.4f);
                c.Dot(x, y, r, C(tone, tone * 0.88f, tone * 0.74f), 1f, 0.85f);
                c.Dot(x - r * 0.3f, y - r * 0.35f, r * 0.45f, C(tone * 1.35f, tone * 1.22f, tone * 1.05f), 0.8f, 0.5f);
            }
            for (var i = 0; i < 40; i++)
            {
                var x = c.R(0f, c.N);
                var y = c.R(0f, c.N);
                for (var b = 0; b < 4; b++)
                    Blade(c, x + c.R(-4f, 4f), y, -Mathf.PI * 0.5f + c.R(-1f, 1f), c.R(10f, 16f), 4.5f, C(0.38f, 0.62f, 0.24f));
            }
        }

        /// <summary>
        /// Lab floor: black rubber tiles (half a metre each, so four per side) with a soft bevel, dark grout, scuffs, the odd
        /// green-trimmed tile. 2 m.
        /// </summary>
        private static void LabFloor(Canvas c)
        {
            const int tiles = 4;
            var tile = C(0.86f, 0.87f, 0.86f);
            var grout = C(0.66f, 0.68f, 0.68f);
            c.Fill((u, v) =>
            {
                float tu = u * tiles, tv = v * tiles;
                int ti = Mathf.FloorToInt(tu), tj = Mathf.FloorToInt(tv);
                float fu = tu - ti, fv = tv - tj;
                var edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
                if (edge < 0.025f) return grout;
                var shade = Art.MeshKit.Hash01(ti, 51, tj) * 0.04f;
                var col = Color.Lerp(tile, C(0.93f, 0.93f, 0.92f), shade * 10f * 0.4f);
                // Soft bevel: lit towards the top-left, shaded towards the bottom-right.
                if (edge < 0.07f)
                {
                    var lit = fu < 0.5f && fv > 0.5f ? 1.25f : fu > 0.5f && fv < 0.5f ? 0.78f : 1f;
                    col = Art.MeshKit.Shade(col, lit);
                }
                var grime = Fbm(u, v, 4, 52);
                col = Color.Lerp(col, C(0.76f, 0.77f, 0.76f), Mathf.SmoothStep(0.55f, 0.8f, grime) * 0.6f);
                // Now and then a tile with a thin green trim.
                
                return col;
            });
            for (var i = 0; i < 140; i++)
            {
                var x = c.R(0f, c.N);
                var y = c.R(0f, c.N);
                var a = c.R(0f, 6.28f);
                var len = c.R(8f, 30f);
                c.Stroke(x, y, x + Mathf.Cos(a) * len, y + Mathf.Sin(a) * len, 1.4f, 0.6f, C(0.72f, 0.73f, 0.73f), 0.5f);
            }
            for (var i = 0; i < 40; i++)
                c.Dot(c.R(0f, c.N), c.R(0f, c.N), c.R(2f, 6f), C(0.78f, 0.79f, 0.79f), 0.5f);
        }

        /// <summary>Grey wrapping noise for breaking up surface edges (linear).</summary>
        /// <summary>
        /// Wear for the map's models (2026-10-04: cracks, damage, rust, blood), sampled in world space by the Stylized
        /// shader where a model's wear weights ask for it. R = grime blotches, G = cracks (1 on a crack), B = chipped
        /// paint (small specks), A = rust patches. 512 px for 4 m.
        /// </summary>
        private static void WearTexture(Canvas c)
        {
            var cracks = new Canvas(c.N, 71);
            var chips = new Canvas(c.N, 73);
            cracks.Fill((u, v) => Color.black);
            chips.Fill((u, v) => Color.black);
            // Branching cracks: random walks that fork now and then, thinning towards their ends.
            for (var i = 0; i < 26; i++)
            {
                var x = cracks.R(0f, c.N);
                var y = cracks.R(0f, c.N);
                var a = cracks.R(0f, 6.28f);
                var width = cracks.R(2f, 3.4f);
                var steps = cracks.Rng.Next(5, 11);
                for (var s = 0; s < steps; s++)
                {
                    a += cracks.R(-0.6f, 0.6f);
                    var len = cracks.R(8f, 22f);
                    var nx = x + Mathf.Cos(a) * len;
                    var ny = y + Mathf.Sin(a) * len;
                    var w1 = width * (1f - (s + 1f) / (steps + 1f));
                    cracks.Stroke(x, y, nx, ny, Mathf.Max(0.8f, width * (1f - s / (steps + 1f))), Mathf.Max(0.8f, w1), Color.white, 1f);
                    if (cracks.R() < 0.25f)
                    {
                        // A short fork.
                        var fa = a + cracks.R(0.6f, 1.2f) * (cracks.R() < 0.5f ? -1f : 1f);
                        cracks.Stroke(nx, ny, nx + Mathf.Cos(fa) * len * 0.6f, ny + Mathf.Sin(fa) * len * 0.6f, Mathf.Max(0.8f, w1 * 0.8f), 0.8f, Color.white, 1f);
                    }
                    x = nx;
                    y = ny;
                }
            }
            for (var i = 0; i < 900; i++) chips.Dot(chips.R(0f, c.N), chips.R(0f, c.N), chips.R(0.8f, 3.2f), Color.white, chips.R(0.6f, 1f), 0.8f);
            c.Fill((u, v) =>
            {
                var x = Mathf.Clamp((int)(u * c.N), 0, c.N - 1);
                var y = Mathf.Clamp((int)(v * c.N), 0, c.N - 1);
                var grime = Mathf.SmoothStep(0.3f, 0.75f, Fbm(u, v, 3, 81));
                var rust = Fbm(u, v, 4, 87);
                return new Color(grime, cracks.Pixels[y * c.N + x].r, chips.Pixels[y * c.N + x].r, rust);
            });
        }

        /// <summary>
        /// Woodland military camouflage (owner, 2026-10-04: trucks, jeeps and the helicopter): olive base with dark green,
        /// brown and black blotches, the usual four-colour woodland pattern. 512 px for about 6 m, tiling.
        /// </summary>
        private static void CamoTexture(Canvas c)
        {
            var olive = C(0.36f, 0.38f, 0.24f);
            var green = C(0.19f, 0.25f, 0.15f);
            var brown = C(0.35f, 0.26f, 0.17f);
            var black = C(0.09f, 0.09f, 0.08f);
            c.Fill((u, v) =>
            {
                // Three stretched blotch layers, each a thresholded noise, laid over the olive base.
                var a = Fbm(u * 1f, v * 1f, 3, 141, 3);
                var b = Fbm(u, v, 4, 157, 3);
                var d = Fbm(u, v, 5, 173, 3);
                var col = olive;
                if (a > 0.54f) col = green;
                if (b > 0.58f) col = brown;
                if (d > 0.63f) col = black;
                return col;
            });
        }

        // ------------------------------------------------------------------ branch camouflage (owner, 2026-10-06)

        /// <summary>
        /// Owner, 2026-10-06: each of the four characters wears the camouflage of a military branch (Claude's choice of
        /// four styles). Generic patterns in each branch's usual style, not copies of a real issue pattern:
        /// Army: four-colour woodland blotches. Marine Corps: digital woodland (square pixels, greens and coyote).
        /// Navy: blue-grey digital. Air Force: tiger stripe (horizontal brush strokes, sage, tan, olive, black).
        /// 512 px, tiling.
        /// </summary>
        public static void PaintBranchCamo()
        {
            Directory.CreateDirectory(Folder);
            foreach (var name in new[] { "camo_army", "camo_marines", "camo_navy", "camo_airforce", "camo_spaceforce", "camo_rival_red", "camo_rival_orange" }) File.Delete($"{Folder}{name}.png");
            Write("camo_army", c => Blotches(c, C(0.38f, 0.4f, 0.25f), C(0.2f, 0.27f, 0.15f), C(0.37f, 0.27f, 0.17f), C(0.09f, 0.09f, 0.08f), 211), 512);
            Write("camo_marines", c => Digital(c, C(0.46f, 0.43f, 0.3f), C(0.29f, 0.33f, 0.2f), C(0.2f, 0.25f, 0.15f), C(0.1f, 0.11f, 0.09f), 233), 512);
            Write("camo_navy", c => Digital(c, C(0.33f, 0.38f, 0.47f), C(0.2f, 0.25f, 0.36f), C(0.13f, 0.16f, 0.25f), C(0.07f, 0.08f, 0.11f), 251), 512);
            Write("camo_airforce", TigerStripe, 512);
            Write("camo_spaceforce", Multicam, 512);
            // Owner, 2026-10-11: the Vaccine stages' rival teams, in their map colours (red, orange): an urban digital
            // pattern in charcoal and crimson, and a desert blotch pattern in orange and brown.
            Write("camo_rival_red", c => Digital(c, C(0.2f, 0.19f, 0.2f), C(0.55f, 0.1f, 0.12f), C(0.34f, 0.07f, 0.09f), C(0.07f, 0.06f, 0.07f), 271), 512);
            Write("camo_rival_orange", c => Blotches(c, C(0.78f, 0.46f, 0.16f), C(0.56f, 0.32f, 0.13f), C(0.31f, 0.18f, 0.08f), C(0.08f, 0.07f, 0.06f), 283), 512);
        }

        /// <summary>
        /// Space Force (character 03, Claude's choice 2026-10-06): an OCP-style multi-terrain pattern. A tan base
        /// fading softly into khaki, soft olive and brown shapes, and small dark-brown flecks on top.
        /// </summary>
        private static void Multicam(Canvas c)
        {
            var tan = C(0.6f, 0.53f, 0.39f);
            var khaki = C(0.69f, 0.63f, 0.47f);
            var olive = C(0.41f, 0.41f, 0.27f);
            var brown = C(0.46f, 0.35f, 0.23f);
            var dark = C(0.26f, 0.2f, 0.13f);
            c.Fill((u, v) =>
            {
                var col = Color.Lerp(tan, khaki, Mathf.SmoothStep(0.4f, 0.65f, Fbm(u, v, 2, 301, 3)));
                col = Color.Lerp(col, olive, Mathf.SmoothStep(0.55f, 0.6f, Fbm(u, v, 4, 307, 3)));
                col = Color.Lerp(col, brown, Mathf.SmoothStep(0.6f, 0.64f, Fbm(u, v, 5, 311, 3)));
                if (Fbm(u, v, 12, 313, 2) > 0.66f) col = dark;
                return col;
            });
        }

        private static void Blotches(Canvas c, Color baseCol, Color a, Color b, Color d, int seed)
        {
            c.Fill((u, v) =>
            {
                var col = baseCol;
                if (Fbm(u, v, 3, seed, 3) > 0.54f) col = a;
                if (Fbm(u, v, 4, seed + 16, 3) > 0.58f) col = b;
                if (Fbm(u, v, 5, seed + 32, 3) > 0.63f) col = d;
                return col;
            });
        }

        /// <summary>Digital camouflage: the same blotch layers, sampled on a grid of square pixels (16 px each).</summary>
        private static void Digital(Canvas c, Color baseCol, Color a, Color b, Color d, int seed)
        {
            const float cell = 16f / 512f;
            c.Fill((u, v) =>
            {
                var cu = (Mathf.Floor(u / cell) + 0.5f) * cell;
                var cv = (Mathf.Floor(v / cell) + 0.5f) * cell;
                // A little per-pixel jitter, so the edges break into steps like the real thing.
                var j = (Noise(cu, cv, 64, seed + 7) - 0.5f) * 0.08f;
                var col = baseCol;
                if (Fbm(cu, cv, 4, seed, 3) + j > 0.52f) col = a;
                if (Fbm(cu, cv, 5, seed + 16, 3) + j > 0.57f) col = b;
                if (Fbm(cu, cv, 6, seed + 32, 3) + j > 0.67f) col = d;
                return col;
            });
        }

        /// <summary>Tiger stripe: horizontal, ragged brush strokes of olive and black over sage and tan.</summary>
        private static void TigerStripe(Canvas c)
        {
            var sage = C(0.47f, 0.48f, 0.38f);
            var tan = C(0.52f, 0.46f, 0.34f);
            var olive = C(0.29f, 0.31f, 0.2f);
            var black = C(0.08f, 0.08f, 0.07f);
            c.Fill((u, v) =>
            {
                // Strokes: a noise that changes only down the texture (v), bent by a slow wave across it, makes
                // horizontal bands; a second noise across (u) cuts them into separate brush strokes.
                var warp = (Fbm(u, v, 3, 281, 2) - 0.5f) * 0.12f;
                var y = Mathf.Repeat(v + warp, 1f);
                var col = Fbm(u, v, 2, 277, 2) > 0.5f ? sage : tan;
                if (Fbm(0.37f, y, 22, 271, 2) > 0.56f && Fbm(u, v, 4, 289, 2) > 0.4f) col = olive;
                if (Fbm(0.71f, y, 28, 283, 2) > 0.64f && Fbm(u, v, 5, 293, 2) > 0.5f) col = black;
                return col;
            });
        }

        /// <summary>
        /// The inner ground (owner, 2026-10-04: one fixed rule, brown dirt inside, green grass round the outside):
        /// trampled brown earth with darker damp patches, dry lighter crust, small stones with a lit top, and a
        /// tiling without stripes. 512 px for 4 m.
        /// </summary>
        private static void DirtGround(Canvas c)
        {
            var dark = C(0.36f, 0.26f, 0.17f);
            var mid = C(0.47f, 0.35f, 0.23f);
            var dry = C(0.56f, 0.44f, 0.3f);
            c.Fill((u, v) =>
            {
                var big = Fbm(u, v, 3, 211);
                var fine = Noise(u, v, 128, 212);
                var col = Color.Lerp(dark, mid, Mathf.SmoothStep(0.25f, 0.7f, big));
                col = Color.Lerp(col, dry, Mathf.SmoothStep(0.62f, 0.85f, Fbm(u, v, 4, 213)) * 0.7f);
                return Color.Lerp(col, col * (0.85f + fine * 0.3f), 0.8f);
            });
            // Owner, 2026-10-05: no cracks on the ground (they belong on buildings).
            // Pebbles: a soft shadow, the stone, a lit top.
            for (var i = 0; i < 260; i++)
            {
                var x = c.R(0f, c.N);
                var y = c.R(0f, c.N);
                var r = c.R(1.2f, 3.6f);
                var stone = Color.Lerp(C(0.5f, 0.45f, 0.38f), C(0.66f, 0.6f, 0.52f), c.R());
                c.Dot(x + 0.8f, y - 0.8f, r * 1.2f, C(0.25f, 0.18f, 0.12f), 0.45f);
                c.Dot(x, y, r, stone, 0.95f, 0.7f);
                c.Dot(x - r * 0.3f, y + r * 0.3f, r * 0.45f, Color.Lerp(stone, Color.white, 0.35f), 0.7f);
            }
        }

        /// <summary>
        /// The outer grass (owner, 2026-10-04): a dense lawn of short blades in several greens over a darker base, with a
        /// few lighter dry tufts. 512 px for 3 m.
        /// </summary>
        private static void LawnGround(Canvas c)
        {
            var baseDark = C(0.16f, 0.32f, 0.12f);
            var baseLight = C(0.24f, 0.42f, 0.16f);
            c.Fill((u, v) => Color.Lerp(baseDark, baseLight, Mathf.SmoothStep(0.2f, 0.8f, Fbm(u, v, 4, 231))));
            var blades = new[] { C(0.22f, 0.45f, 0.15f), C(0.3f, 0.55f, 0.2f), C(0.38f, 0.6f, 0.22f), C(0.18f, 0.38f, 0.13f) };
            for (var i = 0; i < 5200; i++)
            {
                var x = c.R(0f, c.N);
                var y = c.R(0f, c.N);
                var a = 1.57f + c.R(-0.6f, 0.6f);
                var len = c.R(5f, 12f);
                var col = blades[c.Rng.Next(blades.Length)];
                // Dry tufts here and there.
                if (Fbm(x / c.N, y / c.N, 3, 233) > 0.7f) col = Color.Lerp(col, C(0.55f, 0.55f, 0.28f), 0.5f);
                c.Stroke(x, y, x + Mathf.Cos(a) * len, y + Mathf.Sin(a) * len, 2.2f, 0.6f, col, 0.9f);
            }
        }

        private static void NoiseTexture(Canvas c)
        {
            c.Fill((u, v) =>
            {
                var n = Fbm(u, v, 4, 61, 5);
                return new Color(n, n, n, 1f);
            });
        }
    }
}
