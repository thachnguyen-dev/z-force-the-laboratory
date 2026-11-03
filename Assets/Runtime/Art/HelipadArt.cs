using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The start area as a helicopter landing pad (owner, 2026-10-04): a dark grey concrete pad with a lighter rim, a
    /// white circle with a big "H", yellow corner marks and small green landing lights along the edge. Flat, just above
    /// the ground; looks only.
    /// </summary>
    public static class HelipadArt
    {
        private const float Y = 0.02f;

        public static Mesh Build(Rect area)
        {
            var k = new MeshKit();
            var c = new Vector3(area.center.x, 0f, area.center.y);
            var half = Mathf.Min(area.width, area.height) * 0.5f - 1f;
            var pad = new Color(0.27f, 0.29f, 0.32f);
            var rim = new Color(0.42f, 0.44f, 0.47f);
            var white = new Color(0.95f, 0.95f, 0.93f);
            var yellow = new Color(1f, 0.82f, 0.2f);
            void Rect(float x0, float z0, float x1, float z1, Color col, float lift = 0f) =>
                k.Quad(new Vector3(c.x + x0, Y + lift, c.z + z0), new Vector3(c.x + x0, Y + lift, c.z + z1),
                    new Vector3(c.x + x1, Y + lift, c.z + z1), new Vector3(c.x + x1, Y + lift, c.z + z0), col);
            // Rim, then the pad.
            Rect(-half - 0.4f, -half - 0.4f, half + 0.4f, half + 0.4f, rim);
            Rect(-half, -half, half, half, pad, 0.002f);
            // Concrete slab joints.
            for (var i = -2; i <= 2; i++)
            {
                var t = i * half / 3f;
                Rect(t - 0.03f, -half, t + 0.03f, half, MeshKit.Shade(pad, 0.8f), 0.003f);
                Rect(-half, t - 0.03f, half, t + 0.03f, MeshKit.Shade(pad, 0.8f), 0.003f);
            }
            // The circle.
            var r = half * 0.72f;
            const int n = 40;
            for (var i = 0; i < n; i++)
            {
                var a0 = i / (float)n * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Vector3 P(float rr, float a) => new Vector3(c.x + Mathf.Cos(a) * rr, Y + 0.004f, c.z + Mathf.Sin(a) * rr);
                k.Quad(P(r - 0.35f, a0), P(r - 0.35f, a1), P(r, a1), P(r, a0), white);
            }
            // The H.
            var hh = r * 0.55f;
            var hw = r * 0.4f;
            var bar = r * 0.13f;
            Rect(-hw - bar, -hh, -hw + bar, hh, white, 0.005f);
            Rect(hw - bar, -hh, hw + bar, hh, white, 0.005f);
            Rect(-hw, -bar, hw, bar, white, 0.005f);
            // Yellow corner marks.
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                Rect(sx * half - (sx > 0 ? 2f : 0f), sz * half - (sz > 0 ? 0.3f : 0f), sx * half + (sx > 0 ? 0f : 2f), sz * half + (sz > 0 ? 0f : 0.3f), yellow, 0.004f);
                Rect(sx * half - (sx > 0 ? 0.3f : 0f), sz * half - (sz > 0 ? 2f : 0f), sx * half + (sx > 0 ? 0f : 0.3f), sz * half + (sz > 0 ? 0f : 2f), yellow, 0.004f);
            }
            // Landing lights every 2 m along the edge.
            var light = MeshKit.Glow(new Color(0.4f, 1f, 0.45f), 0.85f);
            for (var t = -half + 1f; t <= half - 1f; t += 2f)
                foreach (var (x, z) in new[] { (t, -half - 0.2f), (t, half + 0.2f), (-half - 0.2f, t), (half + 0.2f, t) })
                    k.Box(new Vector3(c.x + x, 0f, c.z + z), new Vector3(0.18f, 0.08f, 0.18f), MeshKit.Shade(pad, 0.7f), Faces.Bottom, light);
            k.RemoveHiddenFaces();
            return k.ToMesh("Helipad");
        }
    }
}
