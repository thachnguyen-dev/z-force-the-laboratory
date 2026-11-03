using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieGame
{
    /// <summary>
    /// Builds layers 2 and 3 of a stage from its map (owner, 2026-10-01): the flat surfaces, then the 3D blocks. Every
    /// cell's prefab goes at its X, Z, merged per material into chunks of 16 × 16 cells, so a whole stage is a few dozen
    /// draw calls and no per-cell objects.
    ///
    /// The camera never turns (D-003: tilted 60°, looking north), so of a block only the top and the south side can ever
    /// be seen. Blocks are cut down to the faces that face the camera when they are loaded (backs, bottoms and east/west
    /// sides go), and a south side is skipped too when the block in front of it is at least as tall and hides it.
    /// </summary>
    public static class TileAssembler
    {
        private const int Chunk = 16;

        /// <summary>Towards the camera (D-003): the camera looks along (0, −sin 60°, cos 60°).</summary>
        public static readonly Vector3 ToCamera = Quaternion.Euler(CameraRig.Tilt, 0f, 0f) * Vector3.back;
        /// <summary>
        /// The sun never moves (it is set to this on every stage); towards it, for shadow casting. Owner, 2026-10-07: the
        /// shadows fall toward the south-west, so the sun is in the north-east, 50 degrees up.
        /// </summary>
        public static readonly Quaternion SunRotation = Quaternion.Euler(50f, 225f, 0f);
        public static readonly Vector3 ToSun = SunRotation * Vector3.back;

        internal struct Part
        {
            public Mesh Mesh;
            public Material Material;
            public Matrix4x4 Local;
        }

        /// <summary>A block cut down to what the camera can see, split so a hidden south side can be skipped.</summary>
        internal class BlockParts
        {
            public readonly List<Part> Top = new List<Part>();
            public readonly List<Part> Front = new List<Part>();
        }

        /// <summary>
        /// Layer 3's grid blocks. Built-in art (<see cref="Art.BlockArt"/>) unless the stage's tile set gives a prefab for
        /// that cell type, in which case the prefab is cut down to its visible faces and merged the same way.
        /// </summary>
        public static void Build(StageMap map, TileSet set, Transform parent)
        {
            var root = new GameObject("Tiles").transform;
            root.SetParent(parent, false);
            var blockParts = new Dictionary<GameObject, BlockParts>();
            var kit = new Art.MeshKit();

            for (var cz = 0; cz < map.height; cz += Chunk)
            for (var cx = 0; cx < map.width; cx += Chunk)
            {
                kit.Clear();
                var byMaterial = new Dictionary<Material, List<CombineInstance>>();
                for (var z = cz; z < Mathf.Min(cz + Chunk, map.height); z++)
                for (var x = cx; x < Mathf.Min(cx + Chunk, map.width); x++)
                {
                    var tile = map[x, z];
                    if (!Tiles.IsBlock(tile)) continue;
                    var prefab = set != null ? set.Block(tile, x, z) : null;
                    if (prefab == null)
                    {
                        Art.BlockArt.Build(kit, map, x, z);
                        continue;
                    }
                    if (!blockParts.TryGetValue(prefab, out var parts)) blockParts[prefab] = parts = Cull(PartsOf(prefab));
                    var cell = Matrix4x4.Translate(StageMap.CellCenter(x, z));
                    Add(byMaterial, parts.Top, cell);
                    if (!FrontHidden(map, x, z, tile)) Add(byMaterial, parts.Front, cell);
                }
                kit.RemoveHiddenFaces();
                Art.PropLayer.Emit(kit.ToMesh($"Blocks {cx},{cz}"), root);
                Emit(byMaterial, root, $"Tiles {cx},{cz}");
            }
        }

        private static void Add(Dictionary<Material, List<CombineInstance>> byMaterial, List<Part> parts, Matrix4x4 cell)
        {
            foreach (var part in parts)
            {
                if (!byMaterial.TryGetValue(part.Material, out var instances)) byMaterial[part.Material] = instances = new List<CombineInstance>();
                for (var sub = 0; sub < part.Mesh.subMeshCount; sub++)
                    instances.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = sub, transform = cell * part.Local });
            }
        }

        /// <summary>
        /// A block's south side is covered when the block in the next cell south is at least as tall (a wall behind a
        /// wall, cover behind a wall…). On the map's south edge it stays: it faces off the map.
        /// </summary>
        internal static bool FrontHidden(StageMap map, int x, int z, Tile tile)
        {
            if (z == 0) return false;
            var front = map[x, z - 1];
            if (front == tile) return true;
            return Tiles.IsSolid(front) && Tiles.IsSolid(tile) && HeightOf(front) >= HeightOf(tile);
        }

        public static float HeightOf(Tile tile)
        {
            switch (tile)
            {
                case Tile.Cliff: return StageMapBuilder.CliffHeight;
                case Tile.Wall: return StageMapBuilder.WallHeight;
                case Tile.Cover: return StageMapBuilder.CoverHeight;
                case Tile.Bush: return Art.ArtLibrary.BushHeight;
                default: return 0f;
            }
        }

        /// <summary>One merged, static mesh per material. Owner, 2026-10-07: walls, cover and props cast shadows.</summary>
        internal static void Emit(Dictionary<Material, List<CombineInstance>> byMaterial, Transform root, string name, bool castShadows = true)
        {
            foreach (var pair in byMaterial)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.isStatic = true;
            }
        }

        private static readonly List<Part> Empty = new List<Part>();

        internal static List<Part> PartsOf(GameObject prefab)
        {
            var list = new List<Part>();
            var rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null) continue;
                list.Add(new Part { Mesh = filter.sharedMesh, Material = renderer.sharedMaterial, Local = rootInverse * filter.transform.localToWorldMatrix });
            }
            return list;
        }

        // ---- Hidden-face removal ----

        /// <summary>
        /// Keeps only the triangles facing the camera, baked into new meshes, sorted into the south side and the rest. A
        /// mesh that can't be read (import setting "Read/Write" off) is kept whole.
        /// </summary>
        internal static BlockParts Cull(List<Part> parts)
        {
            var result = new BlockParts();
            foreach (var part in parts)
            {
                if (!part.Mesh.isReadable)
                {
                    result.Top.Add(part);
                    continue;
                }
                var (top, front) = Split(part);
                if (top != null) result.Top.Add(new Part { Mesh = top, Material = part.Material, Local = Matrix4x4.identity });
                if (front != null) result.Front.Add(new Part { Mesh = front, Material = part.Material, Local = Matrix4x4.identity });
            }
            return result;
        }

        /// <summary>The visible triangles of one part, in block space: (top and other visible faces, south-facing faces).</summary>
        internal static (Mesh top, Mesh front) Split(Part part)
        {
            var mesh = part.Mesh;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var colors = mesh.colors;
            var normalMatrix = part.Local.inverse.transpose;
            var hasUv = uvs.Length == positions.Length;
            var hasColor = colors.Length == positions.Length;
            var hasNormal = normals.Length == positions.Length;
            var top = new MeshBuilder(hasUv, hasColor);
            var front = new MeshBuilder(hasUv, hasColor);
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var triangles = mesh.GetTriangles(sub);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var pa = part.Local.MultiplyPoint3x4(positions[a]);
                    var pb = part.Local.MultiplyPoint3x4(positions[b]);
                    var pc = part.Local.MultiplyPoint3x4(positions[c]);
                    // Unity's front faces wind clockwise; this cross product is their outward normal (as Plane(a, b, c)).
                    var faceNormal = Vector3.Cross(pb - pa, pc - pa);
                    if (faceNormal.sqrMagnitude < 1e-12f) continue;
                    faceNormal.Normalize();
                    // Owner, 2026-10-07: faces towards the sun stay too (they cast the shadow).
                    if (!Art.MeshCull.Keep(faceNormal) || Art.MeshCull.Buried(pa, pb, pc)) continue;
                    var target = faceNormal.z < -0.5f && Mathf.Abs(faceNormal.y) < 0.5f ? front : top;
                    foreach (var v in new[] { a, b, c })
                    {
                        var n = hasNormal ? normalMatrix.MultiplyVector(normals[v]).normalized : faceNormal;
                        target.Add(part.Local.MultiplyPoint3x4(positions[v]), n, hasUv ? uvs[v] : Vector2.zero, hasColor ? colors[v] : Color.white);
                    }
                }
            }
            return (top.Build(mesh.name + " top"), front.Build(mesh.name + " front"));
        }

        private class MeshBuilder
        {
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs;
            private readonly List<Color> _colors;

            public MeshBuilder(bool uvs, bool colors)
            {
                _uvs = uvs ? new List<Vector2>() : null;
                _colors = colors ? new List<Color>() : null;
            }

            public void Add(Vector3 position, Vector3 normal, Vector2 uv, Color color)
            {
                _positions.Add(position);
                _normals.Add(normal);
                _uvs?.Add(uv);
                _colors?.Add(color);
            }

            public Mesh Build(string name)
            {
                if (_positions.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = _positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                if (_uvs != null) mesh.SetUVs(0, _uvs);
                if (_colors != null) mesh.SetColors(_colors);
                var indices = new int[_positions.Count];
                for (var i = 0; i < indices.Length; i++) indices[i] = i;
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---- Placeholders (tests, or no tile set) ----

        private static Mesh _cube;

        private static Mesh Cube()
        {
            if (_cube == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = temp.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Object.Destroy(temp);
                else Object.DestroyImmediate(temp);
            }
            return _cube;
        }

        private static Part Box(float height, Color color) => new Part
        {
            Mesh = Cube(), Material = Primitives.Material(color),
            Local = Matrix4x4.TRS(new Vector3(0f, height * 0.5f, 0f), Quaternion.identity, new Vector3(1f, height, 1f)),
        };

        /// <summary>⚠ Used only when no tile set exists (tests): plain boxes in the placeholder colours.</summary>
        internal static List<Part> PlaceholderBlock(Tile tile)
        {
            switch (tile)
            {
                case Tile.Wall: return new List<Part> { Box(StageMapBuilder.WallHeight, StageMapBuilder.WallColor) };
                case Tile.Cliff: return new List<Part> { Box(StageMapBuilder.CliffHeight, StageMapBuilder.CliffColor) };
                case Tile.Cover: return new List<Part> { Box(StageMapBuilder.CoverHeight, StageMapBuilder.CoverColor) };
                case Tile.Bush: return new List<Part> { Box(Art.ArtLibrary.BushHeight, new Color(0.22f, 0.45f, 0.2f)) };
                default: return new List<Part>();
            }
        }

    }
}
