using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// A running stage: builds the greybox terrain and NavMesh from a <see cref="StageLayout"/>, places loot
    /// randomly (D-205), places and spawns zombies (D-062, D-080) and the Main Boss (D-196). Host-side.
    /// </summary>
    public class StageRuntime : MonoBehaviour
    {
        public StageLayout Layout { get; private set; }
        public Zombie MainBoss { get; private set; }
        public readonly List<Vector3> OpenPoints = new List<Vector3>();

        private GameTuning _tuning;
        private System.Random _rng;
        private readonly HashSet<AreaDef> _entered = new HashSet<AreaDef>();
        private float _areaCheckLeft;
        private float _trickleLeft;
        private int _noiseEvents;
        private int _spawnSeed = 1;
        private readonly List<float> _respawnTimers = new List<float>();

        /// <summary>Co-op (owner, 2026-10-03): more zombies with stronger stats. Set before Build.</summary>
        public bool Coop { get; set; }
        private readonly Dictionary<ZombieType, ZombieDef> _defs = new Dictionary<ZombieType, ZombieDef>();

        /// <summary>A zombie type's stats for this stage: the tuning's, made stronger in co-op.</summary>
        /// <summary>The population budget: co-op allows more of both (the phone holds 55 real zombies at 60 fps).</summary>
        /// <summary>How much bigger a map is than the 112 m standard (2026-10-04: maps grew), for population and loot.</summary>
        public static float AreaScale(float width, float height) => Mathf.Clamp(width * height / (112f * 112f), 1f, 4.5f);
        private float Area => AreaScale(Layout.Size.x, Layout.Size.y);
        public int MaxPopulation => Mathf.RoundToInt(_tuning.spawn.maxPopulation * Area * (Coop ? _tuning.spawn.coopCount : _tuning.spawn.soloCount));
        public int MaxActive => Coop ? _tuning.spawn.maxActive + 10 : _tuning.spawn.maxActive;

        public ZombieDef Def(ZombieType type)
        {
            if (_defs.TryGetValue(type, out var def)) return def;
            def = _tuning.Zombie(type).Copy();
            if (Coop)
            {
                var s = _tuning.spawn;
                def.hp *= s.coopHp;
                def.damage *= s.coopDamage;
                def.slamDamage *= s.coopDamage;
                def.wanderSpeed *= s.coopSpeed;
                def.chaseSpeed *= s.coopSpeed;
            }
            return _defs[type] = def;
        }

        public void Build(StageLayout layout, GameTuning tuning, int seed)
        {
            Layout = layout;
            _tuning = tuning;
            _rng = new System.Random(seed);
            _startedAt = Time.time;
            _trickleLeft = tuning.spawn.trickleInterval;

            BuildTerrain();
            BakeNavMesh();
            if (layout.Endless) FenceLine();
            FindFreePoints();

            SpawnBarrels();
            Population = gameObject.AddComponent<ZombiePopulation>();
            Population.Setup(this, tuning);
            // The tutorial brings its own zombies, step by step.
            if (!layout.Tutorial && !layout.Endless) PlaceZombies();
            // Owner, 2026-10-03: only boss missions have the Main Boss; the others have secondary bosses on the route.
            if (layout.Mission == "boss" && layout.HasBoss) MainBoss = SpawnZombie(layout.BossType, layout.BossPosition);
            foreach (var p in layout.SubBossPositions)
            {
                SpawnZombie(layout.SubBossType, p);
                // Co-op is Hard (owner, 2026-10-07): more secondary bosses.
                if (Coop)
                    for (var i = 0; i < tuning.spawn.coopExtraSubBosses; i++)
                        SpawnZombie(layout.SubBossType, p + Quaternion.Euler(0f, i * 137f, 0f) * new Vector3(3.5f, 0f, 0f));
            }
            if (MainBoss != null) MainBoss.IsMainBoss = true;
            PlaceLoot();
            PlaceBuildingContents();
            if (layout.Tutorial) gameObject.AddComponent<Tutorial>().Setup(this);
            if (layout.Endless) gameObject.AddComponent<Endless>().Setup(this, tuning, seed, false);

            Noise.Emitted += OnNoise;
            LootItem.Emptied += OnLootEmptied;
        }

        private void OnDestroy()
        {
            Noise.Emitted -= OnNoise;
            LootItem.Emptied -= OnLootEmptied;
        }


        /// <summary>
        /// Online client: the static stage only (terrain, buildings, barrels, NavMesh). Zombies, loot, chests and
        /// incubators arrive from the Host. The NavMesh and free points are still built, for host migration.
        /// </summary>
        public void BuildReplica(StageLayout layout, GameTuning tuning, int seed)
        {
            Layout = layout;
            _tuning = tuning;
            _rng = new System.Random(seed);
            _trickleLeft = tuning.spawn.trickleInterval;
            BuildTerrain();
            BakeNavMesh();
            if (layout.Endless) FenceLine();
            FindFreePoints();
            SpawnBarrels();
            if (layout.Endless) gameObject.AddComponent<Endless>().Setup(this, tuning, seed, true);
            Noise.Emitted += OnNoise;
            LootItem.Emptied += OnLootEmptied;
        }

        /// <summary>Barrels are numbered by their order in the layout, so every device agrees on the ids.</summary>
        private void SpawnBarrels()
        {
            for (var i = 0; i < Layout.Barrels.Count; i++) Barrel.Spawn(transform, Layout.Barrels[i], _tuning.barrel).NetId = i;
        }

        /// <summary>Online client: a zombie the Host has.</summary>
        public Zombie SpawnZombieReplica(ZombieType type, int id, Vector3 position, float yaw)
        {
            var mainBoss = Layout.Mission == "boss" && type == Layout.BossType && MainBoss == null;
            var go = new GameObject(mainBoss ? "Main Boss" : $"Zombie {type}") { layer = Layers.Zombie };
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.AddComponent<NavMeshAgent>();
            var zombie = go.AddComponent<Zombie>();
            zombie.InitReplica(Def(type), _tuning.perception, id, position, yaw);
            if (mainBoss)
            {
                MainBoss = zombie;
                zombie.IsMainBoss = true;
            }
            return zombie;
        }

        /// <summary>
        /// Host migration: this device now runs the spawners. Which areas were already entered isn't replicated, so
        /// every area counts as entered (no more first-entry groups this stage).
        /// </summary>
        public void BecomeHost()
        {
            foreach (var area in Layout.Areas) _entered.Add(area);
            _trickleLeft = _tuning.spawn.trickleInterval;
        }

        // ---- Terrain ----

        /// <summary>
        /// Visuals come from the map's tiles (one prefab per cell, merged into chunks). Collision is a handful of invisible
        /// boxes merged from solid cells, plus water boxes that stop walking but not bullets.
        /// </summary>
        private void BuildTerrain()
        {
            StageGrid.Map = Layout.Map;
            var size = Layout.Size;
            var ground = Primitives.Shape(PrimitiveType.Cube, transform, new Vector3(size.x * 0.5f, -0.5f, size.y * 0.5f),
                new Vector3(size.x + 6f, 1f, size.y + 6f), new Color(0.3f, 0.33f, 0.28f), keepCollider: true, name: "Ground");
            ground.layer = Layers.Ground;
            // Props first (their colliders go into the NavMesh; their footprints into the ground's contact shadows).
            var footprints = Layout.Map != null ? Art.PropLayer.BuildProps(Layout.Map, transform) : null;
            // Layers 1 and 2 (owner, 2026-10-01): the base ground and the surfaces, one material on the ground plane.
            var groundRenderer = ground.GetComponent<MeshRenderer>();
            if (Layout.Map == null || Art.GroundLayer.Apply(groundRenderer, Layout.Map, Layout.BaseGroundId, footprints) == null)
                groundRenderer.sharedMaterial = BaseGround.MaterialFor(Layout.BaseGroundId, new Vector2(size.x + 6f, size.y + 6f));
            else
            {
                // The grass carries on well past the map (owner, 2026-10-04: green grass round the outside), so the
                // camera never shows the void beyond the fence: a wide plane just under the ground, looks only.
                var apron = GameObject.CreatePrimitive(PrimitiveType.Quad);
                apron.name = "Ground apron";
                Object.Destroy(apron.GetComponent<Collider>());
                apron.transform.SetParent(transform, false);
                apron.transform.SetPositionAndRotation(new Vector3(size.x * 0.5f, -0.01f, size.y * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                apron.transform.localScale = new Vector3(size.x + 120f, size.y + 120f, 1f);
                var apronRenderer = apron.GetComponent<MeshRenderer>();
                apronRenderer.sharedMaterial = groundRenderer.sharedMaterial;
                apronRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Owner, 2026-10-04: no coloured outlines; the start is a helicopter landing pad.
            if (Layout.SpawnArea.width > 0f)
            {
                var pad = new GameObject("Helipad");
                pad.transform.SetParent(transform, false);
                pad.AddComponent<MeshFilter>().sharedMesh = Art.HelipadArt.Build(Layout.SpawnArea);
                var padRenderer = pad.AddComponent<MeshRenderer>();
                padRenderer.sharedMaterial = Art.ArtMaterials.Stylized;
                padRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            foreach (var building in Layout.Buildings) Building.Create(transform, building);
            // Layer 3, the gameplay blocks; then the decorative details.
            if (Layout.Map != null) TileAssembler.Build(Layout.Map, TileSetFor(Layout), transform);
            if (Layout.Map != null) Art.GrassLayer.BuildCells(Layout.Map, transform);
            if (Layout.Map != null) Art.PropLayer.BuildDecorations(Layout.Map, transform);

            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(transform, false);
            foreach (var block in Layout.Blocks)
                AddBox(colliders, new Vector3(block.Center.x, block.Height * 0.5f, block.Center.y), new Vector3(block.Size.x, block.Height, block.Size.y),
                    Layers.Obstacle, Layout.Map == null ? block.Color : (Color?)null);
        }

        /// <summary>The stage's own tile set (Resources/TileSets/&lt;name&gt;) if it names one, else the default.</summary>
        private static TileSet TileSetFor(StageLayout layout)
        {
            if (!string.IsNullOrEmpty(layout.TileSetName))
            {
                var own = Resources.Load<TileSet>($"TileSets/{layout.TileSetName}");
                if (own != null) return own;
                Debug.LogWarning($"[ZOMBIE] Stage {layout.Id}: tile set {layout.TileSetName} not found; using the default.");
            }
            return Resources.Load<TileSet>(TileSet.ResourceName);
        }

        private static void AddBox(Transform parent, Vector3 center, Vector3 size, int layer, Color? visible)
        {
            GameObject go;
            if (visible.HasValue)
                go = Primitives.Shape(PrimitiveType.Cube, parent, center, size, visible.Value, keepCollider: true, name: "Obstacle");
            else
            {
                go = new GameObject("Collider");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = center;
                go.AddComponent<BoxCollider>().size = size;
            }
            go.layer = layer;
            go.isStatic = true;
        }

        private void MarkFloor(Rect rect, Color color)
        {
            // An outline only, so the base ground and the blocks inside stay visible.
            const float t = 0.15f;
            void Strip(float x, float z, float w, float d) =>
                Primitives.Shape(PrimitiveType.Cube, transform, new Vector3(x, 0.03f, z), new Vector3(w, 0.02f, d), color, name: "Area outline");
            Strip(rect.center.x, rect.yMin + t * 0.5f, rect.width, t);
            Strip(rect.center.x, rect.yMax - t * 0.5f, rect.width, t);
            Strip(rect.xMin + t * 0.5f, rect.center.y, t, rect.height);
            Strip(rect.xMax - t * 0.5f, rect.center.y, t, rect.height);
        }

        /// <summary>The NavMesh from the ground and the solid blocks.</summary>
        /// <summary>
        /// Endless mode (owner, 2026-10-07): the players can't leave through the fence's gaps. An invisible wall along the
        /// fence line (the inner side of the two-cell edge) stops survivors; it is added after the NavMesh, so the zombies'
        /// paths still come in through the gaps, and it is on the shoot-through layer, so shots and sight pass.
        /// </summary>
        private void FenceLine()
        {
            const float edge = 2f, thick = 0.5f, high = 4f;
            var w = Layout.Size.x;
            var d = Layout.Size.y;
            void Wall(Vector3 centre, Vector3 size)
            {
                var go = new GameObject("Endless fence line") { layer = Layers.ShootThrough };
                go.transform.SetParent(transform, false);
                go.transform.position = centre;
                go.AddComponent<BoxCollider>().size = size;
                go.isStatic = true;
            }
            Wall(new Vector3(edge - thick * 0.5f, high * 0.5f, d * 0.5f), new Vector3(thick, high, d));
            Wall(new Vector3(w - edge + thick * 0.5f, high * 0.5f, d * 0.5f), new Vector3(thick, high, d));
            Wall(new Vector3(w * 0.5f, high * 0.5f, edge - thick * 0.5f), new Vector3(w, high, thick));
            Wall(new Vector3(w * 0.5f, high * 0.5f, d - edge + thick * 0.5f), new Vector3(w, high, thick));
        }

        private void BakeNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = (1 << Layers.Ground) | (1 << Layers.Obstacle) | (1 << Layers.ShootThrough);
            // The agent radius is 0.3 (project settings, set by ZombieSetup) so 1-cell gaps stay passable for zombies, the
            // AI and the hostage, like for players (2026-10-04); the voxel size is kept near the old default for bake time.
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.15f;
            surface.BuildNavMesh();
        }

        /// <summary>Walkable points every few cells, per area (pre-placed zombies, fallback spawn points).</summary>
        private void FindFreePoints()
        {
            const int step = 3;
            var map = Layout.Map;
            for (var x = 2; x < (int)Layout.Size.x - 1; x += step)
            for (var z = 2; z < (int)Layout.Size.y - 1; z += step)
            {
                var p = new Vector3(x, 0f, z);
                if (map != null)
                {
                    // All four cells around the point must be walkable.
                    if (Tiles.BlocksMovement(map[x, z]) || Tiles.BlocksMovement(map[x - 1, z]) || Tiles.BlocksMovement(map[x, z - 1]) ||
                        Tiles.BlocksMovement(map[x - 1, z - 1])) continue;
                }
                else if (Physics.CheckSphere(p + Vector3.up * 0.8f, 0.9f, Layers.ObstacleMask)) continue;
                if (!NavMesh.SamplePosition(p, out var hit, 0.6f, NavAreas.WalkMask)) continue;
                var point = hit.position;
                point.y = 0f;
                var area = Layout.AreaAt(point);
                if (area != null) area.FreePoints.Add(point);
                if (!Layout.BossArea.Contains(new Vector2(point.x, point.z))) OpenPoints.Add(point);
            }
        }

        /// <summary>Where dynamic spawns may appear inside <paramref name="area"/>: its spawn cells, else any free point.</summary>
        private List<Vector3> SpawnPointsIn(AreaDef area)
        {
            var list = new List<Vector3>();
            foreach (var p in Layout.ZombieSpawnPoints)
                if (area.Bounds.Contains(new Vector2(p.x, p.z))) list.Add(p);
            return list.Count > 0 ? list : area.FreePoints;
        }

        private List<Vector3> AllSpawnPoints => Layout.ZombieSpawnPoints.Count > 0 ? Layout.ZombieSpawnPoints : OpenPoints;

        private List<Vector3> LootPoints => Layout.LootPoints.Count > 0 ? Layout.LootPoints : OpenPoints;

        private void OnDisable()
        {
            if (StageGrid.Map == Layout?.Map) StageGrid.Map = null;
        }

        // ---- Zombies ----

        private void PlaceZombies()
        {
            // Zombies the map places on exact cells.
            foreach (var (type, position) in Layout.PlacedZombies) AddZombie(type, position);
            // Area groups (owner, 2026-10-03: replays may use different zombie configurations): every run rolls new spots,
            // counts within ±25 % and a little of the Basic share turned Fast.
            var fixedRng = _rng;
            // Each area's count first (rolled within ±25 %), so that if together they ask for more than the stage holds,
            // every area keeps its share (owner, 2026-10-11: they were placed in the map's order until the population was
            // full, so the areas listed last stayed empty: on the Wildlife Reserve every zombie was on the boss's side).
            var counts = new System.Collections.Generic.List<(AreaDef area, ZombieType type, int count)>();
            var wanted = 0;
            foreach (var area in Layout.Areas)
            {
                if (area.FreePoints.Count == 0) continue;
                foreach (var (type, baseCount) in area.Placed)
                {
                    var n = Mathf.Max(baseCount > 0 ? 1 : 0, Mathf.RoundToInt(baseCount * (Coop ? _tuning.spawn.coopCount : _tuning.spawn.soloCount) * (0.75f + (float)fixedRng.NextDouble() * 0.5f)));
                    counts.Add((area, type, n));
                    wanted += n;
                }
            }
            // Owner, 2026-10-11: fewer near the start, more towards the objective; the total stays what the map asks
            // (within the limit).
            var weighted = new float[counts.Count];
            var weightedSum = 0f;
            for (var k = 0; k < counts.Count; k++)
            {
                weighted[k] = counts[k].count * AreaWeight(counts[k].area);
                weightedSum += weighted[k];
            }
            var room = MaxPopulation - (Population != null ? Population.Total : Zombie.Alive.Count);
            var target = Mathf.Min(wanted, Mathf.Max(0, room));
            var share = weightedSum > 0f ? target / weightedSum : 0f;
            var place = new int[counts.Count];
            for (var k = 0; k < counts.Count; k++)
                place[k] = counts[k].count > 0 ? Mathf.Max(1, Mathf.RoundToInt(weighted[k] * share)) : 0;
            // Round by round (each area's first zombie, then every second one, ...), so if the limit is reached the last
            // few come off the biggest groups, never leaving an area empty.
            for (var round = 0; ; round++)
            {
                var any = false;
                for (var k = 0; k < counts.Count; k++)
                {
                    if (round >= place[k]) continue;
                    any = true;
                    var (area, type, _) = counts[k];
                    var at = Jitter(area.FreePoints[fixedRng.Next(area.FreePoints.Count)], fixedRng);
                    // Owner, 2026-10-07: a share of the Basic zombies are animals, small by day, most at night.
                    var placedType = type == ZombieType.Basic ? AnimalOr(type, fixedRng) : type;
                    if (placedType == ZombieType.Basic && fixedRng.NextDouble() < 0.1) placedType = ZombieType.Fast;
                    AddZombie(placedType, at);
                }
                if (!any) break;
            }
        }

        /// <summary>Where the stage's objective is: the Boss Area, the scientist, the bomb target, the Vaccine (the map's centre).</summary>
        public Vector3? ObjectivePoint
        {
            get
            {
                if (Layout.VaccineMission) return new Vector3(Layout.Size.x * 0.5f, 0f, Layout.Size.y * 0.5f);
                if (Layout.Bomb) return Layout.BombTargetPosition;
                if (Layout.Rescue) return Layout.HostagePosition;
                if (Layout.Mission == "boss" && Layout.HasBoss) return Layout.BossPosition;
                return null;
            }
        }

        /// <summary>
        /// An area's share of the zombies (owner, 2026-10-11): from startWeight near the players' start to
        /// objectiveWeight at the objective, by how far along the way from one to the other the area lies.
        /// </summary>
        public float AreaWeight(AreaDef area)
        {
            var objective = ObjectivePoint;
            if (!objective.HasValue) return 1f;
            var c = area.Bounds.center;
            var start = Layout.SpawnArea.center;
            var dStart = Vector2.Distance(c, start);
            var dGoal = Vector2.Distance(c, new Vector2(objective.Value.x, objective.Value.z));
            var along = dStart + dGoal > 0.01f ? dStart / (dStart + dGoal) : 0.5f;
            return Mathf.Lerp(_tuning.spawn.startWeight, _tuning.spawn.objectiveWeight, along);
        }

        /// <summary>
        /// Owner, 2026-10-07: by day a small share, at night most new and placed Basic zombies are animals (a Rat or a
        /// Dog); otherwise the given type. Owner, 2026-10-09: no Dogs at night (the night's animals are Rats).
        /// </summary>
        private ZombieType AnimalOr(ZombieType type, System.Random rng)
        {
            var share = Layout.Dark ? _tuning.spawn.animalShareNight : _tuning.spawn.animalShareDay;
            if (rng.NextDouble() >= share) return type;
            if (Layout.Dark) return ZombieType.Rat;
            return rng.NextDouble() < _tuning.spawn.ratShare ? ZombieType.Rat : ZombieType.Dog;
        }

        /// <summary>Owner, 2026-10-09: waves wait while this many zombies are already active (they can't pile up).</summary>
        private bool WavesFull => Zombie.Alive.Count >= _tuning.spawn.waveMaxActive;

        private static Vector3 Jitter(Vector3 p, System.Random rng) =>
            p + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 1.5f;

        /// <summary>The stage's population (dormant records and the pool of real zombies). Host-side.</summary>
        public ZombiePopulation Population { get; private set; }

        /// <summary>
        /// A real zombie right now, from the pool when one is free (otherwise built). Most code should use
        /// <see cref="ZombiePopulation.Add"/>, which keeps far-away zombies dormant.
        /// </summary>
        public Zombie SpawnZombie(ZombieType type, Vector3 position, float hp = -1f, Vector3? home = null)
        {
            var boss = ZombieTypes.IsBoss(type);
            if (!boss && Zombie.Alive.Count >= _tuning.spawn.maxAlive) return null;
            // Ground only: the NavMesh also covers the tops of low obstacles.
            if (!NavMesh.SamplePosition(position, out var hit, 3f, NavAreas.MaskFor(Def(type))) || hit.position.y > 0.3f) return null;
            var def = Def(type);
            // Owner, 2026-10-11: never inside a wall or an obstacle (a clear spot close by, or none).
            var clear = hit.position;
            if (!ClearSpot(ref clear, Mathf.Clamp(def.radius * 0.6f, 0.2f, 0.4f))) return null;
            hit.position = clear;
            var yaw = (float)_rng.NextDouble() * 360f;
            var pooled = Population != null ? Population.Take(type) : null;
            Zombie zombie;
            if (pooled != null)
            {
                zombie = pooled;
                zombie.Reuse(def, _tuning.perception, _spawnSeed++, hit.position, yaw, hp > 0f ? hp : def.hp);
            }
            else
            {
                var go = new GameObject(type == Layout.BossType && MainBoss == null && Layout.Mission == "boss" ? "Main Boss" : $"Zombie {type}") { layer = Layers.Zombie };
                go.transform.SetParent(transform, false);
                go.transform.position = hit.position;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.AddComponent<NavMeshAgent>();
                zombie = go.AddComponent<Zombie>();
                zombie.Init(def, _tuning.perception, _spawnSeed++);
                zombie.BuildVisual();
                zombie.Pool = Population;
                if (hp > 0f) zombie.SetHp(hp);
            }
            if (home.HasValue) zombie.SetHome(home.Value);
            zombie.NetId = NetIds.Next();
            return zombie;
        }

        private const int SolidMask = (1 << Layers.Obstacle) | (1 << Layers.ShootThrough);

        /// <summary>Owner, 2026-10-11: incubators at the bomb site (the target among them) and how far round it they stand (m).</summary>
        public const int BombSiteIncubators = 4;
        public const float BombSiteSpread = 2.8f;

        /// <summary>
        /// Owner, 2026-10-11 (zombies spawning stuck in walls and obstacles): <paramref name="p"/> moved to the nearest
        /// spot on the ground, on the NavMesh, clear of every wall and obstacle (within 3 m). False when there is none.
        /// </summary>
        public static bool ClearSpot(ref Vector3 p, float radius)
        {
            bool Free(Vector3 q) => !Physics.CheckCapsule(q + Vector3.up * (radius + 0.05f), q + Vector3.up * 1.2f, radius, SolidMask, QueryTriggerInteraction.Ignore);
            if (Free(p)) return true;
            for (var d = 0.5f; d <= 3f; d += 0.5f)
                for (var k = 0; k < 12; k++)
                {
                    var a = (k * 30f + d * 17f) * Mathf.Deg2Rad;
                    var q = p + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * d;
                    if (!NavMesh.SamplePosition(q, out var hit, 0.4f, NavAreas.WalkMask) || hit.position.y > 0.3f) continue;
                    if (!Free(hit.position)) continue;
                    p = hit.position;
                    return true;
                }
            return false;
        }

        /// <summary>Adds a zombie through the population (dormant when nobody is near). True when it was added.</summary>
        public bool AddZombie(ZombieType type, Vector3 position, Vector3? heading = null)
        {
            if (Population == null) return SpawnZombie(type, position) != null;
            var before = Population.Total;
            Population.Add(type, position, heading);
            return Population.Total > before;
        }

        private void Update()
        {
            if (GameSession.Instance != null && !GameSession.Instance.Playing) return;
            if (!World.Simulating) return;
            var dt = Time.deltaTime;

            // The tutorial: no random zombies (each step brings its own). Endless: only its waves.
            if (Layout.Tutorial || Layout.Endless) return;
            _areaCheckLeft -= dt;
            if (_areaCheckLeft <= 0f)
            {
                _areaCheckLeft = 0.5f;
                CheckAreaEntry();
            }

            _trickleLeft -= dt;
            if (_trickleLeft <= 0f)
            {
                _trickleLeft = _tuning.spawn.trickleInterval;
                Trickle();
            }

            for (var i = _respawnTimers.Count - 1; i >= 0; i--)
            {
                _respawnTimers[i] -= dt;
                if (_respawnTimers[i] > 0f) continue;
                _respawnTimers.RemoveAt(i);
                SpawnRandomAmmo();
            }
        }

        private float _startedAt;

        /// <summary>
        /// The stage is still being laid out (its first half second): the zombies placed on the map appear wherever they
        /// stand. After that nothing appears in a player's view (owner, 2026-10-09: SpawnView).
        /// </summary>
        public bool Placing => Time.time - _startedAt < 0.5f;

        /// <summary>A zombie may appear here now: not on a player's screen once the stage is laid out (owner, 2026-10-09).</summary>
        public bool MayAppearAt(Vector3 position) => Placing || !SpawnView.Seen(position);

        private float CountScale => Layout.Difficulty * (Coop ? _tuning.spawn.coopCount : _tuning.spawn.soloCount);
        private int Scaled(int count) => Mathf.Max(1, Mathf.RoundToInt(count * CountScale));

        /// <summary>D-080: entering an area for the first time spawns its entry group away from the player.</summary>
        private void CheckAreaEntry()
        {
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead) continue;
                var area = Layout.AreaAt(survivor.transform.position);
                if (area == null || area.EntryGroup <= 0 || !_entered.Add(area)) continue;
                SpawnGroup(SpawnPointsIn(area), Scaled(area.EntryGroup), 12f, 1000f, survivor.transform.position, allowFast: area.CombatZone);
            }
        }

        /// <summary>Timed spawning into areas that refill (D-203), always away from every player.</summary>
        private void Trickle()
        {
            if (WavesFull) return;
            var candidates = new List<AreaDef>();
            foreach (var area in Layout.Areas) if (area.Refills && area.FreePoints.Count > 0) candidates.Add(area);
            if (candidates.Count == 0) return;
            var chosen = candidates[_rng.Next(candidates.Count)];
            var min = _tuning.spawn.trickleMinDistance;
            SpawnGroup(SpawnPointsIn(chosen), Scaled(_tuning.spawn.trickleCount), min, 1000f, null, allowFast: false);
        }

        /// <summary>D-080: repeated gunfire and explosions draw new zombies in from a distance.</summary>
        private void OnNoise(Vector3 position, float range)
        {
            if (range < 15f || Layout.Tutorial || Layout.Endless) return;
            _noiseEvents++;
            if (_noiseEvents < _tuning.spawn.noiseEventsPerWave) return;
            _noiseEvents = 0;
            if (WavesFull) return;
            SpawnGroup(AllSpawnPoints, Scaled(_tuning.spawn.noiseWaveCount), _tuning.spawn.noiseWaveMin, _tuning.spawn.noiseWaveMax, position,
                allowFast: true, fromNoise: position);
        }

        private void SpawnGroup(List<Vector3> points, int count, float minDistance, float maxDistance, Vector3? around, bool allowFast,
            Vector3? fromNoise = null)
        {
            if (points.Count == 0) return;
            for (var attempt = 0; attempt < 30 && count > 0; attempt++)
            {
                var p = points[_rng.Next(points.Count)];
                if (around.HasValue)
                {
                    var d = Vector3.Distance(p, around.Value);
                    if (d < minDistance || d > maxDistance) continue;
                }
                if (NearAnySurvivor(p, minDistance)) continue;
                var type = allowFast && _rng.NextDouble() < 0.25 ? ZombieType.Fast : ZombieType.Basic;
                // Owner, 2026-10-07: day stages favour humanoid zombies, night stages animal zombies.
                type = AnimalOr(type, _rng);
                if (!AddZombie(type, Jitter(p, _rng), fromNoise)) return;
                count--;
            }
        }

        /// <summary>
        /// Bomb stages (owner, 2026-10-03): zombies pour out of the spawn points around the target and rush it. Only
        /// points at least 8 m from every survivor, so nothing pops up in a player's face.
        /// </summary>
        public void SpawnResponse(Vector3 target, float radius, int count)
        {
            var points = new List<Vector3>();
            foreach (var p in Layout.ZombieSpawnPoints)
                if ((p - target).magnitude <= radius && !NearAnySurvivor(p, 8f) && MayAppearAt(p)) points.Add(p);
            if (points.Count == 0) return;
            for (var i = 0; i < Scaled(count); i++)
            {
                if (WavesFull) return;
                var type = _rng.NextDouble() < 0.3 ? ZombieType.Fast : ZombieType.Basic;
                var zombie = SpawnZombie(type, Jitter(points[_rng.Next(points.Count)], _rng));
                if (zombie == null) return;
                zombie.Hear(target);
            }
        }

        private static bool NearAnySurvivor(Vector3 p, float distance)
        {
            foreach (var survivor in Survivor.All)
                if (!survivor.IsDead && Vector3.Distance(p, survivor.transform.position) < distance) return true;
            return false;
        }


        // ---- Buildings: equipment chests and zombie incubators ----

        /// <summary>Incubators and chests stand on their map cells; whether a building's chest appears is rolled every run.</summary>
        private void PlaceBuildingContents()
        {
            foreach (var building in Layout.Buildings)
            {
                if (building.Incubator) Incubator.Spawn(this, building.IncubatorPoint, _tuning.incubator);
                if (!building.MayHaveChest || _rng.NextDouble() >= _tuning.chest.chance) continue;
                var chest = Chest.Spawn(transform, building.ChestPoint, FreeYaw(building.ChestPoint, 0f));
                // Owner, 2026-10-02: some stages have less loot (the Vaccine stage: 30 %).
                var count = Mathf.RoundToInt(_rng.Next(_tuning.chest.minItems, _tuning.chest.maxItems + 1) * Layout.LootScale * _tuning.loot.amountMultiplier);
                for (var i = 0; i < count; i++) chest.Contents.Add(RandomLoot());
            }
            // Bomb stages: the target incubator (owner, 2026-10-02).
            if (Layout.Bomb)
            {
                Incubator.Spawn(this, Layout.BombTargetPosition, _tuning.incubator).MarkTarget();
                // Owner, 2026-10-11: four incubators at the bomb site: three more round the target (the C4 takes them all).
                for (var i = 0; i < BombSiteIncubators - 1; i++)
                {
                    var a = (i * 120f + 30f) * Mathf.Deg2Rad;
                    var at = Layout.BombTargetPosition + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * BombSiteSpread;
                    if (NavMesh.SamplePosition(at, out var hit, 1.5f, NavAreas.WalkMask) && hit.position.y < 0.3f) at = hit.position;
                    if (!ClearSpot(ref at, 0.6f)) continue;
                    Incubator.Spawn(this, at, _tuning.incubator);
                }
            }
        }

        /// <summary>PLACEHOLDER chest contents: long guns 40 %, ammo 30 %, Health Items 15 %, grenades 15 % (2026-10-04: more guns).</summary>
        private LootData RandomLoot()
        {
            var roll = _rng.Next(100);
            switch (roll < 40 ? 0 : roll < 70 ? 1 : roll < 85 ? 2 : 3)
            {
                case 0:
                    var guns = Arsenal.LongGuns;
                    var def = _tuning.Firearm(guns[_rng.Next(guns.Length)]);
                    return LootData.LongGun(def, def.magazine);
                case 1:
                    var types = Arsenal.AmmoTypes;
                    var type = types[_rng.Next(types.Length)];
                    return LootData.AmmoBox(type, _tuning.FirearmForAmmo(type).ammoPerBox);
                case 2: return LootData.Single(LootKind.Health);
                default: return LootData.Single(LootKind.Grenade);
            }
        }
        // ---- Loot ----

        /// <summary>
        /// D-205: loot goes only on the map's loot cells, in random spots. If a stage has fewer cells than items, the mix is
        /// shuffled and the extra items are left out (the Map Editor warns about it).
        /// </summary>
        private void PlaceLoot()
        {
            var l = _tuning.loot;
            var kinds = new List<LootKind>();
            // Owner, 2026-10-02: some stages have less loot (LootScale; the Vaccine stage 30 %).
            // Bigger maps get proportionally more loot (2026-10-04), then the stage's own loot amount.
            int Scaled(int n) => Mathf.RoundToInt(n * Layout.LootScale * Area * _tuning.loot.amountMultiplier);
            for (var i = 0; i < Scaled(l.longGuns); i++) kinds.Add(LootKind.LongGun);
            for (var i = 0; i < Scaled(l.ammoBoxes); i++) kinds.Add(LootKind.Ammo);
            for (var i = 0; i < Scaled(l.healthItems); i++) kinds.Add(LootKind.Health);
            for (var i = 0; i < Scaled(l.grenades); i++) kinds.Add(LootKind.Grenade);
            var cells = new List<Vector3>(LootPoints);
            Shuffle(cells);
            for (var i = kinds.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            var longGuns = Arsenal.LongGuns;
            for (var i = 0; i < kinds.Count && i < cells.Count; i++)
            {
                switch (kinds[i])
                {
                    case LootKind.LongGun:
                        var def = _tuning.Firearm(longGuns[_rng.Next(longGuns.Length)]);
                        LootItem.SpawnLongGun(cells[i], def, def.magazine, FreeYaw(cells[i], 40f));
                        break;
                    case LootKind.Ammo: SpawnAmmoAt(cells[i]); break;
                    default: LootItem.Spawn(cells[i], kinds[i]); break;
                }
            }
        }

        /// <summary>
        /// A turn for something long lying at <paramref name="p"/> (a gun on the ground, a chest), along whichever axis has
        /// free cells on both sides, so it never pokes through a wall (owner, 2026-10-04: weapons showed on rooftops).
        /// A little random jitter keeps it from looking placed by a grid.
        /// </summary>
        private float FreeYaw(Vector3 p, float jitter)
        {
            var map = Layout.Map;
            var x = Mathf.FloorToInt(p.x);
            var z = Mathf.FloorToInt(p.z);
            bool Free(int cx, int cz) => map == null || !Tiles.BlocksMovement(map[cx, cz]);
            var alongX = Free(x - 1, z) && Free(x + 1, z);
            var alongZ = Free(x, z - 1) && Free(x, z + 1);
            float baseYaw;
            if (alongX && alongZ) baseYaw = _rng.Next(4) * 90f;
            else if (alongX) baseYaw = _rng.Next(2) * 180f;
            else if (alongZ) baseYaw = 90f + _rng.Next(2) * 180f;
            else return _rng.Next(4) * 90f;
            // Diagonal neighbours free too: allow a bigger slant.
            var open = Free(x - 1, z - 1) && Free(x + 1, z + 1) && Free(x - 1, z + 1) && Free(x + 1, z - 1);
            var j = open ? jitter : jitter * 0.3f;
            return baseYaw + ((float)_rng.NextDouble() * 2f - 1f) * j;
        }

        private void SpawnAmmoAt(Vector3 position)
        {
            var types = Arsenal.AmmoTypes;
            var type = types[_rng.Next(types.Length)];
            LootItem.SpawnAmmo(position, type, _tuning.FirearmForAmmo(type).ammoPerBox, respawns: true);
        }

        private void OnLootEmptied(LootItem item) => _respawnTimers.Add(_tuning.loot.ammoRespawnSeconds / Mathf.Max(0.05f, Layout.LootScale));

        /// <summary>
        /// Endless mode: one random item (the chests' mix) on a loot spot away from the players, while fewer than
        /// <paramref name="max"/> lie about.
        /// </summary>
        public void SpawnRandomLoot(int max)
        {
            if (LootPoints.Count == 0 || LootItem.All.Count >= max) return;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var p = LootPoints[_rng.Next(LootPoints.Count)];
                if (NearAnySurvivor(p, 15f)) continue;
                LootItem.Spawn(p, RandomLoot());
                return;
            }
        }

        private void SpawnRandomAmmo()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var p = LootPoints[_rng.Next(LootPoints.Count)];
                if (NearAnySurvivor(p, 15f)) continue;
                SpawnAmmoAt(p);
                return;
            }
        }

        private void Shuffle(List<Vector3> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
