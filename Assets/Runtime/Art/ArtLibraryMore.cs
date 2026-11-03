using UnityEngine;
using P = ZombieGame.Art.ArtPalette;

namespace ZombieGame.Art
{
    /// <summary>
    /// More map pieces (2026-10-03, owner: better maps; Claude may add assets in the game's style). The quarantine camp
    /// (tents, an army truck, a watchtower, cones, signs), street furniture (dumpsters, benches, picnic tables, a
    /// fountain, tyre stacks), the reserve (log piles, stumps) and house interiors (beds, shelves, tables), plus lab
    /// server racks, flower beds and road lane dashes.
    /// </summary>
    public static partial class ArtLibrary
    {
        private static void RegisterMore()
        {
            Add("tent", "Army tent", Kind.Prop, new Vector2(3f, 2.4f), 1.9f, Tent);
            Add("truck", "Army truck", Kind.Prop, new Vector2(2.2f, 5f), 2.4f, Truck);
            Add("watchtower", "Watchtower", Kind.Prop, new Vector2(2f, 2f), 4.5f, Watchtower);
            Add("dumpster", "Dumpster", Kind.Prop, new Vector2(1.8f, 1f), 1.2f, Dumpster);
            Add("bench", "Bench", Kind.Prop, new Vector2(1.6f, 0.5f), 0.8f, Bench);
            Add("picnic_table", "Picnic table", Kind.Prop, new Vector2(1.8f, 1.6f), 0.8f, PicnicTable);
            Add("fountain", "Fountain", Kind.Prop, new Vector2(3.2f, 3.2f), 1.2f, Fountain, round: true);
            Add("tire_stack", "Tyre stack", Kind.Prop, new Vector2(0.9f, 0.9f), 0.9f, TireStack, round: true);
            Add("cones", "Traffic cones", Kind.Prop, new Vector2(1f, 0.6f), 0.7f, Cones);
            Add("sign", "Quarantine sign", Kind.Prop, new Vector2(1.2f, 0.3f), 1.8f, Sign);
            Add("log_pile", "Log pile", Kind.Prop, new Vector2(2f, 1.2f), 1f, LogPile);
            Add("stump", "Tree stump", Kind.Prop, new Vector2(0.8f, 0.8f), 0.5f, Stump, round: true);
            Add("bed", "Bed", Kind.Prop, new Vector2(1f, 2f), 0.6f, Bed);
            Add("shelf", "Shelf", Kind.Prop, new Vector2(1.6f, 0.5f), 1.9f, Shelf);
            Add("table", "Table", Kind.Prop, new Vector2(1.4f, 0.9f), 0.8f, Table);
            Add("server_rack", "Server rack", Kind.Prop, new Vector2(0.8f, 0.8f), 2f, ServerRack);
            Add("flower_bed", "Flower bed", Kind.Decoration, Vector2.one, 0f, FlowerBed);
            Add("lane_dash", "Lane dash", Kind.Decoration, Vector2.one, 0f, LaneDash);
            Add("blood_free_stain", "Goo stain", Kind.Decoration, Vector2.one, 0f,
                (k, s) => Splat(k, s, 0.6f, new Color(0.18f, 0.32f, 0.16f), new Color(0.24f, 0.42f, 0.2f)));
        }

        private static readonly Color Canvas = new Color(0.45f, 0.5f, 0.3f);
        private static readonly Color Army = new Color(0.36f, 0.42f, 0.26f);

        private static void Tent(MeshKit k, int seed)
        {
            // A ridge tent: two canvas slopes, a dark open flap on the front, guy ropes as dark pegs.
            float hx = 1.5f, hz = 1.2f, h = 1.9f;
            var c = Canvas;
            k.Quad(new Vector3(-hx, 0f, -hz), new Vector3(-hx, h, 0f), new Vector3(hx, h, 0f), new Vector3(hx, 0f, -hz), MeshKit.Shade(c, 1.05f));
            k.Quad(new Vector3(hx, 0f, hz), new Vector3(hx, h, 0f), new Vector3(-hx, h, 0f), new Vector3(-hx, 0f, hz), MeshKit.Shade(c, 0.8f));
            k.Tri(new Vector3(-hx, 0f, hz), new Vector3(-hx, h, 0f), new Vector3(-hx, 0f, -hz), MeshKit.Shade(c, 0.9f));
            k.Tri(new Vector3(hx, 0f, -hz), new Vector3(hx, h, 0f), new Vector3(hx, 0f, hz), MeshKit.Shade(c, 0.9f));
            // A red cross panel on the slope for some (medical tents).
            if (seed % 3 == 0)
            {
                var red = new Color(0.86f, 0.2f, 0.18f);
                k.Quad(new Vector3(-0.12f, 0.75f, -0.72f + 0.01f), new Vector3(-0.12f, 1.25f, -0.4f + 0.01f), new Vector3(0.12f, 1.25f, -0.4f + 0.01f), new Vector3(0.12f, 0.75f, -0.72f + 0.01f), red);
                k.Quad(new Vector3(-0.32f, 0.93f, -0.6f + 0.01f), new Vector3(-0.32f, 1.07f, -0.52f + 0.01f), new Vector3(0.32f, 1.07f, -0.52f + 0.01f), new Vector3(0.32f, 0.93f, -0.6f + 0.01f), red);
            }
            k.Box(new Vector3(0f, h, 0f), new Vector3(hx * 2f + 0.1f, 0.08f, 0.1f), MeshKit.Shade(c, 0.6f), Faces.Bottom);
            // Seams down the south slope, a lighter band near the ridge, and a dark open doorway in the east end.
            for (var i = -1; i <= 1; i++)
            {
                var sx = i * 0.95f;
                k.Quad(new Vector3(sx - 0.03f, 0.01f, -hz - 0.005f), new Vector3(sx - 0.03f, h, -0.005f), new Vector3(sx + 0.03f, h, -0.005f), new Vector3(sx + 0.03f, 0.01f, -hz - 0.005f), MeshKit.Shade(c, 0.65f));
            }
            k.Quad(new Vector3(-hx, h * 0.8f, -hz * 0.2f - 0.006f), new Vector3(-hx, h * 0.95f, -hz * 0.05f - 0.006f), new Vector3(hx, h * 0.95f, -hz * 0.05f - 0.006f), new Vector3(hx, h * 0.8f, -hz * 0.2f - 0.006f), MeshKit.Shade(c, 1.25f));
            k.Quad(new Vector3(-hx * 0.6f, 0.01f, -hz - 0.01f), new Vector3(-hx * 0.6f, 0.15f, -hz - 0.01f), new Vector3(hx * 0.6f, 0.15f, -hz - 0.01f), new Vector3(hx * 0.6f, 0.01f, -hz - 0.01f), MeshKit.Shade(c, 0.5f));
        }

        private static void Truck(MeshKit k, int seed)
        {
            // Wheels, a long bed with an olive canvas cover, a cab at the south end with a dark windscreen.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1.6f, 0.8f, 1.8f })
                k.BevelBox(new Vector3(x, 0f, z), new Vector3(0.32f, 0.8f, 0.8f), 0.12f, P.Black, new Color(0.14f, 0.14f, 0.16f));
            k.BevelBox(new Vector3(0f, 0.45f, 0f), new Vector3(2.1f, 0.4f, 4.9f), 0.1f, MeshKit.Shade(Army, 0.8f));
            k.BevelBox(new Vector3(0f, 0.85f, 0.75f), new Vector3(2.1f, 1.45f, 3.3f), 0.25f, Canvas, MeshKit.Shade(Canvas, 1.12f));
            k.BevelBox(new Vector3(0f, 0.85f, -1.7f), new Vector3(2f, 1.25f, 1.4f), 0.18f, Army, MeshKit.Shade(Army, 1.15f));
            k.Quad(new Vector3(-0.8f, 1.35f, -2.402f), new Vector3(-0.8f, 1.9f, -2.402f), new Vector3(0.8f, 1.9f, -2.402f), new Vector3(0.8f, 1.35f, -2.402f), new Color(0.22f, 0.32f, 0.4f));
            k.Quad(new Vector3(-0.9f, 0.95f, -2.402f), new Vector3(-0.9f, 1.15f, -2.402f), new Vector3(-0.55f, 1.15f, -2.402f), new Vector3(-0.55f, 0.95f, -2.402f), new Color(1f, 0.92f, 0.6f));
            k.Quad(new Vector3(0.55f, 0.95f, -2.402f), new Vector3(0.55f, 1.15f, -2.402f), new Vector3(0.9f, 1.15f, -2.402f), new Vector3(0.9f, 0.95f, -2.402f), new Color(1f, 0.92f, 0.6f));
            // A white star stencil on the cab roof.
            k.Tri(new Vector3(-0.3f, 2.105f, -1.7f), new Vector3(0f, 2.105f, -1.3f), new Vector3(0.3f, 2.105f, -1.7f), new Color(0.9f, 0.9f, 0.85f));
        }

        private static void Watchtower(MeshKit k, int seed)
        {
            var wood = P.Wood;
            foreach (var x in new[] { -0.8f, 0.8f })
            foreach (var z in new[] { -0.8f, 0.8f })
                k.Box(new Vector3(x, 0f, z), new Vector3(0.18f, 3f, 0.18f), MeshKit.Shade(wood, 0.8f), Faces.Bottom);
            // Cross braces on the front, the platform, sandbag-like walls, a pitched roof and a lamp.
            k.Quad(new Vector3(-0.8f, 0.3f, -0.9f), new Vector3(0.7f, 2.6f, -0.9f), new Vector3(0.8f, 2.5f, -0.9f), new Vector3(-0.7f, 0.2f, -0.9f), MeshKit.Shade(wood, 0.7f));
            k.BevelBox(new Vector3(0f, 3f, 0f), new Vector3(2f, 0.2f, 2f), 0.05f, wood, MeshKit.Shade(wood, 1.1f));
            k.Box(new Vector3(0f, 3.2f, -0.9f), new Vector3(2f, 0.7f, 0.2f), Army, Faces.Bottom, MeshKit.Shade(Army, 1.2f));
            k.Box(new Vector3(-0.9f, 3.2f, 0f), new Vector3(0.2f, 0.7f, 1.6f), Army, Faces.Bottom, MeshKit.Shade(Army, 1.2f));
            k.Box(new Vector3(0.9f, 3.2f, 0f), new Vector3(0.2f, 0.7f, 1.6f), Army, Faces.Bottom, MeshKit.Shade(Army, 1.2f));
            k.Quad(new Vector3(-1.2f, 4.2f, -1.2f), new Vector3(0f, 4.8f, 0f), new Vector3(1.2f, 4.2f, -1.2f), new Vector3(-1.2f, 4.2f, -1.2f), MeshKit.Shade(Army, 1.1f));
            k.Tri(new Vector3(-1.2f, 4.2f, -1.2f), new Vector3(0f, 4.8f, 0f), new Vector3(1.2f, 4.2f, -1.2f), MeshKit.Shade(Army, 1.15f));
            k.Tri(new Vector3(1.2f, 4.2f, -1.2f), new Vector3(0f, 4.8f, 0f), new Vector3(1.2f, 4.2f, 1.2f), MeshKit.Shade(Army, 0.9f));
            k.Tri(new Vector3(-1.2f, 4.2f, 1.2f), new Vector3(0f, 4.8f, 0f), new Vector3(-1.2f, 4.2f, -1.2f), MeshKit.Shade(Army, 0.9f));
            foreach (var x in new[] { -0.9f, 0.9f })
                k.Box(new Vector3(x, 3.9f, -0.9f), new Vector3(0.1f, 0.4f, 0.1f), MeshKit.Shade(wood, 0.8f), Faces.Bottom);
            k.Box(new Vector3(0.5f, 3.7f, -1.02f), new Vector3(0.3f, 0.18f, 0.12f), MeshKit.Glow(new Color(1f, 0.95f, 0.7f), 0.9f), Faces.Bottom, MeshKit.Glow(new Color(1f, 0.95f, 0.7f), 0.9f));
        }

        private static void Dumpster(MeshKit k, int seed)
        {
            var colour = seed % 2 == 0 ? new Color(0.25f, 0.5f, 0.38f) : new Color(0.3f, 0.42f, 0.62f);
            k.BevelBox(Vector3.zero, new Vector3(1.8f, 1.05f, 1f), 0.08f, colour, MeshKit.Shade(colour, 0.7f));
            k.Box(new Vector3(0f, 1.05f, 0.05f), new Vector3(1.85f, 0.1f, 1.05f), new Color(0.15f, 0.16f, 0.2f), Faces.Bottom);
            k.Quad(new Vector3(-0.6f, 0.4f, -0.502f), new Vector3(-0.6f, 0.6f, -0.502f), new Vector3(0.6f, 0.6f, -0.502f), new Vector3(0.6f, 0.4f, -0.502f), P.Hazard);
            // Trash bags spilling over.
            k.Blob(new Vector3(-0.4f, 0.95f, 0.1f), new Vector3(0.6f, 0.45f, 0.5f), seed, new Color(0.12f, 0.12f, 0.16f), new Color(0.22f, 0.22f, 0.28f), 0.2f);
        }

        private static void Bench(MeshKit k, int seed)
        {
            var wood = P.Wood;
            foreach (var x in new[] { -0.65f, 0.65f })
                k.Box(new Vector3(x, 0f, 0f), new Vector3(0.08f, 0.45f, 0.45f), P.MetalDark, Faces.Bottom);
            k.BevelBox(new Vector3(0f, 0.42f, 0f), new Vector3(1.6f, 0.07f, 0.45f), 0.02f, wood, MeshKit.Shade(wood, 1.1f));
            k.Box(new Vector3(0f, 0.5f, 0.2f), new Vector3(1.6f, 0.3f, 0.06f), MeshKit.Shade(wood, 0.9f), Faces.Bottom);
        }

        private static void PicnicTable(MeshKit k, int seed)
        {
            var wood = P.Wood;
            k.BevelBox(new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.08f, 0.8f), 0.02f, wood, MeshKit.Shade(wood, 1.12f));
            foreach (var z in new[] { -0.65f, 0.65f })
                k.BevelBox(new Vector3(0f, 0.38f, z), new Vector3(1.8f, 0.06f, 0.3f), 0.02f, MeshKit.Shade(wood, 0.9f), wood);
            foreach (var x in new[] { -0.7f, 0.7f })
                k.Box(new Vector3(x, 0f, 0f), new Vector3(0.1f, 0.65f, 1.5f), MeshKit.Shade(wood, 0.7f), Faces.Bottom);
        }

        private static void Fountain(MeshKit k, int seed)
        {
            // A dry, cracked fountain: stone basin, a pedestal, green goo pooled at the bottom.
            var stone = new Color(0.62f, 0.64f, 0.7f);
            k.Cylinder(Vector3.zero, 1.6f, 0.55f, 16, stone, true, MeshKit.Shade(stone, 1.15f));
            k.Cylinder(new Vector3(0f, 0.551f, 0f), 1.35f, 0.01f, 16, new Color(0.2f, 0.3f, 0.2f), true, MeshKit.Glow(new Color(0.3f, 0.6f, 0.28f), 0.15f));
            k.Cylinder(new Vector3(0f, 0.55f, 0f), 0.3f, 0.6f, 8, stone, true, MeshKit.Shade(stone, 1.1f));
            k.Cylinder(new Vector3(0f, 1.15f, 0f), 0.6f, 0.15f, 12, stone, true, MeshKit.Shade(stone, 1.15f));
        }

        private static void TireStack(MeshKit k, int seed)
        {
            for (var i = 0; i < 3; i++)
            {
                k.Cylinder(new Vector3(0.03f * i, i * 0.26f, -0.02f * i), 0.42f, 0.26f, 10, P.Black, true, new Color(0.14f, 0.14f, 0.17f));
                k.Cylinder(new Vector3(0.03f * i, i * 0.26f + 0.262f, -0.02f * i), 0.2f, 0.005f, 10, new Color(0.05f, 0.05f, 0.06f), true, new Color(0.05f, 0.05f, 0.06f));
            }
        }

        private static void Cones(MeshKit k, int seed)
        {
            foreach (var x in new[] { -0.32f, 0.32f })
            {
                k.Box(new Vector3(x, 0f, 0f), new Vector3(0.4f, 0.05f, 0.4f), P.Black, Faces.Bottom);
                k.Cylinder(new Vector3(x, 0.05f, 0f), 0.16f, 0.6f, 8, new Color(1f, 0.5f, 0.15f), true, null, 0.03f);
                k.Cylinder(new Vector3(x, 0.32f, 0f), 0.105f, 0.1f, 8, new Color(0.95f, 0.95f, 0.92f), false, null, 0.085f);
            }
        }

        private static void Sign(MeshKit k, int seed)
        {
            foreach (var x in new[] { -0.45f, 0.45f })
                k.Box(new Vector3(x, 0f, 0f), new Vector3(0.07f, 1.1f, 0.07f), P.MetalDark, Faces.Bottom);
            k.BevelBox(new Vector3(0f, 1f, 0f), new Vector3(1.2f, 0.8f, 0.08f), 0.03f, P.Hazard, MeshKit.Shade(P.Hazard, 1.05f));
            // "Biohazard" mark: a black ring and a dot.
            const int n = 10;
            for (var i = 0; i < n; i++)
            {
                var a0 = i / (float)n * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Vector3 Q(float r, float a) => new Vector3(Mathf.Cos(a) * r, 1.4f + Mathf.Sin(a) * r, -0.045f);
                k.Quad(Q(0.2f, a0), Q(0.28f, a0), Q(0.28f, a1), Q(0.2f, a1), P.Black);
            }
            k.Quad(new Vector3(-0.06f, 1.34f, -0.046f), new Vector3(-0.06f, 1.46f, -0.046f), new Vector3(0.06f, 1.46f, -0.046f), new Vector3(0.06f, 1.34f, -0.046f), P.Black);
            k.Quad(new Vector3(-0.5f, 1.1f, -0.046f), new Vector3(-0.5f, 1.16f, -0.046f), new Vector3(0.5f, 1.16f, -0.046f), new Vector3(0.5f, 1.1f, -0.046f), P.Black);
        }

        private static void LogPile(MeshKit k, int seed)
        {
            var bark = new Color(0.45f, 0.3f, 0.18f);
            var cut = new Color(0.82f, 0.66f, 0.42f);
            for (var row = 0; row < 3; row++)
            for (var i = 0; i < 3 - row; i++)
            {
                var before = k.Transform;
                k.Transform = before * Matrix4x4.TRS(new Vector3(-0.9f, 0.2f + row * 0.34f, -0.4f + i * 0.4f + row * 0.2f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
                k.Cylinder(Vector3.zero, 0.19f, 1.8f, 7, bark, true, cut);
                k.Transform = before;
            }
        }

        private static void Stump(MeshKit k, int seed)
        {
            k.Cylinder(Vector3.zero, 0.38f, 0.45f, 8, new Color(0.42f, 0.28f, 0.17f), true, new Color(0.8f, 0.63f, 0.4f), 0.34f);
            k.Cylinder(new Vector3(0f, 0.451f, 0f), 0.2f, 0.005f, 8, new Color(0.6f, 0.45f, 0.28f), true, new Color(0.6f, 0.45f, 0.28f));
        }

        private static void Bed(MeshKit k, int seed)
        {
            var frame = new Color(0.55f, 0.6f, 0.7f);
            k.Box(Vector3.zero, new Vector3(1f, 0.35f, 2f), frame, Faces.Bottom);
            k.BevelBox(new Vector3(0f, 0.35f, 0f), new Vector3(0.95f, 0.2f, 1.9f), 0.06f, new Color(0.9f, 0.9f, 0.86f));
            k.BevelBox(new Vector3(0f, 0.55f, 0.7f), new Vector3(0.75f, 0.12f, 0.35f), 0.05f, Color.white);
            k.BevelBox(new Vector3(0f, 0.55f, -0.25f), new Vector3(0.96f, 0.06f, 1.2f), 0.03f, seed % 2 == 0 ? new Color(0.3f, 0.5f, 0.7f) : new Color(0.7f, 0.4f, 0.35f));
        }

        private static void Shelf(MeshKit k, int seed)
        {
            var wood = P.WoodDark;
            k.Box(Vector3.zero, new Vector3(1.6f, 1.9f, 0.45f), wood, Faces.Bottom, MeshKit.Shade(wood, 1.15f));
            var r = Rng(seed);
            for (var s = 0; s < 3; s++)
            {
                var y = 0.2f + s * 0.58f;
                k.Quad(new Vector3(-0.72f, y, -0.226f), new Vector3(-0.72f, y + 0.48f, -0.226f), new Vector3(0.72f, y + 0.48f, -0.226f), new Vector3(0.72f, y, -0.226f), MeshKit.Shade(wood, 0.5f));
                for (var i = 0; i < 4; i++)
                {
                    var c = Color.HSVToRGB((float)r.NextDouble(), 0.5f, 0.85f);
                    k.Box(new Vector3(-0.55f + i * 0.36f, y, -0.12f), new Vector3(0.22f, R(r, 0.2f, 0.42f), 0.2f), c, Faces.Bottom);
                }
            }
        }

        private static void Table(MeshKit k, int seed)
        {
            var wood = P.Wood;
            foreach (var x in new[] { -0.6f, 0.6f })
            foreach (var z in new[] { -0.35f, 0.35f })
                k.Box(new Vector3(x, 0f, z), new Vector3(0.08f, 0.72f, 0.08f), MeshKit.Shade(wood, 0.7f), Faces.Bottom);
            k.BevelBox(new Vector3(0f, 0.72f, 0f), new Vector3(1.4f, 0.07f, 0.9f), 0.02f, wood, MeshKit.Shade(wood, 1.12f));
            k.Cylinder(new Vector3(0.3f, 0.79f, 0.1f), 0.08f, 0.14f, 8, new Color(0.9f, 0.9f, 0.88f), true);
        }

        private static void ServerRack(MeshKit k, int seed)
        {
            k.BevelBox(Vector3.zero, new Vector3(0.8f, 2f, 0.8f), 0.04f, P.MetalDark, P.Metal);
            for (var i = 0; i < 7; i++)
            {
                var y = 0.2f + i * 0.25f;
                k.Quad(new Vector3(-0.32f, y, -0.402f), new Vector3(-0.32f, y + 0.16f, -0.402f), new Vector3(0.32f, y + 0.16f, -0.402f), new Vector3(0.32f, y, -0.402f), P.Black);
                var light = (i + seed) % 3 == 0 ? MeshKit.Glow(new Color(1f, 0.3f, 0.2f), 0.9f) : MeshKit.Glow(P.Toxic, 0.9f);
                k.Quad(new Vector3(0.2f, y + 0.05f, -0.404f), new Vector3(0.2f, y + 0.1f, -0.404f), new Vector3(0.26f, y + 0.1f, -0.404f), new Vector3(0.26f, y + 0.05f, -0.404f), light);
            }
        }

        private static void FlowerBed(MeshKit k, int seed)
        {
            var r = Rng(seed);
            k.Blob(Vector3.zero, new Vector3(1.1f, 0.35f, 0.8f), seed, P.GreenDark, P.Green, 0.2f);
            for (var i = 0; i < 7; i++)
            {
                var c = i % 3 == 0 ? new Color(1f, 0.85f, 0.3f) : i % 3 == 1 ? new Color(0.95f, 0.45f, 0.55f) : new Color(0.75f, 0.55f, 0.95f);
                k.Box(new Vector3(R(r, -0.4f, 0.4f), 0.22f, R(r, -0.3f, 0.3f)), new Vector3(0.1f, 0.1f, 0.1f), c, Faces.Bottom, MeshKit.Shade(c, 1.1f));
            }
        }

        private static void LaneDash(MeshKit k, int seed)
        {
            var paint = new Color(0.95f, 0.85f, 0.4f);
            k.Quad(new Vector3(-0.55f, 0.006f, -0.07f), new Vector3(-0.55f, 0.006f, 0.07f), new Vector3(0.55f, 0.006f, 0.07f), new Vector3(0.55f, 0.006f, -0.07f), paint);
        }
    }
}
