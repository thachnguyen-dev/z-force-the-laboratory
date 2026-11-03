using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieGame.Art
{
    /// <summary>
    /// Owner, 2026-10-07: meshes keep only the faces that can matter. The camera never turns (D-003) and the sun never moves,
    /// so a face of something that stands still is needed only if it faces the camera (to be seen) or the sun (to cast its
    /// shadow: a shadow drawn without the sun-facing faces would let light through). Faces turned away from both, and faces
    /// under the ground, are dropped.
    /// </summary>
    public static class MeshCull
    {
        /// <summary>
        /// True when a face with this (unnormalised) normal must stay. <paramref name="castsShadow"/> false (things inside
        /// buildings, which cast no shadow): only the faces the camera sees.
        /// </summary>
        public static bool Keep(Vector3 normal, float margin = 0.01f, bool castsShadow = true)
        {
            if (normal.sqrMagnitude < 1e-14f) return false;
            var n = normal.normalized;
            return Vector3.Dot(n, TileAssembler.ToCamera) > margin || (castsShadow && Vector3.Dot(n, TileAssembler.ToSun) > margin);
        }

        /// <summary>True when a whole triangle lies under the ground (never seen, never casting).</summary>
        public static bool Buried(Vector3 a, Vector3 b, Vector3 c) => a.y < -0.02f && b.y < -0.02f && c.y < -0.02f;

        private static readonly Dictionary<(Mesh, int, int, int, int, int), Mesh> Cache = new Dictionary<(Mesh, int, int, int, int, int), Mesh>();

        /// <summary>
        /// A copy of <paramref name="source"/> placed by <paramref name="place"/> (turn, pose, size, mirroring baked in) with
        /// only the faces that can matter; its own colours and texture coordinates untouched. Shared by every copy placed the
        /// same way (the turn to the nearest degree).
        /// </summary>
        /// <param name="lift">How far the posed model is raised to rest on the ground (overturned, on its side), baked in too:
        /// the faces under the ground are found where the model really stands (owner, 2026-10-08: an overturned car, wholly
        /// below zero before its lift, lost every face and became an invisible wall).</param>
        public static Mesh Placed(Mesh source, float yaw, Quaternion pose, float scale, bool flip, bool castsShadow = true, float lift = 0f)
        {
            var turn = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f));
            var poseKey = pose.GetHashCode();
            var key = (source, turn, poseKey * 31 + Mathf.RoundToInt(lift * 1000f), Mathf.RoundToInt(scale * 1000f), flip ? 1 : 0, castsShadow ? 0 : 1);
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var place = Matrix4x4.TRS(new Vector3(0f, lift, 0f), Quaternion.Euler(0f, turn, 0f) * pose, new Vector3(flip ? -scale : scale, scale, scale));
            mesh = Bake(source, place, $"{source.name} placed", castsShadow);
            Cache[key] = mesh;
            return mesh;
        }

        public static Mesh Bake(Mesh source, Matrix4x4 place, string name, bool castsShadow = true)
        {
            var v = source.vertices;
            var n = source.normals;
            var colors = source.colors;
            var normalMatrix = place.inverse.transpose;
            var mirrored = place.determinant < 0f;
            var outV = new Vector3[v.Length];
            for (var i = 0; i < v.Length; i++) outV[i] = place.MultiplyPoint3x4(v[i]);
            // The faces kept, per submesh; mirroring turns the winding round.
            var used = new int[v.Length];
            for (var i = 0; i < used.Length; i++) used[i] = -1;
            var subs = new List<List<int>>();
            var next = 0;
            for (var s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (var i = 0; i + 2 < tris.Length; i += 3)
                {
                    int i0 = tris[i], i1 = mirrored ? tris[i + 2] : tris[i + 1], i2 = mirrored ? tris[i + 1] : tris[i + 2];
                    Vector3 a = outV[i0], b = outV[i1], c = outV[i2];
                    if (!Keep(Vector3.Cross(b - a, c - a), 0.01f, castsShadow) || Buried(a, b, c)) continue;
                    foreach (var k in new[] { i0, i1, i2 })
                    {
                        if (used[k] < 0) used[k] = next++;
                        keep.Add(used[k]);
                    }
                }
                subs.Add(keep);
            }
            var mesh = new Mesh { name = name, indexFormat = next > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var nv = new Vector3[next];
            var nn = new Vector3[next];
            var nc = colors.Length == v.Length ? new Color[next] : null;
            for (var i = 0; i < v.Length; i++)
            {
                var j = used[i];
                if (j < 0) continue;
                nv[j] = outV[i];
                nn[j] = n.Length == v.Length ? (normalMatrix.MultiplyVector(n[i])).normalized : Vector3.up;
                if (nc != null) nc[j] = colors[i];
            }
            mesh.vertices = nv;
            mesh.normals = nn;
            if (nc != null) mesh.colors = nc;
            // Texture coordinates (pattern, wear, blood) as they were.
            var uv = new List<Vector4>();
            for (var ch = 0; ch < 4; ch++)
            {
                if (!source.HasVertexAttribute(VertexAttribute.TexCoord0 + ch)) continue;
                uv.Clear();
                source.GetUVs(ch, uv);
                if (uv.Count != v.Length) continue;
                var outUv = new Vector4[next];
                for (var i = 0; i < v.Length; i++) if (used[i] >= 0) outUv[used[i]] = uv[i];
                mesh.SetUVs(ch, outUv);
            }
            mesh.subMeshCount = subs.Count;
            for (var s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
