using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieGame.Art
{
    /// <summary>
    /// Builds the map art's meshes in code (owner, 2026-10-02: Claude makes all map graphics). Flat-shaded, chunky
    /// shapes with vertex colours; vertex alpha below 1 marks glowing parts (see the Zombie/Stylized shader).
    /// Faces are wound clockwise seen from outside (Unity's front faces).
    /// </summary>
    public class MeshKit
    {
        private readonly List<Vector3> _v = new List<Vector3>(256);
        private readonly List<Vector3> _n = new List<Vector3>(256);
        private readonly List<Color> _c = new List<Color>(256);
        /// <summary>Per-vertex wear (grime, cracks, chips, rust) and blood for the Stylized shader (2026-10-04).</summary>
        private readonly List<Vector4> _w = new List<Vector4>(256);
        private readonly List<float> _bl = new List<float>(256);

        /// <summary>Wear given to everything added from now on (zero = none, the default).</summary>
        public Vector4 Wear;
        public float Blood;
        private readonly List<int> _t = new List<int>(512);

        /// <summary>Applied to everything added (a piece's position, turn and size inside its prefab).</summary>
        public Matrix4x4 Transform = Matrix4x4.identity;

        public int VertexCount => _v.Count;

        public void Clear()
        {
            _v.Clear();
            _n.Clear();
            _c.Clear();
            _t.Clear();
            Transform = Matrix4x4.identity;
        }

        // ---- Basic faces ----

        /// <summary>A flat quad, corners clockwise seen from the front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            a = Transform.MultiplyPoint3x4(a);
            b = Transform.MultiplyPoint3x4(b);
            c = Transform.MultiplyPoint3x4(c);
            d = Transform.MultiplyPoint3x4(d);
            var n = Vector3.Cross(b - a, c - a).normalized;
            var i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (var k = 0; k < 4; k++)
            {
                _n.Add(n);
                _c.Add(color);
                _w.Add(Wear);
                _bl.Add(Blood);
            }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        /// <summary>A quad with a colour per corner (gradients: grime at the bottom, light at the top).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
        {
            var i = _v.Count;
            Quad(a, b, c, d, ca);
            _c[i + 1] = cb;
            _c[i + 2] = cc;
            _c[i + 3] = cd;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            a = Transform.MultiplyPoint3x4(a);
            b = Transform.MultiplyPoint3x4(b);
            c = Transform.MultiplyPoint3x4(c);
            var n = Vector3.Cross(b - a, c - a).normalized;
            var i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            for (var k = 0; k < 3; k++)
            {
                _n.Add(n);
                _c.Add(color);
                _w.Add(Wear);
                _bl.Add(Blood);
            }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        /// <summary>A smooth-shaded triangle (blobs, foliage).</summary>
        public void SmoothTri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc)
        {
            var i = _v.Count;
            _v.Add(Transform.MultiplyPoint3x4(a));
            _v.Add(Transform.MultiplyPoint3x4(b));
            _v.Add(Transform.MultiplyPoint3x4(c));
            _n.Add(Transform.MultiplyVector(na).normalized);
            _n.Add(Transform.MultiplyVector(nb).normalized);
            _n.Add(Transform.MultiplyVector(nc).normalized);
            _c.Add(ca); _c.Add(cb); _c.Add(cc);
            for (var k = 0; k < 3; k++)
            {
                _w.Add(Wear);
                _bl.Add(Blood);
            }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        // ---- Solids ----

        /// <summary>An axis-aligned box from its bottom centre. <paramref name="skip"/>: faces to leave out.</summary>
        public void Box(Vector3 bottomCenter, Vector3 size, Color color, Faces skip = Faces.Bottom, Color? top = null)
        {
            var h = new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var p = bottomCenter;
            float x0 = p.x - h.x, x1 = p.x + h.x, y0 = p.y, y1 = p.y + size.y, z0 = p.z - h.z, z1 = p.z + h.z;
            var topColor = top ?? Shade(color, 1.12f);
            if ((skip & Faces.Top) == 0) Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), topColor);
            if ((skip & Faces.South) == 0) Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), color);
            if ((skip & Faces.North) == 0) Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Shade(color, 0.8f));
            if ((skip & Faces.East) == 0) Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Shade(color, 0.9f));
            if ((skip & Faces.West) == 0) Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), Shade(color, 0.9f));
            if ((skip & Faces.Bottom) == 0) Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Shade(color, 0.6f));
        }

        /// <summary>
        /// A box with chamfered edges (the chunky cartoon look): the top and four sides are inset by
        /// <paramref name="bevel"/> and joined by slanted strips and corner triangles. No bottom.
        /// </summary>
        public void BevelBox(Vector3 bottomCenter, Vector3 size, float bevel, Color color, Color? top = null)
        {
            bevel = Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f);
            var topColor = top ?? Shade(color, 1.12f);
            var edgeColor = Color.Lerp(color, topColor, 0.5f);
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            var p = bottomCenter;
            float x0 = p.x - hx, x1 = p.x + hx, z0 = p.z - hz, z1 = p.z + hz, y0 = p.y, y1 = p.y + size.y;
            float ix0 = x0 + bevel, ix1 = x1 - bevel, iz0 = z0 + bevel, iz1 = z1 - bevel, iy1 = y1 - bevel;
            // Top.
            Quad(new Vector3(ix0, y1, iz0), new Vector3(ix0, y1, iz1), new Vector3(ix1, y1, iz1), new Vector3(ix1, y1, iz0), topColor);
            // Sides (vertical part, up to the bevel).
            Quad(new Vector3(ix0, y0, z0), new Vector3(ix0, iy1, z0), new Vector3(ix1, iy1, z0), new Vector3(ix1, y0, z0), color);
            Quad(new Vector3(ix1, y0, z1), new Vector3(ix1, iy1, z1), new Vector3(ix0, iy1, z1), new Vector3(ix0, y0, z1), Shade(color, 0.8f));
            Quad(new Vector3(x1, y0, iz0), new Vector3(x1, iy1, iz0), new Vector3(x1, iy1, iz1), new Vector3(x1, y0, iz1), Shade(color, 0.9f));
            Quad(new Vector3(x0, y0, iz1), new Vector3(x0, iy1, iz1), new Vector3(x0, iy1, iz0), new Vector3(x0, y0, iz0), Shade(color, 0.9f));
            // Vertical corner strips.
            Quad(new Vector3(x0, y0, iz0), new Vector3(x0, iy1, iz0), new Vector3(ix0, iy1, z0), new Vector3(ix0, y0, z0), Shade(color, 0.95f));
            Quad(new Vector3(ix1, y0, z0), new Vector3(ix1, iy1, z0), new Vector3(x1, iy1, iz0), new Vector3(x1, y0, iz0), Shade(color, 0.95f));
            Quad(new Vector3(x1, y0, iz1), new Vector3(x1, iy1, iz1), new Vector3(ix1, iy1, z1), new Vector3(ix1, y0, z1), Shade(color, 0.85f));
            Quad(new Vector3(ix0, y0, z1), new Vector3(ix0, iy1, z1), new Vector3(x0, iy1, iz1), new Vector3(x0, y0, iz1), Shade(color, 0.85f));
            // Top edge bevels.
            Quad(new Vector3(ix0, iy1, z0), new Vector3(ix0, y1, iz0), new Vector3(ix1, y1, iz0), new Vector3(ix1, iy1, z0), edgeColor);
            Quad(new Vector3(ix1, iy1, z1), new Vector3(ix1, y1, iz1), new Vector3(ix0, y1, iz1), new Vector3(ix0, iy1, z1), Shade(edgeColor, 0.85f));
            Quad(new Vector3(x1, iy1, iz0), new Vector3(ix1, y1, iz0), new Vector3(ix1, y1, iz1), new Vector3(x1, iy1, iz1), Shade(edgeColor, 0.92f));
            Quad(new Vector3(x0, iy1, iz1), new Vector3(ix0, y1, iz1), new Vector3(ix0, y1, iz0), new Vector3(x0, iy1, iz0), Shade(edgeColor, 0.92f));
            // Top corners.
            Tri(new Vector3(x0, iy1, iz0), new Vector3(ix0, y1, iz0), new Vector3(ix0, iy1, z0), edgeColor);
            Tri(new Vector3(ix1, iy1, z0), new Vector3(ix1, y1, iz0), new Vector3(x1, iy1, iz0), edgeColor);
            Tri(new Vector3(x1, iy1, iz1), new Vector3(ix1, y1, iz1), new Vector3(ix1, iy1, z1), Shade(edgeColor, 0.88f));
            Tri(new Vector3(ix0, iy1, z1), new Vector3(ix0, y1, iz1), new Vector3(x0, iy1, iz1), Shade(edgeColor, 0.88f));
        }

        /// <summary>An upright cylinder from its bottom centre (flat-shaded facets, chunky).</summary>
        public void Cylinder(Vector3 bottomCenter, float radius, float height, int segments, Color color, bool top = true, Color? topColor = null,
            float topRadius = -1f)
        {
            if (topRadius < 0f) topRadius = radius;
            var y0 = bottomCenter.y;
            var y1 = y0 + height;
            var capColor = topColor ?? Shade(color, 1.12f);
            for (var i = 0; i < segments; i++)
            {
                var a0 = (i + 0f) / segments * Mathf.PI * 2f;
                var a1 = (i + 1f) / segments * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var b0 = bottomCenter + d0 * radius;
                var b1 = bottomCenter + d1 * radius;
                var t0 = bottomCenter + d0 * topRadius + Vector3.up * height;
                var t1 = bottomCenter + d1 * topRadius + Vector3.up * height;
                // Facing outward: (b0, t0, t1, b1) is clockwise from outside.
                var side = Shade(color, 0.85f + 0.15f * Mathf.Max(0f, -Mathf.Sin((a0 + a1) * 0.5f)));
                Quad(b0, t0, t1, b1, side);
                if (top) Tri(new Vector3(bottomCenter.x, y1, bottomCenter.z), t1, t0, capColor);
            }
        }

        /// <summary>A lumpy rock: an icosphere pushed about by noise, flattened at the bottom, flat-shaded.</summary>
        public void Rock(Vector3 bottomCenter, Vector3 size, int seed, Color color, Color top, float lumpiness = 0.28f)
        {
            Sphere(bottomCenter, size, seed, lumpiness, (n, p) =>
            {
                var up = Mathf.Clamp01(n.y * 0.9f + 0.1f);
                return Color.Lerp(color, top, up * up);
            }, flat: true, flatBottom: true);
        }

        /// <summary>A soft blob (foliage): a smooth-shaded lumpy sphere, coloured by height.</summary>
        public void Blob(Vector3 bottomCenter, Vector3 size, int seed, Color bottom, Color top, float lumpiness = 0.18f)
        {
            Sphere(bottomCenter, size, seed, lumpiness, (n, p) => Color.Lerp(bottom, top, Mathf.Clamp01(n.y * 0.6f + 0.45f)), flat: false, flatBottom: true);
        }

        private delegate Color Colorer(Vector3 normal, Vector3 position);

        private void Sphere(Vector3 bottomCenter, Vector3 size, int seed, float lumpiness, Colorer colorer, bool flat, bool flatBottom)
        {
            Icosphere(1, out var verts, out var tris);
            var rng = new System.Random(seed);
            var offset = new Vector3((float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 50f);
            var center = bottomCenter + Vector3.up * size.y * 0.5f;
            var placed = new Vector3[verts.Count];
            for (var i = 0; i < verts.Count; i++)
            {
                var d = verts[i];
                var bump = 1f + (Noise3(d * 1.7f + offset) - 0.5f) * 2f * lumpiness;
                var p = new Vector3(d.x * size.x * 0.5f, d.y * size.y * 0.5f, d.z * size.z * 0.5f) * bump;
                if (flatBottom && p.y < -size.y * 0.42f) p.y = -size.y * 0.42f;
                placed[i] = center + p;
            }
            for (var i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = placed[tris[i]], b = placed[tris[i + 1]], c = placed[tris[i + 2]];
                if (flat)
                {
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    Tri(a, b, c, colorer(n, (a + b + c) / 3f));
                }
                else
                {
                    Vector3 na = verts[tris[i]], nb = verts[tris[i + 1]], nc = verts[tris[i + 2]];
                    SmoothTri(a, b, c, na, nb, nc, colorer(na, a), colorer(nb, b), colorer(nc, c));
                }
            }
        }

        private static List<Vector3> _icoVerts;
        private static List<int> _icoTris;

        /// <summary>A unit icosphere (subdivided once: 80 triangles). The table is already clockwise from outside in Unity's axes.</summary>
        private static void Icosphere(int subdivisions, out List<Vector3> verts, out List<int> tris)
        {
            if (_icoVerts != null)
            {
                verts = _icoVerts;
                tris = _icoTris;
                return;
            }
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (var i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (var s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var next = new List<int>();
                var vs = verts;
                int Mid(int a, int b)
                {
                    var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out var m)) return m;
                    vs.Add(((vs[a] + vs[b]) * 0.5f).normalized);
                    cache[key] = vs.Count - 1;
                    return vs.Count - 1;
                }
                for (var i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            _icoVerts = verts;
            _icoTris = tris;
        }

        // ---- Output ----

        /// <summary>
        /// Drops every triangle that can't matter (<see cref="MeshCull"/>: facing neither the fixed camera nor the sun, or under
        /// the ground), then the unused vertices. Safe for anything that doesn't move. Owner, 2026-10-07: the owner's models
        /// are culled too now (they were exempt, "no poly reduction yet").
        /// </summary>
        /// <summary>Vertex ranges that came from the owner's models (kept for reference; no longer exempt from culling).</summary>
        private readonly List<(int from, int to)> _keepAll = new List<(int from, int to)>();

        private bool KeptWhole(int vertex)
        {
            foreach (var (from, to) in _keepAll)
                if (vertex >= from && vertex < to) return true;
            return false;
        }

        /// <param name="castsShadow">False for pieces that cast no shadow (inside buildings): only the faces the camera sees.</param>
        public void RemoveHiddenFaces(bool castsShadow = true)
        {
            var keep = new List<int>(_t.Count);
            for (var i = 0; i + 2 < _t.Count; i += 3)
            {
                Vector3 a = _v[_t[i]], b = _v[_t[i + 1]], c = _v[_t[i + 2]];
                if (!MeshCull.Keep(Vector3.Cross(b - a, c - a), 0.01f, castsShadow) || MeshCull.Buried(a, b, c)) continue;
                keep.Add(_t[i]);
                keep.Add(_t[i + 1]);
                keep.Add(_t[i + 2]);
            }
            var remap = new Dictionary<int, int>();
            var v = new List<Vector3>();
            var nn = new List<Vector3>();
            var cc = new List<Color>();
            var ww = new List<Vector4>();
            var bb = new List<float>();
            for (var i = 0; i < keep.Count; i++)
            {
                if (!remap.TryGetValue(keep[i], out var j))
                {
                    j = v.Count;
                    remap[keep[i]] = j;
                    v.Add(_v[keep[i]]);
                    nn.Add(_n[keep[i]]);
                    cc.Add(_c[keep[i]]);
                    ww.Add(_w[keep[i]]);
                    bb.Add(_bl[keep[i]]);
                }
                keep[i] = j;
            }
            _keepAll.Clear();
            _v.Clear(); _v.AddRange(v);
            _n.Clear(); _n.AddRange(nn);
            _c.Clear(); _c.AddRange(cc);
            _w.Clear(); _w.AddRange(ww);
            _bl.Clear(); _bl.AddRange(bb);
            _t.Clear(); _t.AddRange(keep);
        }

        /// <summary>
        /// Appends a whole mesh (its vertex colours, normals and triangles) at <paramref name="place"/>. Pack meshes store
        /// linear colours; they're turned back to sRGB here because the kit converts on output.
        /// </summary>
        /// <param name="keepAllFaces">Exempt from RemoveHiddenFaces.</param>
        public void AddMesh(Mesh mesh, Matrix4x4 place, bool keepAllFaces = false)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var c = mesh.colors;
            var t = mesh.triangles;
            // Pack meshes carry their own wear (UV1) and blood (UV2.x).
            var wear = new List<Vector4>();
            var blood = new List<Vector2>();
            mesh.GetUVs(1, wear);
            mesh.GetUVs(2, blood);
            var m = Transform * place;
            var normalMatrix = m.inverse.transpose;
            var start = _v.Count;
            for (var i = 0; i < v.Length; i++)
            {
                _v.Add(m.MultiplyPoint3x4(v[i]));
                _n.Add(n.Length == v.Length ? normalMatrix.MultiplyVector(n[i]).normalized : Vector3.up);
                var col = c.Length == v.Length ? c[i] : Color.white;
                _c.Add(QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? new Color(Mathf.LinearToGammaSpace(col.r), Mathf.LinearToGammaSpace(col.g), Mathf.LinearToGammaSpace(col.b), col.a) : col);
                _w.Add(wear.Count == v.Length ? wear[i] : Wear);
                _bl.Add(blood.Count == v.Length ? blood[i].x : Blood);
            }
            // A mirrored placement turns the faces inside out unless the winding is turned round too.
            if (m.determinant < 0f)
                for (var i = 0; i + 2 < t.Length; i += 3)
                {
                    _t.Add(start + t[i]);
                    _t.Add(start + t[i + 2]);
                    _t.Add(start + t[i + 1]);
                }
            else
                foreach (var index in t) _t.Add(start + index);
            if (keepAllFaces) _keepAll.Add((start, _v.Count));
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            // Colours here are written as sRGB (like material colours); in a linear project vertex colours are used as-is,
            // so convert them, or every piece renders far lighter than designed.
            var shaded = Shaded();
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                var linear = new List<Color>(shaded.Count);
                foreach (var c in shaded) linear.Add(new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a));
                mesh.SetColors(linear);
            }
            else mesh.SetColors(shaded);
            // Wear only when something carries it, so plain map art stays as cheap as before.
            var anyWear = false;
            for (var i = 0; i < _w.Count && !anyWear; i++) anyWear = _w[i] != Vector4.zero || _bl[i] > 0f;
            if (anyWear)
            {
                mesh.SetUVs(1, _w);
                var b2 = new List<Vector2>(_bl.Count);
                foreach (var b in _bl) b2.Add(new Vector2(b, 0f));
                mesh.SetUVs(2, b2);
            }
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The colours with shading (owner, 2026-10-07: no flat blocks of colour): darker towards the bottom of the piece
        /// (when it has height), and a slow and a fine tonal variation in metres (grime, fading, stains), slightly cooler
        /// where darker. Glowing vertices keep their colour.
        /// </summary>
        private List<Color> Shaded()
        {
            var shaded = new List<Color>(_c.Count);
            if (_v.Count == 0) return shaded;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in _v)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            var tall = maxY - minY > 0.1f;
            for (var i = 0; i < _c.Count; i++)
            {
                var c = _c[i];
                if (c.a < 1f)
                {
                    shaded.Add(c);
                    continue;
                }
                var p = _v[i];
                var k = 0.86f + 0.2f * Noise3(p * 0.9f + new Vector3(7.3f, 0f, 1.9f)) + 0.1f * (Noise3(p * 4.5f) - 0.5f);
                if (tall) k *= Mathf.Lerp(0.8f, 1.05f, Mathf.InverseLerp(minY, maxY, p.y));
                var cool = Mathf.Clamp01(1f - k) * 0.5f;
                shaded.Add(new Color(Mathf.Clamp01(c.r * k * (1f - cool * 0.12f)), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k * (1f + cool * 0.12f)), c.a));
            }
            return shaded;
        }

        // ---- Helpers ----

        public static Color Shade(Color c, float f) => new Color(Mathf.Min(1f, c.r * f), Mathf.Min(1f, c.g * f), Mathf.Min(1f, c.b * f), c.a);

        /// <summary>A glowing version of a colour (vertex alpha below 1 = emissive in Zombie/Stylized).</summary>
        public static Color Glow(Color c, float amount = 1f) => new Color(c.r, c.g, c.b, 1f - Mathf.Clamp01(amount));

        /// <summary>Smooth value noise in 0..1 (deterministic, world-space safe).</summary>
        public static float Noise3(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float L(int x, int y, int z) => Hash01(x, y, z);
            var x00 = Mathf.Lerp(L(xi, yi, zi), L(xi + 1, yi, zi), fx);
            var x10 = Mathf.Lerp(L(xi, yi + 1, zi), L(xi + 1, yi + 1, zi), fx);
            var x01 = Mathf.Lerp(L(xi, yi, zi + 1), L(xi + 1, yi, zi + 1), fx);
            var x11 = Mathf.Lerp(L(xi, yi + 1, zi + 1), L(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        public static float Noise2(float x, float z) => Noise3(new Vector3(x, 0.5f, z));

        public static float Hash01(int x, int y, int z)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }

    [System.Flags]
    public enum Faces
    {
        None = 0,
        Top = 1,
        South = 2,
        North = 4,
        East = 8,
        West = 16,
        Bottom = 32,
        Hidden = North | East | West | Bottom,
    }
}
