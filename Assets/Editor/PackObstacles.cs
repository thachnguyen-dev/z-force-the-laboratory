using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Colours and wear for the owner's obstacle pack and grass. Round 2 (owner, 2026-10-04: research real colours,
    /// recolour each part, add cracks, damage and blood):
    /// - Palettes follow real objects in an abandoned town: cars mostly white, black, grey and silver (about three
    ///   quarters of cars worldwide), with blue and faded red; concrete-grey road barriers with small amber reflectors;
    ///   dark green, blue, grey or brown steel dumpsters with black plastic lids; faded acrylic courts; muted pastel
    ///   houses with dark shingle roofs; olive and tan military gear; yellow or blue hazard drums.
    /// - Everything is desaturated and weathered, with only small accents (survival-horror look).
    /// - Each surface also gets wear weights for the Stylized shader: grime, cracks, chipped paint and rust, with more
    ///   grime low down. Some variants are wrecks (burnt, rusted) or blood-stained.
    /// Variants are baked as "id", "id_v1", "id_v2"… Rules work on the baked model (y up, bottom at 0): by piece, by height
    /// (t = 0 at the bottom, 1 at the top) and by which way a face points (n.y near 1 = facing up). Vertex alpha below 1
    /// glows.
    /// </summary>
    public static class PackObstacles
    {
        /// <summary>A surface: colour, wear weights (x grime, y cracks, z chipped paint, w rust) and blood.</summary>
        public struct Look
        {
            public Color Colour;
            public Vector4 Wear;
            public float Blood;
            /// <summary>Military camouflage (1 = fully camouflaged), 2026-10-04.</summary>
            public float Camo;

            public static implicit operator Look(Color c) => new Look { Colour = c };
        }

        public delegate Look Painter(int variant, int index, PackParts.Part part, Vector3 p, Vector3 n, Bounds all);

        public class Recipe
        {
            public int Variants = 1;
            public Painter Paint;
        }

        private static Color C(float r, float g, float b, float glow = 0f) => new Color(r, g, b, 1f - glow);
        private static T Pick<T>(int v, params T[] options) => options[v % options.Length];
        private static float T(Vector3 p, Bounds all) => (p.y - all.min.y) / Mathf.Max(1e-4f, all.size.y);
        private static float Tx(Vector3 p, Bounds all) => (p.x - all.min.x) / Mathf.Max(1e-4f, all.size.x);
        private static float PartT(PackParts.Part part, Bounds all) => (part.Bounds.center.y - all.min.y) / Mathf.Max(1e-4f, all.size.y);
        private static bool Small(PackParts.Part part, float area = 0.03f) => part.Area < area;
        private static bool Up(Vector3 n, float k = 0.7f) => n.y > k;
        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // ---------------------------------------------------------------- materials (wear presets)

        private static readonly Vector4 PaintedMetal = new Vector4(0.55f, 0.05f, 0.3f, 0.4f);
        private static readonly Vector4 RustyMetal = new Vector4(0.7f, 0.1f, 0.35f, 0.95f);
        /// <summary>Worn paint: chipped and grimy with some rust (the more worn military variants).</summary>
        private static readonly Vector4 WornPaint = new Vector4(0.7f, 0.1f, 0.55f, 0.55f);
        private static readonly Vector4 Burnt = new Vector4(0.9f, 0.4f, 0.2f, 1f);
        private static readonly Vector4 Concrete = new Vector4(0.6f, 0.85f, 0.35f, 0f);
        private static readonly Vector4 Plaster = new Vector4(0.5f, 0.6f, 0.4f, 0f);
        private static readonly Vector4 Wood = new Vector4(0.55f, 0.35f, 0.25f, 0f);
        private static readonly Vector4 Fabric = new Vector4(0.75f, 0f, 0f, 0f);
        private static readonly Vector4 Plastic = new Vector4(0.5f, 0.1f, 0.2f, 0f);
        private static readonly Vector4 Glass = new Vector4(0.35f, 0.9f, 0f, 0f);
        private static readonly Vector4 Rubber = new Vector4(0.35f, 0f, 0f, 0f);
        private static readonly Vector4 Canvas = new Vector4(0.7f, 0.05f, 0.1f, 0f);
        private static readonly Vector4 Asphalt = new Vector4(0.45f, 0.9f, 0.2f, 0f);

        /// <summary>A surface of <paramref name="wear"/>, with extra grime towards the ground.</summary>
        /// <summary>A camouflaged surface: the camo pattern instead of the colour, with the wear of painted metal.</summary>
        private static Look Camo(Vector4 wear, Vector3 p, Bounds all, float blood = 0f)
        {
            var look = M(C(0.36f, 0.38f, 0.24f), wear, p, all, blood);
            look.Camo = 1f;
            return look;
        }

        private static Look M(Color c, Vector4 wear, Vector3 p, Bounds all, float blood = 0f)
        {
            var t = T(p, all);
            wear.x = Mathf.Clamp01(wear.x + (1f - t) * 0.3f);
            // Blood splashes low down, thinning out higher up.
            return new Look { Colour = c, Wear = wear, Blood = blood * Mathf.Lerp(1f, 0.25f, t) };
        }

        /// <summary>Lights, screens and glowing goo stay clean so they keep glowing.</summary>
        private static Look Lit(Color c) => new Look { Colour = c };

        // ---------------------------------------------------------------- common colours (sRGB)

        private static readonly Color Tyre = C(0.11f, 0.11f, 0.12f);
        private static readonly Color Rim = C(0.5f, 0.52f, 0.54f);
        private static readonly Color Chrome = C(0.6f, 0.62f, 0.64f);
        private static readonly Color CarGlass = C(0.17f, 0.21f, 0.25f);
        private static readonly Color PaneGlass = C(0.42f, 0.5f, 0.55f);
        private static readonly Color ConcreteGrey = C(0.6f, 0.59f, 0.56f);
        private static readonly Color AsphaltGrey = C(0.3f, 0.31f, 0.32f);
        private static readonly Color WoodBrown = C(0.48f, 0.36f, 0.25f);
        private static readonly Color DarkWood = C(0.3f, 0.22f, 0.16f);
        private static readonly Color Olive = C(0.33f, 0.36f, 0.24f);
        private static readonly Color Tan = C(0.6f, 0.53f, 0.39f);
        private static readonly Color BlackPlastic = C(0.1f, 0.1f, 0.11f);
        private static readonly Color BurntPaint = C(0.16f, 0.13f, 0.11f);
        private static readonly Color HeadLight = C(0.85f, 0.84f, 0.76f);
        private static readonly Color TailLight = C(0.55f, 0.1f, 0.08f);

        /// <summary>A wheel: a low, roundish piece that isn't the body.</summary>
        private static bool Wheel(int i, PackParts.Part part, Bounds all) =>
            i > 0 && PartT(part, all) < 0.35f && part.Bounds.size.y > all.size.y * 0.18f && part.Bounds.size.y < all.size.y * 0.65f;

        /// <summary>A tyre with a grey hub in the middle (the hub is the part of the wheel near its axle).</summary>
        private static Look WheelLook(PackParts.Part part, Vector3 p, Bounds all, bool burnt)
        {
            var c = part.Bounds.center;
            var r = part.Bounds.size.y * 0.5f;
            // The axle runs along whichever side of the wheel is thinnest.
            var axleAlongZ = part.Bounds.size.z < part.Bounds.size.x;
            var d = axleAlongZ ? new Vector2(p.x - c.x, p.y - c.y).magnitude : new Vector2(p.z - c.z, p.y - c.y).magnitude;
            if (burnt) return M(C(0.12f, 0.1f, 0.09f), RustyMetal, p, all);
            return d < r * 0.5f ? M(Rim, PaintedMetal, p, all) : M(Tyre, Rubber, p, all);
        }

        /// <summary>
        /// A road vehicle. Body paint per variant; "wreck" variants are burnt black-brown or rusted through with broken
        /// glass; "bloody" ones are splashed. Glass in the cabin band, a dark plastic lower band, head and tail lights at
        /// the two ends, black tyres with grey hubs.
        /// </summary>
        private static Painter Vehicle(float glassFrom, (Color paint, string kind)[] variants) => (v, i, part, p, n, all) =>
        {
            var (paint, kind) = Pick(v, variants);
            var burnt = kind == "burnt";
            var rusted = kind == "rusted";
            var blood = kind == "bloody" ? 1f : 0f;
            if (Wheel(i, part, all)) return WheelLook(part, p, all, burnt);
            var body = burnt ? Shade(BurntPaint, 0.9f + 0.2f * Tx(p, all)) : paint;
            var bodyWear = burnt ? Burnt : rusted ? RustyMetal : PaintedMetal;
            if (i > 0 && Small(part, 0.02f)) return M(burnt ? BurntPaint : Chrome, bodyWear, p, all, blood);
            var t = T(p, all);
            var x = Tx(p, all);
            if (t < 0.18f) return M(burnt ? BurntPaint : BlackPlastic, bodyWear, p, all, blood);
            if (t > glassFrom && !Up(n, 0.75f))
                return burnt || rusted ? M(C(0.08f, 0.08f, 0.09f), Burnt, p, all) : M(CarGlass, Glass, p, all, blood * 0.5f);
            // Lights on the end faces at bumper height.
            if (!burnt && t < 0.45f && Mathf.Abs(n.x) > 0.6f && (x < 0.06f || x > 0.94f))
                return M(x > 0.5f ? HeadLight : TailLight, Glass, p, all);
            return M(i == 0 ? body : Shade(body, 0.85f), bodyWear, p, all, blood);
        };

        public static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>
        {
            // ---------------------------------------------------------------- blocks bullets
            ["car"] = new Recipe
            {
                Variants = 9,
                Paint = Vehicle(0.58f, new[]
                {
                    (C(0.8f, 0.8f, 0.78f), "paint"), (C(0.13f, 0.13f, 0.14f), "paint"), (C(0.42f, 0.43f, 0.44f), "paint"),
                    (C(0.64f, 0.66f, 0.67f), "paint"), (C(0.2f, 0.27f, 0.38f), "paint"), (C(0.5f, 0.17f, 0.14f), "paint"),
                    (C(0.36f, 0.27f, 0.21f), "rusted"), (BurntPaint, "burnt"), (C(0.78f, 0.78f, 0.76f), "bloody"),
                }),
            },
            ["van"] = new Recipe
            {
                Variants = 5,
                Paint = Vehicle(0.56f, new[]
                {
                    (C(0.82f, 0.82f, 0.8f), "paint"), (C(0.45f, 0.46f, 0.47f), "paint"), (C(0.35f, 0.43f, 0.5f), "paint"),
                    (BurntPaint, "burnt"), (C(0.8f, 0.8f, 0.78f), "bloody"),
                }),
            },
            // Owner, 2026-10-04: trucks, jeeps and the helicopter in woodland military camouflage (the Stylized
            // shader's camo pattern); glass, tyres, rotors and skids keep their own colours. Variant 1 is more worn.
            ["truck"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var wear = v == 1 ? WornPaint : PaintedMetal;
                if (Wheel(i, part, all)) return WheelLook(part, p, all, false);
                if (Small(part, 0.02f)) return M(C(0.25f, 0.26f, 0.24f), wear, p, all);
                if (i == 0) return T(p, all) < 0.2f ? M(BlackPlastic, wear, p, all) : Camo(Canvas, p, all);
                if (i == 1) return T(p, all) > 0.62f && !Up(n, 0.75f) ? M(CarGlass, Glass, p, all) : Camo(wear, p, all);
                return part.Bounds.size.y > all.size.y * 0.3f ? Camo(wear, p, all) : M(C(0.25f, 0.24f, 0.22f), wear, p, all);
            } },
            ["jeep"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var wear = v == 1 ? WornPaint : PaintedMetal;
                if (Wheel(i, part, all)) return WheelLook(part, p, all, false);
                if (Small(part, 0.015f)) return M(C(0.22f, 0.23f, 0.21f), wear, p, all);
                return T(p, all) < 0.18f ? M(C(0.2f, 0.21f, 0.18f), wear, p, all) : Camo(wear, p, all);
            } },
            // Owner, 2026-10-05: the backpack every character wears, in military camouflage. The two tall pieces are the
            // shoulder straps (the side against the wearer's back): dark webbing; buckles and the radio antenna black.
            ["backpack"] = new Recipe { Variants = 1, Paint = (v, i, part, p, n, all) =>
            {
                if (i == 2 || i == 3) return M(C(0.2f, 0.22f, 0.16f), Canvas, p, all);
                if (Small(part, 0.02f)) return M(C(0.12f, 0.12f, 0.12f), Plastic, p, all);
                return Camo(Canvas, p, all);
            } },
            ["helicopter"] = new Recipe { Variants = 1, Paint = (v, i, part, p, n, all) =>
            {
                var t = T(p, all);
                // The flat pieces up top are the rotor blades; the long low ones are the skids.
                if (part.Bounds.size.y < all.size.y * 0.12f && PartT(part, all) > 0.8f) return M(C(0.16f, 0.17f, 0.17f), PaintedMetal, p, all);
                if (PartT(part, all) < 0.15f) return M(C(0.2f, 0.21f, 0.2f), RustyMetal, p, all);
                if (i == 0)
                {
                    // The cockpit glass wraps the nose (the end away from the tail boom, -x).
                    var x = Tx(p, all);
                    if (x < 0.24f && t > 0.32f && t < 0.75f && !Up(n, 0.8f)) return M(C(0.2f, 0.26f, 0.3f), Glass, p, all);
                    return Camo(PaintedMetal, p, all);
                }
                return Small(part, 0.01f) ? Lit(C(0.9f, 0.2f, 0.15f, 0.4f)) : Camo(PaintedMetal, p, all);
            } },
            ["bus_stop"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var frame = Pick(v, C(0.2f, 0.3f, 0.26f), C(0.32f, 0.34f, 0.36f));
                var s = part.Bounds.size;
                if (s.y < all.size.y * 0.2f && PartT(part, all) > 0.7f) return M(frame, PaintedMetal, p, all);  // roof
                if (s.y > all.size.y * 0.4f && Mathf.Min(s.x, s.z) < all.size.x * 0.06f) return M(Shade(frame, 0.75f), RustyMetal, p, all); // posts
                if (s.y > all.size.y * 0.35f) return part.Area > 1f ? M(PaneGlass, Glass, p, all) : M(C(0.7f, 0.66f, 0.55f), Plaster, p, all); // glass, faded advert
                return Small(part, 0.02f) ? M(Chrome, PaintedMetal, p, all) : M(WoodBrown, Wood, p, all);   // bench
            } },
            ["atm"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var body = Pick(v, C(0.4f, 0.43f, 0.46f), C(0.18f, 0.24f, 0.33f));
                if (i == 1) return Lit(C(0.25f, 0.45f, 0.5f, 0.35f));                   // a dim, dying screen
                if (i > 1) return M(C(0.16f, 0.17f, 0.19f), Plastic, p, all);
                var t = T(p, all);
                if (t < 0.08f) return M(C(0.2f, 0.21f, 0.23f), PaintedMetal, p, all);
                return M(t > 0.86f ? Shade(body, 0.8f) : body, PaintedMetal, p, all);
            } },
            ["clinic"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var blood = v == 1 ? 0.8f : 0f;
                if (i == 2) return M(ConcreteGrey, Concrete, p, all);                     // the round base
                if (i <= 1)
                    return Up(n) ? M(C(0.55f, 0.56f, 0.57f), Concrete, p, all)
                        : M(i == 0 ? C(0.85f, 0.84f, 0.8f) : C(0.76f, 0.8f, 0.8f), Plaster, p, all, blood);
                if (PartT(part, all) > 0.55f && Small(part, 0.1f)) return M(C(0.66f, 0.13f, 0.12f), PaintedMetal, p, all); // the red cross, signs
                return M(Small(part, 0.05f) ? PaneGlass : C(0.48f, 0.56f, 0.6f), Glass, p, all);
            } },
            ["control_tower"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                switch (i)
                {
                    case 0: return M(C(0.26f, 0.27f, 0.29f), PaintedMetal, p, all);       // roof
                    case 1: return Up(n) ? M(C(0.26f, 0.27f, 0.29f), PaintedMetal, p, all) : M(C(0.32f, 0.42f, 0.46f), Glass, p, all); // the glass cab
                    case 2: return M(C(0.66f, 0.65f, 0.62f), Concrete, p, all);           // the column
                    case 3: return M(ConcreteGrey, Concrete, p, all);
                }
                if (Small(part, 0.01f)) return Lit(C(0.9f, 0.2f, 0.15f, 0.5f));            // warning lights
                return M(C(0.3f, 0.32f, 0.34f), RustyMetal, p, all);
            } },
            ["dead_tree"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var t = T(p, all);
                var low = Pick(v, C(0.26f, 0.2f, 0.15f), C(0.25f, 0.24f, 0.22f));
                var high = Pick(v, C(0.42f, 0.35f, 0.28f), C(0.46f, 0.44f, 0.41f));
                return M(Color.Lerp(low, high, Mathf.SmoothStep(0.05f, 0.85f, t)), Wood, p, all);
            } },
            ["gas_station"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                if (i == 1) return M(AsphaltGrey, Asphalt, p, all);                         // the forecourt slab
                if (i == 0)
                    return Up(n) ? M(C(0.78f, 0.78f, 0.76f), PaintedMetal, p, all)
                        : M(T(p, all) > 0.75f ? C(0.58f, 0.16f, 0.14f) : C(0.8f, 0.8f, 0.78f), PaintedMetal, p, all);
                if (i == 2) return Up(n) ? M(C(0.5f, 0.5f, 0.49f), Concrete, p, all) : M(C(0.78f, 0.7f, 0.5f), Plaster, p, all); // the shop
                if (Small(part, 0.02f)) return M(Chrome, PaintedMetal, p, all);
                return PartT(part, all) < 0.45f ? M(C(0.58f, 0.16f, 0.14f), PaintedMetal, p, all) : M(C(0.34f, 0.36f, 0.38f), RustyMetal, p, all);
            } },
            ["house"] = new Recipe { Variants = 4, Paint = (v, i, part, p, n, all) =>
            {
                var wall = Pick(v, C(0.78f, 0.72f, 0.6f), C(0.6f, 0.67f, 0.72f), C(0.6f, 0.65f, 0.54f), C(0.74f, 0.6f, 0.57f));
                var roof = Pick(v, C(0.3f, 0.3f, 0.32f), C(0.42f, 0.26f, 0.21f), C(0.27f, 0.3f, 0.35f), C(0.44f, 0.35f, 0.27f));
                if (i == 1) return M(ConcreteGrey, Concrete, p, all);                      // the plot slab
                var t = T(p, all);
                if (i == 0 || (t > 0.55f && n.y > 0.3f)) return M(roof, new Vector4(0.6f, 0.3f, 0.2f, 0f), p, all);
                if (i == 2) return M(wall, Plaster, p, all);
                if (Small(part, 0.05f))
                    return PartT(part, all) < 0.25f ? M(DarkWood, Wood, p, all)
                        : PartT(part, all) > 0.7f ? M(C(0.5f, 0.28f, 0.22f), Concrete, p, all)   // brick chimney
                        : M(PaneGlass, Glass, p, all);
                return M(C(0.82f, 0.81f, 0.78f), Plaster, p, all);                          // trims and floors
            } },
            ["military_tent"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var canvas = Pick(v, C(0.37f, 0.4f, 0.27f), C(0.58f, 0.52f, 0.39f));
                if (i <= 1) return M(i == 0 ? canvas : Shade(canvas, 0.88f), Canvas, p, all);
                if (i == 2) return M(Shade(canvas, 0.55f), Canvas, p, all);                // groundsheet
                if (part.Bounds.size.y < all.size.y * 0.05f && part.Area > 0.2f) return M(Shade(canvas, 0.78f), Canvas, p, all); // flaps
                return Small(part, 0.02f) ? M(C(0.7f, 0.67f, 0.6f), Fabric, p, all) : M(DarkWood, Wood, p, all); // ropes, poles
            } },
            ["mini_store"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                var wall = Pick(v, C(0.36f, 0.5f, 0.5f), C(0.7f, 0.58f, 0.34f), C(0.55f, 0.32f, 0.27f));
                if (i == 0)
                    return Up(n) && T(p, all) > 0.85f ? M(C(0.45f, 0.45f, 0.46f), Concrete, p, all)
                        : T(p, all) < 0.05f ? M(ConcreteGrey, Concrete, p, all) : M(wall, Plaster, p, all);
                if (i == 1) return Up(n) ? M(C(0.45f, 0.45f, 0.46f), Concrete, p, all) : M(C(0.55f, 0.18f, 0.15f), PaintedMetal, p, all); // roof, faded red edge
                return PartT(part, all) > 0.6f ? Lit(C(0.8f, 0.7f, 0.35f, 0.15f)) : M(PaneGlass, Glass, p, all); // sign / windows
            } },
            ["toxic_waste"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                if (i > 0) return M(Small(part, 0.1f) && PartT(part, all) < 0.2f ? C(0.15f, 0.15f, 0.16f) : C(0.3f, 0.31f, 0.3f), RustyMetal, p, all);
                var t = T(p, all);
                if (t > 0.9f && Up(n)) return Lit(C(0.33f, 0.62f, 0.2f, 0.4f));          // glowing goo
                var drum = Pick(v, C(0.75f, 0.62f, 0.17f), C(0.18f, 0.29f, 0.48f), C(0.7f, 0.58f, 0.18f));
                var wear = v == 2 ? RustyMetal : PaintedMetal;
                if (Mathf.Abs(t - 0.32f) < 0.04f || Mathf.Abs(t - 0.66f) < 0.04f) return M(C(0.13f, 0.13f, 0.14f), wear, p, all);
                return M(drum, wear, p, all);
            } },
            ["trash_bin"] = new Recipe { Variants = 4, Paint = (v, i, part, p, n, all) =>
            {
                // Steel front-load dumpster: dark green, blue, grey or brown, a black plastic lid, heavy rust and grime.
                var body = Pick(v, C(0.2f, 0.31f, 0.23f), C(0.17f, 0.25f, 0.37f), C(0.35f, 0.37f, 0.37f), C(0.35f, 0.26f, 0.18f));
                if (i == 0) return M(T(p, all) < 0.08f ? Shade(body, 0.55f) : body, RustyMetal, p, all);
                if (i == 1) return M(BlackPlastic, Plastic, p, all);                         // the lid
                return Wheel(i, part, all) || PartT(part, all) < 0.2f ? M(Tyre, Rubber, p, all) : M(C(0.18f, 0.19f, 0.2f), Plastic, p, all); // bags inside
            } },
            ["tree"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                var t = T(p, all);
                if (t < 0.36f) return M(C(0.38f, 0.28f, 0.2f), Wood, p, all);
                var leaf = Pick(v, C(0.3f, 0.45f, 0.22f), C(0.23f, 0.37f, 0.2f), C(0.58f, 0.42f, 0.19f));
                return n.y > 0.45f ? Color.Lerp(leaf, Color.white, 0.12f) : leaf;
            } },
            ["wooden_fence"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var plank = Pick(v, C(0.5f, 0.4f, 0.3f), C(0.46f, 0.44f, 0.41f));
                if (i <= 1) return M(Shade(plank, 0.72f), Wood, p, all);                     // posts
                if (Small(part, 0.01f)) return M(C(0.45f, 0.45f, 0.46f), RustyMetal, p, all); // nails
                return M((i & 1) == 0 ? plank : Shade(plank, 0.88f), Wood, p, all);
            } },

            // ---------------------------------------------------------------- bullets pass
            ["animal_enclosure"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                if (i == 0) return M(ConcreteGrey, Concrete, p, all, 0.6f);                 // floor (something was kept here)
                if (i == 1) return M(C(0.26f, 0.3f, 0.26f), RustyMetal, p, all);            // top frame
                if (part.Bounds.size.y > all.size.y * 0.6f) return M(C(0.28f, 0.3f, 0.29f), RustyMetal, p, all); // corner posts
                return Small(part, 0.01f) ? M(C(0.7f, 0.6f, 0.32f), Fabric, p, all) : M(C(0.42f, 0.36f, 0.3f), RustyMetal, p, all); // straw / bars
            } },
            ["barrier"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                // A concrete jersey barrier: plain weathered concrete, small amber reflectors along the top.
                var concrete = Pick(v, C(0.62f, 0.61f, 0.58f), C(0.5f, 0.5f, 0.48f));
                var t = T(p, all);
                var x = Tx(p, all);
                if (t > 0.86f && !Up(n, 0.8f) && (Mathf.Abs(x - 0.25f) < 0.035f || Mathf.Abs(x - 0.75f) < 0.035f))
                    return Lit(C(0.85f, 0.55f, 0.15f, 0.35f));
                return M(t < 0.12f ? Shade(concrete, 0.8f) : concrete, Concrete, p, all);
            } },
            ["chair"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                var fabric = Pick(v, C(0.16f, 0.17f, 0.2f), C(0.3f, 0.32f, 0.36f), C(0.32f, 0.2f, 0.18f));
                if (PartT(part, all) < 0.3f) return M(i % 2 == 0 ? BlackPlastic : C(0.36f, 0.37f, 0.39f), Plastic, p, all); // base and wheels
                return i <= 1 ? M(fabric, Fabric, p, all) : M(BlackPlastic, Plastic, p, all);
            } },
            ["computer"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                if (i == 0) return M(C(0.72f, 0.69f, 0.6f), Plastic, p, all);              // the old beige case
                if (i == 1) return Lit(C(0.2f, 0.35f, 0.42f, 0.3f));                        // the screen
                return M(i == 2 ? C(0.3f, 0.31f, 0.33f) : C(0.22f, 0.23f, 0.25f), Plastic, p, all);
            } },
            ["couch"] = new Recipe { Variants = 5, Paint = (v, i, part, p, n, all) =>
            {
                var fabric = Pick(v, C(0.4f, 0.28f, 0.2f), C(0.4f, 0.42f, 0.3f), C(0.45f, 0.18f, 0.16f), C(0.42f, 0.42f, 0.44f), C(0.5f, 0.45f, 0.36f));
                var blood = v == 4 ? 1f : 0f;
                if (Small(part, 0.03f) && PartT(part, all) < 0.2f) return M(DarkWood, Wood, p, all); // legs
                return M(i >= 4 ? Color.Lerp(fabric, Color.white, 0.1f) : fabric, Fabric, p, all, blood);
            } },
            ["fire_hydrant"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                if (i == 1) return M(ConcreteGrey, Concrete, p, all);
                if (i == 0 || i == 2) return M(C(0.6f, 0.16f, 0.13f), PaintedMetal, p, all);
                return M(PartT(part, all) > 0.85f ? C(0.75f, 0.62f, 0.2f) : Chrome, PaintedMetal, p, all);
            } },
            ["lab_shelf"] = new Recipe { Paint = (v, i, part, p, n, all) =>
            {
                if (i <= 4) return M(C(0.78f, 0.8f, 0.81f), PaintedMetal, p, all);          // shelves
                if (part.Bounds.size.y > all.size.y * 0.6f) return M(C(0.5f, 0.53f, 0.56f), PaintedMetal, p, all); // posts
                var bottles = new[] { C(0.3f, 0.75f, 0.42f, 0.35f), C(0.3f, 0.6f, 0.75f, 0.35f), C(0.7f, 0.35f, 0.55f, 0.3f), C(0.72f, 0.62f, 0.3f, 0.25f) };
                return Lit(bottles[i % bottles.Length]);                                     // bottles and jars
            } },
            ["lab_workbench"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var blood = v == 1 ? 1f : 0f;
                if (i == 0) return Up(n) && T(p, all) > 0.55f ? M(C(0.82f, 0.83f, 0.84f), Plastic, p, all, blood) : M(C(0.5f, 0.55f, 0.58f), PaintedMetal, p, all, blood);
                var items = new[] { Lit(C(0.3f, 0.6f, 0.75f, 0.3f)), M(C(0.3f, 0.31f, 0.34f), Plastic, p, all), Lit(C(0.35f, 0.75f, 0.4f, 0.3f)), M(C(0.8f, 0.8f, 0.78f), Plastic, p, all) };
                return items[i % items.Length];
            } },
            ["motorcycle"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                var paint = Pick(v, C(0.5f, 0.15f, 0.13f), C(0.13f, 0.13f, 0.15f), C(0.35f, 0.38f, 0.28f));
                // The wheels are the two pieces at either end, low down.
                if (PartT(part, all) < 0.4f && Mathf.Abs(part.Bounds.center.x - all.center.x) > all.size.x * 0.25f) return M(Tyre, Rubber, p, all);
                if (i <= 1) return M(paint, PaintedMetal, p, all);
                return M(Small(part, 0.04f) ? Chrome : C(0.18f, 0.18f, 0.2f), RustyMetal, p, all);
            } },
            ["park_bench"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                if (i == 1 || i == 2) return M(C(0.17f, 0.18f, 0.2f), RustyMetal, p, all);  // iron ends
                if (Small(part, 0.1f)) return M(C(0.45f, 0.46f, 0.48f), RustyMetal, p, all);
                return M(Pick(v, C(0.5f, 0.38f, 0.26f), C(0.3f, 0.36f, 0.3f)), Wood, p, all);
            } },
            ["shelf"] = new Recipe { Variants = 2, Paint = (v, i, part, p, n, all) =>
            {
                var wood = Pick(v, C(0.48f, 0.36f, 0.25f), C(0.62f, 0.52f, 0.4f));
                if (i <= 5) return M((i & 1) == 0 ? wood : Shade(wood, 0.88f), Wood, p, all);
                var items = new[] { C(0.6f, 0.22f, 0.18f), C(0.25f, 0.4f, 0.55f), C(0.72f, 0.62f, 0.32f), C(0.38f, 0.5f, 0.32f) };
                return M(items[i % items.Length], Plastic, p, all);
            } },

            // ---------------------------------------------------------------- the owner's grass (replaces Claude's bushes)
            ["grass"] = new Recipe { Variants = 3, Paint = (v, i, part, p, n, all) =>
            {
                var t = T(p, all);
                var low = Pick(v, C(0.15f, 0.33f, 0.12f), C(0.13f, 0.3f, 0.15f), C(0.19f, 0.34f, 0.12f));
                var tip = Pick(v, C(0.42f, 0.66f, 0.26f), C(0.34f, 0.6f, 0.28f), C(0.55f, 0.66f, 0.28f));
                return Color.Lerp(low, tip, Mathf.SmoothStep(0f, 1f, t));
            } },
        };
    }
}
