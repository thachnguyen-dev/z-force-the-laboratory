using UnityEngine;
using P = ZombieGame.Art.ArtPalette;

namespace ZombieGame.Art
{
    /// <summary>
    /// Map art that depends on what a block belongs to (2026-10-03, owner: better map quality, references allowed):
    /// house walls with plaster, trim and windows (labs keep the facility panels); the map edge as a tree line (town,
    /// reserve) or a perimeter wall with razor wire and floodlights (lab, checkpoint); long rows of cover as fences.
    /// Like the rest of the block art, only tops and south faces are built.
    /// </summary>
    public static class ThemeArt
    {
        // ------------------------------------------------------------------ which look

        /// <summary>
        /// Wall styles. 2026-10-04 (owner: buildings recognisable by type): Shop, Clinic, Garage, Warehouse and Security
        /// join the house, cabin, military and lab-facility looks. The style comes from the building's name prefix
        /// (House, Cabin, Shop, Clinic, Garage, Warehouse, Military / Barracks / Post, Lab, Security), so renaming a
        /// building in the Map Editor changes its look.
        /// </summary>
        public enum WallKind { Facility, House, Cabin, Military, Shop, Clinic, Garage, Warehouse, Security }

        /// <summary>The building whose outline holds this wall cell, by its name (houses, shops, clinic, cabins, posts…).</summary>
        public static WallKind KindOf(StageMap map, int x, int z, out MapBuilding building)
        {
            building = null;
            foreach (var b in map.buildings)
            {
                if (x < b.x || x > b.x + b.w || z < b.z || z > b.z + b.d) continue;
                building = b;
                return KindOf(b.name);
            }
            return WallKind.Facility;
        }

        public static WallKind KindOf(string name)
        {
            name = name ?? "";
            if (name.StartsWith("House")) return WallKind.House;
            if (name.StartsWith("Cabin")) return WallKind.Cabin;
            if (name.StartsWith("Shop")) return WallKind.Shop;
            if (name.StartsWith("Clinic")) return WallKind.Clinic;
            if (name.StartsWith("Garage")) return WallKind.Garage;
            if (name.StartsWith("Warehouse")) return WallKind.Warehouse;
            if (name.StartsWith("Security")) return WallKind.Security;
            if (name.StartsWith("Military") || name.StartsWith("Barracks") || name.StartsWith("Post")) return WallKind.Military;
            return WallKind.Facility;
        }

        // Muted, weathered colours (researched palette, 2026-10-04).
        private static readonly Color[] Plasters =
        {
            new Color(0.78f, 0.72f, 0.6f), new Color(0.6f, 0.67f, 0.72f), new Color(0.6f, 0.65f, 0.54f),
            new Color(0.74f, 0.6f, 0.57f), new Color(0.72f, 0.66f, 0.5f),
        };

        private static readonly Color[] ShopColours = { new Color(0.36f, 0.5f, 0.5f), new Color(0.7f, 0.58f, 0.34f), new Color(0.55f, 0.32f, 0.27f) };
        private static readonly Color[] WarehouseColours = { new Color(0.5f, 0.33f, 0.27f), new Color(0.52f, 0.54f, 0.53f) };

        public static Color WallColour(WallKind kind, MapBuilding b)
        {
            var h = b != null ? Mathf.Abs((b.name ?? "").GetHashCode()) : 0;
            switch (kind)
            {
                case WallKind.House: return Plasters[h % Plasters.Length];
                case WallKind.Cabin: return new Color(0.55f, 0.4f, 0.27f);
                case WallKind.Military: return new Color(0.4f, 0.44f, 0.29f);
                case WallKind.Shop: return ShopColours[h % ShopColours.Length];
                case WallKind.Clinic: return new Color(0.86f, 0.86f, 0.83f);
                case WallKind.Garage: return new Color(0.45f, 0.5f, 0.55f);
                case WallKind.Warehouse: return WarehouseColours[h % WarehouseColours.Length];
                case WallKind.Security: return new Color(0.33f, 0.4f, 0.5f);
                default: return new Color(0.6f, 0.67f, 0.68f);
            }
        }

        private static bool Is(StageMap map, int x, int z, Tile t) => map[x, z] == t;

        // ------------------------------------------------------------------ house walls

        /// <summary>
        /// A house wall: plaster with a darker base trim and a white top trim, a window every few metres on the front
        /// (some boarded up — it's an outbreak town), plank walls for cabins, olive panels with a stencil for military posts.
        /// </summary>
        public static void HouseWall(MeshKit k, StageMap map, int x, int z, WallKind kind, MapBuilding b)
        {
            var h = StageMapBuilder.WallHeight;
            float x0 = x, x1 = x + 1f, z0 = z, z1 = z + 1f;
            var body = WallColour(kind, b);
            var trim = kind == WallKind.Cabin ? new Color(0.42f, 0.27f, 0.15f) : new Color(0.95f, 0.94f, 0.9f);
            var baseTrim = MeshKit.Shade(body, 0.62f);
            // Top: a trim cap.
            k.Quad(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), trim);
            if (TileAssembler.FrontHidden(map, x, z, Tile.Wall)) return;
            const float d = 0.004f;
            var f = z0;
            // Base trim, body with a soft gradient, top trim lip.
            k.Quad(new Vector3(x0, 0f, f - 0.04f), new Vector3(x0, 0.4f, f - 0.04f), new Vector3(x1, 0.4f, f - 0.04f), new Vector3(x1, 0f, f - 0.04f), baseTrim);
            k.Quad(new Vector3(x0, 0.4f, f - 0.04f), new Vector3(x0, 0.4f, f), new Vector3(x1, 0.4f, f), new Vector3(x1, 0.4f, f - 0.04f), MeshKit.Shade(baseTrim, 1.3f));
            k.Quad(new Vector3(x0, 0.4f, f), new Vector3(x0, h - 0.25f, f), new Vector3(x1, h - 0.25f, f), new Vector3(x1, 0.4f, f), MeshKit.Shade(body, 0.86f), body, body, MeshKit.Shade(body, 0.86f));
            k.Quad(new Vector3(x0, h - 0.25f, f - 0.06f), new Vector3(x0, h, f - 0.06f), new Vector3(x1, h, f - 0.06f), new Vector3(x1, h - 0.25f, f - 0.06f), trim);
            k.Quad(new Vector3(x0, h - 0.25f, f), new Vector3(x0, h - 0.25f, f - 0.06f), new Vector3(x1, h - 0.25f, f - 0.06f), new Vector3(x1, h - 0.25f, f), MeshKit.Shade(trim, 0.7f));
            if (kind == WallKind.Cabin)
                for (var i = 1; i < 6; i++)
                {
                    var y = 0.4f + i * 0.4f;
                    k.Quad(new Vector3(x0, y - 0.03f, f - d), new Vector3(x0, y, f - d), new Vector3(x1, y, f - d), new Vector3(x1, y - 0.03f, f - d), MeshKit.Shade(body, 0.6f));
                }
            // A window on every third cell of a run, unless it's an end of the wall (doorway or corner next to it).
            var run = Is(map, x - 1, z, Tile.Wall) && Is(map, x + 1, z, Tile.Wall);
            if (!run || ((x % 3) + 3) % 3 != 1) return;
            var glass = new Color(0.2f, 0.32f, 0.42f);
            float wx0 = x0 + 0.18f, wx1 = x1 - 0.18f, wy0 = 1.1f, wy1 = 2.2f;
            k.Quad(new Vector3(wx0 - 0.07f, wy0 - 0.07f, f - d), new Vector3(wx0 - 0.07f, wy1 + 0.07f, f - d), new Vector3(wx1 + 0.07f, wy1 + 0.07f, f - d), new Vector3(wx1 + 0.07f, wy0 - 0.07f, f - d), trim);
            var boarded = MeshKit.Hash01(x, 21, z) < 0.35f;
            k.Quad(new Vector3(wx0, wy0, f - d * 2f), new Vector3(wx0, wy1, f - d * 2f), new Vector3(wx1, wy1, f - d * 2f), new Vector3(wx1, wy0, f - d * 2f),
                boarded ? new Color(0.12f, 0.12f, 0.16f) : glass, boarded ? new Color(0.12f, 0.12f, 0.16f) : MeshKit.Shade(glass, 1.5f),
                boarded ? new Color(0.12f, 0.12f, 0.16f) : MeshKit.Shade(glass, 1.5f), boarded ? new Color(0.12f, 0.12f, 0.16f) : glass);
            if (boarded)
            {
                var plank = new Color(0.66f, 0.47f, 0.28f);
                k.Quad(new Vector3(wx0 - 0.1f, wy0 + 0.25f, f - d * 3f), new Vector3(wx0 - 0.1f, wy0 + 0.45f, f - d * 3f), new Vector3(wx1 + 0.1f, wy1 - 0.25f, f - d * 3f), new Vector3(wx1 + 0.1f, wy1 - 0.45f, f - d * 3f), plank);
                k.Quad(new Vector3(wx0 - 0.1f, wy1 - 0.45f, f - d * 3f), new Vector3(wx0 - 0.1f, wy1 - 0.25f, f - d * 3f), new Vector3(wx1 + 0.1f, wy0 + 0.45f, f - d * 3f), new Vector3(wx1 + 0.1f, wy0 + 0.25f, f - d * 3f), MeshKit.Shade(plank, 0.85f));
            }
            else
                k.Quad(new Vector3((wx0 + wx1) * 0.5f - 0.03f, wy0, f - d * 3f), new Vector3((wx0 + wx1) * 0.5f - 0.03f, wy1, f - d * 3f),
                    new Vector3((wx0 + wx1) * 0.5f + 0.03f, wy1, f - d * 3f), new Vector3((wx0 + wx1) * 0.5f + 0.03f, wy0, f - d * 3f), trim);
            // Sill.
            k.Box(new Vector3((wx0 + wx1) * 0.5f, wy0 - 0.12f, f - 0.06f), new Vector3(wx1 - wx0 + 0.2f, 0.06f, 0.12f), trim, Faces.Hidden);
        }

        /// <summary>
        /// Wear for a building wall cell (owner, 2026-10-05: cracks only on buildings): grime, cracks and chipped paint
        /// from the shared wear texture, heavier on some stretches of wall than others.
        /// </summary>
        public static Vector4 WallWear(int x, int z)
        {
            var heavy = MeshKit.Noise2(x * 0.21f, z * 0.21f);
            return new Vector4(0.4f + heavy * 0.35f, 0.8f + heavy * 0.2f, 0.3f + heavy * 0.35f, 0f);
        }

        /// <summary>
        /// Claw scratches on a wall's front face, on about one cell in eight: three or four parallel gouges raked down at
        /// a slant, dark with a pale chipped edge, now and then with a smear of blood.
        /// </summary>
        public static void ScratchMarks(MeshKit k, int x, int z)
        {
            if (MeshKit.Hash01(x, 57, z) > 0.125f) return;
            var f = z - 0.014f;
            var marks = MeshKit.Hash01(x, 58, z) < 0.5f ? 3 : 4;
            var cx = x + 0.3f + MeshKit.Hash01(x, 59, z) * 0.4f;
            var top = 1.9f + MeshKit.Hash01(x, 60, z) * 0.5f;
            var slant = (MeshKit.Hash01(x, 61, z) - 0.5f) * 0.5f;
            var length = 0.85f + MeshKit.Hash01(x, 62, z) * 0.45f;
            var bloody = MeshKit.Hash01(x, 63, z) < 0.3f;
            var gouge = bloody ? new Color(0.32f, 0.03f, 0.02f) : new Color(0.08f, 0.07f, 0.06f);
            var chipped = new Color(0.78f, 0.76f, 0.7f);
            for (var i = 0; i < marks; i++)
            {
                var sx = cx + (i - (marks - 1) * 0.5f) * 0.11f;
                var x0 = sx + slant * 0.5f;
                var x1 = sx - slant * 0.5f;
                var y0 = top - i * 0.03f;
                var y1 = y0 - length * (0.85f + 0.15f * MeshKit.Hash01(x, 64 + i, z));
                // A pale chipped edge, then the dark gouge over it; both taper at the ends.
                k.Quad(new Vector3(x1 - 0.016f, y1, f + 0.002f), new Vector3(x0 - 0.03f, y0, f + 0.002f), new Vector3(x0 + 0.03f, y0, f + 0.002f), new Vector3(x1 + 0.016f, y1, f + 0.002f), chipped);
                k.Quad(new Vector3(x1 - 0.008f, y1 + 0.03f, f), new Vector3(x0 - 0.02f, y0 - 0.02f, f), new Vector3(x0 + 0.02f, y0 - 0.02f, f), new Vector3(x1 + 0.008f, y1 + 0.03f, f), gouge);
            }
        }

        // ------------------------------------------------------------------ map edge

        /// <summary>The map edge for towns and the reserve: a dense wall of chunky trees (cliff cells keep blocking).</summary>
        public static void TreeLine(MeshKit k, StageMap map, int x, int z)
        {
            var canopy = new Color(0.07f, 0.2f, 0.17f);
            var canopyTop = new Color(0.16f, 0.36f, 0.24f);
            var top = 3.6f + MeshKit.Noise2(x * 0.3f, z * 0.3f) * 1.2f;
            // A flat canopy roof over the whole edge (mostly hidden by the trees in front of it).
            k.Quad(new Vector3(x, top, z), new Vector3(x, top, z + 1f), new Vector3(x + 1f, top, z + 1f), new Vector3(x + 1f, top, z), Color.Lerp(canopy, canopyTop, 0.4f));
            var front = !Is(map, x, z - 1, Tile.Cliff) && z > 0;
            var edge = front || !Is(map, x - 1, z, Tile.Cliff) || !Is(map, x + 1, z, Tile.Cliff) || !Is(map, x, z + 1, Tile.Cliff);
            if (!edge || MeshKit.Hash01(x, 31, z) < 0.35f) return;
            var jitter = new Vector3(MeshKit.Hash01(x, 32, z) - 0.5f, 0f, MeshKit.Hash01(x, 33, z) - 0.5f) * 0.6f;
            var p = new Vector3(x + 0.5f, 0f, z + 0.5f) + jitter;
            var size = 1.9f + MeshKit.Hash01(x, 34, z) * 0.9f;
            if (front) k.Cylinder(p, 0.2f, 1.4f, 6, new Color(0.24f, 0.16f, 0.1f), false);
            k.Blob(p + Vector3.up * 1.1f, new Vector3(size, top - 0.6f, size), x * 131 + z, canopy, canopyTop, 0.22f);
        }

        /// <summary>The map edge for labs and the checkpoint: a tall perimeter wall, razor wire on top, floodlights.</summary>
        public static void PerimeterWall(MeshKit k, StageMap map, int x, int z)
        {
            const float h = 4f;
            float x0 = x, x1 = x + 1f, z0 = z, z1 = z + 1f;
            var topColour = new Color(0.16f, 0.19f, 0.27f);
            k.Quad(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, h, z0), topColour);
            if (Is(map, x, z - 1, Tile.Cliff) || z == 0) return;
            var panel = new Color(0.3f, 0.35f, 0.46f);
            var panelLow = new Color(0.18f, 0.21f, 0.3f);
            k.Quad(new Vector3(x0, 0f, z0), new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, 0f, z0), panelLow, panel, panel, panelLow);
            // Panel joints every 2 m, a hazard band, and wire coils on top.
            if ((x & 1) == 0) k.Quad(new Vector3(x0, 0f, z0 - 0.004f), new Vector3(x0, h, z0 - 0.004f), new Vector3(x0 + 0.05f, h, z0 - 0.004f), new Vector3(x0 + 0.05f, 0f, z0 - 0.004f), panelLow);
            for (var i = 0; i < 4; i++)
            {
                var sx = x0 + i * 0.25f;
                k.Quad(new Vector3(sx, 0.35f, z0 - 0.006f), new Vector3(sx + 0.12f, 0.7f, z0 - 0.006f), new Vector3(sx + 0.25f, 0.7f, z0 - 0.006f), new Vector3(sx + 0.13f, 0.35f, z0 - 0.006f),
                    (((x * 4 + i) & 1) == 0) ? P.Hazard : P.Black);
            }
            var before = k.Transform;
            k.Transform = before * Matrix4x4.TRS(new Vector3(x0, h + 0.15f, z0 + 0.3f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            k.Cylinder(Vector3.zero, 0.16f, 1f, 6, new Color(0.55f, 0.6f, 0.66f), false);
            k.Transform = before;
            if (((x % 9) + 9) % 9 == 0)
            {
                k.Cylinder(new Vector3(x0 + 0.5f, h, z0 + 0.5f), 0.07f, 1.2f, 6, P.MetalDark, true, P.Metal);
                k.Box(new Vector3(x0 + 0.5f, h + 1.15f, z0 + 0.25f), new Vector3(0.5f, 0.22f, 0.3f), P.MetalDark, Faces.Bottom, P.Metal);
                k.Quad(new Vector3(x0 + 0.3f, h + 1.18f, z0 + 0.098f), new Vector3(x0 + 0.3f, h + 1.34f, z0 + 0.098f), new Vector3(x0 + 0.7f, h + 1.34f, z0 + 0.098f), new Vector3(x0 + 0.7f, h + 1.18f, z0 + 0.098f), MeshKit.Glow(new Color(1f, 0.95f, 0.75f), 0.9f));
            }
        }

        // ------------------------------------------------------------------ the steel mesh fence around the map

        /// <summary>Fence panels are 2 m wide (the owner's model, scaled), one every other cell along the edge.</summary>
        public const float FencePanel = 2f;

        /// <summary>
        /// Cliff cells (the map edge) draw the owner's steel mesh fence on their inner side, facing the play area; the cells
        /// themselves are bare ground. False when the fence model isn't available (the old edge art is used then).
        /// </summary>
        public static bool FenceBorder(MeshKit k, StageMap map, int x, int z)
        {
            if (!PackArt.Has("fences")) return false;
            bool Open(int cx, int cz) => cx >= 0 && cz >= 0 && cx < map.width && cz < map.height && map[cx, cz] != Tile.Cliff;
            var scale = FencePanel;
            // A panel per two cells, placed by the even cell of each pair so neighbours line up.
            if (Open(x, z + 1) && (x & 1) == 0) AddPanel(k, new Vector3(x + 1f, 0f, z + 1f), 0f, scale);
            if (Open(x, z - 1) && (x & 1) == 0) AddPanel(k, new Vector3(x + 1f, 0f, z), 180f, scale);
            if (Open(x + 1, z) && (z & 1) == 0) AddPanel(k, new Vector3(x + 1f, 0f, z + 1f), 90f, scale);
            if (Open(x - 1, z) && (z & 1) == 0) AddPanel(k, new Vector3(x, 0f, z + 1f), -90f, scale);
            return true;
        }

        private static void AddPanel(MeshKit k, Vector3 at, float yaw, float scale) =>
            PackArt.AddTo(k, "fences", Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale));

        // ------------------------------------------------------------------ fences (long rows of cover)

        /// <summary>A cover cell in a straight row of at least four: drawn as a fence instead of crates.</summary>
        public static bool FenceRun(StageMap map, int x, int z, out bool horizontal)
        {
            int Run(int dx, int dz)
            {
                var n = 1;
                for (var i = 1; i < 6 && Is(map, x + dx * i, z + dz * i, Tile.Cover); i++) n++;
                for (var i = 1; i < 6 && Is(map, x - dx * i, z - dz * i, Tile.Cover); i++) n++;
                return n;
            }
            var hx = Run(1, 0);
            var hz = Run(0, 1);
            horizontal = hx >= hz;
            return Mathf.Max(hx, hz) >= 4;
        }

        public static void Fence(MeshKit k, StageMap map, int x, int z, bool horizontal, string theme)
        {
            var before = k.Transform;
            // Built along local x; vertical runs are turned a quarter.
            k.Transform = before * Matrix4x4.TRS(new Vector3(x + 0.5f, 0f, z + 0.5f), Quaternion.Euler(0f, horizontal ? 0f : 90f, 0f), Vector3.one);
            switch (theme)
            {
                case "reserve":
                    // Log rails between posts.
                    k.Cylinder(new Vector3(-0.5f, 0f, 0f), 0.11f, 1.25f, 6, new Color(0.45f, 0.3f, 0.18f), true, new Color(0.62f, 0.45f, 0.28f));
                    foreach (var y in new[] { 0.45f, 0.95f })
                        k.Box(new Vector3(0f, y, 0f), new Vector3(1.02f, 0.13f, 0.13f), new Color(0.55f, 0.37f, 0.22f), Faces.Bottom, new Color(0.66f, 0.47f, 0.3f));
                    break;
                case "town":
                    // A cream picket fence.
                    var cream = new Color(0.92f, 0.88f, 0.76f);
                    for (var i = 0; i < 4; i++)
                        k.BevelBox(new Vector3(-0.375f + i * 0.25f, 0f, 0f), new Vector3(0.14f, 1.15f, 0.08f), 0.03f, cream, MeshKit.Shade(cream, 1.05f));
                    k.Box(new Vector3(0f, 0.7f, -0.03f), new Vector3(1.01f, 0.1f, 0.06f), MeshKit.Shade(cream, 0.85f), Faces.Bottom);
                    break;
                default:
                    // Lab / checkpoint: a concrete foot, steel posts and a chain-link screen (dark mesh lines).
                    var concrete = new Color(0.36f, 0.4f, 0.5f);
                    k.Box(Vector3.zero, new Vector3(1.01f, 0.35f, 0.3f), concrete, Faces.Bottom, MeshKit.Shade(concrete, 1.2f));
                    k.Cylinder(new Vector3(-0.5f, 0.35f, 0f), 0.05f, 1.15f, 6, P.MetalDark, true, P.Metal);
                    var mesh = new Color(0.55f, 0.62f, 0.7f);
                    k.Quad(new Vector3(-0.5f, 0.35f, -0.01f), new Vector3(-0.5f, 1.45f, -0.01f), new Vector3(0.5f, 1.45f, -0.01f), new Vector3(0.5f, 0.35f, -0.01f), MeshKit.Shade(mesh, 0.55f));
                    for (var i = 0; i < 5; i++)
                    {
                        var a = -0.5f + i * 0.25f;
                        k.Quad(new Vector3(a, 0.35f, -0.02f), new Vector3(a + 0.2f, 1.45f, -0.02f), new Vector3(a + 0.24f, 1.45f, -0.02f), new Vector3(a + 0.04f, 0.35f, -0.02f), mesh);
                    }
                    k.Box(new Vector3(0f, 1.42f, 0f), new Vector3(1.01f, 0.06f, 0.06f), P.Metal, Faces.Bottom, P.MetalLight);
                    break;
            }
            k.Transform = before;
        }
    }
}
