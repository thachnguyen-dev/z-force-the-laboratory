using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Free-placed props and decorations (owner, 2026-10-02: maps no longer follow the grid). Built-in pieces are built
    /// straight into one merged mesh per 16 m chunk; prefabs (Resources/Props, Resources/Decorations) are merged the same
    /// way. Props get colliders on the obstacle layer, so they block walking, bullets, the NavMesh and (through
    /// <see cref="StageGrid"/>) sight; bush props are listed as bush areas instead.
    /// </summary>
    public static class PropLayer
    {
        private const float Chunk = 16f;
        /// <summary>Colliders are at least this tall, so a low crate still stops bullets like a cover cell does.</summary>
        public const float MinColliderHeight = 1.6f;

        public static GameObject Prefab(string id, ArtLibrary.Kind kind) =>
            string.IsNullOrEmpty(id) ? null : Resources.Load<GameObject>((kind == ArtLibrary.Kind.Prop ? "Props/" : "Decorations/") + id);

        /// <summary>A piece exists either built in or as a prefab.</summary>
        public static bool Exists(string id, ArtLibrary.Kind kind) => ArtLibrary.Get(id) != null || Prefab(id, kind) != null;

        /// <summary>Builds the props; returns the cells they cover (for contact shadows). Also fills the bush areas.</summary>
        public static bool[,] BuildProps(StageMap map, Transform parent)
        {
            var covered = new bool[map.width, map.height];
            StageGrid.HasSolidProps = false;
            StageGrid.BushMask = null;
            if (map.props == null || map.props.Count == 0) return covered;
            var root = new GameObject("Props").transform;
            root.SetParent(parent, false);
            var colliders = new GameObject("Prop colliders").transform;
            colliders.SetParent(parent, false);
            Build(map.props, ArtLibrary.Kind.Prop, root, true, map.buildings);

            foreach (var p in map.props)
            {
                var piece = ArtLibrary.Get(p.id);
                var info = piece == null ? Prefab(p.id, ArtLibrary.Kind.Prop)?.GetComponent<PropInfo>() : null;
                if (piece == null && info == null) continue;
                if (piece != null && piece.Walkable)
                {
                    PoleColliders(colliders, p, piece);
                    continue;
                }
                var footprint = piece != null ? PackProps.Footprint(piece, p) : info.Footprint;
                var round = piece != null ? piece.Round : info.Round;
                var height = piece != null ? PackProps.Height(piece, p) : info.Height;
                var bush = piece != null ? piece.Bush : info.Bush;
                var scale = p.scale > 0f ? p.scale : 1f;
                var center = new Vector3(p.x, 0f, p.z);
                if (bush)
                {
                    MarkBush(map, center, footprint.x * 0.5f * scale);
                    continue;
                }
                // The owner's second obstacle kind blocks movement only: bullets and sight pass (2026-10-04).
                var shootThrough = piece != null && piece.ShootThrough;
                var go = new GameObject($"Prop {p.id}") { layer = shootThrough ? Layers.ShootThrough : Layers.Obstacle };
                go.transform.SetParent(colliders, false);
                go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, p.rotation, 0f));
                var colliderHeight = Mathf.Max(MinColliderHeight, height * scale);
                if (round)
                {
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius = footprint.x * 0.5f * scale;
                    capsule.height = Mathf.Max(colliderHeight, capsule.radius * 2f);
                    capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
                }
                else
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(footprint.x * scale, colliderHeight, footprint.y * scale);
                    box.center = new Vector3(0f, colliderHeight * 0.5f, 0f);
                }
                go.isStatic = true;
                if (colliderHeight > NavBlockHeight + 0.1f) NavBlock(go, footprint * scale, round);
                if (!shootThrough) StageGrid.HasSolidProps = true;
                MarkCovered(covered, center, p.rotation, footprint * scale, round);
            }
            return covered;
        }

        /// <summary>
        /// The NavMesh sees only a collider's faces, not its inside: a prop taller than the agents (2 m) left a hollow,
        /// walkable room under its roof, so zombies were placed and walked inside cars, vans, tall trees (owner,
        /// 2026-10-11). A block lower than the agents, filling the bottom of the same footprint, closes it. It sits wholly
        /// inside the prop's collider, so movement, bullets and sight are unchanged.
        /// </summary>
        public const float NavBlockHeight = 1.8f;

        private static void NavBlock(GameObject prop, Vector2 footprint, bool round)
        {
            var go = new GameObject("NavMesh block") { layer = prop.layer };
            go.transform.SetParent(prop.transform, false);
            // A round prop narrower than the block is tall: a capsule; otherwise (and for square props) a box.
            if (round && footprint.x < NavBlockHeight)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = footprint.x * 0.5f;
                capsule.height = NavBlockHeight;
                capsule.direction = 1;
                capsule.center = new Vector3(0f, NavBlockHeight * 0.5f, 0f);
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                // A round footprint: the square inside its circle.
                var side = round ? footprint.x * 0.7f : 0f;
                box.size = round ? new Vector3(side, NavBlockHeight, side) : new Vector3(footprint.x, NavBlockHeight, footprint.y);
                box.center = new Vector3(0f, NavBlockHeight * 0.5f, 0f);
            }
            go.isStatic = true;
        }

        /// <summary>
        /// A walkable piece's poles (the basketball court's hoops; owner 2026-10-04): small round colliders that block
        /// movement only, placed with the piece's turn, size and mirroring.
        /// </summary>
        private static void PoleColliders(Transform parent, MapDecoration p, ArtLibrary.Piece piece)
        {
            var def = PackProps.Get(piece.Model);
            if (def == null) return;
            var scale = def.Size * (p.scale > 0f ? p.scale : 1f);
            var turn = Quaternion.Euler(0f, p.rotation, 0f);
            foreach (var pole in PackProps.Poles(def.Id))
            {
                var local = new Vector3(p.flip && !piece.NoFlip ? -pole.x : pole.x, 0f, pole.y) * scale;
                var go = new GameObject($"Pole {p.id}") { layer = Layers.ShootThrough };
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(p.x, 0f, p.z) + turn * local;
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = PackProps.PoleRadius;
                capsule.height = MinColliderHeight;
                capsule.center = new Vector3(0f, MinColliderHeight * 0.5f, 0f);
                go.isStatic = true;
            }
        }

        public static void BuildDecorations(StageMap map, Transform parent)
        {
            if (map.decorations == null || map.decorations.Count == 0) return;
            var root = new GameObject("Decorations").transform;
            root.SetParent(parent, false);
            // Wind-blown leaves and paper belong to normal stages only (owner, 2026-10-05: not in rain or darkness).
            Build(map.decorations, ArtLibrary.Kind.Decoration, root, map.weather != "rain" && !map.dark);
            // Raindrop rings on the puddles while it rains (owner, 2026-10-05).
            if (Application.isPlaying && map.weather == "rain")
                foreach (var d in map.decorations)
                    if (d.id == "rain_puddle") RainRipples.Add(root, new Vector3(d.x, 0f, d.z), 0.8f * (d.scale > 0f ? d.scale : 1f));
        }

        /// <summary>
        /// One of the owner's pack models as its own object sharing the model's mesh (2026-10-04): every face is kept (no
        /// polygon reduction yet), and only what's on screen is drawn. Turned, sized, mirrored and coloured as placed.
        /// </summary>
        /// <param name="indoors">Inside a building (owner, 2026-10-07: no shadows there).</param>
        public static GameObject PlaceModel(Transform root, MapDecoration d, ArtLibrary.Piece piece, bool indoors = false)
        {
            var def = PackProps.Get(piece.Model);
            if (def == null) return null;
            // Variant 0 = chosen from the spot, so a row of cars isn't all one colour.
            var variant = d.variant > 0 ? d.variant - 1 : Mathf.Abs(Mathf.FloorToInt(d.x * 13f) * 31 + Mathf.FloorToInt(d.z * 7f));
            var mesh = PackProps.Mesh(def.Id, variant);
            if (mesh == null) return null;
            var scale = def.Size * (d.scale > 0f ? d.scale : 1f);
            // Owner, 2026-10-08: in play the helicopter lands, spins its rotor and takes off (Helicopter).
            if (d.id == "helicopter" && Application.isPlaying)
                return Helicopter.Create(root, mesh, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, d.rotation, 0f), Vector3.one * scale),
                    new Vector3(d.x, 0f, d.z), d.rotation).gameObject;
            var go = new GameObject($"Model {d.id}");
            go.transform.SetParent(root, false);
            // Posed models (overturned, on their side, leaning) are tipped, then lifted to rest on the ground.
            var lift = PackProps.Posed(d) ? PackProps.PosedBounds(def, d.roll, d.pitch).lift * (d.scale > 0f ? d.scale : 1f) : 0f;
            go.transform.SetPositionAndRotation(new Vector3(d.x, lift, d.z), Quaternion.Euler(0f, d.rotation, 0f) * PackProps.Pose(d.roll, d.pitch));
            go.transform.localScale = new Vector3(d.flip && !piece.NoFlip ? -scale : scale, scale, scale);
            go.isStatic = true;
            // Owner, 2026-10-07: only the faces the camera or the sun can reach; the turn, pose, size and mirroring are baked
            // into the (shared) mesh, so the object itself stays unturned.
            var flip = d.flip && !piece.NoFlip;
            go.transform.SetPositionAndRotation(new Vector3(d.x, 0f, d.z), Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = MeshCull.Placed(mesh, d.rotation, PackProps.Pose(d.roll, d.pitch), scale, flip, !indoors, lift);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ArtMaterials.Stylized;
            // Owner, 2026-10-07: obstacles and props cast real shadows; nothing inside a building does (the roof covers it).
            renderer.shadowCastingMode = indoors ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go;
        }

        /// <summary>True when (x, z) is inside one of the map's buildings.</summary>
        private static bool Indoors(List<MapBuilding> buildings, float x, float z)
        {
            if (buildings == null) return false;
            foreach (var b in buildings)
                if (x > b.x && x < b.x + b.w && z > b.z && z < b.z + b.d) return true;
            return false;
        }

        /// <param name="buildings">Pieces inside these cast no shadow (owner, 2026-10-07) and keep only the faces the camera sees.</param>
        private static void Build(List<MapDecoration> placed, ArtLibrary.Kind kind, Transform root, bool windAllowed = true, List<MapBuilding> buildings = null)
        {
            // Per 16 m chunk, outdoor and indoor pieces apart.
            var kits = new Dictionary<(Vector2Int at, bool indoors), MeshKit>();
            var prefabChunks = new Dictionary<(Vector2Int at, bool indoors), Dictionary<Material, List<CombineInstance>>>();
            var prefabParts = new Dictionary<string, List<TileAssembler.Part>>();
            var missing = new HashSet<string>();
            var index = 0;
            foreach (var d in placed)
            {
                index++;
                var indoors = Indoors(buildings, d.x, d.z);
                var key = (new Vector2Int(Mathf.FloorToInt(d.x / Chunk), Mathf.FloorToInt(d.z / Chunk)), indoors);
                var scale = d.scale > 0f ? d.scale : 1f;
                // Decorations lie just above the surfaces; props stand on the ground.
                var lift = kind == ArtLibrary.Kind.Decoration ? 0.012f : 0f;
                var place = Matrix4x4.TRS(new Vector3(d.x, lift, d.z), Quaternion.Euler(0f, d.rotation, 0f), Vector3.one * scale);
                var piece = ArtLibrary.Get(d.id);
                if (piece != null && piece.Bush && kind == ArtLibrary.Kind.Prop && GrassLayer.Available)
                {
                    GrassLayer.Prop(root, new Vector3(d.x, 0f, d.z), piece.Footprint.x * scale, Mathf.FloorToInt(d.x * 31f) * 7919 + Mathf.FloorToInt(d.z * 17f));
                    continue;
                }
                if (piece != null && piece.Wind)
                {
                    if (!windAllowed) continue;
                    var windSeed = Mathf.FloorToInt(d.x * 31f) * 7919 + Mathf.FloorToInt(d.z * 17f) + index;
                    // In play the bits move on their own; outside play (editor previews) they lie still, merged.
                    if (Application.isPlaying)
                    {
                        WindBlown.Place(root, d, windSeed);
                        continue;
                    }
                    if (!kits.TryGetValue(key, out var windKit)) kits[key] = windKit = new MeshKit();
                    windKit.Transform = place;
                    piece.Build(windKit, windSeed);
                    continue;
                }
                if (piece != null && piece.Model != null)
                {
                    PlaceModel(root, d, piece, indoors);
                    continue;
                }
                if (piece != null)
                {
                    if (!kits.TryGetValue(key, out var kit)) kits[key] = kit = new MeshKit();
                    kit.Transform = place;
                    // The seed comes from the position, so the same spot always looks the same.
                    piece.Build(kit, Mathf.FloorToInt(d.x * 31f) * 7919 + Mathf.FloorToInt(d.z * 17f) + index);
                    continue;
                }
                if (!prefabParts.TryGetValue(d.id ?? "", out var parts))
                {
                    var prefab = Prefab(d.id, kind);
                    prefabParts[d.id ?? ""] = parts = prefab != null ? TileAssembler.PartsOf(prefab) : new List<TileAssembler.Part>();
                    if (prefab == null) missing.Add(d.id);
                }
                if (parts.Count == 0) continue;
                if (!prefabChunks.TryGetValue(key, out var byMaterial)) prefabChunks[key] = byMaterial = new Dictionary<Material, List<CombineInstance>>();
                foreach (var part in parts)
                {
                    if (!byMaterial.TryGetValue(part.Material, out var list)) byMaterial[part.Material] = list = new List<CombineInstance>();
                    for (var sub = 0; sub < part.Mesh.subMeshCount; sub++)
                        list.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = sub, transform = place * part.Local });
                }
            }
            foreach (var pair in kits)
            {
                var casts = kind == ArtLibrary.Kind.Prop && !pair.Key.indoors;
                pair.Value.RemoveHiddenFaces(casts);
                Emit(pair.Value.ToMesh($"{kind}{(pair.Key.indoors ? " indoors" : "")} {pair.Key.at.x},{pair.Key.at.y}"), root, casts);
            }
            foreach (var pair in prefabChunks)
                TileAssembler.Emit(pair.Value, root, $"{kind} prefabs{(pair.Key.indoors ? " indoors" : "")} {pair.Key.at.x},{pair.Key.at.y}", kind == ArtLibrary.Kind.Prop && !pair.Key.indoors);
            foreach (var id in missing) Debug.LogWarning($"[ZOMBIE] {kind} '{id}' is neither built in nor a prefab; skipped.");
        }

        /// <summary>A static chunk of map art. Owner, 2026-10-07: walls, buildings and props cast shadows (flat decorations don't).</summary>
        public static void Emit(Mesh mesh, Transform root, bool castShadows = true)
        {
            if (mesh.vertexCount == 0) return;
            var go = new GameObject(mesh.name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ArtMaterials.Stylized;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            go.isStatic = true;
        }

        private static void MarkCovered(bool[,] covered, Vector3 center, float rotation, Vector2 size, bool round)
        {
            var reach = Mathf.CeilToInt(Mathf.Max(size.x, size.y)) + 1;
            var inverse = Quaternion.Euler(0f, -rotation, 0f);
            for (var x = Mathf.FloorToInt(center.x) - reach; x <= Mathf.FloorToInt(center.x) + reach; x++)
            for (var z = Mathf.FloorToInt(center.z) - reach; z <= Mathf.FloorToInt(center.z) + reach; z++)
            {
                if (x < 0 || z < 0 || x >= covered.GetLength(0) || z >= covered.GetLength(1)) continue;
                var local = inverse * (new Vector3(x + 0.5f, 0f, z + 0.5f) - center);
                var inside = round ? local.magnitude <= size.x * 0.5f : Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
                if (inside) covered[x, z] = true;
            }
        }

        private static void MarkBush(StageMap map, Vector3 center, float radius)
        {
            const float res = StageGrid.BushMaskResolution;
            if (StageGrid.BushMask == null)
                StageGrid.BushMask = new bool[Mathf.CeilToInt(map.width / res), Mathf.CeilToInt(map.height / res)];
            var mask = StageGrid.BushMask;
            var r = Mathf.CeilToInt(radius / res) + 1;
            int cx = Mathf.FloorToInt(center.x / res), cz = Mathf.FloorToInt(center.z / res);
            for (var x = cx - r; x <= cx + r; x++)
            for (var z = cz - r; z <= cz + r; z++)
            {
                if (x < 0 || z < 0 || x >= mask.GetLength(0) || z >= mask.GetLength(1)) continue;
                var p = new Vector3((x + 0.5f) * res, 0f, (z + 0.5f) * res);
                if ((p - center).magnitude <= radius) mask[x, z] = true;
            }
        }
    }

    /// <summary>On a prop prefab (Resources/Props): its footprint and what it does, like a built-in prop.</summary>
    public class PropInfo : MonoBehaviour
    {
        public Vector2 Footprint = Vector2.one;
        public bool Round;
        public float Height = 1f;
        public bool Bush;
    }
}
