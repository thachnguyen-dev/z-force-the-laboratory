using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Furniture and fittings for the enterable buildings (owner, 2026-10-04: each building type should be recognisable
    /// the moment you walk in; Claude makes the extra assets). Built in code in the game's chunky style, muted and worn
    /// like the owner's models (the same wear weights: grime, cracks, chips, rust, blood).
    /// Convention: a piece's back is at +z and its front faces -z, so the interior designer can stand it against a wall.
    /// Footprints are width (x) by depth (z) at rotation 0. All of these block movement only (bullets and sight pass),
    /// like the owner's shoot-through furniture.
    /// </summary>
    public static partial class ArtLibrary
    {
        /// <summary>Ids of the interior furniture (all shoot-through).</summary>
        public static readonly string[] Interiors =
        {
            "dining_table", "dining_chair", "coffee_table", "tv_stand", "kitchen_counter", "fridge", "wardrobe", "bed",
            "store_shelf", "cashier_counter", "drinks_fridge",
            "hospital_bed", "iv_stand", "medical_cabinet", "curtain_screen", "reception_desk", "waiting_bench",
            "tool_cabinet", "tool_shelf", "workbench", "tire_rack", "oil_drums",
            "pallet_rack", "metal_cage", "pallet_boxes", "crate", "crate_stack", "table",
            "bunk_bed", "footlocker", "weapon_rack", "ammo_crate", "radio_desk",
            "hazmat_rack", "fume_hood",
            "office_desk", "bookshelf", "filing_cabinet",
        };

        private static void RegisterInteriors()
        {
            // Household.
            Add("dining_table", "Dining table", Kind.Prop, new Vector2(1.6f, 0.9f), 0.78f, DiningTable);
            Add("dining_chair", "Dining chair", Kind.Prop, new Vector2(0.5f, 0.5f), 0.95f, DiningChair);
            Add("coffee_table", "Coffee table", Kind.Prop, new Vector2(1.1f, 0.6f), 0.45f, CoffeeTable);
            Add("tv_stand", "TV stand", Kind.Prop, new Vector2(1.6f, 0.5f), 1.2f, TvStand);
            Add("kitchen_counter", "Kitchen counter", Kind.Prop, new Vector2(2.2f, 0.65f), 0.95f, KitchenCounter);
            Add("fridge", "Fridge", Kind.Prop, new Vector2(0.8f, 0.7f), 1.85f, Fridge);
            Add("wardrobe", "Wardrobe", Kind.Prop, new Vector2(1.3f, 0.6f), 2f, Wardrobe);
            // Shop.
            Add("store_shelf", "Store shelf (double-sided)", Kind.Prop, new Vector2(2.4f, 0.9f), 1.6f, StoreShelf);
            Add("cashier_counter", "Cashier counter", Kind.Prop, new Vector2(2f, 0.8f), 1.05f, CashierCounter);
            Add("drinks_fridge", "Drinks fridge", Kind.Prop, new Vector2(1.8f, 0.75f), 2f, DrinksFridge);
            // Clinic.
            Add("hospital_bed", "Hospital bed", Kind.Prop, new Vector2(1f, 2.1f), 0.85f, HospitalBed);
            Add("iv_stand", "IV stand", Kind.Prop, new Vector2(0.45f, 0.45f), 1.9f, IvStand);
            Add("medical_cabinet", "Medical cabinet", Kind.Prop, new Vector2(1.3f, 0.5f), 1.9f, MedicalCabinet);
            Add("curtain_screen", "Curtain screen", Kind.Prop, new Vector2(1.8f, 0.3f), 1.8f, CurtainScreen);
            Add("reception_desk", "Reception desk", Kind.Prop, new Vector2(2.2f, 0.8f), 1.1f, ReceptionDesk);
            Add("waiting_bench", "Waiting chairs", Kind.Prop, new Vector2(1.9f, 0.6f), 0.9f, WaitingBench);
            // Garage.
            Add("tool_cabinet", "Tool chest", Kind.Prop, new Vector2(1.2f, 0.6f), 1.1f, ToolCabinet);
            Add("tool_shelf", "Tool wall", Kind.Prop, new Vector2(2f, 0.5f), 2f, ToolShelf);
            Add("workbench", "Workbench", Kind.Prop, new Vector2(2f, 0.8f), 1f, Workbench);
            Add("tire_rack", "Tyre rack", Kind.Prop, new Vector2(1.8f, 0.6f), 1.5f, TireRack);
            Add("oil_drums", "Oil drums", Kind.Prop, new Vector2(1.3f, 0.7f), 1f, OilDrums);
            // Warehouse.
            Add("pallet_rack", "Pallet rack", Kind.Prop, new Vector2(2.8f, 1.1f), 2.7f, PalletRack);
            Add("metal_cage", "Wire mesh cage", Kind.Prop, new Vector2(2.2f, 2f), 2.2f, MetalCage);
            Add("pallet_boxes", "Boxes on a pallet", Kind.Prop, new Vector2(1.2f, 1.1f), 1.2f, PalletBoxes);
            // Military.
            Add("bunk_bed", "Bunk bed", Kind.Prop, new Vector2(1f, 2.05f), 1.7f, BunkBed);
            Add("footlocker", "Footlocker", Kind.Prop, new Vector2(0.9f, 0.5f), 0.5f, Footlocker);
            Add("weapon_rack", "Weapon rack", Kind.Prop, new Vector2(1.7f, 0.45f), 1.7f, WeaponRack);
            Add("ammo_crate", "Ammo crates", Kind.Prop, new Vector2(1.1f, 0.7f), 0.8f, AmmoCrate);
            Add("radio_desk", "Radio desk", Kind.Prop, new Vector2(1.7f, 0.75f), 1.3f, RadioDesk);
            // Lab.
            Add("hazmat_rack", "Protective gear rack", Kind.Prop, new Vector2(1.7f, 0.55f), 1.9f, HazmatRack);
            Add("fume_hood", "Fume hood", Kind.Prop, new Vector2(1.7f, 0.85f), 2.2f, FumeHood);
            // Security.
            Add("office_desk", "Office desk with computer", Kind.Prop, new Vector2(1.6f, 0.8f), 1.2f, OfficeDesk);
            Add("bookshelf", "Bookshelf", Kind.Prop, new Vector2(1.6f, 0.45f), 2f, Bookshelf);
            Add("filing_cabinet", "Filing cabinet", Kind.Prop, new Vector2(0.55f, 0.65f), 1.35f, FilingCabinet);
            // Floor decoration.
            Add("rug", "Rug", Kind.Decoration, new Vector2(2.4f, 1.6f), 0f, Rug);
            Add("car_lift", "Car lift (floor)", Kind.Decoration, new Vector2(3f, 5f), 0f, CarLift);
            // Furniture lets bullets and sight through; solid crates don't.
            foreach (var id in Interiors)
                if (id != "crate" && id != "crate_stack" && _pieces.TryGetValue(id, out var p)) p.ShootThrough = true;
        }

        // ---------------------------------------------------------------- materials (sRGB) and wear presets

        private static readonly Vector4 WPaint = new Vector4(0.5f, 0.05f, 0.35f, 0.25f);
        private static readonly Vector4 WRust = new Vector4(0.65f, 0.1f, 0.3f, 0.85f);
        private static readonly Vector4 WWood = new Vector4(0.55f, 0.35f, 0.25f, 0f);
        private static readonly Vector4 WFabric = new Vector4(0.7f, 0f, 0f, 0f);
        private static readonly Vector4 WPlastic = new Vector4(0.45f, 0.1f, 0.15f, 0f);

        private static readonly Color FWood = new Color(0.5f, 0.37f, 0.26f);
        private static readonly Color FWoodDark = new Color(0.33f, 0.24f, 0.17f);
        private static readonly Color FSteel = new Color(0.55f, 0.58f, 0.6f);
        private static readonly Color FSteelDark = new Color(0.3f, 0.32f, 0.34f);
        private static readonly Color FWhite = new Color(0.84f, 0.85f, 0.84f);
        private static readonly Color FBlack = new Color(0.12f, 0.12f, 0.13f);
        private static readonly Color FOlive = new Color(0.33f, 0.37f, 0.24f);
        private static readonly Color FCardboard = new Color(0.62f, 0.48f, 0.32f);
        private static readonly Color FPallet = new Color(0.58f, 0.46f, 0.32f);
        private static readonly Color FScreen = new Color(0.25f, 0.45f, 0.55f, 0.6f);   // a dim glow

        private static void W(MeshKit k, Vector4 wear, float blood = 0f)
        {
            k.Wear = wear;
            k.Blood = blood;
        }

        /// <summary>Four legs under a top of <paramref name="size"/>.</summary>
        private static void Legs(MeshKit k, Vector3 size, float leg, Color c, float inset = 0.06f)
        {
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                k.Box(new Vector3(sx * (size.x * 0.5f - inset - leg * 0.5f), 0f, sz * (size.z * 0.5f - inset - leg * 0.5f)), new Vector3(leg, size.y, leg), c);
        }

        /// <summary>Small coloured goods in rows on a shelf board (shop goods, books, jars).</summary>
        private static void Goods(MeshKit k, System.Random r, float x0, float x1, float y, float z, float depth, float maxH, Color[] colours)
        {
            var x = x0;
            while (x < x1 - 0.08f)
            {
                var w = R(r, 0.08f, 0.22f);
                if (x + w > x1) break;
                var h = R(r, maxH * 0.5f, maxH);
                k.Box(new Vector3(x + w * 0.5f, y, z), new Vector3(w * 0.9f, h, depth), colours[r.Next(colours.Length)]);
                x += w + R(r, 0f, 0.05f);
            }
        }

        // ---------------------------------------------------------------- household

        private static void DiningTable(MeshKit k, int seed)
        {
            W(k, WWood);
            k.BevelBox(new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 0.08f, 0.9f), 0.03f, FWood);
            Legs(k, new Vector3(1.6f, 0.7f, 0.9f), 0.08f, FWoodDark);
        }

        private static void DiningChair(MeshKit k, int seed)
        {
            W(k, WWood);
            k.Box(new Vector3(0f, 0.42f, 0f), new Vector3(0.46f, 0.06f, 0.46f), FWood);
            Legs(k, new Vector3(0.46f, 0.42f, 0.46f), 0.05f, FWoodDark, 0.02f);
            k.Box(new Vector3(0f, 0.48f, 0.2f), new Vector3(0.46f, 0.47f, 0.06f), FWoodDark);
        }

        private static void CoffeeTable(MeshKit k, int seed)
        {
            W(k, WWood);
            k.BevelBox(new Vector3(0f, 0.36f, 0f), new Vector3(1.1f, 0.07f, 0.6f), 0.03f, FWoodDark);
            Legs(k, new Vector3(1.1f, 0.36f, 0.6f), 0.06f, FWoodDark);
            var r = Rng(seed);
            k.Box(new Vector3(R(r, -0.3f, 0.3f), 0.43f, R(r, -0.1f, 0.1f)), new Vector3(0.25f, 0.03f, 0.18f), new Color(0.75f, 0.73f, 0.65f)); // a magazine
        }

        private static void TvStand(MeshKit k, int seed)
        {
            W(k, WWood);
            k.BevelBox(new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.5f, 0.45f), 0.03f, FWoodDark);
            W(k, WPlastic);
            k.Box(new Vector3(0f, 0.5f, 0.08f), new Vector3(0.3f, 0.06f, 0.2f), FBlack);
            k.Box(new Vector3(0f, 0.56f, 0.1f), new Vector3(1.2f, 0.66f, 0.07f), FBlack);
            W(k, Vector4.zero);
            // A cracked, dead screen.
            k.Quad(new Vector3(-0.55f, 0.6f, 0.06f), new Vector3(-0.55f, 1.17f, 0.06f), new Vector3(0.55f, 1.17f, 0.06f), new Vector3(0.55f, 0.6f, 0.06f), new Color(0.13f, 0.15f, 0.18f));
        }

        private static void KitchenCounter(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.Box(new Vector3(0f, 0f, 0f), new Vector3(2.2f, 0.88f, 0.65f), new Color(0.72f, 0.7f, 0.64f));
            k.Box(new Vector3(0f, 0.88f, 0f), new Vector3(2.24f, 0.06f, 0.68f), new Color(0.45f, 0.44f, 0.42f));
            // A sink and a stove top.
            k.Box(new Vector3(-0.5f, 0.94f, 0f), new Vector3(0.55f, 0.01f, 0.4f), FSteel);
            foreach (var x in new[] { 0.45f, 0.75f })
            foreach (var z in new[] { -0.12f, 0.14f })
                k.Cylinder(new Vector3(x, 0.94f, z), 0.1f, 0.015f, 10, FBlack);
            // Cupboard doors.
            for (var i = 0; i < 4; i++)
                k.Quad(new Vector3(-1.05f + i * 0.55f, 0.1f, -0.326f), new Vector3(-1.05f + i * 0.55f, 0.78f, -0.326f), new Vector3(-0.55f + i * 0.55f, 0.78f, -0.326f), new Vector3(-0.55f + i * 0.55f, 0.1f, -0.326f), new Color(0.65f, 0.63f, 0.57f));
        }

        private static void Fridge(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.BevelBox(new Vector3(0f, 0f, 0f), new Vector3(0.8f, 1.85f, 0.7f), 0.06f, new Color(0.82f, 0.82f, 0.8f));
            k.Box(new Vector3(-0.32f, 0.9f, -0.37f), new Vector3(0.04f, 0.5f, 0.04f), FSteelDark);
            k.Quad(new Vector3(-0.4f, 1.2f, -0.352f), new Vector3(-0.4f, 1.22f, -0.352f), new Vector3(0.4f, 1.22f, -0.352f), new Vector3(0.4f, 1.2f, -0.352f), FSteelDark);
        }

        private static void Wardrobe(MeshKit k, int seed)
        {
            W(k, WWood);
            k.BevelBox(Vector3.zero, new Vector3(1.3f, 2f, 0.6f), 0.04f, FWood);
            k.Quad(new Vector3(-0.01f, 0.1f, -0.302f), new Vector3(-0.01f, 1.9f, -0.302f), new Vector3(0.01f, 1.9f, -0.302f), new Vector3(0.01f, 0.1f, -0.302f), FWoodDark);
        }

        private static void Rug(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var colours = new[] { new Color(0.5f, 0.22f, 0.2f), new Color(0.25f, 0.33f, 0.45f), new Color(0.45f, 0.42f, 0.3f) };
            var c = colours[r.Next(colours.Length)];
            W(k, WFabric, R(r, 0f, 1f) < 0.3f ? 0.7f : 0f);
            k.Quad(new Vector3(-1.2f, 0.008f, -0.8f), new Vector3(-1.2f, 0.008f, 0.8f), new Vector3(1.2f, 0.008f, 0.8f), new Vector3(1.2f, 0.008f, -0.8f), c);
            k.Quad(new Vector3(-0.95f, 0.01f, -0.55f), new Vector3(-0.95f, 0.01f, 0.55f), new Vector3(0.95f, 0.01f, 0.55f), new Vector3(0.95f, 0.01f, -0.55f), MeshKit.Shade(c, 1.25f));
            W(k, Vector4.zero);
        }

        // ---------------------------------------------------------------- shop

        private static readonly Color[] ShopGoods =
        {
            new Color(0.75f, 0.25f, 0.2f), new Color(0.25f, 0.45f, 0.7f), new Color(0.85f, 0.7f, 0.3f), new Color(0.35f, 0.6f, 0.35f),
            new Color(0.85f, 0.85f, 0.8f), new Color(0.6f, 0.35f, 0.55f),
        };

        private static void StoreShelf(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            var frame = new Color(0.78f, 0.79f, 0.78f);
            k.Box(new Vector3(0f, 0f, 0f), new Vector3(2.4f, 0.12f, 0.9f), FSteelDark);
            k.Box(new Vector3(0f, 0.12f, 0f), new Vector3(2.4f, 1.45f, 0.08f), frame);           // the spine
            foreach (var y in new[] { 0.12f, 0.55f, 1.0f })
            {
                k.Box(new Vector3(0f, y, 0f), new Vector3(2.4f, 0.04f, 0.88f), frame);
                W(k, WPlastic);
                Goods(k, r, -1.15f, 1.15f, y + 0.04f, -0.24f, 0.3f, 0.32f, ShopGoods);
                Goods(k, r, -1.15f, 1.15f, y + 0.04f, 0.24f, 0.3f, 0.32f, ShopGoods);
                W(k, WPaint);
            }
            k.Box(new Vector3(-1.18f, 0.12f, 0f), new Vector3(0.05f, 1.45f, 0.88f), frame);
            k.Box(new Vector3(1.18f, 0.12f, 0f), new Vector3(0.05f, 1.45f, 0.88f), frame);
            W(k, Vector4.zero);
        }

        private static void CashierCounter(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.BevelBox(Vector3.zero, new Vector3(2f, 0.95f, 0.8f), 0.04f, new Color(0.55f, 0.32f, 0.27f));
            k.Box(new Vector3(0f, 0.95f, 0f), new Vector3(2.04f, 0.05f, 0.84f), new Color(0.75f, 0.74f, 0.7f));
            W(k, WPlastic);
            // The till, its screen and a card reader.
            k.BevelBox(new Vector3(0.45f, 1f, 0.05f), new Vector3(0.45f, 0.14f, 0.4f), 0.03f, FBlack);
            k.Box(new Vector3(0.45f, 1.14f, 0.15f), new Vector3(0.36f, 0.26f, 0.04f), FBlack);
            k.Quad(new Vector3(0.3f, 1.17f, 0.128f), new Vector3(0.3f, 1.37f, 0.128f), new Vector3(0.6f, 1.37f, 0.128f), new Vector3(0.6f, 1.17f, 0.128f), FScreen);
            k.Box(new Vector3(-0.5f, 1f, -0.15f), new Vector3(0.14f, 0.05f, 0.2f), FSteelDark);
            W(k, Vector4.zero);
        }

        private static void DrinksFridge(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            k.BevelBox(Vector3.zero, new Vector3(1.8f, 2f, 0.75f), 0.05f, new Color(0.25f, 0.3f, 0.35f));
            W(k, Vector4.zero);
            // Lit glass doors with rows of bottles behind.
            k.Quad(new Vector3(-0.85f, 0.15f, -0.38f), new Vector3(-0.85f, 1.85f, -0.38f), new Vector3(0.85f, 1.85f, -0.38f), new Vector3(0.85f, 0.15f, -0.38f), new Color(0.55f, 0.72f, 0.78f, 0.75f));
            for (var y = 0.25f; y < 1.7f; y += 0.4f)
                Goods(k, r, -0.8f, 0.8f, y, -0.4f, 0.02f, 0.25f, new[] { new Color(0.7f, 0.2f, 0.2f), new Color(0.3f, 0.6f, 0.35f), new Color(0.9f, 0.75f, 0.3f), new Color(0.25f, 0.4f, 0.7f) });
            k.Quad(new Vector3(-0.01f, 0.15f, -0.395f), new Vector3(-0.01f, 1.85f, -0.395f), new Vector3(0.01f, 1.85f, -0.395f), new Vector3(0.01f, 0.15f, -0.395f), FSteelDark);
        }

        // ---------------------------------------------------------------- clinic

        private static void HospitalBed(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            // Wheeled frame, mattress, pillow at the back (+z), a raised head rail.
            Legs(k, new Vector3(0.95f, 0.45f, 2f), 0.05f, FSteel);
            foreach (var sx in new[] { -0.4f, 0.4f })
            foreach (var sz in new[] { -0.9f, 0.9f })
                k.Cylinder(new Vector3(sx, 0f, sz), 0.07f, 0.1f, 8, FBlack);
            k.Box(new Vector3(0f, 0.45f, 0f), new Vector3(0.95f, 0.08f, 2f), FSteel);
            W(k, WFabric, R(r, 0f, 1f) < 0.35f ? 0.9f : 0f);
            k.BevelBox(new Vector3(0f, 0.53f, 0f), new Vector3(0.88f, 0.16f, 1.95f), 0.05f, new Color(0.78f, 0.82f, 0.84f));
            k.BevelBox(new Vector3(0f, 0.69f, 0.7f), new Vector3(0.6f, 0.1f, 0.35f), 0.04f, FWhite);
            k.BevelBox(new Vector3(0f, 0.69f, -0.25f), new Vector3(0.86f, 0.06f, 1f), 0.03f, new Color(0.45f, 0.62f, 0.7f));   // blanket
            W(k, WPaint);
            k.Box(new Vector3(0f, 0.45f, 0.98f), new Vector3(0.95f, 0.5f, 0.05f), FSteel);
            W(k, Vector4.zero);
        }

        private static void IvStand(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.Cylinder(Vector3.zero, 0.2f, 0.05f, 10, FSteelDark);
            k.Cylinder(Vector3.zero, 0.025f, 1.85f, 6, FSteel);
            k.Box(new Vector3(0f, 1.8f, 0f), new Vector3(0.4f, 0.03f, 0.03f), FSteel);
            W(k, Vector4.zero);
            k.BevelBox(new Vector3(0.15f, 1.5f, 0f), new Vector3(0.14f, 0.22f, 0.06f), 0.02f, new Color(0.75f, 0.85f, 0.8f, 0.8f));
        }

        private static void MedicalCabinet(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            k.BevelBox(Vector3.zero, new Vector3(1.3f, 1.9f, 0.5f), 0.04f, FWhite);
            W(k, Vector4.zero);
            // Glass upper doors with boxes behind, a red cross on the lower doors.
            k.Quad(new Vector3(-0.6f, 1f, -0.26f), new Vector3(-0.6f, 1.8f, -0.26f), new Vector3(0.6f, 1.8f, -0.26f), new Vector3(0.6f, 1f, -0.26f), new Color(0.45f, 0.58f, 0.62f));
            for (var y = 1.05f; y < 1.7f; y += 0.36f) Goods(k, r, -0.55f, 0.55f, y, -0.27f, 0.02f, 0.22f, new[] { FWhite, new Color(0.35f, 0.55f, 0.7f), new Color(0.7f, 0.3f, 0.25f) });
            var red = new Color(0.66f, 0.13f, 0.12f);
            k.Quad(new Vector3(-0.06f, 0.35f, -0.26f), new Vector3(-0.06f, 0.75f, -0.26f), new Vector3(0.06f, 0.75f, -0.26f), new Vector3(0.06f, 0.35f, -0.26f), red);
            k.Quad(new Vector3(-0.2f, 0.49f, -0.262f), new Vector3(-0.2f, 0.61f, -0.262f), new Vector3(0.2f, 0.61f, -0.262f), new Vector3(0.2f, 0.49f, -0.262f), red);
        }

        private static void CurtainScreen(MeshKit k, int seed)
        {
            W(k, WPaint);
            foreach (var x in new[] { -0.88f, 0.88f }) k.Cylinder(new Vector3(x, 0f, 0f), 0.025f, 1.8f, 6, FSteel);
            k.Box(new Vector3(0f, 1.76f, 0f), new Vector3(1.8f, 0.04f, 0.04f), FSteel);
            W(k, WFabric, Rng(seed).NextDouble() < 0.3 ? 0.8f : 0f);
            // Pleated fabric hanging from the rail.
            for (var i = 0; i < 8; i++)
            {
                var x0 = -0.84f + i * 0.21f;
                var z = (i & 1) == 0 ? -0.05f : 0.05f;
                k.Box(new Vector3(x0 + 0.105f, 0.25f, z), new Vector3(0.22f, 1.48f, 0.03f), new Color(0.55f, 0.72f, 0.72f));
            }
            W(k, Vector4.zero);
        }

        private static void ReceptionDesk(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.BevelBox(Vector3.zero, new Vector3(2.2f, 1.05f, 0.8f), 0.04f, FWhite);
            k.Box(new Vector3(0f, 1.05f, -0.1f), new Vector3(2.24f, 0.05f, 0.6f), new Color(0.45f, 0.62f, 0.7f));
            var red = new Color(0.66f, 0.13f, 0.12f);
            k.Quad(new Vector3(-0.08f, 0.35f, -0.402f), new Vector3(-0.08f, 0.75f, -0.402f), new Vector3(0.08f, 0.75f, -0.402f), new Vector3(0.08f, 0.35f, -0.402f), red);
            k.Quad(new Vector3(-0.22f, 0.47f, -0.404f), new Vector3(-0.22f, 0.63f, -0.404f), new Vector3(0.22f, 0.63f, -0.404f), new Vector3(0.22f, 0.47f, -0.404f), red);
            W(k, WPlastic);
            k.Box(new Vector3(0.6f, 1.1f, 0.15f), new Vector3(0.5f, 0.32f, 0.05f), FBlack);
            W(k, Vector4.zero);
            k.Quad(new Vector3(0.38f, 1.14f, 0.122f), new Vector3(0.38f, 1.38f, 0.122f), new Vector3(0.82f, 1.38f, 0.122f), new Vector3(0.82f, 1.14f, 0.122f), FScreen);
        }

        private static void WaitingBench(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.Box(new Vector3(0f, 0f, 0f), new Vector3(1.9f, 0.04f, 0.5f), FSteelDark);
            foreach (var x in new[] { -0.85f, 0.85f }) k.Box(new Vector3(x, 0f, 0f), new Vector3(0.06f, 0.42f, 0.45f), FSteelDark);
            W(k, WPlastic);
            for (var i = 0; i < 3; i++)
            {
                var x = -0.62f + i * 0.62f;
                k.BevelBox(new Vector3(x, 0.42f, 0f), new Vector3(0.56f, 0.07f, 0.5f), 0.03f, new Color(0.3f, 0.45f, 0.58f));
                k.BevelBox(new Vector3(x, 0.49f, 0.21f), new Vector3(0.56f, 0.42f, 0.07f), 0.03f, new Color(0.3f, 0.45f, 0.58f));
            }
            W(k, Vector4.zero);
        }

        // ---------------------------------------------------------------- garage

        private static void ToolCabinet(MeshKit k, int seed)
        {
            W(k, WPaint);
            var red = new Color(0.62f, 0.16f, 0.14f);
            k.BevelBox(new Vector3(0f, 0.08f, 0f), new Vector3(1.2f, 0.95f, 0.6f), 0.04f, red);
            for (var i = 0; i < 5; i++)
            {
                var y = 0.2f + i * 0.17f;
                k.Quad(new Vector3(-0.55f, y, -0.302f), new Vector3(-0.55f, y + 0.13f, -0.302f), new Vector3(0.55f, y + 0.13f, -0.302f), new Vector3(0.55f, y, -0.302f), MeshKit.Shade(red, 1.1f));
                k.Quad(new Vector3(-0.25f, y + 0.09f, -0.305f), new Vector3(-0.25f, y + 0.11f, -0.305f), new Vector3(0.25f, y + 0.11f, -0.305f), new Vector3(0.25f, y + 0.09f, -0.305f), FSteel);
            }
            W(k, WRust);
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var z in new[] { -0.22f, 0.22f })
                k.Cylinder(new Vector3(x, 0f, z), 0.05f, 0.08f, 8, FBlack);
            W(k, Vector4.zero);
        }

        private static void ToolShelf(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WWood);
            // A pegboard with hanging tools above a low shelf of cans and boxes.
            k.Box(new Vector3(0f, 0.9f, 0.2f), new Vector3(2f, 1.1f, 0.05f), new Color(0.6f, 0.5f, 0.36f));
            W(k, WRust);
            k.Box(new Vector3(0f, 0f, 0f), new Vector3(2f, 0.04f, 0.5f), FSteelDark);
            k.Box(new Vector3(0f, 0.8f, 0f), new Vector3(2f, 0.04f, 0.5f), FSteelDark);
            foreach (var x in new[] { -0.97f, 0.97f }) k.Box(new Vector3(x, 0f, 0f), new Vector3(0.05f, 2f, 0.5f), FSteelDark);
            W(k, WPaint);
            for (var i = 0; i < 7; i++)
            {
                var x = -0.8f + i * 0.27f;
                var c = i % 3 == 0 ? new Color(0.62f, 0.16f, 0.14f) : FSteel;
                k.Box(new Vector3(x, R(r, 1.2f, 1.45f), 0.15f), new Vector3(0.05f, R(r, 0.25f, 0.45f), 0.04f), c);   // spanners, saws
            }
            Goods(k, r, -0.9f, 0.9f, 0.04f, 0f, 0.3f, 0.35f, new[] { new Color(0.62f, 0.16f, 0.14f), new Color(0.3f, 0.45f, 0.6f), FCardboard, new Color(0.85f, 0.7f, 0.25f) });
            Goods(k, r, -0.9f, 0.9f, 0.84f, 0f, 0.3f, 0.25f, new[] { FCardboard, FSteel, new Color(0.25f, 0.3f, 0.25f) });
            W(k, Vector4.zero);
        }

        private static void Workbench(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WWood);
            k.BevelBox(new Vector3(0f, 0.85f, 0f), new Vector3(2f, 0.1f, 0.8f), 0.03f, FWood);
            W(k, WRust);
            Legs(k, new Vector3(2f, 0.85f, 0.8f), 0.08f, FSteelDark);
            k.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.8f, 0.04f, 0.65f), FSteelDark);
            // A vice and some tools on top.
            k.Box(new Vector3(-0.75f, 0.95f, -0.25f), new Vector3(0.22f, 0.16f, 0.22f), new Color(0.25f, 0.35f, 0.5f));
            W(k, WPaint);
            for (var i = 0; i < 3; i++) k.Box(new Vector3(R(r, -0.3f, 0.7f), 0.95f, R(r, -0.2f, 0.2f)), new Vector3(0.3f, 0.03f, 0.06f), FSteel);
            W(k, Vector4.zero);
        }

        private static void TireRack(MeshKit k, int seed)
        {
            W(k, WRust);
            foreach (var x in new[] { -0.88f, 0.88f }) k.Box(new Vector3(x, 0f, 0f), new Vector3(0.05f, 1.5f, 0.55f), FSteelDark);
            foreach (var y in new[] { 0.3f, 0.95f }) k.Box(new Vector3(0f, y, 0f), new Vector3(1.8f, 0.04f, 0.55f), FSteelDark);
            W(k, new Vector4(0.35f, 0f, 0f, 0f));
            // Tyres standing on edge in two rows.
            foreach (var y in new[] { 0.34f, 0.99f })
                for (var i = 0; i < 5; i++)
                {
                    var x = -0.66f + i * 0.33f;
                    k.Box(new Vector3(x, y, 0f), new Vector3(0.24f, 0.5f, 0.5f), new Color(0.13f, 0.13f, 0.14f));
                }
            W(k, Vector4.zero);
        }

        private static void OilDrums(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var colours = new[] { new Color(0.2f, 0.32f, 0.5f), new Color(0.55f, 0.15f, 0.13f), new Color(0.25f, 0.27f, 0.28f) };
            W(k, WRust);
            foreach (var x in new[] { -0.33f, 0.33f })
            {
                var c = colours[r.Next(colours.Length)];
                k.Cylinder(new Vector3(x, 0f, 0f), 0.3f, 0.9f, 14, c, true, MeshKit.Shade(c, 0.85f));
                k.Cylinder(new Vector3(x, 0.3f, 0f), 0.31f, 0.04f, 14, MeshKit.Shade(c, 0.7f), false);
                k.Cylinder(new Vector3(x, 0.6f, 0f), 0.31f, 0.04f, 14, MeshKit.Shade(c, 0.7f), false);
            }
            W(k, Vector4.zero);
        }

        private static void CarLift(MeshKit k, int seed)
        {
            // Yellow lift arms and safety markings on the floor (looks only: a car stands over it).
            var yellow = new Color(0.82f, 0.68f, 0.2f);
            W(k, WPaint);
            foreach (var x in new[] { -1.2f, 1.2f })
                k.Quad(new Vector3(x - 0.2f, 0.01f, -2.2f), new Vector3(x - 0.2f, 0.01f, 2.2f), new Vector3(x + 0.2f, 0.01f, 2.2f), new Vector3(x + 0.2f, 0.01f, -2.2f), yellow);
            for (var i = 0; i < 6; i++)
            {
                var z = -2.4f + i * 0.96f;
                k.Quad(new Vector3(-1.5f, 0.012f, z), new Vector3(-1.5f, 0.012f, z + 0.25f), new Vector3(-1.2f, 0.012f, z + 0.5f), new Vector3(-1.2f, 0.012f, z + 0.25f), FBlack);
            }
            W(k, Vector4.zero);
        }

        // ---------------------------------------------------------------- warehouse

        private static void PalletRack(MeshKit k, int seed)
        {
            var r = Rng(seed);
            var orange = new Color(0.72f, 0.4f, 0.16f);
            var blue = new Color(0.2f, 0.32f, 0.5f);
            W(k, WRust);
            foreach (var x in new[] { -1.36f, 1.36f })
            foreach (var z in new[] { -0.5f, 0.5f })
                k.Box(new Vector3(x, 0f, z), new Vector3(0.08f, 2.7f, 0.08f), blue);
            foreach (var y in new[] { 0.15f, 1.35f, 2.55f })
            foreach (var z in new[] { -0.5f, 0.5f })
                k.Box(new Vector3(0f, y, z), new Vector3(2.8f, 0.1f, 0.07f), orange);
            W(k, WWood);
            foreach (var y in new[] { 0.25f, 1.45f })
                foreach (var x in new[] { -0.7f, 0.7f })
                {
                    if (r.NextDouble() < 0.2) continue;   // an empty bay
                    k.Box(new Vector3(x, y, 0f), new Vector3(1.2f, 0.12f, 1f), FPallet);
                    W(k, new Vector4(0.5f, 0.1f, 0f, 0f));
                    var h = R(r, 0.5f, 0.9f);
                    k.BevelBox(new Vector3(x, y + 0.12f, 0f), new Vector3(1.1f, h, 0.95f), 0.03f, MeshKit.Shade(FCardboard, R(r, 0.85f, 1.1f)));
                    W(k, WWood);
                }
            W(k, Vector4.zero);
        }

        private static void MetalCage(MeshKit k, int seed)
        {
            var r = Rng(seed);
            // A wire-mesh storage cage (tool crib): posts, mesh panels as thin bars, boxes inside.
            W(k, WRust);
            var steel = new Color(0.45f, 0.48f, 0.5f);
            foreach (var x in new[] { -1.08f, 1.08f })
            foreach (var z in new[] { -0.98f, 0.98f })
                k.Box(new Vector3(x, 0f, z), new Vector3(0.06f, 2.2f, 0.06f), steel);
            for (var i = 0; i <= 10; i++)
            {
                var t = -1.08f + i * 0.216f;
                k.Box(new Vector3(t, 0f, -0.98f), new Vector3(0.02f, 2.2f, 0.02f), steel);
                k.Box(new Vector3(t, 0f, 0.98f), new Vector3(0.02f, 2.2f, 0.02f), steel);
            }
            for (var i = 0; i <= 9; i++)
            {
                var t = -0.98f + i * 0.218f;
                k.Box(new Vector3(-1.08f, 0f, t), new Vector3(0.02f, 2.2f, 0.02f), steel);
                k.Box(new Vector3(1.08f, 0f, t), new Vector3(0.02f, 2.2f, 0.02f), steel);
            }
            foreach (var y in new[] { 0.05f, 1.1f, 2.15f })
            {
                k.Box(new Vector3(0f, y, -0.98f), new Vector3(2.16f, 0.04f, 0.03f), steel);
                k.Box(new Vector3(0f, y, 0.98f), new Vector3(2.16f, 0.04f, 0.03f), steel);
                k.Box(new Vector3(-1.08f, y, 0f), new Vector3(0.03f, 0.04f, 1.96f), steel);
                k.Box(new Vector3(1.08f, y, 0f), new Vector3(0.03f, 0.04f, 1.96f), steel);
            }
            W(k, new Vector4(0.5f, 0.1f, 0f, 0f));
            for (var i = 0; i < 4; i++)
                k.BevelBox(new Vector3(R(r, -0.6f, 0.6f), 0f, R(r, -0.5f, 0.5f)), new Vector3(R(r, 0.4f, 0.7f), R(r, 0.3f, 0.7f), R(r, 0.4f, 0.6f)), 0.03f, i % 2 == 0 ? FCardboard : FOlive);
            W(k, Vector4.zero);
        }

        private static void PalletBoxes(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WWood);
            k.Box(Vector3.zero, new Vector3(1.2f, 0.14f, 1.1f), FPallet);
            W(k, new Vector4(0.55f, 0.1f, 0f, 0f));
            foreach (var x in new[] { -0.3f, 0.3f })
            foreach (var z in new[] { -0.27f, 0.27f })
            {
                var h = R(r, 0.4f, 0.55f);
                k.BevelBox(new Vector3(x, 0.14f, z), new Vector3(0.56f, h, 0.52f), 0.03f, MeshKit.Shade(FCardboard, R(r, 0.85f, 1.1f)));
                if (r.NextDouble() < 0.5) k.BevelBox(new Vector3(x, 0.14f + h, z), new Vector3(0.5f, R(r, 0.3f, 0.45f), 0.48f), 0.03f, MeshKit.Shade(FCardboard, R(r, 0.85f, 1.1f)));
            }
            W(k, Vector4.zero);
        }

        // ---------------------------------------------------------------- military

        private static void BunkBed(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WRust);
            foreach (var x in new[] { -0.45f, 0.45f })
            foreach (var z in new[] { -0.98f, 0.98f })
                k.Box(new Vector3(x, 0f, z), new Vector3(0.06f, 1.7f, 0.06f), FOlive);
            foreach (var y in new[] { 0.35f, 1.25f })
            {
                k.Box(new Vector3(0f, y, 0f), new Vector3(0.95f, 0.06f, 2f), MeshKit.Shade(FOlive, 0.8f));
                W(k, WFabric, r.NextDouble() < 0.25 ? 0.8f : 0f);
                k.BevelBox(new Vector3(0f, y + 0.06f, 0f), new Vector3(0.86f, 0.12f, 1.92f), 0.04f, new Color(0.42f, 0.44f, 0.33f));   // olive blanket
                k.BevelBox(new Vector3(0f, y + 0.18f, 0.72f), new Vector3(0.55f, 0.08f, 0.32f), 0.03f, new Color(0.75f, 0.74f, 0.68f)); // pillow
                W(k, WRust);
            }
            k.Box(new Vector3(0.45f, 0.4f, -0.6f), new Vector3(0.04f, 0.9f, 0.04f), FOlive);   // ladder
            W(k, Vector4.zero);
        }

        private static void Footlocker(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.BevelBox(Vector3.zero, new Vector3(0.9f, 0.48f, 0.5f), 0.03f, FOlive);
            k.Box(new Vector3(0f, 0.2f, -0.255f), new Vector3(0.12f, 0.12f, 0.02f), FSteel);
            W(k, Vector4.zero);
        }

        private static void WeaponRack(MeshKit k, int seed)
        {
            W(k, WWood);
            k.Box(new Vector3(0f, 0f, 0.1f), new Vector3(1.7f, 0.1f, 0.35f), FWoodDark);
            k.Box(new Vector3(0f, 0.1f, 0.18f), new Vector3(1.7f, 1.6f, 0.08f), FWood);
            k.Box(new Vector3(0f, 1.2f, 0.05f), new Vector3(1.7f, 0.08f, 0.2f), FWoodDark);
            W(k, WRust);
            // Rifles standing in the rack.
            for (var i = 0; i < 5; i++)
            {
                var x = -0.64f + i * 0.32f;
                k.Box(new Vector3(x, 0.1f, 0.05f), new Vector3(0.08f, 1.35f, 0.06f), new Color(0.18f, 0.19f, 0.2f));
                k.Box(new Vector3(x, 0.1f, 0.03f), new Vector3(0.1f, 0.35f, 0.1f), new Color(0.3f, 0.26f, 0.2f));
            }
            W(k, Vector4.zero);
        }

        private static void AmmoCrate(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            k.BevelBox(new Vector3(-0.27f, 0f, 0f), new Vector3(0.52f, 0.35f, 0.65f), 0.03f, FOlive);
            k.BevelBox(new Vector3(0.27f, 0f, 0f), new Vector3(0.52f, 0.35f, 0.65f), 0.03f, MeshKit.Shade(FOlive, 0.9f));
            if (r.NextDouble() < 0.7) k.BevelBox(new Vector3(0f, 0.35f, 0f), new Vector3(0.52f, 0.35f, 0.65f), 0.03f, MeshKit.Shade(FOlive, 1.08f));
            // Yellow stencil bands.
            foreach (var x in new[] { -0.27f, 0.27f })
                k.Quad(new Vector3(x - 0.18f, 0.14f, -0.327f), new Vector3(x - 0.18f, 0.2f, -0.327f), new Vector3(x + 0.18f, 0.2f, -0.327f), new Vector3(x + 0.18f, 0.14f, -0.327f), new Color(0.75f, 0.65f, 0.25f));
            W(k, Vector4.zero);
        }

        private static void RadioDesk(MeshKit k, int seed)
        {
            W(k, WPaint);
            k.Box(new Vector3(0f, 0.72f, 0f), new Vector3(1.7f, 0.06f, 0.75f), MeshKit.Shade(FOlive, 0.9f));
            Legs(k, new Vector3(1.7f, 0.72f, 0.75f), 0.06f, FSteelDark);
            // Field radio sets with dials and a map board.
            k.BevelBox(new Vector3(-0.35f, 0.78f, 0.12f), new Vector3(0.7f, 0.4f, 0.4f), 0.03f, new Color(0.25f, 0.28f, 0.22f));
            k.BevelBox(new Vector3(0.4f, 0.78f, 0.15f), new Vector3(0.5f, 0.28f, 0.35f), 0.03f, new Color(0.25f, 0.28f, 0.22f));
            W(k, Vector4.zero);
            foreach (var x in new[] { -0.55f, -0.35f, -0.15f })
                k.Cylinder(new Vector3(x, 0.98f, -0.08f), 0.04f, 0.02f, 8, new Color(0.3f, 0.75f, 0.4f, 0.6f));
            k.Box(new Vector3(0.4f, 1.06f, 0.15f), new Vector3(0.03f, 0.35f, 0.03f), FBlack);   // antenna
        }

        // ---------------------------------------------------------------- lab

        private static void HazmatRack(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            k.Box(new Vector3(0f, 0f, 0.1f), new Vector3(1.7f, 0.08f, 0.4f), FSteelDark);
            foreach (var x in new[] { -0.82f, 0.82f }) k.Box(new Vector3(x, 0f, 0.1f), new Vector3(0.05f, 1.9f, 0.05f), FSteel);
            k.Box(new Vector3(0f, 1.82f, 0.1f), new Vector3(1.7f, 0.04f, 0.04f), FSteel);
            // Yellow protective suits on hangers, gas masks on a shelf, boots below.
            for (var i = 0; i < 3; i++)
            {
                var x = -0.52f + i * 0.52f;
                W(k, WFabric, r.NextDouble() < 0.2 ? 0.8f : 0f);
                k.BevelBox(new Vector3(x, 0.45f, 0.08f), new Vector3(0.42f, 1.25f, 0.2f), 0.06f, new Color(0.82f, 0.68f, 0.18f));
                k.BevelBox(new Vector3(x, 1.5f, 0.08f), new Vector3(0.3f, 0.28f, 0.22f), 0.08f, new Color(0.82f, 0.68f, 0.18f));   // hood
                W(k, WPlastic);
                k.Cylinder(new Vector3(x, 1.52f, -0.04f), 0.07f, 0.08f, 8, FBlack);   // visor/filter
                k.Box(new Vector3(x, 0.08f, -0.05f), new Vector3(0.3f, 0.18f, 0.25f), FBlack);   // boots
            }
            W(k, Vector4.zero);
        }

        private static void FumeHood(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WPaint);
            k.Box(Vector3.zero, new Vector3(1.7f, 0.9f, 0.85f), new Color(0.6f, 0.64f, 0.66f));
            k.Box(new Vector3(0f, 0.9f, 0.05f), new Vector3(1.7f, 1.3f, 0.75f), FWhite);
            W(k, Vector4.zero);
            // The sash window, lit inside, with flasks behind it.
            k.Quad(new Vector3(-0.75f, 1f, -0.33f), new Vector3(-0.75f, 1.75f, -0.33f), new Vector3(0.75f, 1.75f, -0.33f), new Vector3(0.75f, 1f, -0.33f), new Color(0.6f, 0.78f, 0.75f, 0.8f));
            Goods(k, r, -0.65f, 0.65f, 0.95f, -0.35f, 0.02f, 0.3f, new[] { new Color(0.35f, 0.75f, 0.42f, 0.6f), new Color(0.35f, 0.6f, 0.78f, 0.6f), FWhite });
            k.Quad(new Vector3(-0.85f, 0.75f, -0.43f), new Vector3(-0.85f, 0.85f, -0.43f), new Vector3(0.85f, 0.85f, -0.43f), new Vector3(0.85f, 0.75f, -0.43f), new Color(0.8f, 0.65f, 0.15f));   // hazard band
        }

        // ---------------------------------------------------------------- security office

        private static void OfficeDesk(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WWood);
            k.BevelBox(new Vector3(0f, 0.72f, 0f), new Vector3(1.6f, 0.06f, 0.8f), 0.02f, new Color(0.55f, 0.5f, 0.43f));
            W(k, WPaint);
            k.Box(new Vector3(-0.55f, 0f, 0f), new Vector3(0.45f, 0.72f, 0.75f), FSteelDark);   // drawer pedestal
            k.Box(new Vector3(0.75f, 0f, 0f), new Vector3(0.05f, 0.72f, 0.75f), FSteelDark);
            W(k, WPlastic);
            // Monitor, keyboard and a stack of papers.
            k.Box(new Vector3(0.1f, 0.78f, 0.2f), new Vector3(0.2f, 0.04f, 0.16f), FBlack);
            k.BevelBox(new Vector3(0.1f, 0.82f, 0.22f), new Vector3(0.62f, 0.4f, 0.06f), 0.02f, FBlack);
            k.Box(new Vector3(0.1f, 0.78f, -0.12f), new Vector3(0.5f, 0.02f, 0.16f), new Color(0.2f, 0.21f, 0.23f));
            W(k, Vector4.zero);
            k.Quad(new Vector3(-0.17f, 0.86f, 0.188f), new Vector3(-0.17f, 1.18f, 0.188f), new Vector3(0.37f, 1.18f, 0.188f), new Vector3(0.37f, 0.86f, 0.188f), FScreen);
            for (var i = 0; i < r.Next(1, 4); i++)
                k.Box(new Vector3(-0.5f + R(r, -0.05f, 0.05f), 0.78f + i * 0.02f, R(r, -0.2f, 0.1f)), new Vector3(0.3f, 0.02f, 0.22f), new Color(0.85f, 0.84f, 0.78f));
        }

        private static void Bookshelf(MeshKit k, int seed)
        {
            var r = Rng(seed);
            W(k, WWood);
            k.Box(new Vector3(0f, 0f, 0.2f), new Vector3(1.6f, 2f, 0.05f), FWoodDark);
            foreach (var x in new[] { -0.78f, 0.78f }) k.Box(new Vector3(x, 0f, 0f), new Vector3(0.05f, 2f, 0.45f), FWood);
            foreach (var y in new[] { 0f, 0.5f, 1f, 1.5f, 1.96f }) k.Box(new Vector3(0f, y, 0f), new Vector3(1.6f, 0.04f, 0.45f), FWood);
            W(k, new Vector4(0.5f, 0f, 0.1f, 0f));
            var books = new[] { new Color(0.55f, 0.2f, 0.18f), new Color(0.2f, 0.3f, 0.45f), new Color(0.3f, 0.4f, 0.28f), new Color(0.7f, 0.62f, 0.45f), new Color(0.25f, 0.24f, 0.22f) };
            foreach (var y in new[] { 0.04f, 0.54f, 1.04f, 1.54f })
                Goods(k, r, -0.72f, 0.72f, y, 0f, 0.3f, 0.42f, books);
            W(k, Vector4.zero);
        }

        private static void FilingCabinet(MeshKit k, int seed)
        {
            W(k, WPaint);
            var grey = new Color(0.45f, 0.48f, 0.5f);
            k.BevelBox(Vector3.zero, new Vector3(0.55f, 1.35f, 0.65f), 0.03f, grey);
            for (var i = 0; i < 4; i++)
            {
                var y = 0.1f + i * 0.31f;
                k.Quad(new Vector3(-0.24f, y, -0.327f), new Vector3(-0.24f, y + 0.27f, -0.327f), new Vector3(0.24f, y + 0.27f, -0.327f), new Vector3(0.24f, y, -0.327f), MeshKit.Shade(grey, 1.1f));
                k.Quad(new Vector3(-0.08f, y + 0.18f, -0.33f), new Vector3(-0.08f, y + 0.21f, -0.33f), new Vector3(0.08f, y + 0.21f, -0.33f), new Vector3(0.08f, y + 0.18f, -0.33f), FSteelDark);
            }
            W(k, Vector4.zero);
        }
    }
}
