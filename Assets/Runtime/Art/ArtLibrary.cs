using System;
using System.Collections.Generic;
using UnityEngine;
using P = ZombieGame.Art.ArtPalette;

namespace ZombieGame.Art
{
    /// <summary>
    /// Every built-in map piece (owner, 2026-10-02: Claude makes the map graphics). Props are physical (they block
    /// movement, bullets and sight, except bushes, which hide whoever is inside); decorations are looks only. Each piece is
    /// built in code, in its own space: bottom centre at the origin, its front facing south (−z, towards the camera).
    /// Prefabs in Resources/Props or Resources/Decorations with the same id replace a built-in piece.
    /// </summary>
    public static partial class ArtLibrary
    {
        public enum Kind { Prop, Decoration }

        public class Piece
        {
            public string Id;
            public string Name;
            public Kind Kind;
            /// <summary>Props: the footprint in metres (x, z) for a box, or the diameter (x) when <see cref="Round"/>.</summary>
            public Vector2 Footprint = Vector2.one;
            public bool Round;
            public float Height = 1f;
            /// <summary>A bush: no collision, hides whoever is inside (owner-approved bush rules).</summary>
            public bool Bush;
            /// <summary>Builds the piece into a kit (already transformed to where it stands); the int is a per-instance seed.</summary>
            public Action<MeshKit, int> Build;
            /// <summary>The owner's pack model (2026-10-04): placed as its own object (shared mesh) instead of merged.</summary>
            public string Model;
            /// <summary>Blocks movement only: bullets and sight pass (the owner's second obstacle kind).</summary>
            public bool ShootThrough;
            /// <summary>A building: may be turned but never mirrored.</summary>
            public bool NoFlip;
            /// <summary>No collider at all (a flat court you can walk on).</summary>
            public bool Walkable;
            /// <summary>Wind-blown bits (owner, 2026-10-05): built at runtime as separate pieces that the wind moves.</summary>
            public bool Wind;
        }

        private static Dictionary<string, Piece> _pieces;

        public static IReadOnlyDictionary<string, Piece> Pieces
        {
            get
            {
                if (_pieces == null) Register();
                return _pieces;
            }
        }

        public static Piece Get(string id) => id != null && Pieces.TryGetValue(id, out var p) ? p : null;

        public static List<Piece> OfKind(Kind kind)
        {
            var list = new List<Piece>();
            foreach (var p in Pieces.Values) if (p.Kind == kind) list.Add(p);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        private static void Add(string id, string name, Kind kind, Vector2 footprint, float height, Action<MeshKit, int> build, bool round = false, bool bush = false)
        {
            _pieces[id] = new Piece { Id = id, Name = name, Kind = kind, Footprint = footprint, Height = height, Build = build, Round = round, Bush = bush };
        }

        private static void Register()
        {
            _pieces = new Dictionary<string, Piece>();
            // ---- Props (physical) ----
            Add("crate", "Wooden crate", Kind.Prop, new Vector2(1f, 1f), 1f, (k, s) => Crate(k, Vector3.zero, 0.95f, s));
            Add("crate_stack", "Crate stack", Kind.Prop, new Vector2(2f, 1f), 1.9f, CrateStack);
            Add("barrels", "Toxic barrels", Kind.Prop, new Vector2(1f, 1f), 0.95f, Barrels);
            Add("rock", "Rock", Kind.Prop, new Vector2(1.4f, 1.4f), 1.1f, RockProp, round: true);
            Add("barrier", "Concrete barrier", Kind.Prop, new Vector2(2f, 0.7f), 1f, Barrier);
            Add("sandbags", "Sandbags", Kind.Prop, new Vector2(2f, 0.8f), 0.8f, Sandbags);
            Add("car", "Wrecked car", Kind.Prop, new Vector2(2f, 3.8f), 1.4f, Car);
            Add("container", "Biohazard container", Kind.Prop, new Vector2(1.2f, 1.2f), 1.3f, Container);
            Add("generator", "Generator", Kind.Prop, new Vector2(1.6f, 1f), 1.2f, Generator);
            Add("tree", "Dead tree", Kind.Prop, new Vector2(0.8f, 0.8f), 3.2f, Tree, round: true);
            Add("lockers", "Lab lockers", Kind.Prop, new Vector2(1.8f, 0.6f), 2f, Lockers);
            Add("desk", "Lab desk", Kind.Prop, new Vector2(1.6f, 0.8f), 1.1f, Desk);
            Add("tank", "Chemical tank", Kind.Prop, new Vector2(1.6f, 1.6f), 2.2f, Tank, round: true);
            Add("lamp", "Lamp post", Kind.Prop, new Vector2(0.3f, 0.3f), 3.6f, Lamp, round: true);
            Add("bush", "Bush", Kind.Prop, new Vector2(2f, 2f), BushHeight, (k, s) => Bush(k, Vector3.zero, 1f, s), round: true, bush: true);
            Add("bush_small", "Small bush", Kind.Prop, new Vector2(1.2f, 1.2f), 1.6f, (k, s) => Bush(k, Vector3.zero, 0.6f, s, 1.6f), round: true, bush: true);

            // ---- Decorations (looks only) ----
            Add("crack", "Crack", Kind.Decoration, Vector2.one, 0f, Crack);
            Add("toxic_puddle", "Toxic puddle", Kind.Decoration, Vector2.one, 0f, (k, s) => Splat(k, s, 0.55f, new Color(0.3f, 0.62f, 0.26f), MeshKit.Glow(new Color(0.38f, 0.75f, 0.3f), 0.1f)));
            Add("oil_stain", "Oil stain", Kind.Decoration, Vector2.one, 0f, (k, s) => Splat(k, s, 0.7f, new Color(0.24f, 0.26f, 0.32f), new Color(0.18f, 0.2f, 0.26f)));
            // Owner, 2026-10-03: nothing on the map may look like water: the old "puddle" is a mud patch now (id kept for old maps).
            Add("puddle", "Mud patch", Kind.Decoration, Vector2.one, 0f, (k, s) => Splat(k, s, 0.9f, new Color(0.26f, 0.19f, 0.12f), new Color(0.2f, 0.14f, 0.09f)));
            Add("rubble", "Rubble", Kind.Decoration, Vector2.one, 0f, Rubble);
            Add("papers", "Papers", Kind.Decoration, Vector2.one, 0f, Papers);
            Add("hazard_stripes", "Hazard stripes", Kind.Decoration, Vector2.one, 0f, HazardStripes);
            Add("arrow", "Road arrow", Kind.Decoration, Vector2.one, 0f, Arrow);
            Add("grate", "Floor grate", Kind.Decoration, Vector2.one, 0f, Grate);
            Add("manhole", "Manhole", Kind.Decoration, Vector2.one, 0f, Manhole);
            Add("leaves", "Dead leaves", Kind.Decoration, Vector2.one, 0f, Leaves);
            Add("cables", "Cables", Kind.Decoration, Vector2.one, 0f, Cables);
            Add("tire", "Old tire", Kind.Decoration, Vector2.one, 0f, Tire);
            Add("mushrooms", "Glowing fungus", Kind.Decoration, Vector2.one, 0f, Mushrooms);
            Add("grass_tuft", "Grass tuft", Kind.Decoration, Vector2.one, 0f, GrassTuft);
            Add("bones_free_debris", "Lab debris", Kind.Decoration, Vector2.one, 0f, LabDebris);
            RegisterMore();
            RegisterDecor();
            RegisterInteriors();
            RegisterStreet();
            RegisterPack();
        }

        /// <summary>
        /// The owner's obstacle pack (2026-10-04). Registered last, so "car", "tree", "truck", "barrier" and "shelf" now
        /// use the owner's models instead of Claude's code-built ones.
        /// </summary>
        private static void RegisterPack()
        {
            foreach (var def in PackProps.All)
            {
                if (!PackArt.Has(def.Id)) continue;
                var extent = PackProps.Extent(def);
                var round = def.Trunk > 0f;
                var size = def.Size;
                var id = def.Id;
                _pieces[id] = new Piece
                {
                    Id = id,
                    Name = def.Name,
                    Kind = Kind.Prop,
                    // Colliders a little inside the model's outline, so the edges you see are what you bump into.
                    Footprint = round ? new Vector2(extent.x * def.Trunk, extent.x * def.Trunk) : new Vector2(extent.x * def.Body.x, extent.z * def.Body.y),
                    Round = round,
                    Height = extent.y,
                    Model = id,
                    ShootThrough = def.ShootThrough,
                    NoFlip = def.Building,
                    Walkable = def.Walkable,
                    // For anything that merges pieces into a kit (previews); the stage places the model as its own object.
                    Build = (kit, seed) => kit.AddMesh(PackProps.Mesh(id, seed), Matrix4x4.Scale(Vector3.one * size), keepAllFaces: true),
                };
            }
        }

        private static System.Random Rng(int seed) => new System.Random(seed * 7919 + 17);
        private static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ======================================================================== props

        /// <summary>A chunky crate: chamfered wood, dark metal corner straps, plank grooves, a green biohazard stencil.</summary>
        public static void Crate(MeshKit k, Vector3 at, float size, int seed)
        {
            var r = Rng(seed);
            var wood = Color.Lerp(P.Wood, P.WoodDark, R(r, 0f, 0.5f));
            k.BevelBox(at, new Vector3(size, size * 0.92f, size), 0.07f * size, wood, MeshKit.Shade(wood, 1.15f));
            var h = size * 0.92f;
            var half = size * 0.5f;
            // Plank grooves on the front and the top.
            for (var i = 1; i < 3; i++)
            {
                var y = at.y + h * i / 3f;
                k.Quad(new Vector3(at.x - half + 0.08f, y - 0.012f, at.z - half - 0.002f), new Vector3(at.x - half + 0.08f, y + 0.012f, at.z - half - 0.002f),
                    new Vector3(at.x + half - 0.08f, y + 0.012f, at.z - half - 0.002f), new Vector3(at.x + half - 0.08f, y - 0.012f, at.z - half - 0.002f), MeshKit.Shade(wood, 0.55f));
                var z = at.z - half + size * i / 3f;
                k.Quad(new Vector3(at.x - half + 0.08f, at.y + h + 0.002f, z - 0.012f), new Vector3(at.x - half + 0.08f, at.y + h + 0.002f, z + 0.012f),
                    new Vector3(at.x + half - 0.08f, at.y + h + 0.002f, z + 0.012f), new Vector3(at.x + half - 0.08f, at.y + h + 0.002f, z - 0.012f), MeshKit.Shade(wood, 0.6f));
            }
            // Corner straps.
            foreach (var sx in new[] { -1f, 1f })
                k.Box(new Vector3(at.x + sx * (half - 0.05f), at.y, at.z - half + 0.05f), new Vector3(0.12f, h + 0.01f, 0.12f), P.MetalDark, Faces.Hidden, P.Metal);
            // Stencil: a green square with a dark centre (reads as a biohazard mark at this size).
            var cy = at.y + h * 0.5f;
            k.Quad(new Vector3(at.x - 0.17f * size, cy - 0.17f * size, at.z - half - 0.004f), new Vector3(at.x - 0.17f * size, cy + 0.17f * size, at.z - half - 0.004f),
                new Vector3(at.x + 0.17f * size, cy + 0.17f * size, at.z - half - 0.004f), new Vector3(at.x + 0.17f * size, cy - 0.17f * size, at.z - half - 0.004f), P.ToxicSoft);
            k.Quad(new Vector3(at.x - 0.07f * size, cy - 0.07f * size, at.z - half - 0.006f), new Vector3(at.x - 0.07f * size, cy + 0.07f * size, at.z - half - 0.006f),
                new Vector3(at.x + 0.07f * size, cy + 0.07f * size, at.z - half - 0.006f), new Vector3(at.x + 0.07f * size, cy - 0.07f * size, at.z - half - 0.006f), MeshKit.Shade(wood, 0.5f));
        }

        private static void CrateStack(MeshKit k, int seed)
        {
            Crate(k, new Vector3(-0.5f, 0f, 0f), 0.95f, seed);
            Crate(k, new Vector3(0.5f, 0f, 0.02f), 0.95f, seed + 1);
            Crate(k, new Vector3(-0.2f, 0.88f, 0.05f), 0.85f, seed + 2);
        }

        public static void Barrel(MeshKit k, Vector3 at, int seed, Color body)
        {
            var r = Rng(seed);
            var height = R(r, 0.78f, 0.9f);
            k.Cylinder(at, 0.27f, height, 10, body, true, MeshKit.Shade(body, 1.25f));
            // Rims and a green label band.
            k.Cylinder(at + Vector3.up * 0.18f, 0.285f, 0.05f, 10, MeshKit.Shade(body, 0.7f), false);
            k.Cylinder(at + Vector3.up * (height - 0.2f), 0.285f, 0.05f, 10, MeshKit.Shade(body, 0.7f), false);
            k.Cylinder(at + Vector3.up * (height * 0.42f), 0.28f, 0.14f, 10, P.ToxicSoft, false);
            // Lid ring and goo running down the front.
            k.Cylinder(at + Vector3.up * height, 0.18f, 0.03f, 8, MeshKit.Shade(body, 0.6f), true, MeshKit.Shade(body, 0.8f));
            if (r.NextDouble() < 0.7)
            {
                var gx = at.x + R(r, -0.1f, 0.1f);
                var len = R(r, 0.25f, 0.5f);
                k.Quad(new Vector3(gx - 0.05f, at.y + height - len, at.z - 0.285f), new Vector3(gx - 0.06f, at.y + height, at.z - 0.285f),
                    new Vector3(gx + 0.06f, at.y + height, at.z - 0.285f), new Vector3(gx + 0.05f, at.y + height - len, at.z - 0.285f), MeshKit.Glow(P.Toxic, 0.55f));
            }
        }

        private static void Barrels(MeshKit k, int seed)
        {
            var r = Rng(seed);
            Barrel(k, new Vector3(-0.22f, 0f, -0.18f), seed, Color.Lerp(P.MetalDark, P.Black, R(r, 0f, 1f)));
            Barrel(k, new Vector3(0.24f, 0f, -0.14f), seed + 1, Color.Lerp(P.MetalDark, P.Rust, R(r, 0f, 0.4f)));
            Barrel(k, new Vector3(0.0f, 0f, 0.24f), seed + 2, P.MetalDark);
        }

        private static void RockProp(MeshKit k, int seed)
        {
            var r = Rng(seed);
            k.Rock(Vector3.zero, new Vector3(R(r, 1.2f, 1.45f), R(r, 0.9f, 1.15f), R(r, 1.1f, 1.35f)), seed, P.Charcoal, P.Moss);
            // A small glowing crystal on top.
            if (r.NextDouble() < 0.6) Crystal(k, new Vector3(R(r, -0.2f, 0.2f), 0.75f, R(r, -0.15f, 0.15f)), R(r, 0.25f, 0.4f));
        }

        public static void Crystal(MeshKit k, Vector3 at, float height)
        {
            var c = MeshKit.Glow(P.Toxic, 0.7f);
            var w = height * 0.28f;
            var top = at + Vector3.up * height;
            var a = at + new Vector3(-w, 0f, -w * 0.6f);
            var b = at + new Vector3(w, 0f, -w * 0.6f);
            var d = at + new Vector3(0f, 0f, w);
            k.Tri(a, top, b, c);
            k.Tri(b, top, d, MeshKit.Shade(c, 0.8f));
            k.Tri(d, top, a, MeshKit.Shade(c, 0.9f));
        }

        private static void Barrier(MeshKit k, int seed)
        {
            // A jersey barrier: wide foot, narrow top, chevron paint on the front.
            float x0 = -1f, x1 = 1f, foot = 0.35f, top = 0.13f, h = 1f, mid = 0.32f;
            var c = P.Concrete;
            // Front: lower slope, upper slope.
            k.Quad(new Vector3(x0, 0f, -foot), new Vector3(x0, mid, -foot * 0.75f), new Vector3(x1, mid, -foot * 0.75f), new Vector3(x1, 0f, -foot), MeshKit.Shade(c, 0.95f));
            k.Quad(new Vector3(x0, mid, -foot * 0.75f), new Vector3(x0, h, -top), new Vector3(x1, h, -top), new Vector3(x1, mid, -foot * 0.75f), c);
            k.Quad(new Vector3(x0, h, -top), new Vector3(x0, h, top), new Vector3(x1, h, top), new Vector3(x1, h, -top), P.ConcreteLight);
            // Chevrons on the upper front.
            for (var i = 0; i < 4; i++)
            {
                var cx = -0.75f + i * 0.5f;
                var color = i % 2 == 0 ? P.Black : MeshKit.Glow(P.ToxicSoft, 0.3f);
                k.Quad(new Vector3(cx - 0.18f, 0.45f, -0.235f), new Vector3(cx - 0.02f, 0.85f, -0.155f), new Vector3(cx + 0.14f, 0.85f, -0.155f), new Vector3(cx - 0.02f, 0.45f, -0.235f), color);
            }
            // Grime at the foot.
            k.Quad(new Vector3(x0, 0f, -foot - 0.003f), new Vector3(x0, 0.12f, -foot * 0.95f - 0.003f), new Vector3(x1, 0.12f, -foot * 0.95f - 0.003f), new Vector3(x1, 0f, -foot - 0.003f), MeshKit.Shade(c, 0.6f));
        }

        private static void Sandbags(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var row = 0; row < 3; row++)
            {
                var count = 4 - (row == 2 ? 1 : 0);
                var w = 2f / 4f;
                for (var i = 0; i < count; i++)
                {
                    var x = -1f + w * 0.5f + i * w + (row % 2 == 1 ? w * 0.25f : 0f) + (row == 2 ? w * 0.25f : 0f);
                    var color = Color.Lerp(P.Olive, P.WoodDark, R(r, 0f, 0.35f));
                    k.BevelBox(new Vector3(x, row * 0.25f, R(r, -0.03f, 0.03f)), new Vector3(w * 0.96f, 0.27f, 0.62f - row * 0.06f), 0.09f, color);
                }
            }
        }

        /// <summary>Faded car paints: readable against the dark ground from above.</summary>
        private static readonly Color[] CarPaints =
        {
            new Color(0.36f, 0.42f, 0.36f), new Color(0.46f, 0.32f, 0.22f), new Color(0.3f, 0.36f, 0.4f), new Color(0.5f, 0.5f, 0.46f),
        };

        private static void Car(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var paint = CarPaints[r.Next(CarPaints.Length)];
            var roof = MeshKit.Shade(paint, 1.18f);
            var glass = new Color(0.16f, 0.24f, 0.26f);
            // Wheels (chunky black blocks) peeking out under the body.
            foreach (var x in new[] { -0.9f, 0.9f })
            foreach (var z in new[] { -1.2f, 1.2f })
                k.BevelBox(new Vector3(x, 0f, z), new Vector3(0.32f, 0.6f, 0.64f), 0.1f, P.Black, new Color(0.12f, 0.12f, 0.13f));
            // Body: a lower chunky block, then the cabin with glass sides and a lighter roof.
            k.BevelBox(new Vector3(0f, 0.22f, 0f), new Vector3(1.9f, 0.6f, 3.7f), 0.18f, paint, MeshKit.Shade(paint, 1.1f));
            k.BevelBox(new Vector3(0f, 0.82f, 0.2f), new Vector3(1.64f, 0.5f, 1.95f), 0.2f, glass, roof);
            // Windscreen and rear window on the bonnet side, as flat dark panels.
            k.Quad(new Vector3(-0.66f, 0.825f, -0.79f), new Vector3(-0.6f, 1.12f, -0.63f), new Vector3(0.6f, 1.12f, -0.63f), new Vector3(0.66f, 0.825f, -0.79f), MeshKit.Shade(glass, 1.25f));
            // Bonnet and boot lines, headlights (one cracked and glowing green), a red tail light.
            k.Quad(new Vector3(-0.75f, 0.823f, -1.5f), new Vector3(-0.75f, 0.823f, -1.46f), new Vector3(0.75f, 0.823f, -1.46f), new Vector3(0.75f, 0.823f, -1.5f), MeshKit.Shade(paint, 0.7f));
            k.Quad(new Vector3(-0.78f, 0.45f, -1.853f), new Vector3(-0.78f, 0.66f, -1.853f), new Vector3(-0.42f, 0.66f, -1.853f), new Vector3(-0.42f, 0.45f, -1.853f), MeshKit.Glow(P.ToxicSoft, 0.55f));
            k.Quad(new Vector3(0.42f, 0.45f, -1.853f), new Vector3(0.42f, 0.66f, -1.853f), new Vector3(0.78f, 0.66f, -1.853f), new Vector3(0.78f, 0.45f, -1.853f), new Color(0.6f, 0.62f, 0.55f));
            k.Quad(new Vector3(-0.5f, 0.3f, -1.853f), new Vector3(-0.5f, 0.4f, -1.853f), new Vector3(0.5f, 0.4f, -1.853f), new Vector3(0.5f, 0.3f, -1.853f), P.Black);
            // Rust patches and grime on top.
            for (var i = 0; i < 3; i++)
            {
                var x = R(r, -0.7f, 0.4f);
                var z = R(r, -1.6f, 1.3f);
                var y = z > -0.75f && z < 1.15f ? 1.322f : 0.823f;
                k.Quad(new Vector3(x, y, z), new Vector3(x, y, z + 0.3f), new Vector3(x + R(r, 0.2f, 0.4f), y, z + 0.3f), new Vector3(x + 0.3f, y, z), P.Rust);
            }
        }

        private static void Container(MeshKit k, int seed)
        {
            k.BevelBox(Vector3.zero, new Vector3(1.15f, 1.25f, 1.15f), 0.1f, P.MetalDark, P.Metal);
            // Glowing window on the front, warning band, latch.
            k.Quad(new Vector3(-0.3f, 0.55f, -0.582f), new Vector3(-0.3f, 0.95f, -0.582f), new Vector3(0.3f, 0.95f, -0.582f), new Vector3(0.3f, 0.55f, -0.582f), MeshKit.Glow(P.Toxic, 0.75f));
            k.Quad(new Vector3(-0.47f, 0.22f, -0.582f), new Vector3(-0.47f, 0.36f, -0.582f), new Vector3(0.47f, 0.36f, -0.582f), new Vector3(0.47f, 0.22f, -0.582f), P.Hazard);
            for (var i = 0; i < 4; i++)
            {
                var x = -0.4f + i * 0.24f;
                k.Quad(new Vector3(x, 0.22f, -0.584f), new Vector3(x + 0.08f, 0.36f, -0.584f), new Vector3(x + 0.15f, 0.36f, -0.584f), new Vector3(x + 0.07f, 0.22f, -0.584f), P.Black);
            }
            k.Box(new Vector3(0.45f, 0.62f, -0.6f), new Vector3(0.1f, 0.22f, 0.05f), P.MetalLight, Faces.Hidden);
            // Vent on the lid.
            k.Box(new Vector3(0f, 1.25f, 0.1f), new Vector3(0.5f, 0.06f, 0.4f), P.Metal, Faces.Hidden, P.MetalLight);
        }

        private static void Generator(MeshKit k, int seed)
        {
            k.BevelBox(Vector3.zero, new Vector3(1.5f, 0.95f, 0.9f), 0.08f, P.Metal, P.MetalLight);
            // Grille on the front (dark slats), a green status light, an exhaust pipe on top.
            for (var i = 0; i < 6; i++)
            {
                var y = 0.2f + i * 0.1f;
                k.Quad(new Vector3(-0.6f, y, -0.452f), new Vector3(-0.6f, y + 0.05f, -0.452f), new Vector3(0.1f, y + 0.05f, -0.452f), new Vector3(0.1f, y, -0.452f), P.Black);
            }
            k.Quad(new Vector3(0.35f, 0.6f, -0.452f), new Vector3(0.35f, 0.72f, -0.452f), new Vector3(0.5f, 0.72f, -0.452f), new Vector3(0.5f, 0.6f, -0.452f), MeshKit.Glow(P.Toxic, 0.9f));
            k.Cylinder(new Vector3(0.5f, 0.95f, 0.15f), 0.08f, 0.3f, 8, P.MetalDark, true, P.Black);
            k.Box(new Vector3(-0.3f, 0.95f, 0f), new Vector3(0.5f, 0.12f, 0.5f), P.MetalDark, Faces.Hidden, P.Metal);
        }

        private static void Tree(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var bark = new Color(0.12f, 0.1f, 0.09f);
            k.Cylinder(Vector3.zero, 0.26f, 1.7f, 7, bark, false, null, 0.17f);
            // Roots and a crooked branch.
            for (var i = 0; i < 3; i++)
            {
                var a = i * 2.1f + R(r, 0f, 0.6f);
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                k.Tri(d * 0.2f + Vector3.up * 0.25f, d * 0.55f, d * 0.2f + new Vector3(-d.z, 0f, d.x) * 0.14f, MeshKit.Shade(bark, 1.2f));
            }
            // A round, puffy crown of dark leaves with sickly green tips.
            var top = new Vector3(R(r, -0.1f, 0.1f), 1.4f, R(r, -0.1f, 0.1f));
            k.Blob(top, new Vector3(2.0f, 1.5f, 1.9f), seed, P.GreenDark, P.LeafTip, 0.22f);
            k.Blob(top + new Vector3(R(r, -0.6f, -0.3f), 0.5f, R(r, -0.3f, 0.2f)), new Vector3(1.2f, 1f, 1.2f), seed + 1, P.Leaf, P.LeafTip, 0.2f);
            k.Blob(top + new Vector3(R(r, 0.3f, 0.6f), 0.4f, R(r, -0.2f, 0.3f)), new Vector3(1.1f, 0.9f, 1.1f), seed + 2, P.Leaf, Color.Lerp(P.LeafTip, P.Toxic, 0.3f), 0.2f);
        }

        private static void Lockers(MeshKit k, int seed)
        {
            for (var i = 0; i < 3; i++)
            {
                var x = -0.6f + i * 0.6f;
                k.BevelBox(new Vector3(x, 0f, 0f), new Vector3(0.58f, 1.95f, 0.55f), 0.04f, P.MetalDark, P.Metal);
                // Vent slits and a green status light.
                for (var v = 0; v < 3; v++)
                {
                    var y = 1.55f + v * 0.08f;
                    k.Quad(new Vector3(x - 0.18f, y, -0.277f), new Vector3(x - 0.18f, y + 0.03f, -0.277f), new Vector3(x + 0.18f, y + 0.03f, -0.277f), new Vector3(x + 0.18f, y, -0.277f), P.Black);
                }
                k.Quad(new Vector3(x + 0.16f, 1.0f, -0.277f), new Vector3(x + 0.16f, 1.06f, -0.277f), new Vector3(x + 0.22f, 1.06f, -0.277f), new Vector3(x + 0.22f, 1.0f, -0.277f), MeshKit.Glow(P.Toxic, 0.9f));
            }
        }

        private static void Desk(MeshKit k, int seed)
        {
            foreach (var x in new[] { -0.7f, 0.7f })
                k.Box(new Vector3(x, 0f, 0f), new Vector3(0.08f, 0.72f, 0.7f), P.MetalDark, Faces.Bottom);
            k.BevelBox(new Vector3(0f, 0.72f, 0f), new Vector3(1.6f, 0.07f, 0.8f), 0.02f, P.Slate, P.ConcreteLight);
            // Monitor with a glowing green screen, and a keyboard.
            k.Box(new Vector3(0.1f, 0.79f, 0.15f), new Vector3(0.5f, 0.34f, 0.06f), P.Black, Faces.Bottom);
            k.Quad(new Vector3(-0.12f, 0.83f, 0.118f), new Vector3(-0.12f, 1.09f, 0.118f), new Vector3(0.32f, 1.09f, 0.118f), new Vector3(0.32f, 0.83f, 0.118f), MeshKit.Glow(P.ToxicSoft, 0.85f));
            k.Box(new Vector3(0.1f, 0.79f, -0.15f), new Vector3(0.42f, 0.03f, 0.14f), P.MetalDark, Faces.Bottom, P.Metal);
        }

        private static void Tank(MeshKit k, int seed)
        {
            k.Cylinder(Vector3.zero, 0.75f, 1.8f, 12, P.Metal, true, P.MetalLight);
            k.Cylinder(new Vector3(0f, 1.8f, 0f), 0.5f, 0.25f, 12, P.MetalDark, true, P.Metal, 0.25f);
            // Glowing level strip and hazard band.
            k.Cylinder(new Vector3(0f, 0.55f, 0f), 0.76f, 0.12f, 12, P.Hazard, false);
            k.Quad(new Vector3(-0.08f, 0.8f, -0.752f), new Vector3(-0.08f, 1.55f, -0.752f), new Vector3(0.08f, 1.55f, -0.752f), new Vector3(0.08f, 0.8f, -0.752f), MeshKit.Glow(P.Toxic, 0.85f));
            foreach (var x in new[] { -0.55f, 0.55f })
                k.Box(new Vector3(x, 0f, -0.45f), new Vector3(0.12f, 0.3f, 0.12f), P.Black, Faces.Hidden);
        }

        private static void Lamp(MeshKit k, int seed)
        {
            k.Cylinder(Vector3.zero, 0.14f, 0.3f, 8, P.MetalDark, true, P.Metal);
            k.Cylinder(new Vector3(0f, 0.3f, 0f), 0.06f, 3.1f, 6, P.MetalDark, true, P.Metal);
            k.Box(new Vector3(0f, 3.3f, -0.25f), new Vector3(0.14f, 0.08f, 0.6f), P.MetalDark, Faces.Bottom);
            k.Box(new Vector3(0f, 3.22f, -0.45f), new Vector3(0.24f, 0.08f, 0.3f), MeshKit.Glow(P.Toxic, 0.95f), Faces.None, MeshKit.Glow(P.Toxic, 0.95f));
        }

        /// <summary>Owner, 2026-10-03: bushes stand about as tall as a player, so hiding in one reads clearly.</summary>
        public const float BushHeight = 1.85f;

        /// <summary>
        /// A tall puffy bush: a lower ring of smooth blobs and a crown of blobs on top, dark at the bottom, lighter
        /// toxic-green tips, a few glowing buds. About <see cref="BushHeight"/> tall.
        /// </summary>
        public static void Bush(MeshKit k, Vector3 at, float radius, int seed, float height = BushHeight)
        {
            var r = Rng(seed);
            var lower = radius > 0.8f ? 5 : 3;
            var tip = Color.Lerp(P.LeafTip, P.Green, R(r, 0f, 0.4f));
            for (var i = 0; i < lower; i++)
            {
                var a = i * (6.283f / lower) + R(r, -0.3f, 0.3f);
                var d = radius * R(r, 0.35f, 0.55f);
                var s = radius * R(r, 1.0f, 1.2f);
                var center = at + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                k.Blob(center, new Vector3(s * 1.15f, height * 0.62f, s * 1.15f), seed * 13 + i, P.GreenDark, Color.Lerp(P.Green, tip, 0.5f), 0.2f);
            }
            var upper = radius > 0.8f ? 4 : 2;
            for (var i = 0; i < upper; i++)
            {
                var a = i * (6.283f / upper) + R(r, 0f, 1f);
                var d = i == 0 && upper > 2 ? 0f : radius * R(r, 0.15f, 0.35f);
                var s = radius * R(r, 0.85f, 1.05f);
                var center = at + new Vector3(Mathf.Cos(a) * d, height * 0.38f, Mathf.Sin(a) * d);
                k.Blob(center, new Vector3(s * 1.1f, height * 0.62f, s * 1.1f), seed * 29 + i, P.Green, tip, 0.22f);
            }
            for (var i = 0; i < 4; i++)
            {
                var a = R(r, 0f, 6.28f);
                var p = at + new Vector3(Mathf.Cos(a) * radius * 0.55f, height * R(r, 0.55f, 0.95f), Mathf.Sin(a) * radius * 0.55f - radius * 0.25f);
                k.Box(p, new Vector3(0.09f, 0.09f, 0.09f), MeshKit.Glow(P.Toxic, 0.9f), Faces.Bottom, MeshKit.Glow(P.Toxic, 0.9f));
            }
        }

        // ======================================================================== decorations

        private const float Flat = 0.006f;

        /// <summary>An irregular flat blob on the ground (puddles, stains).</summary>
        private static void Splat(MeshKit k, int seed, float radius, Color edge, Color center)
        {
            var r = Rng(seed);
            const int n = 14;
            var points = new Vector3[n];
            for (var i = 0; i < n; i++)
            {
                var a = i / (float)n * Mathf.PI * 2f;
                var d = radius * (0.65f + 0.45f * MeshKit.Noise2(Mathf.Cos(a) * 1.6f + seed, Mathf.Sin(a) * 1.6f));
                points[i] = new Vector3(Mathf.Cos(a) * d * 1.2f, Flat, Mathf.Sin(a) * d);
            }
            var c = new Vector3(0f, Flat, 0f);
            for (var i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                // Clockwise seen from above: centre, next, this.
                k.Quad(c, b * 0.55f + new Vector3(0f, Flat * 0.45f, 0f), a * 0.55f + new Vector3(0f, Flat * 0.45f, 0f), c, center);
                k.Quad(a * 0.55f + new Vector3(0f, Flat * 0.45f, 0f), b * 0.55f + new Vector3(0f, Flat * 0.45f, 0f), b, a, edge);
            }
        }

        /// <summary>A flat strip on the ground from a to b (cracks, cables, paint).</summary>
        private static void Strip(MeshKit k, Vector3 a, Vector3 b, float width, Color color, float y = Flat)
        {
            var d = (b - a);
            d.y = 0f;
            var side = new Vector3(-d.z, 0f, d.x).normalized * width * 0.5f;
            a.y = b.y = y;
            k.Quad(a + side, b + side, b - side, a - side, color);
        }

        private static void Crack(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var dark = new Color(0.04f, 0.05f, 0.09f);
            var p = new Vector3(R(r, -0.6f, -0.3f), 0f, R(r, -0.2f, 0.2f));
            var angle = R(r, -0.6f, 0.6f);
            for (var i = 0; i < 6; i++)
            {
                angle += R(r, -0.7f, 0.7f);
                var next = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * R(r, 0.18f, 0.32f);
                Strip(k, p, next, R(r, 0.02f, 0.04f), dark);
                if (r.NextDouble() < 0.35)
                {
                    var ba = angle + (r.NextDouble() < 0.5 ? 1f : -1f) * R(r, 0.6f, 1.2f);
                    Strip(k, p, p + new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * R(r, 0.12f, 0.25f), 0.025f, dark);
                }
                p = next;
            }
        }

        private static void Rubble(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var i = 0; i < 6; i++)
            {
                var s = R(r, 0.08f, 0.22f);
                var p = new Vector3(R(r, -0.45f, 0.45f), 0f, R(r, -0.35f, 0.35f));
                k.Rock(p, new Vector3(s * 1.3f, s * 0.7f, s), seed * 31 + i, Color.Lerp(P.Concrete, P.Slate, R(r, 0f, 1f)), P.ConcreteLight, 0.3f);
            }
        }

        private static void Papers(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var i = 0; i < 4; i++)
            {
                var p = new Vector3(R(r, -0.4f, 0.4f), 0f, R(r, -0.3f, 0.3f));
                var a = R(r, 0f, 3.14f);
                var u = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.11f;
                var v = new Vector3(-u.z, 0f, u.x) * 1.4f;
                var y = Vector3.up * (Flat + i * 0.002f);
                var color = r.NextDouble() < 0.15 ? P.GreenDark : Color.Lerp(P.Paper, P.Slate, R(r, 0.35f, 0.7f));
                k.Quad(p - u - v + y, p - u + v + y, p + u + v + y, p + u - v + y, color);
            }
        }

        private static void HazardStripes(MeshKit k, int seed)
        {
            for (var i = 0; i < 6; i++)
            {
                var x = -1f + i * 0.34f;
                var color = i % 2 == 0 ? P.Hazard : P.Black;
                k.Quad(new Vector3(x, Flat, -0.18f), new Vector3(x + 0.18f, Flat, 0.18f), new Vector3(x + 0.36f, Flat, 0.18f), new Vector3(x + 0.18f, Flat, -0.18f), MeshKit.Shade(color, 0.85f));
            }
        }

        private static void Arrow(MeshKit k, int seed)
        {
            var paint = new Color(0.62f, 0.64f, 0.6f);
            Strip(k, new Vector3(0f, 0f, -0.9f), new Vector3(0f, 0f, 0.3f), 0.22f, paint);
            k.Tri(new Vector3(-0.42f, Flat, 0.3f), new Vector3(0f, Flat, 0.95f), new Vector3(0.42f, Flat, 0.3f), paint);
        }

        private static void Grate(MeshKit k, int seed)
        {
            k.Quad(new Vector3(-0.5f, Flat, -0.4f), new Vector3(-0.5f, Flat, 0.4f), new Vector3(0.5f, Flat, 0.4f), new Vector3(0.5f, Flat, -0.4f), P.MetalDark);
            for (var i = 0; i < 7; i++)
            {
                var x = -0.42f + i * 0.14f;
                k.Quad(new Vector3(x, Flat * 2f, -0.33f), new Vector3(x, Flat * 2f, 0.33f), new Vector3(x + 0.06f, Flat * 2f, 0.33f), new Vector3(x + 0.06f, Flat * 2f, -0.33f), P.Black);
            }
            k.Quad(new Vector3(0.3f, Flat * 2.5f, -0.33f), new Vector3(0.3f, Flat * 2.5f, 0.33f), new Vector3(0.36f, Flat * 2.5f, 0.33f), new Vector3(0.36f, Flat * 2.5f, -0.33f), MeshKit.Glow(P.ToxicSoft, 0.5f));
        }

        private static void Manhole(MeshKit k, int seed)
        {
            const int n = 14;
            for (var i = 0; i < n; i++)
            {
                var a0 = i / (float)n * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Vector3 P0(float rr, float a) => new Vector3(Mathf.Cos(a) * rr, Flat, Mathf.Sin(a) * rr);
                k.Quad(P0(0.45f, a0), P0(0.45f, a1), P0(0.55f, a1), P0(0.55f, a0), P.MetalLight);
                k.Tri(new Vector3(0f, Flat, 0f), P0(0.45f, a1), P0(0.45f, a0), i % 2 == 0 ? P.MetalDark : P.Metal);
            }
        }

        private static void Leaves(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var i = 0; i < 12; i++)
            {
                var p = new Vector3(R(r, -0.6f, 0.6f), 0f, R(r, -0.5f, 0.5f));
                var a = R(r, 0f, 6.28f);
                var u = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.07f;
                var v = new Vector3(-u.z, 0f, u.x) * 0.6f;
                var color = Color.Lerp(new Color(0.24f, 0.2f, 0.1f), P.Leaf, R(r, 0f, 1f));
                var y = Vector3.up * (Flat + i * 0.0005f);
                k.Quad(p - u + y, p + v + y, p + u + y, p - v + y, color);
            }
        }

        private static void Cables(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var c = 0; c < 2; c++)
            {
                var p = new Vector3(-1f, 0f, R(r, -0.3f, 0.3f));
                var color = c == 0 ? P.Black : new Color(0.1f, 0.22f, 0.12f);
                for (var i = 0; i < 8; i++)
                {
                    var next = p + new Vector3(0.26f, 0f, R(r, -0.12f, 0.12f));
                    Strip(k, p, next, 0.05f, color, Flat * (2 + c));
                    p = next;
                }
            }
        }

        private static void Tire(MeshKit k, int seed)
        {
            k.Cylinder(Vector3.zero, 0.36f, 0.2f, 10, P.Black, true, new Color(0.1f, 0.1f, 0.11f));
            k.Cylinder(new Vector3(0f, 0.2f, 0f), 0.17f, 0.005f, 10, new Color(0.03f, 0.03f, 0.03f), true, new Color(0.03f, 0.03f, 0.03f));
        }

        private static void Mushrooms(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var i = 0; i < 5; i++)
            {
                var p = new Vector3(R(r, -0.35f, 0.35f), 0f, R(r, -0.3f, 0.3f));
                var h = R(r, 0.08f, 0.2f);
                k.Cylinder(p, 0.025f, h, 5, new Color(0.6f, 0.62f, 0.55f), false);
                k.Cylinder(p + Vector3.up * h, R(r, 0.06f, 0.11f), 0.04f, 7, MeshKit.Glow(P.Toxic, 0.85f), true, MeshKit.Glow(P.Toxic, 0.95f), 0.02f);
            }
        }

        private static void GrassTuft(MeshKit k, int seed)
        {
            var r = Rng(seed);
            for (var i = 0; i < 9; i++)
            {
                var p = new Vector3(R(r, -0.25f, 0.25f), 0f, R(r, -0.2f, 0.2f));
                var a = R(r, 0f, 3.14f);
                var w = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.04f;
                var tip = p + new Vector3(R(r, -0.08f, 0.08f), R(r, 0.18f, 0.35f), R(r, -0.08f, 0.08f));
                var c = Color.Lerp(P.Leaf, P.LeafTip, R(r, 0.2f, 1f));
                k.Tri(p - w, tip, p + w, c);
                k.Tri(p + w, tip, p - w, MeshKit.Shade(c, 0.8f));
            }
        }

        private static void LabDebris(MeshKit k, int seed)
        {
            var r = Rng(seed);
            // A tipped-over stool, a broken glass panel and a couple of vials.
            k.Box(new Vector3(R(r, -0.3f, 0.3f), 0f, R(r, -0.2f, 0.2f)), new Vector3(0.4f, 0.05f, 0.4f), P.Metal, Faces.Bottom, P.MetalLight);
            var g = new Vector3(R(r, -0.4f, 0.4f), Flat, R(r, -0.3f, 0.3f));
            k.Tri(g, g + new Vector3(0.3f, 0f, 0.1f), g + new Vector3(0.1f, 0f, -0.25f), new Color(0.3f, 0.45f, 0.45f));
            for (var i = 0; i < 2; i++)
            {
                var p = new Vector3(R(r, -0.4f, 0.4f), 0f, R(r, -0.3f, 0.3f));
                k.Box(p, new Vector3(0.18f, 0.06f, 0.06f), MeshKit.Glow(P.Toxic, 0.6f), Faces.Bottom, MeshKit.Glow(P.Toxic, 0.6f));
            }
        }
    }
}
