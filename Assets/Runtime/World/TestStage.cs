using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// ⚠ Placeholder content made by Claude (docs/placeholders.md): a 120 × 120 m greybox stage with a Spawn Area,
    /// a Warehouse, a Town, a Checkpoint combat zone and a walled Boss Area. Replaced when real stages are designed.
    /// </summary>
    public static class TestStage
    {
        private static readonly Color Cliff = new Color(0.42f, 0.36f, 0.3f);
        private static readonly Color Concrete = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color Shelf = new Color(0.45f, 0.35f, 0.25f);
        private static readonly Color HouseWall = new Color(0.5f, 0.48f, 0.45f);
        private static readonly Color Car = new Color(0.3f, 0.35f, 0.45f);
        private static readonly Color Sandbag = new Color(0.62f, 0.56f, 0.4f);
        private static readonly Color Military = new Color(0.33f, 0.38f, 0.28f);
        private static readonly Color BossWall = new Color(0.35f, 0.3f, 0.3f);

        public static StageLayout Build()
        {
            var s = new StageLayout
            {
                Name = "Test Stage",
                Size = new Vector2(120f, 120f),
                PlayerSpawn = new Vector3(14f, 0f, 14f),
                SpawnArea = new Rect(4f, 4f, 22f, 22f),
                BossArea = new Rect(86f, 84f, 30f, 30f),
                BossPosition = new Vector3(104f, 0f, 104f),
            };

            // D-075: natural terrain closes the map edge.
            Add(s, 60f, -1.5f, 126f, 3f, 5f, Cliff);
            Add(s, 60f, 121.5f, 126f, 3f, 5f, Cliff);
            Add(s, -1.5f, 60f, 3f, 126f, 5f, Cliff);
            Add(s, 121.5f, 60f, 3f, 126f, 5f, Cliff);

            Warehouse(s);
            Town(s);
            Checkpoint(s);
            BossYard(s);

            // Scattered cover in the open ground between areas.
            foreach (var (x, z) in new[] { (32f, 18f), (38f, 30f), (22f, 36f), (46f, 16f), (60f, 20f), (70f, 12f), (84f, 10f), (48f, 84f),
                         (60f, 90f), (72f, 80f), (30f, 92f), (18f, 104f), (40f, 108f), (66f, 106f), (80f, 76f) })
                Add(s, x, z, 2.2f, 2.2f, 1.6f, Shelf);

            s.Areas.Add(new AreaDef { Name = "Spawn Area", Bounds = s.SpawnArea });
            return s;
        }

        private static void Warehouse(StageLayout s)
        {
            // x 10..42, z 46..78, doors on the south and east; a zombie incubator inside.
            var warehouse = new BuildingDef
            {
                Name = "Warehouse", Bounds = new Rect(10f, 46f, 32f, 32f), Wall = 0.6f, WallColor = Concrete, Incubator = true,
                RoofColor = new Color(0.34f, 0.36f, 0.38f),
            };
            warehouse.Doors.Add(new Door(Side.South, 16f, 4f));
            warehouse.Doors.Add(new Door(Side.East, 16f, 4f));
            s.AddBuilding(warehouse);
            foreach (var z in new[] { 52f, 58f, 64f, 70f })
            {
                Add(s, 18f, z, 8f, 1.2f, 2f, Shelf);
                Add(s, 32f, z, 12f, 1.2f, 2f, Shelf);
            }
            var area = new AreaDef { Name = "Warehouse", Bounds = new Rect(10f, 46f, 32f, 32f), EntryGroup = 4 };
            area.Placed.Add((ZombieType.Basic, 10));
            s.Areas.Add(area);
        }

        private static void Town(StageLayout s)
        {
            // Houses you can walk into (owner, 2026-09-30); the store has a zombie incubator.
            House(s, "House 1", new Rect(50f, 34f, 8f, 8f), new Door(Side.South, 4f), new Door(Side.East, 4f));
            House(s, "House 2", new Rect(62f, 34f, 10f, 6f), new Door(Side.North, 5f));
            House(s, "House 3", new Rect(50f, 48f, 6f, 12f), new Door(Side.East, 6f));
            House(s, "Store", new Rect(66f, 46f, 12f, 10f), new Door(Side.West, 5f, 2.4f), new Door(Side.South, 6f, 2.4f)).Incubator = true;
            House(s, "House 4", new Rect(53f, 64f, 10f, 4f), new Door(Side.South, 5f));
            House(s, "House 5", new Rect(73f, 63f, 6f, 6f), new Door(Side.West, 3f));
            foreach (var (x, z, rot) in new[] { (61f, 46f, true), (64f, 58f, false), (48f, 64f, false), (80f, 42f, true), (68f, 44f, false) })
                Add(s, x, z, rot ? 4.2f : 2f, rot ? 2f : 4.2f, 1.4f, Car);
            var area = new AreaDef { Name = "Town", Bounds = new Rect(46f, 30f, 36f, 40f), EntryGroup = 5, Refills = true };
            area.Placed.Add((ZombieType.Basic, 14));
            area.Placed.Add((ZombieType.Fast, 2));
            s.Areas.Add(area);
            foreach (var (x, z) in new[] { (58f, 45f), (63f, 52f), (70f, 60f), (49f, 46f), (80f, 58f) }) s.Barrels.Add(new Vector3(x, 0f, z));
        }

        private static void Checkpoint(StageLayout s)
        {
            // Guard towers, sandbag lines and fences (D-204: a zombie-heavy combat zone).
            Add(s, 90f, 24f, 3f, 3f, 4.5f, Military);
            Add(s, 112f, 66f, 3f, 3f, 4.5f, Military);
            foreach (var z in new[] { 32f, 44f, 56f })
            {
                Add(s, 94f, z, 8f, 1f, 1f, Sandbag);
                Add(s, 108f, z + 4f, 8f, 1f, 1f, Sandbag);
            }
            Add(s, 101f, 50f, 0.3f, 10f, 2.5f, Military);
            Add(s, 101f, 30f, 0.3f, 8f, 2.5f, Military);
            House(s, "Barracks", new Rect(92f, 62f, 8f, 5f), Military, new Door(Side.South, 4f, 2f));
            var area = new AreaDef { Name = "Checkpoint", Bounds = new Rect(86f, 20f, 30f, 50f), EntryGroup = 8, CombatZone = true, Refills = true };
            area.Placed.Add((ZombieType.Basic, 18));
            area.Placed.Add((ZombieType.Fast, 4));
            area.Placed.Add((ZombieType.Tank, 1));
            s.Areas.Add(area);
            foreach (var (x, z) in new[] { (92f, 38f), (106f, 34f), (98f, 58f), (110f, 48f), (88f, 50f) }) s.Barrels.Add(new Vector3(x, 0f, z));
        }

        private static void BossYard(StageLayout s)
        {
            const float h = 3.5f, t = 0.8f;
            // Walls x 86..116, z 84..114; entrances on the south and west sides.
            Add(s, 91f, 84f, 10f, t, h, BossWall);
            Add(s, 110f, 84f, 12f, t, h, BossWall);
            Add(s, 101f, 114f, 30f, t, h, BossWall);
            Add(s, 86f, 90f, t, 12f, h, BossWall);
            Add(s, 86f, 108f, t, 12f, h, BossWall);
            Add(s, 116f, 99f, t, 30f, h, BossWall);
            Add(s, 96f, 106f, 3f, 3f, 1.8f, Military);
            Add(s, 110f, 94f, 3f, 3f, 1.8f, Military);
            s.Areas.Add(new AreaDef { Name = "Boss Area", Bounds = s.BossArea });
        }

        private static BuildingDef House(StageLayout s, string name, Rect bounds, params Door[] doors) => House(s, name, bounds, HouseWall, doors);

        private static BuildingDef House(StageLayout s, string name, Rect bounds, Color wall, params Door[] doors)
        {
            var house = new BuildingDef { Name = name, Bounds = bounds, WallColor = wall };
            house.Doors.AddRange(doors);
            return s.AddBuilding(house);
        }

        private static void Add(StageLayout s, float x, float z, float width, float depth, float height, Color color) =>
            s.Blocks.Add(new Block(x, z, width, depth, height, color));
    }
}
