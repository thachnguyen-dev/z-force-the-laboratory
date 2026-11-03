using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The base ground (owner, 2026-10-01): the bottom layer under the whole stage. It is simple and mostly uniform, so
    /// it never competes with the map blocks or decorations, and each stage picks a preset. The texture is generated (a
    /// soft, low-contrast mottle that repeats every <see cref="RepeatMetres"/>), so no image is needed.
    /// ⚠ Placeholder colours.
    /// </summary>
    public static class BaseGround
    {
        public const string DefaultId = "charcoal";
        public const float RepeatMetres = 16f;
        private const int Size = 256;

        public struct Preset
        {
            public string Id;
            public string Name;
            public Color Dark;
            public Color Light;

            public Preset(string id, string name, Color dark, Color light)
            {
                Id = id;
                Name = name;
                Dark = dark;
                Light = light;
            }
        }

        public static readonly Preset[] Presets =
        {
            new Preset("charcoal", "Dirt", new Color(0.44f, 0.32f, 0.2f), new Color(0.49f, 0.36f, 0.23f)),
            new Preset("moss", "Dark dirt", new Color(0.34f, 0.25f, 0.16f), new Color(0.39f, 0.29f, 0.19f)),
            new Preset("earth", "Light dirt", new Color(0.52f, 0.4f, 0.26f), new Color(0.57f, 0.44f, 0.29f)),
            new Preset("ash", "Red dirt", new Color(0.46f, 0.29f, 0.19f), new Color(0.51f, 0.33f, 0.22f)),
            new Preset("rust", "Sandy dirt", new Color(0.55f, 0.45f, 0.3f), new Color(0.6f, 0.49f, 0.33f)),
        };

        public static Preset Find(string id)
        {
            foreach (var p in Presets)
                if (p.Id == id) return p;
            return Presets[0];
        }

        public static bool Exists(string id)
        {
            foreach (var p in Presets)
                if (p.Id == id) return true;
            return false;
        }

        public static string[] Ids()
        {
            var ids = new string[Presets.Length];
            for (var i = 0; i < ids.Length; i++) ids[i] = Presets[i].Id;
            return ids;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> Textures = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>The preset's repeating texture (made once, then kept).</summary>
        public static Texture2D TextureFor(string id)
        {
            var preset = Find(id);
            if (Textures.TryGetValue(preset.Id, out var t) && t != null) return t;
            return Textures[preset.Id] = Texture(preset);
        }

        /// <summary>A material for a ground surface <paramref name="metres"/> across whose UVs run 0..1.</summary>
        public static Material MaterialFor(string id, Vector2 metres)
        {
            var preset = Find(id);
            var material = new Material(Primitives.Material(Color.white)) { name = $"Base ground {preset.Id}", color = Color.white };
            var texture = TextureFor(preset.Id);
            var scale = new Vector2(metres.x / RepeatMetres, metres.y / RepeatMetres);
            material.mainTexture = texture;
            material.mainTextureScale = scale;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", scale);
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            return material;
        }

        private static Texture2D Texture(Preset preset)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = $"Base ground {preset.Id}", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[Size * Size];
            var random = new System.Random(preset.Id.Length * 7919 + preset.Id[0]);
            float[,] Lattice(int n)
            {
                var l = new float[n, n];
                for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++) l[i, j] = (float)random.NextDouble();
                return l;
            }
            var coarse = Lattice(4);
            var medium = Lattice(10);
            // Value noise on a wrapping lattice, so the texture repeats without a seam.
            float Sample(float[,] l, float u, float v)
            {
                var n = l.GetLength(0);
                float x = u * n, y = v * n;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);
                float L(int i, int j) => l[((i % n) + n) % n, ((j % n) + n) % n];
                return Mathf.Lerp(Mathf.Lerp(L(x0, y0), L(x0 + 1, y0), fx), Mathf.Lerp(L(x0, y0 + 1), L(x0 + 1, y0 + 1), fx), fy);
            }
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                float u = (float)x / Size, v = (float)y / Size;
                var t = Sample(coarse, u, v) * 0.6f + Sample(medium, u, v) * 0.3f + (float)random.NextDouble() * 0.1f;
                pixels[y * Size + x] = Color.Lerp(preset.Dark, preset.Light, t);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
