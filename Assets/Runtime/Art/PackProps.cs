using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The owner's obstacle pack (2026-10-04) as map props, placeable in the Map Editor and used by the stage generator.
    /// Two kinds (owner):
    /// - Solid: you can't walk through it or shoot through it (vehicles, buildings, trees, bins…).
    /// - Shoot-through: you can't walk through it, but bullets and sight pass (benches, barriers, shelves, chairs…).
    /// Vehicles and objects may be turned and mirrored; buildings may only be turned (owner). Colour variants come from
    /// PackBuilder ("id", "id_v1"…). Sizes (owner, 2026-10-04): absolute sizes may be stylised, but proportions between
    /// objects should be close to real life. Each size is the real-world length of the model's longest side × 1.03 (the
    /// 1.8 m character over a 1.75 m person). The models are chunky, so a little wider and taller than real for that
    /// length. By owner request, held weapons are
    /// oversized like Brawl Stars.
    /// </summary>
    public static class PackProps
    {
        public class Def
        {
            public string Id, Name;
            /// <summary>The model's largest side, in metres.</summary>
            public float Size;
            public bool ShootThrough;
            /// <summary>A building: may be turned, never mirrored.</summary>
            public bool Building;
            /// <summary>Round collider (trees): its diameter as a share of the model's width.</summary>
            public float Trunk;
            /// <summary>A flat walkable piece (none in use: the basketball court was removed, owner 2026-10-05): only its upright poles block movement.</summary>
            public bool Walkable;
            /// <summary>The collider as a share of the model's width and depth (the helicopter: its fuselage, not the rotor).</summary>
            public Vector2 Body = new Vector2(0.92f, 0.92f);
        }

        public static readonly Def[] All =
        {
            // Solid: blocks movement and bullets.
            new Def { Id = "car", Name = "Car", Size = 5.0f },
            new Def { Id = "van", Name = "Van", Size = 5.5f },
            new Def { Id = "truck", Name = "Army truck", Size = 6.9f },
            new Def { Id = "helicopter", Name = "Helicopter", Size = 12.4f, Building = true, Body = new Vector2(0.75f, 0.4f) },
            new Def { Id = "jeep", Name = "Jeep", Size = 4f },
            new Def { Id = "bus_stop", Name = "Bus stop", Size = 4.1f, Building = true },
            new Def { Id = "atm", Name = "ATM", Size = 1.75f },
            new Def { Id = "clinic", Name = "Clinic (solid building)", Size = 9f, Building = true },
            new Def { Id = "control_tower", Name = "Control tower", Size = 8f, Building = true },
            new Def { Id = "dead_tree", Name = "Dead tree", Size = 5f, Trunk = 0.28f },
            new Def { Id = "gas_station", Name = "Gas station (solid building)", Size = 11f, Building = true },
            new Def { Id = "house", Name = "House (solid building)", Size = 9f, Building = true },
            new Def { Id = "military_tent", Name = "Military tent", Size = 5.2f },
            new Def { Id = "mini_store", Name = "Mini store (solid building)", Size = 8f, Building = true },
            new Def { Id = "toxic_waste", Name = "Toxic waste drum", Size = 0.9f, Trunk = 0.9f },
            new Def { Id = "trash_bin", Name = "Dumpster", Size = 2f },
            new Def { Id = "tree", Name = "Tree", Size = 6f, Trunk = 0.22f },
            new Def { Id = "wooden_fence", Name = "Wooden fence", Size = 2.4f },
            // Shoot-through: blocks movement only.
            new Def { Id = "animal_enclosure", Name = "Animal enclosure", Size = 2.5f, ShootThrough = true },
            new Def { Id = "barrier", Name = "Road barrier", Size = 3.1f, ShootThrough = true },
            new Def { Id = "chair", Name = "Office chair", Size = 1.1f, ShootThrough = true },
            new Def { Id = "computer", Name = "Computer", Size = 0.6f, ShootThrough = true },
            new Def { Id = "couch", Name = "Couch", Size = 2.2f, ShootThrough = true },
            new Def { Id = "fire_hydrant", Name = "Fire hydrant", Size = 0.8f, ShootThrough = true },
            new Def { Id = "lab_shelf", Name = "Lab shelf", Size = 1.85f, ShootThrough = true },
            new Def { Id = "lab_workbench", Name = "Lab workbench", Size = 1.85f, ShootThrough = true },
            new Def { Id = "motorcycle", Name = "Motorcycle", Size = 2.2f, ShootThrough = true },
            new Def { Id = "park_bench", Name = "Park bench", Size = 1.65f, ShootThrough = true },
            new Def { Id = "shelf", Name = "Shelf", Size = 1.85f, ShootThrough = true },
        };

        private static Dictionary<string, Def> _byId;

        public static Def Get(string id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, Def>();
                foreach (var d in All) _byId[d.Id] = d;
            }
            return id != null && _byId.TryGetValue(id, out var def) ? def : null;
        }

        private static readonly Dictionary<string, int> VariantCounts = new Dictionary<string, int>();

        /// <summary>How many colour variants were baked for a model (1 = just the base colours).</summary>
        public static int Variants(string id)
        {
            if (VariantCounts.TryGetValue(id, out var n)) return n;
            n = 1;
            while (PackArt.Has($"{id}_v{n}")) n++;
            VariantCounts[id] = n;
            return n;
        }

        /// <summary>The mesh for a variant: 0 = base, 1 = "_v1"…, wrapped to what exists.</summary>
        public static Mesh Mesh(string id, int variant)
        {
            var v = ((variant % Variants(id)) + Variants(id)) % Variants(id);
            return PackArt.Model(v == 0 ? id : $"{id}_v{v}");
        }

        private static readonly Dictionary<string, List<Vector2>> PoleCache = new Dictionary<string, List<Vector2>>();

        /// <summary>
        /// The upright poles of a walkable piece (the basketball court's hoops, owner 2026-10-04: walkable, only the hoop
        /// poles block movement), in the model's own space (x, z at scale 1). Found from the mesh: on a 4 cm grid, the
        /// columns whose vertices run from near the court (below 25 % of the model's height) up high (above 60 %);
        /// neighbouring columns are one pole. Fence posts round the court and the backboards don't qualify.
        /// </summary>
        public static List<Vector2> Poles(string id)
        {
            if (PoleCache.TryGetValue(id, out var poles)) return poles;
            poles = new List<Vector2>();
            PoleCache[id] = poles;
            var mesh = PackArt.Model(id);
            if (mesh == null) return poles;
            var b = mesh.bounds;
            const float cell = 0.04f;
            var columns = new Dictionary<Vector2Int, (float low, float high)>();
            foreach (var v in mesh.vertices)
            {
                var t = (v.y - b.min.y) / Mathf.Max(1e-4f, b.size.y);
                if (t < 0.05f) continue; // the court surface itself
                var k = new Vector2Int(Mathf.RoundToInt(v.x / cell), Mathf.RoundToInt(v.z / cell));
                columns[k] = columns.TryGetValue(k, out var col) ? (Mathf.Min(col.low, t), Mathf.Max(col.high, t)) : (t, t);
            }
            var groups = new List<(Vector2 sum, int n)>();
            foreach (var kv in columns)
            {
                if (kv.Value.low > 0.25f || kv.Value.high < 0.6f) continue;
                var p = new Vector2(kv.Key.x, kv.Key.y) * cell;
                var found = -1;
                for (var i = 0; i < groups.Count; i++)
                    if ((groups[i].sum / groups[i].n - p).sqrMagnitude < 0.1f * 0.1f) { found = i; break; }
                if (found < 0) groups.Add((p, 1));
                else groups[found] = (groups[found].sum + p, groups[found].n + 1);
            }
            foreach (var (sum, n) in groups) poles.Add(sum / n);
            return poles;
        }

        /// <summary>Collider radius of a pole, in metres.</summary>
        public const float PoleRadius = 0.3f;


        // ------------------------------------------------------------------ poses (2026-10-04)

        /// <summary>The model's tilt: roll around its length (x), then pitch around its width (z).</summary>
        public static Quaternion Pose(float roll, float pitch) => Quaternion.Euler(0f, 0f, pitch) * Quaternion.Euler(roll, 0f, 0f);

        public static bool Posed(MapDecoration d) => Mathf.Abs(d.roll) > 0.01f || Mathf.Abs(d.pitch) > 0.01f;

        /// <summary>
        /// The posed model's size (before its turn) and how far it must be lifted so its lowest point touches the
        /// ground, in metres at scale 1.
        /// </summary>
        public static (Vector3 size, float lift) PosedBounds(Def def, float roll, float pitch)
        {
            var mesh = PackArt.Model(def.Id);
            var b = mesh != null ? mesh.bounds : new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            var pose = Pose(roll, pitch);
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var p = pose * (corner * def.Size);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (max - min, -min.y);
        }

        /// <summary>A placed piece's footprint (width x, depth z before its turn), posed if it is.</summary>
        public static Vector2 Footprint(ArtLibrary.Piece piece, MapDecoration d)
        {
            var def = piece.Model != null ? Get(piece.Model) : null;
            if (def == null || !Posed(d) || piece.Round) return piece.Footprint;
            var (size, _) = PosedBounds(def, d.roll, d.pitch);
            return new Vector2(size.x * 0.92f, size.z * 0.92f);
        }

        /// <summary>A placed piece's height, posed if it is.</summary>
        public static float Height(ArtLibrary.Piece piece, MapDecoration d)
        {
            var def = piece.Model != null ? Get(piece.Model) : null;
            if (def == null || !Posed(d)) return piece.Height;
            return PosedBounds(def, d.roll, d.pitch).size.y;
        }
        /// <summary>The model's size in metres at scale 1 (x = width, y = height, z = depth before turning).</summary>
        public static Vector3 Extent(Def def)
        {
            var mesh = PackArt.Model(def.Id);
            return mesh != null ? mesh.bounds.size * def.Size : Vector3.one * def.Size;
        }
    }
}
