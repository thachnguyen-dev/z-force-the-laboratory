using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Optional prefab overrides for layer 3's grid blocks (walls, cliffs, cover, bushes). Empty slots use the built-in art
    /// (Art.BlockArt, 2026-10-02). Surfaces are drawn by the ground (Art.GroundLayer); the base ground is BaseGround.
    /// Swap this asset or any prefab in it to reskin every stage without touching the maps.
    /// </summary>
    [CreateAssetMenu(menuName = "Zombie/Tile Set")]
    public class TileSet : ScriptableObject
    {
        public const string ResourceName = "TileSet";

        [Header("Layer 3: 3D blocks (1 × 1 m footprint, pivot at the bottom centre)")]
        public GameObject wall;
        public GameObject cliff;
        public GameObject cover;
        public GameObject bush;
        /// <summary>
        /// Optional extra looks mixed cell by cell (the same way every time), e.g. crates, rocks, barrels and broken walls
        /// for cover. When a list has entries, the single prefab above is one more option.
        /// </summary>
        public GameObject[] wallVariants = new GameObject[0];
        public GameObject[] cliffVariants = new GameObject[0];
        public GameObject[] coverVariants = new GameObject[0];
        public GameObject[] bushVariants = new GameObject[0];

        /// <summary>The 3D block for a gameplay cell (null = none: ground and markers have no block).</summary>
        public GameObject Block(Tile tile, int x, int z)
        {
            switch (tile)
            {
                case Tile.Wall: return Mix(wall, wallVariants, x, z);
                case Tile.Cliff: return Mix(cliff, cliffVariants, x, z);
                case Tile.Cover: return Mix(cover, coverVariants, x, z);
                case Tile.Bush: return Mix(bush, bushVariants, x, z);
                default: return null;
            }
        }

        private static GameObject Mix(GameObject main, GameObject[] variants, int x, int z)
        {
            if (variants == null || variants.Length == 0) return main;
            var count = variants.Length + (main != null ? 1 : 0);
            var i = Hash(x, z) % count;
            if (i == variants.Length) return main;
            return variants[i] != null ? variants[i] : main;
        }

        private static GameObject Pick(GameObject[] options, int x, int z)
        {
            if (options == null || options.Length == 0) return null;
            var i = Hash(x, z) % options.Length;
            return options[i] != null ? options[i] : options[0];
        }

        private static int Hash(int x, int z) => ((x * 73856093) ^ (z * 19349663)) & 0x7fffffff;
    }
}
