using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Decorative details (owner, 2026-10-04: Claude may add details that aren't obstacles, such as road markings, trash,
    /// scraps of paper, scratch marks and blood stains). Flat, looks only: no collision, no effect on sight or bullets.
    /// Placed by the stage generator and freely in the Map Editor's Decoration tool.
    /// </summary>
    public static partial class ArtLibrary
    {
        private static void RegisterDecor()
        {
            Add("blood_stain", "Blood stain", Kind.Decoration, Vector2.one, 0f, BloodStain);
            Add("blood_trail", "Blood trail", Kind.Decoration, new Vector2(2.4f, 0.6f), 0f, BloodTrail);
            Add("claw_marks", "Scratch marks", Kind.Decoration, Vector2.one, 0f, ClawMarks);
            Add("trash", "Trash", Kind.Decoration, Vector2.one, 0f, Trash);
            Add("crosswalk", "Crosswalk", Kind.Decoration, new Vector2(4.5f, 3f), 0f, Crosswalk);
            Add("stop_line", "Stop line", Kind.Decoration, new Vector2(3f, 0.4f), 0f, StopLine);
        }

        private const float DecorY = 0.006f;

        /// <summary>A flat blob facing up: a fan of triangles with a wobbly edge.</summary>
        private static void Blob(MeshKit k, Vector3 centre, float radius, System.Random r, Color colour, float y = DecorY, float stretch = 1f)
        {
            const int n = 14;
            var radii = new float[n];
            for (var i = 0; i < n; i++) radii[i] = radius * R(r, 0.65f, 1.15f);
            var lift = Vector3.up * y;
            for (var i = 0; i < n; i++)
            {
                var a0 = i / (float)n * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Sin(a0) * radii[i] * stretch, 0f, Mathf.Cos(a0) * radii[i]);
                var p1 = new Vector3(Mathf.Sin(a1) * radii[(i + 1) % n] * stretch, 0f, Mathf.Cos(a1) * radii[(i + 1) % n]);
                // Clockwise seen from above, so it faces the camera.
                k.Tri(centre + lift, centre + p0 + lift, centre + p1 + lift, colour);
            }
        }

        // Owner, 2026-10-08: the infected blood is green (as the spray where zombies are shot, BloodSpray).
        private static readonly Color BloodDark = new Color(0.06f, 0.2f, 0.03f);
        private static readonly Color BloodWet = new Color(0.14f, 0.42f, 0.06f);

        private static void BloodStain(MeshKit k, int seed)
        {
            var r = Rng(seed);
            Blob(k, Vector3.zero, R(r, 0.35f, 0.55f), r, BloodDark);
            Blob(k, new Vector3(R(r, -0.08f, 0.08f), 0f, R(r, -0.08f, 0.08f)), R(r, 0.18f, 0.28f), r, BloodWet, DecorY + 0.002f);
            // Splashes thrown out to one side.
            var a = R(r, 0f, 6.28f);
            for (var i = 0; i < 7; i++)
            {
                var d = R(r, 0.5f, 0.95f);
                var b = a + R(r, -0.7f, 0.7f);
                Blob(k, new Vector3(Mathf.Sin(b) * d, 0f, Mathf.Cos(b) * d), R(r, 0.03f, 0.09f), r, BloodDark);
            }
        }

        private static void BloodTrail(MeshKit k, int seed)
        {
            var r = Rng(seed);
            // Drops and smears along x, thinning out towards the end (something was dragged).
            for (var i = 0; i < 9; i++)
            {
                var t = i / 8f;
                var p = new Vector3(Mathf.Lerp(-1.1f, 1.1f, t), 0f, R(r, -0.12f, 0.12f));
                Blob(k, p, Mathf.Lerp(0.2f, 0.06f, t) * R(r, 0.8f, 1.2f), r, i % 3 == 0 ? BloodWet : BloodDark, DecorY, 1.8f);
            }
        }

        private static void ClawMarks(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var dark = new Color(0.16f, 0.14f, 0.13f);
            var light = new Color(0.7f, 0.68f, 0.64f);
            var marks = r.Next(3, 5);
            var a = R(r, -0.3f, 0.3f);
            var along = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var side = new Vector3(along.z, 0f, -along.x);
            for (var i = 0; i < marks; i++)
            {
                var offset = side * ((i - (marks - 1) * 0.5f) * 0.14f) + along * R(r, -0.08f, 0.08f);
                var length = R(r, 0.6f, 0.9f);
                var w = 0.025f;
                var start = offset - along * length * 0.5f;
                var mid = offset;
                var end = offset + along * length * 0.5f + side * R(r, -0.05f, 0.05f);
                var y = Vector3.up * DecorY;
                // A gouge: a dark groove with a light scraped edge, tapering to a point at both ends.
                k.Tri(start + y, mid - side * w + y, mid + side * w + y, dark);
                k.Tri(mid - side * w + y, end + y, mid + side * w + y, dark);
                k.Tri(start + y, mid + side * w + y, mid + side * (w * 2.2f) + y, light);
            }
        }

        private static void Trash(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var colours = new[] { new Color(0.85f, 0.2f, 0.18f), new Color(0.25f, 0.55f, 0.85f), new Color(0.95f, 0.8f, 0.25f), new Color(0.9f, 0.9f, 0.88f) };
            // Flattened cans, a bottle, wrappers and a torn bag.
            for (var i = 0; i < r.Next(2, 4); i++)
                k.Box(new Vector3(R(r, -0.4f, 0.4f), 0f, R(r, -0.4f, 0.4f)), new Vector3(0.12f, 0.05f, 0.08f), colours[r.Next(colours.Length)]);
            if (r.NextDouble() < 0.6)
                k.Box(new Vector3(R(r, -0.3f, 0.3f), 0f, R(r, -0.3f, 0.3f)), new Vector3(0.08f, 0.07f, 0.26f), new Color(0.25f, 0.5f, 0.3f));
            for (var i = 0; i < r.Next(2, 5); i++)
            {
                var p = new Vector3(R(r, -0.5f, 0.5f), 0f, R(r, -0.5f, 0.5f));
                var b = R(r, 0f, 3.14f);
                var u = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 0.09f;
                var v = new Vector3(-u.z, 0f, u.x) * 0.8f;
                var y = Vector3.up * (DecorY + i * 0.002f);
                k.Quad(p - u - v + y, p - u + v + y, p + u + v + y, p + u - v + y, colours[r.Next(colours.Length)]);
            }
            if (r.NextDouble() < 0.4) Blob(k, new Vector3(R(r, -0.3f, 0.3f), 0f, R(r, -0.3f, 0.3f)), 0.2f, r, new Color(0.2f, 0.21f, 0.23f), 0.03f);
        }

        private static void Crosswalk(MeshKit k, int seed)
        {
            var paint = new Color(0.92f, 0.92f, 0.88f);
            // Stripes across the road (along x), 0.5 m wide with 0.45 m gaps; the crossing runs along z.
            for (var i = 0; i < 5; i++)
            {
                var x = -2f + i * 0.95f;
                k.Quad(new Vector3(x - 0.25f, DecorY, -1.5f), new Vector3(x - 0.25f, DecorY, 1.5f), new Vector3(x + 0.25f, DecorY, 1.5f), new Vector3(x + 0.25f, DecorY, -1.5f), paint);
            }
        }

        private static void StopLine(MeshKit k, int seed)
        {
            var paint = new Color(0.92f, 0.92f, 0.88f);
            k.Quad(new Vector3(-1.5f, DecorY, -0.17f), new Vector3(-1.5f, DecorY, 0.17f), new Vector3(1.5f, DecorY, 0.17f), new Vector3(1.5f, DecorY, -0.17f), paint);
        }
    }
}
