using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Layers 1 and 2 on the ground (owner, 2026-10-01/02): the base ground and the painted surfaces, drawn by the
    /// Zombie/Ground shader from a small control texture (one texel per cell), so painted cells come out as natural
    /// shapes rather than squares. Also bakes a soft contact shadow where the ground meets walls, cliffs, cover and props.
    /// </summary>
    public static class GroundLayer
    {
        /// <summary>Gives the stage's ground plane its material. <paramref name="footprints"/>: prop cells for contact shadows.</summary>
        public static Material Apply(Renderer ground, StageMap map, string baseGroundId, bool[,] footprints)
        {
            var material = ArtMaterials.NewGround();
            if (material == null || map == null) return null;
            material.name = $"Ground {map.name}";
            var w = map.width;
            var h = map.height;
            var control0 = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { name = "Control 0", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var control1 = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { name = "Control 1", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var p0 = new Color32[w * h];
            var p1 = new Color32[w * h];
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var s = map.SurfaceAt(x, z);
                byte On(Surface want) => s == want ? (byte)255 : (byte)0;
                p0[z * w + x] = new Color32(On(Surface.Floor), On(Surface.Grass), On(Surface.Grass2), On(Surface.Road));
                p1[z * w + x] = new Color32(On(Surface.Road2), Occlusion(map, footprints, x, z), 0, 255);
            }
            control0.SetPixels32(p0);
            control0.Apply(false, true);
            control1.SetPixels32(p1);
            control1.Apply(false, true);

            material.SetTexture("_Control0", control0);
            material.SetTexture("_Control1", control1);
            material.SetVector("_MapSize", new Vector4(w, h, 1f, 0f));
            // 2026-10-04: the fixed ground rule replaces the per-stage base ground presets: dirt inside, grass outside.
            void Tex(string property, string file)
            {
                var t = ArtMaterials.GroundTexture(file);
                if (t != null) material.SetTexture(property, t);
            }
            Tex("_Noise", "noise");
            Tex("_FloorTex", "floor");
            Tex("_DirtTex", "dirt");
            Tex("_LawnTex", "lawn");
            Tex("_GrassTex", "lawn");
            Tex("_Grass2Tex", "lawn");
            Tex("_RoadTex", "road");
            Tex("_Road2Tex", "road2");
            ground.sharedMaterial = material;
            return material;
        }

        /// <summary>How much a cell is darkened by solid things next to it (0..255).</summary>
        private static byte Occlusion(StageMap map, bool[,] footprints, int x, int z)
        {
            bool Solid(int cx, int cz)
            {
                if (cx < 0 || cz < 0 || cx >= map.width || cz >= map.height) return false;
                var t = map[cx, cz];
                // The map edge (a fence on open grass, 2026-10-04) casts no contact shadow.
                return (Tiles.IsSolid(t) && t != Tile.Cliff) || (footprints != null && footprints[cx, cz]);
            }
            if (map[x, z] == Tile.Cliff) return 0;
            if (Tiles.IsSolid(map[x, z])) return 255;
            var o = footprints != null && footprints[x, z] ? 0.55f : 0f;
            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if ((dx == 0 && dz == 0) || !Solid(x + dx, z + dz)) continue;
                // The light comes from the south (behind the camera): a solid to the north casts more on this cell.
                var weight = dx != 0 && dz != 0 ? 0.22f : 0.38f;
                if (dz > 0) weight *= 1.25f;
                o += weight;
            }
            return (byte)(Mathf.Clamp01(o) * 255f);
        }
    }
}
