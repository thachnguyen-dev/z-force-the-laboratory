using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>Splits a mesh into connected pieces (triangles sharing a vertex position), for colouring by part.</summary>
    public static class PackParts
    {
        public class Part
        {
            public readonly List<int> Triangles = new List<int>();
            public Bounds Bounds;
            public float Area;
        }

        public static List<Part> Split(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            // Weld by position so seams in normals don't split a piece.
            var key = new Dictionary<Vector3Int, int>();
            var welded = new int[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 10000f);
                if (!key.TryGetValue(k, out var id)) key[k] = id = key.Count;
                welded[i] = id;
            }
            var parent = new int[key.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) a = parent[a] = parent[parent[a]];
                return a;
            }
            for (var i = 0; i < t.Length; i += 3)
            {
                int a = Find(welded[t[i]]), b = Find(welded[t[i + 1]]), c = Find(welded[t[i + 2]]);
                parent[b] = a;
                parent[Find(c)] = a;
            }
            var parts = new Dictionary<int, Part>();
            for (var i = 0; i < t.Length; i += 3)
            {
                var root = Find(welded[t[i]]);
                if (!parts.TryGetValue(root, out var p))
                {
                    parts[root] = p = new Part();
                    p.Bounds = new Bounds(v[t[i]], Vector3.zero);
                }
                p.Triangles.Add(i);
                for (var j = 0; j < 3; j++) p.Bounds.Encapsulate(v[t[i + j]]);
                p.Area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            }
            var list = new List<Part>(parts.Values);
            list.Sort((a, b) => b.Area.CompareTo(a.Area));
            return list;
        }
    }
}
