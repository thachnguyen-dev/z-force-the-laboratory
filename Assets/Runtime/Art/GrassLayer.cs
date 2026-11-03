using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The owner's grass model (2026-10-04) replaces Claude's code-built bushes: every bush cell, and every bush prop, is
    /// drawn as clumps of it. Each clump is its own object sharing the model's mesh (all faces kept, no polygon
    /// reduction yet), so only clumps on screen are drawn. Hiding rules don't change: they follow the bush cells/props.
    /// Clumps are about as tall as a player (1.85 m, owner 2026-10-03). Owner, 2026-10-05: each clump sits exactly on its
    /// grid cell, never turned or mirrored, all alike, slightly wider than the cell so a bush area joins up.
    /// </summary>
    public static class GrassLayer
    {
        public const string Model = "grass";

        public static bool Available => PackArt.Has(Model);

        private static Transform _root;

        /// <summary>
        /// How wide a clump is drawn, in cells. The model is one cell wide; a little more makes neighbouring clumps overlap,
        /// so a bush area of many cells reads as one mass with no gaps (owner, 2026-10-05).
        /// </summary>
        public const float CellCover = 1.4f;

        /// <summary>Clumps for every bush cell of the map, each exactly on its cell.</summary>
        public static void BuildCells(StageMap map, Transform parent)
        {
            _root = null;
            if (!Available) return;
            for (var x = 0; x < map.width; x++)
            for (var z = 0; z < map.height; z++)
                if (map[x, z] == Tile.Bush) Clump(parent, x, z);
        }

        /// <summary>
        /// A bush prop: the cells it covers (by their centres, within its diameter), each with an aligned clump. Bush props
        /// are never turned or mirrored (owner, 2026-10-05).
        /// </summary>
        public static void Prop(Transform parent, Vector3 centre, float diameter, int seed)
        {
            var r = Mathf.Max(0.5f, diameter * 0.5f);
            for (var x = Mathf.FloorToInt(centre.x - r); x <= Mathf.FloorToInt(centre.x + r); x++)
            for (var z = Mathf.FloorToInt(centre.z - r); z <= Mathf.FloorToInt(centre.z + r); z++)
                if (new Vector2(x + 0.5f - centre.x, z + 0.5f - centre.z).sqrMagnitude <= r * r) Clump(parent, x, z);
        }

        /// <summary>
        /// One clump on cell (x, z): centred on it, not turned, not mirrored, not nudged, all the same size, height and
        /// shade, so a run of cells joins up cleanly (owner, 2026-10-05: hiding grass aligned to the grid).
        /// </summary>
        private static void Clump(Transform parent, int x, int z)
        {
            if (_root == null || _root.parent != parent)
            {
                _root = new GameObject("Grass").transform;
                _root.SetParent(parent, false);
            }
            var go = new GameObject("Grass clump");
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(new Vector3(x + 0.5f, 0f, z + 0.5f), Quaternion.identity);
            go.transform.localScale = new Vector3(CellCover, ArtLibrary.BushHeight, CellCover);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = PackProps.Mesh(Model, 0);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ArtMaterials.Stylized;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
