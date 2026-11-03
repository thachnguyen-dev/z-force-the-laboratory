using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The map art's colours (owner: green + black, an abandoned research facility after a zombie-virus outbreak, chibi;
    /// 2026-10-03: brighter and clearly cartoon, not gloomy). Clean light concrete and painted metal, saturated greens,
    /// warm wood, with near-black kept for small accents and toxic neon green as the glowing accent.
    /// </summary>
    public static class ArtPalette
    {
        public static readonly Color Black = new Color(0.12f, 0.13f, 0.15f);
        public static readonly Color CharcoalDark = new Color(0.14f, 0.17f, 0.26f);
        public static readonly Color Charcoal = new Color(0.22f, 0.27f, 0.38f);
        public static readonly Color Slate = new Color(0.33f, 0.4f, 0.52f);
        public static readonly Color Concrete = new Color(0.66f, 0.67f, 0.63f);
        public static readonly Color ConcreteLight = new Color(0.82f, 0.83f, 0.78f);
        public static readonly Color MetalDark = new Color(0.3f, 0.36f, 0.4f);
        public static readonly Color Metal = new Color(0.5f, 0.58f, 0.62f);
        public static readonly Color MetalLight = new Color(0.72f, 0.79f, 0.8f);

        public static readonly Color Toxic = new Color(0.45f, 1f, 0.35f);
        public static readonly Color ToxicSoft = new Color(0.42f, 0.85f, 0.35f);
        public static readonly Color GreenDark = new Color(0.17f, 0.42f, 0.18f);
        public static readonly Color Green = new Color(0.27f, 0.6f, 0.24f);
        public static readonly Color Moss = new Color(0.38f, 0.62f, 0.26f);
        public static readonly Color Leaf = new Color(0.22f, 0.52f, 0.2f);
        public static readonly Color LeafTip = new Color(0.55f, 0.85f, 0.35f);

        public static readonly Color Hazard = new Color(1f, 0.8f, 0.2f);
        public static readonly Color Warning = new Color(0.95f, 0.3f, 0.2f);
        public static readonly Color Rust = new Color(0.72f, 0.42f, 0.24f);
        public static readonly Color Wood = new Color(0.75f, 0.52f, 0.3f);
        public static readonly Color WoodDark = new Color(0.55f, 0.37f, 0.22f);
        public static readonly Color Olive = new Color(0.56f, 0.56f, 0.32f);
        public static readonly Color Paper = new Color(0.95f, 0.94f, 0.88f);
        public static readonly Color Glass = new Color(0.32f, 0.5f, 0.56f);
    }
}
