using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Interiors by building type (owner, 2026-10-04: walking into a building should tell you at once what it is).
    /// Layouts follow real ones (researched): the shop's counter beside the entrance, coolers on the back wall and shelf
    /// rows in the middle; the clinic's reception and waiting chairs at the front, beds with curtains and drip stands
    /// further in; the garage's car on a lift with tool chests and a workbench on the walls; the warehouse's pallet racks,
    /// a wire cage, goods by the door and a small office corner; bunks, footlockers and weapon racks in the barracks;
    /// protective gear at the lab's entrance and its hazards away from the door; rows of desks with filing cabinets and
    /// bookshelves in the security office; a sofa facing the TV, a dining set and a kitchen counter at home.
    /// Furniture stands against walls (its back to the wall) or free in the room with space all round; the cells by a
    /// door stay clear, and every piece is checked so the rest of the room stays reachable from the doors.
    /// The vehicle in front (truck, jeep, van, cars and motorcycles) and the shop's ATM are placed outside the main door.
    /// </summary>
    public static partial class StageForge
    {
        public enum Room { House, Shop, Clinic, Garage, Warehouse, Military, Lab, Security }

        /// <summary>The building type from its name (the same prefixes the wall styles use).</summary>
        public static Room RoomOf(string name)
        {
            name = name ?? "";
            if (name.StartsWith("Shop")) return Room.Shop;
            if (name.StartsWith("Clinic")) return Room.Clinic;
            if (name.StartsWith("Garage")) return Room.Garage;
            if (name.StartsWith("Warehouse")) return Room.Warehouse;
            if (name.StartsWith("Security")) return Room.Security;
            if (name.StartsWith("Military") || name.StartsWith("Barracks") || name.StartsWith("Post")) return Room.Military;
            if (name.Contains("Lab")) return Room.Lab;
            return Room.House;
        }

        /// <summary>Where a piece goes: against a wall, free-standing, by the door, far from it, or the garage bay (the car).</summary>
        private enum Spot { Wall, Free, NearDoor, FarFromDoor, Bay }

        private class Item
        {
            public string Id;
            public Spot Spot;
            public int Min, Max;
            /// <summary>Pieces that go with it: id, offset in its own space (x across, z along its depth), extra turn.</summary>
            public (string id, Vector2 offset, float turn)[] With;

            public Item(string id, Spot spot, int min, int max, params (string, Vector2, float)[] with)
            {
                Id = id;
                Spot = spot;
                Min = min;
                Max = max;
                With = with;
            }
        }

        private static readonly Vector2 InFront = new Vector2(0f, -0.95f);

        private static List<Item> Template(Room room)
        {
            switch (room)
            {
                case Room.Shop:
                    return new List<Item>
                    {
                        new Item("cashier_counter", Spot.NearDoor, 1, 1),
                        new Item("drinks_fridge", Spot.FarFromDoor, 1, 2),
                        new Item("store_shelf", Spot.Free, 2, 4),
                        new Item("shelf", Spot.Wall, 1, 3),
                    };
                case Room.Clinic:
                    return new List<Item>
                    {
                        new Item("reception_desk", Spot.NearDoor, 1, 1),
                        new Item("waiting_bench", Spot.NearDoor, 1, 2),
                        new Item("hospital_bed", Spot.FarFromDoor, 2, 3, ("iv_stand", new Vector2(0.85f, 0.6f), 0f)),
                        new Item("curtain_screen", Spot.FarFromDoor, 1, 2),
                        new Item("medical_cabinet", Spot.Wall, 1, 2),
                        new Item("lab_shelf", Spot.Wall, 0, 1),
                    };
                case Room.Garage:
                    return new List<Item>
                    {
                        new Item("car", Spot.Bay, 1, 1),
                        new Item("tool_cabinet", Spot.Wall, 1, 2),
                        new Item("tool_shelf", Spot.Wall, 1, 1),
                        new Item("workbench", Spot.Wall, 1, 1),
                        new Item("tire_rack", Spot.Wall, 0, 1),
                        new Item("oil_drums", Spot.Wall, 1, 1),
                    };
                case Room.Warehouse:
                    return new List<Item>
                    {
                        new Item("metal_cage", Spot.FarFromDoor, 1, 2),
                        new Item("pallet_rack", Spot.Wall, 2, 4),
                        new Item("pallet_rack", Spot.Free, 1, 3),
                        new Item("pallet_boxes", Spot.NearDoor, 2, 4),
                        new Item("crate_stack", Spot.Wall, 1, 2),
                        new Item("table", Spot.Free, 1, 1, ("chair", InFront, 180f), ("chair", new Vector2(0f, 0.95f), 0f)),
                    };
                case Room.Military:
                    return new List<Item>
                    {
                        new Item("bunk_bed", Spot.Wall, 2, 4, ("footlocker", new Vector2(0f, -1.35f), 0f)),
                        new Item("weapon_rack", Spot.Wall, 1, 2),
                        new Item("ammo_crate", Spot.Wall, 1, 2),
                        new Item("radio_desk", Spot.Wall, 1, 1, ("dining_chair", InFront, 180f)),
                        new Item("table", Spot.Free, 0, 1, ("dining_chair", InFront, 180f), ("dining_chair", new Vector2(0f, 0.95f), 0f)),
                    };
                case Room.Lab:
                    return new List<Item>
                    {
                        new Item("hazmat_rack", Spot.NearDoor, 1, 2),
                        new Item("fume_hood", Spot.FarFromDoor, 1, 2),
                        new Item("toxic_waste", Spot.FarFromDoor, 2, 3),
                        new Item("lab_workbench", Spot.Free, 1, 3, ("chair", InFront, 180f)),
                        new Item("lab_shelf", Spot.Wall, 2, 3),
                        new Item("office_desk", Spot.Wall, 1, 1, ("chair", InFront, 180f)),
                    };
                case Room.Security:
                    return new List<Item>
                    {
                        new Item("office_desk", Spot.NearDoor, 1, 1, ("chair", new Vector2(0f, 0.9f), 0f)),
                        new Item("office_desk", Spot.Free, 2, 4, ("chair", InFront, 180f)),
                        new Item("office_desk", Spot.Wall, 1, 2, ("chair", InFront, 180f)),
                        new Item("bookshelf", Spot.Wall, 1, 2),
                        new Item("filing_cabinet", Spot.Wall, 2, 4),
                    };
                default:
                    return new List<Item>
                    {
                        new Item("kitchen_counter", Spot.Wall, 1, 1),
                        new Item("fridge", Spot.Wall, 1, 1),
                        new Item("couch", Spot.Wall, 1, 1),
                        new Item("tv_stand", Spot.Wall, 1, 1),
                        new Item("dining_table", Spot.Free, 1, 1, ("dining_chair", InFront, 180f), ("dining_chair", new Vector2(0f, 0.95f), 0f)),
                        new Item("bed", Spot.Wall, 0, 1),
                        new Item("wardrobe", Spot.Wall, 0, 1),
                        new Item("shelf", Spot.Wall, 0, 1),
                    };
            }
        }

        // ------------------------------------------------------------------ layout

        private class Layout
        {
            public MapBuilding B;
            public bool[,] Walk;        // walkable interior cells (grid of the whole map, only this building used)
            public List<Vector2Int> Doors = new List<Vector2Int>();
            public HashSet<Vector2Int> DoorZone = new HashSet<Vector2Int>();
            public int X0, Z0, X1, Z1;  // interior, inclusive
        }

        /// <summary>Furnishes a building by its type; then the caller adds its loot and chest spots in the room left.</summary>
        private static void DesignInterior(Ctx c, MapBuilding b)
        {
            var room = RoomOf(b.name);
            var map = c.Map;
            var l = new Layout { B = b, X0 = b.x + 1, Z0 = b.z + 1, X1 = b.x + b.w - 1, Z1 = b.z + b.d - 1, Walk = new bool[map.width, map.height] };
            for (var x = l.X0; x <= l.X1; x++)
            for (var z = l.Z0; z <= l.Z1; z++)
                l.Walk[x, z] = map[x, z] != Tile.Wall;
            for (var x = b.x; x <= b.x + b.w; x++)
            for (var z = b.z; z <= b.z + b.d; z++)
            {
                var edge = x == b.x || x == b.x + b.w || z == b.z || z == b.z + b.d;
                if (edge && map[x, z] != Tile.Wall) l.Doors.Add(new Vector2Int(x, z));
            }
            // The way in stays clear: two cells straight in from each doorway cell, and one cell either side of them.
            foreach (var d in l.Doors)
            {
                var inward = d.y == b.z ? Vector2Int.up : d.y == b.z + b.d ? Vector2Int.down : d.x == b.x ? Vector2Int.right : Vector2Int.left;
                var side = new Vector2Int(inward.y, inward.x);
                for (var k = 1; k <= 2; k++)
                for (var s = -1; s <= 1; s++)
                    l.DoorZone.Add(d + inward * k + side * s);
            }

            foreach (var item in Template(room))
            {
                // Bigger rooms get more of each piece (2026-10-04: buildings grew 30 %), as far as they fit.
                var n = Mathf.RoundToInt(c.R(item.Min, item.Max + 1) * Mathf.Clamp((l.X1 - l.X0 + 1) * (l.Z1 - l.Z0 + 1) / 70f, 1f, 2.2f));
                for (var i = 0; i < n; i++) PlaceInside(c, l, item);
            }
            // Markers that are real things: fuel barrels in the warehouse (they explode), incubators in the lab.
            if (room == Room.Warehouse) for (var i = 0; i < c.R(2, 4); i++) MarkerInside(c, l, Tile.Barrel);
            if (room == Room.Lab) for (var i = 0; i < c.R(1, 3); i++) MarkerInside(c, l, Tile.Incubator);
            // A rug under the sofa corner at home, the lift under the garage car.
            if (room == Room.House && c.Chance(0.7f))
            {
                var centre = new Vector2((l.X0 + l.X1 + 1) * 0.5f, (l.Z0 + l.Z1 + 1) * 0.5f);
                c.Map.decorations.Add(new MapDecoration { id = "rug", x = centre.x + c.R(-1, 2), z = centre.y + c.R(-1, 2), rotation = c.R(0, 2) * 90f, scale = 1f });
            }
            if (room == Room.Garage)
            {
                var car = c.Map.props.FindLast(p => p.id == "car" && p.x > b.x && p.x < b.x + b.w && p.z > b.z && p.z < b.z + b.d);
                // The car model is long along its x; the lift markings are long along z.
                if (car != null) c.Map.decorations.Add(new MapDecoration { id = "car_lift", x = car.x, z = car.z, rotation = car.rotation + 90f, scale = 1f });
            }
            Outside(c, l, room);
        }

        /// <summary>The cells a piece covers at a quarter turn, as a rectangle of whole cells (at least its footprint).</summary>
        private static Vector2Int Cells(Vector2 footprint, int quarter) =>
            (quarter & 1) == 0
                ? new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(footprint.x - 0.05f)), Mathf.Max(1, Mathf.CeilToInt(footprint.y - 0.05f)))
                : new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(footprint.y - 0.05f)), Mathf.Max(1, Mathf.CeilToInt(footprint.x - 0.05f)));

        private static bool Free(Ctx c, Layout l, int x, int z) =>
            x >= l.X0 && x <= l.X1 && z >= l.Z0 && z <= l.Z1 && l.Walk[x, z] && c.Map[x, z] == Tile.Ground && !c.Occupied[x, z];

        private static float DoorDistance(Layout l, Vector2 p)
        {
            var best = float.MaxValue;
            foreach (var d in l.Doors) best = Mathf.Min(best, Vector2.Distance(p, new Vector2(d.x + 0.5f, d.y + 0.5f)));
            return best;
        }

        /// <summary>
        /// Tries every spot and turn for a piece and takes the best one that keeps the room reachable: against a wall (its
        /// back to it) for wall pieces, with a clear cell all round for free-standing ones; nearest the door for
        /// "near door" pieces, furthest for "far" ones, random for the rest.
        /// </summary>
        private static bool PlaceInside(Ctx c, Layout l, Item item)
        {
            var piece = Art.ArtLibrary.Get(item.Id);
            if (piece == null) return false;
            var back = BackTurn(piece);
            var candidates = new List<(RectInt cells, int quarter, float score)>();
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var size = Cells(piece.Footprint, (quarter + Mathf.RoundToInt(back / 90f)) & 3);
                for (var x = l.X0; x + size.x - 1 <= l.X1; x++)
                for (var z = l.Z0; z + size.y - 1 <= l.Z1; z++)
                {
                    var r = new RectInt(x, z, size.x, size.y);
                    // The garage bay ignores the doorway rule: the car stands just inside its wide door.
                    if (item.Spot == Spot.Bay ? !RectFreeIgnoringDoor(c, l, r) : !RectFree(c, l, r)) continue;
                    var wall = item.Spot != Spot.Free && item.Spot != Spot.Bay;
                    if (item.Spot == Spot.Bay ? false : wall ? !BackOnWall(c, l, r, quarter) : !RingFree(c, l, r)) continue;
                    var centre = r.center;
                    var doorDist = DoorDistance(l, centre);
                    var score = item.Spot == Spot.NearDoor ? -doorDist : item.Spot == Spot.FarFromDoor ? doorDist
                        : item.Spot == Spot.Bay ? -Vector2.Distance(centre, new Vector2((l.X0 + l.X1 + 1) * 0.5f, (l.Z0 + l.Z1 + 1) * 0.5f)) : c.R();
                    candidates.Add((r, quarter, score + c.R() * 0.5f));
                }
            }
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            var tries = 0;
            foreach (var (cells, quarter, _) in candidates)
            {
                if (++tries > 40) break;
                if (!KeepsRoomReachable(c, l, cells)) continue;
                Mark(c, cells, true);
                // Centre of the cells, pushed against the wall by the slack between the cells and the footprint.
                var total = (quarter + Mathf.RoundToInt(back / 90f)) & 3;
                var size = Cells(piece.Footprint, total);
                var world = (total & 1) == 0 ? piece.Footprint : new Vector2(piece.Footprint.y, piece.Footprint.x);
                var slack = (quarter & 1) == 0 ? size.y - world.y : size.x - world.x;
                var backDir = Quaternion.Euler(0f, quarter * 90f, 0f) * Vector3.forward;
                var centre = new Vector3(cells.x + size.x * 0.5f, 0f, cells.y + size.y * 0.5f);
                if (item.Spot != Spot.Free && item.Spot != Spot.Bay) centre += backDir * Mathf.Max(0f, slack * 0.5f - 0.05f);
                var yaw = quarter * 90f + back;
                c.Map.props.Add(new MapDecoration
                {
                    id = item.Id, x = centre.x, z = centre.z, rotation = yaw + (c.R() - 0.5f) * 4f, scale = 1f,
                    flip = !piece.NoFlip && piece.Model != null && c.Chance(0.5f),
                });
                if (item.With != null)
                    foreach (var (id, offset, turn) in item.With) Companion(c, l, id, centre, quarter * 90f, offset, turn);
                return true;
            }
            return false;
        }

        /// <summary>A piece that goes with another (a chair at a desk): placed if its cell is free and the room stays open.</summary>
        private static void Companion(Ctx c, Layout l, string id, Vector3 centre, float turn, Vector2 offset, float extraTurn)
        {
            var piece = Art.ArtLibrary.Get(id);
            if (piece == null) return;
            var p = centre + Quaternion.Euler(0f, turn, 0f) * new Vector3(offset.x, 0f, offset.y);
            var cell = new RectInt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z), 1, 1);
            if (!RectFree(c, l, cell) || !KeepsRoomReachable(c, l, cell)) return;
            Mark(c, cell, true);
            c.Map.props.Add(new MapDecoration { id = id, x = p.x, z = p.z, rotation = turn + extraTurn + BackTurn(piece) + (c.R() - 0.5f) * 20f, scale = 1f });
        }

        /// <summary>A marker object (barrel, incubator) on a free cell, keeping the room reachable.</summary>
        private static void MarkerInside(Ctx c, Layout l, Tile tile)
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var x = c.R(l.X0, l.X1 + 1);
                var z = c.R(l.Z0, l.Z1 + 1);
                var cell = new RectInt(x, z, 1, 1);
                if (!RectFree(c, l, cell) || !KeepsRoomReachable(c, l, cell)) continue;
                c.Map[x, z] = tile;
                c.Occupied[x, z] = true;
                return;
            }
        }

        private static bool RectFreeIgnoringDoor(Ctx c, Layout l, RectInt r)
        {
            for (var x = r.xMin; x < r.xMax; x++)
            for (var z = r.yMin; z < r.yMax; z++)
                if (!Free(c, l, x, z)) return false;
            return true;
        }

        private static bool RectFree(Ctx c, Layout l, RectInt r)
        {
            for (var x = r.xMin; x < r.xMax; x++)
            for (var z = r.yMin; z < r.yMax; z++)
                if (!Free(c, l, x, z) || l.DoorZone.Contains(new Vector2Int(x, z))) return false;
            return true;
        }

        /// <summary>Free-standing pieces keep a clear (walkable) cell all round, so you can walk round them.</summary>
        private static bool RingFree(Ctx c, Layout l, RectInt r)
        {
            for (var x = r.xMin - 1; x <= r.xMax; x++)
            for (var z = r.yMin - 1; z <= r.yMax; z++)
            {
                if (x >= r.xMin && x < r.xMax && z >= r.yMin && z < r.yMax) continue;
                if (!Free(c, l, x, z)) return false;
            }
            return true;
        }

        /// <summary>The whole back side of the piece (at this turn) touches wall, and the cells in front are walkable.</summary>
        private static bool BackOnWall(Ctx c, Layout l, RectInt r, int quarter)
        {
            var map = c.Map;
            switch (quarter)
            {
                case 0: // back to the north (+z)
                    for (var x = r.xMin; x < r.xMax; x++) if (map[x, r.yMax] != Tile.Wall || !Free(c, l, x, r.yMin - 1)) return false;
                    return true;
                case 1: // back to the east (+x)
                    for (var z = r.yMin; z < r.yMax; z++) if (map[r.xMax, z] != Tile.Wall || !Free(c, l, r.xMin - 1, z)) return false;
                    return true;
                case 2: // back to the south (-z)
                    for (var x = r.xMin; x < r.xMax; x++) if (map[x, r.yMin - 1] != Tile.Wall || !Free(c, l, x, r.yMax)) return false;
                    return true;
                default: // back to the west (-x)
                    for (var z = r.yMin; z < r.yMax; z++) if (map[r.xMin - 1, z] != Tile.Wall || !Free(c, l, r.xMax, z)) return false;
                    return true;
            }
        }

        private static void Mark(Ctx c, RectInt r, bool on)
        {
            for (var x = r.xMin; x < r.xMax; x++)
            for (var z = r.yMin; z < r.yMax; z++) c.Occupied[x, z] = on;
        }

        /// <summary>
        /// With these cells taken, can every other walkable cell of the room (markers included) still be reached from a
        /// door? Walking counts 4 neighbours; the furniture's own cells are blocked.
        /// </summary>
        private static bool KeepsRoomReachable(Ctx c, Layout l, RectInt taken)
        {
            bool Walkable(int x, int z) =>
                x >= l.X0 && x <= l.X1 && z >= l.Z0 && z <= l.Z1 && l.Walk[x, z] && !c.Occupied[x, z] && !taken.Contains(new Vector2Int(x, z))
                && c.Map[x, z] != Tile.Barrel && c.Map[x, z] != Tile.Incubator;
            var seen = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var d in l.Doors)
                foreach (var n in new[] { new Vector2Int(d.x + 1, d.y), new Vector2Int(d.x - 1, d.y), new Vector2Int(d.x, d.y + 1), new Vector2Int(d.x, d.y - 1) })
                    if (Walkable(n.x, n.y) && seen.Add(n)) queue.Enqueue(n);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var n in new[] { new Vector2Int(p.x + 1, p.y), new Vector2Int(p.x - 1, p.y), new Vector2Int(p.x, p.y + 1), new Vector2Int(p.x, p.y - 1) })
                    if (Walkable(n.x, n.y) && seen.Add(n)) queue.Enqueue(n);
            }
            for (var x = l.X0; x <= l.X1; x++)
            for (var z = l.Z0; z <= l.Z1; z++)
                if (Walkable(x, z) && !seen.Contains(new Vector2Int(x, z))) return false;
            return true;
        }

        private static readonly Dictionary<string, float> BackTurns = new Dictionary<string, float>();

        /// <summary>
        /// The extra turn that puts a piece's back to +z. Claude's furniture is built that way (0). The owner's models are
        /// checked once: the side where the model is tallest (a sofa's backrest, a chair's back) is its back.
        /// </summary>
        public static float BackTurn(Art.ArtLibrary.Piece piece)
        {
            if (piece.Model == null) return 0f;
            if (BackTurns.TryGetValue(piece.Id, out var turn)) return turn;
            turn = 0f;
            var mesh = Art.PackArt.Model(piece.Model);
            if (mesh != null)
            {
                var b = mesh.bounds;
                var high = Vector2.zero;
                var n = 0;
                foreach (var v in mesh.vertices)
                {
                    if (v.y < b.min.y + b.size.y * 0.7f) continue;
                    high += new Vector2(v.x - b.center.x, v.z - b.center.z);
                    n++;
                }
                if (n > 0)
                {
                    high /= n;
                    // Only a clear lean counts (shelves and tables are symmetric: no turn needed).
                    if (high.magnitude > Mathf.Max(b.size.x, b.size.z) * 0.08f)
                    {
                        if (Mathf.Abs(high.x) > Mathf.Abs(high.y)) turn = high.x > 0f ? -90f : 90f;
                        else turn = high.y > 0f ? 0f : 180f;
                    }
                }
            }
            BackTurns[piece.Id] = turn;
            return turn;
        }

        // ------------------------------------------------------------------ outside: the vehicle in front, the shop's ATM

        private static void Outside(Ctx c, Layout l, Room room)
        {
            // The main door: the one on the south wall (the generator always makes one), else any.
            var main = l.Doors.FindAll(d => d.y == l.B.z);
            if (main.Count == 0) main = l.Doors;
            if (main.Count == 0) return;
            var door = main[main.Count / 2];
            var outward = door.y == l.B.z ? Vector2Int.down : door.y == l.B.z + l.B.d ? Vector2Int.up : door.x == l.B.x ? Vector2Int.left : Vector2Int.right;
            var side = new Vector2Int(-outward.y, outward.x);
            switch (room)
            {
                case Room.Shop:
                    OutsideOne(c, "atm", door, outward, side, 1.5f, 2.2f, 0f);
                    break;
                case Room.Warehouse:
                    OutsideOne(c, "truck", door, outward, side, 4.5f, 5f, 90f);
                    break;
                case Room.Military:
                    OutsideOne(c, "jeep", door, outward, side, 3.5f, 4.5f, 90f);
                    OutsideOne(c, "sandbags", door, outward, side, 1.6f, -2.6f, 0f);
                    break;
                case Room.Lab:
                    OutsideOne(c, "van", door, outward, side, 3.5f, 5f, 90f);
                    break;
                case Room.Security:
                    OutsideOne(c, "car", door, outward, side, 3.5f, 5f, 90f);
                    OutsideOne(c, "motorcycle", door, outward, side, 2.2f, -3.5f, 90f);
                    if (c.Chance(0.5f)) OutsideOne(c, "motorcycle", door, outward, side, 2.2f, -5.5f, 90f);
                    break;
            }
        }

        /// <summary>
        /// One piece outside a door: <paramref name="ahead"/> metres out from the wall, <paramref name="across"/> metres to
        /// the side (so the doorway stays clear), turned <paramref name="turn"/> from facing the door. Tries the other side
        /// and a little further out if that spot is taken.
        /// </summary>
        private static void OutsideOne(Ctx c, string id, Vector2Int door, Vector2Int outward, Vector2Int side, float ahead, float across, float turn)
        {
            // Tries the preferred spot first, then nearby ones further out and to either side, then parked the other way.
            var baseYaw = outward == Vector2Int.down ? 180f : outward == Vector2Int.up ? 0f : outward == Vector2Int.left ? 270f : 90f;
            foreach (var t in new[] { turn, turn + 90f })
            foreach (var extra in new[] { 0f, 1f, 2f, 3f, 4f })
            foreach (var shift in new[] { 0f, 1.5f, 3f, -1.5f })
            foreach (var flipSide in new[] { 1f, -1f })
            {
                var sideways = (Mathf.Abs(across) + shift) * Mathf.Sign(across == 0f ? 1f : across) * flipSide;
                var p = new Vector2(door.x + 0.5f, door.y + 0.5f) + (Vector2)outward * (ahead + extra) + (Vector2)side * sideways;
                if (Piece(c, id, Mathf.Round(p.x * 2f) / 2f, Mathf.Round(p.y * 2f) / 2f, baseYaw + t)) return;
            }
        }
    }
}
