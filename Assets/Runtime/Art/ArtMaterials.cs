using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>The map art's materials (Resources, so their shaders ship in builds).</summary>
    public static class ArtMaterials
    {
        public const string StylizedResource = "ZombieStylized";
        public const string GroundResource = "ZombieGround";
        public const string GroundTextures = "Art/Ground/";

        private static Material _stylized;

        /// <summary>The one material for every built-in map piece (vertex colours carry the colour).</summary>
        public static Material Stylized
        {
            get
            {
                if (_stylized != null) return _stylized;
                _stylized = Resources.Load<Material>(StylizedResource);
                if (_stylized == null)
                {
                    var shader = Shader.Find("Zombie/Stylized");
                    _stylized = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Simple Lit")) { name = StylizedResource };
                }
                return _stylized;
            }
        }

        public static Material NewGround()
        {
            var template = Resources.Load<Material>(GroundResource);
            if (template != null) return new Material(template);
            var shader = Shader.Find("Zombie/Ground");
            return shader != null ? new Material(shader) : null;
        }

        public static Texture2D GroundTexture(string name) => Resources.Load<Texture2D>(GroundTextures + name);
    }
}
