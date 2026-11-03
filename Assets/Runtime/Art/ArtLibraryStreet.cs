using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Street pieces (owner, 2026-10-05): street lights and traffic lights along the roads, brown rain puddles for rainy
    /// stages, and wind-blown leaves and paper for normal stages. Proportions follow the owner's pack: a car is 4.6 m
    /// long, so a street light stands about 6.5 m tall with its lamp out over the road, and a traffic light 5 m with the
    /// signal on an arm across the lane.
    /// </summary>
    public static partial class ArtLibrary
    {
        /// <summary>The road furniture the story generator places (kept when other code-built props are cleared).</summary>
        public static readonly string[] StreetFurniture = { "street_light", "traffic_light" };

        private static readonly Color PoleMetal = new Color(0.27f, 0.3f, 0.3f);
        private static readonly Color PoleMetalLight = new Color(0.42f, 0.45f, 0.44f);
        private static readonly Color LampWarm = new Color(1f, 0.88f, 0.62f);

        private static void RegisterStreet()
        {
            Add("street_light", "Street light", Kind.Prop, new Vector2(0.35f, 0.35f), 6.5f, StreetLight, round: true);
            Add("traffic_light", "Traffic light", Kind.Prop, new Vector2(0.35f, 0.35f), 5.2f, TrafficLight, round: true);
            Add("rain_puddle", "Rain puddle (brown water)", Kind.Decoration, new Vector2(2.2f, 1.7f), 0f, RainPuddle);
            Add("wind_leaves", "Leaves (blown by the wind)", Kind.Decoration, Vector2.one, 0f, (k, s) => WindLeaves(k, s));
            Add("wind_paper", "Loose paper (blown by the wind)", Kind.Decoration, Vector2.one, 0f, (k, s) => WindPaper(k, s));
            _pieces["wind_leaves"].Wind = true;
            _pieces["wind_paper"].Wind = true;
        }

        // ------------------------------------------------------------------ street light

        /// <summary>A tapered steel pole, an arm reaching 1.8 m forward (+z) over the road, and a lamp head lit underneath.</summary>
        private static void StreetLight(MeshKit k, int seed)
        {
            k.Wear = new Vector4(0.35f, 0f, 0.3f, 0.25f);
            k.Cylinder(Vector3.zero, 0.2f, 0.45f, 8, PoleMetal, true, PoleMetalLight);
            k.Cylinder(new Vector3(0f, 0.45f, 0f), 0.11f, 2.6f, 8, PoleMetal, false);
            k.Cylinder(new Vector3(0f, 3.05f, 0f), 0.085f, 3.1f, 8, PoleMetal, true, PoleMetalLight);
            // The arm: a slim box rising slightly towards the lamp.
            k.Box(new Vector3(0f, 5.95f, 0.9f), new Vector3(0.12f, 0.12f, 1.85f), PoleMetal, Faces.None, PoleMetalLight);
            // The head: a flat housing with a warm lit lens underneath.
            k.Box(new Vector3(0f, 5.82f, 1.85f), new Vector3(0.42f, 0.2f, 0.8f), PoleMetal, Faces.None, PoleMetalLight);
            k.Wear = Vector4.zero;
            k.Quad(new Vector3(-0.17f, 5.81f, 1.5f), new Vector3(0.17f, 5.81f, 1.5f), new Vector3(0.17f, 5.81f, 2.2f), new Vector3(-0.17f, 5.81f, 2.2f), MeshKit.Glow(LampWarm, 0.95f));
            // A small lit top so the lamp reads from the game's high camera.
            k.Quad(new Vector3(-0.12f, 6.025f, 1.65f), new Vector3(-0.12f, 6.025f, 2.05f), new Vector3(0.12f, 6.025f, 2.05f), new Vector3(0.12f, 6.025f, 1.65f), MeshKit.Glow(LampWarm, 0.5f));
        }

        // ------------------------------------------------------------------ traffic light

        /// <summary>
        /// A pole with an arm reaching 2.6 m forward (+z) across the lane; the signal hangs at its end, its lamps facing
        /// along the road (±x): red lit one way, green the other. A small pedestrian signal on the pole.
        /// </summary>
        private static void TrafficLight(MeshKit k, int seed)
        {
            var housing = new Color(0.16f, 0.17f, 0.17f);
            var dark = new Color(0.08f, 0.08f, 0.08f);
            k.Wear = new Vector4(0.35f, 0f, 0.3f, 0.2f);
            k.Cylinder(Vector3.zero, 0.18f, 0.35f, 8, PoleMetal, true, PoleMetalLight);
            k.Cylinder(new Vector3(0f, 0.35f, 0f), 0.09f, 4.75f, 8, PoleMetal, true, PoleMetalLight);
            k.Box(new Vector3(0f, 4.92f, 1.3f), new Vector3(0.11f, 0.11f, 2.65f), PoleMetal, Faces.None, PoleMetalLight);
            // The signal head under the arm's end, with a visor over each lamp row.
            k.Box(new Vector3(0f, 3.95f, 2.45f), new Vector3(0.34f, 0.95f, 0.34f), housing, Faces.None, housing);
            // A yellow backplate edge, so the signal reads against the dark road from the game's camera.
            k.Box(new Vector3(0f, 3.88f, 2.45f), new Vector3(0.3f, 1f, 0.5f), new Color(0.85f, 0.68f, 0.12f), Faces.None, new Color(0.95f, 0.78f, 0.2f));
            k.Box(new Vector3(0f, 2.3f, 0.18f), new Vector3(0.26f, 0.5f, 0.24f), housing, Faces.None, housing);
            k.Wear = Vector4.zero;
            var red = new Color(1f, 0.16f, 0.1f);
            var amber = new Color(1f, 0.65f, 0.1f);
            var green = new Color(0.25f, 1f, 0.45f);
            foreach (var side in new[] { -1f, 1f })
            {
                var lit = side < 0f ? 0 : 2;
                var x = side * 0.172f;
                for (var i = 0; i < 3; i++)
                {
                    var y = 4.65f - i * 0.3f;
                    var colour = i == 0 ? red : i == 1 ? amber : green;
                    var lamp = i == lit ? MeshKit.Glow(colour, 0.95f) : MeshKit.Shade(colour, 0.25f);
                    // A square lens facing ±x.
                    k.Quad(new Vector3(x, y - 0.11f, 2.34f), new Vector3(x, y + 0.11f, 2.34f), new Vector3(x, y + 0.11f, 2.56f), new Vector3(x, y - 0.11f, 2.56f), lamp);
                    k.Quad(new Vector3(x, y - 0.11f, 2.56f), new Vector3(x, y + 0.11f, 2.56f), new Vector3(x, y + 0.11f, 2.34f), new Vector3(x, y - 0.11f, 2.34f), lamp);
                    k.Box(new Vector3(x + side * 0.07f, y + 0.12f, 2.45f), new Vector3(0.14f, 0.03f, 0.26f), dark, Faces.None, dark);
                }
                // The lit colour also shows on top of the head, so it reads from above.
                k.Quad(new Vector3(side < 0 ? -0.17f : 0f, 4.905f, 2.2f), new Vector3(side < 0 ? -0.17f : 0f, 4.905f, 2.7f),
                    new Vector3(side < 0 ? 0f : 0.17f, 4.905f, 2.7f), new Vector3(side < 0 ? 0f : 0.17f, 4.905f, 2.2f), MeshKit.Glow(side < 0 ? red : green, 0.95f));
            }
            // Pedestrian signal: a lit red hand panel facing -z.
            k.Quad(new Vector3(-0.1f, 2.1f, 0.055f), new Vector3(-0.1f, 2.5f, 0.055f), new Vector3(0.1f, 2.5f, 0.055f), new Vector3(0.1f, 2.1f, 0.055f), MeshKit.Glow(new Color(1f, 0.35f, 0.15f), 0.8f));
        }

        // ------------------------------------------------------------------ rain puddle

        /// <summary>
        /// Owner, 2026-10-05: puddles on the wet ground of rainy stages, brown dirty water rather than blue. A dark damp
        /// rim, muddy brown water, and a couple of pale sky streaks that make it read as wet.
        /// </summary>
        private static void RainPuddle(MeshKit k, int seed)
        {
            var r = Rng(seed);
            // One smooth, rounded outline (no jagged points), drawn three times: a thin damp rim, the water, a lighter middle.
            float p1 = R(r, 0f, 6.28f), p2 = R(r, 0f, 6.28f), w1 = R(r, 0.1f, 0.2f), w2 = R(r, 0.05f, 0.12f);
            float Edge(float a) => 1f + w1 * Mathf.Sin(a * 2f + p1) + w2 * Mathf.Sin(a * 3f + p2);
            void Pool(float size, Color colour, float y)
            {
                const int n = 28;
                for (var i = 0; i < n; i++)
                {
                    var a0 = i / (float)n * Mathf.PI * 2f;
                    var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                    var q0 = new Vector3(Mathf.Sin(a0) * 1.3f, 0f, Mathf.Cos(a0)) * (size * Edge(a0));
                    var q1 = new Vector3(Mathf.Sin(a1) * 1.3f, 0f, Mathf.Cos(a1)) * (size * Edge(a1));
                    k.Tri(new Vector3(0f, y, 0f), q0 + Vector3.up * y, q1 + Vector3.up * y, colour);
                }
            }
            Pool(0.82f, new Color(0.36f, 0.27f, 0.17f), DecorY);
            Pool(0.74f, new Color(0.43f, 0.32f, 0.2f), DecorY + 0.002f);
            Pool(0.45f, new Color(0.5f, 0.39f, 0.26f), DecorY + 0.003f);
            // Pale sky reflected in the water: a few soft streaks across it.
            var streak = new Color(0.74f, 0.7f, 0.62f);
            for (var i = 0; i < 3; i++)
            {
                var c = new Vector3(R(r, -0.4f, 0.4f), 0f, R(r, -0.25f, 0.3f));
                var a = new Vector3(-0.32f, 0f, 0.1f) * R(r, 0.5f, 1f);
                Strip(k, c - a, c + a, R(r, 0.05f, 0.09f), MeshKit.Shade(streak, R(r, 0.85f, 1.05f)), DecorY + 0.005f);
            }
        }

        // ------------------------------------------------------------------ wind-blown leaves and paper

        /// <summary>How many separate bits make up one wind decoration (each moves on its own at runtime).</summary>
        public static int WindBits(string id, int seed) => id == "wind_paper" ? 1 + (Mathf.Abs(seed) % 2) : 5 + (Mathf.Abs(seed) % 4);

        /// <summary>Where bit <paramref name="i"/> lies (local, flat), and its turn, from the seed.</summary>
        public static (Vector3 at, float yaw, int variant) WindBit(string id, int seed, int i)
        {
            var r = Rng(seed * 31 + i * 7 + 3);
            var spread = id == "wind_paper" ? 0.5f : 0.6f;
            return (new Vector3(R(r, -spread, spread), 0f, R(r, -spread * 0.8f, spread * 0.8f)), R(r, 0f, 360f), r.Next(0, 4));
        }

        private static Mesh[] _leafMeshes, _paperMeshes;

        /// <summary>One leaf (a small pointed shape, autumn colours), in four colour versions, shared by every leaf.</summary>
        public static Mesh LeafMesh(int variant)
        {
            if (_leafMeshes == null)
            {
                _leafMeshes = new Mesh[4];
                var colours = new[] { new Color(0.62f, 0.33f, 0.1f), new Color(0.75f, 0.55f, 0.15f), new Color(0.45f, 0.28f, 0.12f), new Color(0.5f, 0.5f, 0.18f) };
                for (var v = 0; v < 4; v++)
                {
                    var k = new MeshKit();
                    Leaf(k, Vector3.zero, 0f, colours[v], 0f);
                    _leafMeshes[v] = k.ToMesh($"Wind leaf {v}");
                }
            }
            return _leafMeshes[Mathf.Abs(variant) % 4];
        }

        /// <summary>One sheet of paper (A4 proportions, a fold line and a few printed lines), in four versions.</summary>
        public static Mesh PaperMesh(int variant)
        {
            if (_paperMeshes == null)
            {
                _paperMeshes = new Mesh[4];
                for (var v = 0; v < 4; v++)
                {
                    var k = new MeshKit();
                    Sheet(k, Vector3.zero, 0f, v, 0f);
                    _paperMeshes[v] = k.ToMesh($"Wind paper {v}");
                }
            }
            return _paperMeshes[Mathf.Abs(variant) % 4];
        }

        /// <summary>Static look (editor previews): the bits as they lie before the wind moves them.</summary>
        private static void WindLeaves(MeshKit k, int seed)
        {
            var colours = new[] { new Color(0.62f, 0.33f, 0.1f), new Color(0.75f, 0.55f, 0.15f), new Color(0.45f, 0.28f, 0.12f), new Color(0.5f, 0.5f, 0.18f) };
            for (var i = 0; i < WindBits("wind_leaves", seed); i++)
            {
                var (at, yaw, variant) = WindBit("wind_leaves", seed, i);
                Leaf(k, at, yaw, colours[variant], DecorY + i * 0.0006f);
            }
        }

        private static void WindPaper(MeshKit k, int seed)
        {
            for (var i = 0; i < WindBits("wind_paper", seed); i++)
            {
                var (at, yaw, variant) = WindBit("wind_paper", seed, i);
                Sheet(k, at, yaw, variant, DecorY + i * 0.0008f);
            }
        }

        /// <summary>A leaf about 12 cm long: a pointed diamond with a darker midrib, both faces drawn (it flips in the wind).</summary>
        private static void Leaf(MeshKit k, Vector3 at, float yaw, Color colour, float y)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float z) => at + q * new Vector3(x, 0f, z) + Vector3.up * y;
            var tip = P(0f, 0.075f);
            var stem = P(0f, -0.06f);
            var left = P(-0.042f, 0.005f);
            var right = P(0.042f, 0.005f);
            k.Quad(stem, left, tip, right, colour);
            k.Quad(stem, right, tip, left, MeshKit.Shade(colour, 0.8f));
            var rib = MeshKit.Shade(colour, 0.6f);
            k.Quad(P(-0.004f, -0.075f), P(-0.004f, 0.06f), P(0.004f, 0.06f), P(0.004f, -0.075f), rib);
        }

        /// <summary>A sheet about 21 × 30 cm, both faces drawn.</summary>
        private static void Sheet(MeshKit k, Vector3 at, float yaw, int variant, float y)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float z, float lift = 0f) => at + q * new Vector3(x, 0f, z) + Vector3.up * (y + lift);
            var paper = variant == 3 ? new Color(0.86f, 0.82f, 0.62f) : Color.Lerp(P_Paper, new Color(0.78f, 0.78f, 0.74f), variant * 0.3f);
            const float w = 0.105f, h = 0.15f;
            k.Quad(P(-w, -h), P(-w, h), P(w, h), P(w, -h), paper);
            k.Quad(P(w, -h), P(w, h), P(-w, h), P(-w, -h), MeshKit.Shade(paper, 0.82f));
            // Printed lines and a fold.
            var ink = new Color(0.45f, 0.47f, 0.52f);
            for (var i = 0; i < 5; i++)
            {
                var z = h - 0.04f - i * 0.045f;
                var len = i == 4 ? 0.05f : 0.075f;
                k.Quad(P(-0.075f, z - 0.006f, 0.0004f), P(-0.075f, z + 0.006f, 0.0004f), P(-0.075f + len * 2f, z + 0.006f, 0.0004f), P(-0.075f + len * 2f, z - 0.006f, 0.0004f), ink);
            }
            k.Quad(P(-w, -0.004f, 0.0003f), P(-w, 0.004f, 0.0003f), P(w, 0.004f, 0.0003f), P(w, -0.004f, 0.0003f), MeshKit.Shade(paper, 0.88f));
        }

        private static Color P_Paper => ArtPalette.Paper;
    }
}
