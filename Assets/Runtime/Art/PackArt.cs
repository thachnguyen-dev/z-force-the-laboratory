using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// The owner's pack models (2026-10-04), coloured by the editor's PackBuilder and kept in Resources/Pack. A model is
    /// placed as a sized and turned copy, cached per model, size and turn (turns rounded to 15°). Owner, 2026-10-04: no
    /// polygon reduction yet (a separate optimisation stage comes once the graphics are final), so every face is kept.
    /// </summary>
    public static class PackArt
    {
        public const string ResourceFolder = "Pack";

        private static readonly Dictionary<string, Mesh> Models = new Dictionary<string, Mesh>();
        private static readonly Dictionary<(string, int, int, bool), Mesh> Culled = new Dictionary<(string, int, int, bool), Mesh>();

        public static Mesh Model(string id)
        {
            if (Models.TryGetValue(id, out var m) && m != null) return m;
            m = Resources.Load<Mesh>($"{ResourceFolder}/{id}");
            Models[id] = m;
            return m;
        }

        public static bool Has(string id) => Model(id) != null;

        private static readonly Dictionary<string, Vector3> Muzzles = new Dictionary<string, Vector3>();

        /// <summary>
        /// The muzzle of a weapon model, in the model's own space: the middle of its front tip (the vertices within 3 % of
        /// its front end, +x). Owner, 2026-10-04: the flash and the bullets start there.
        /// </summary>
        public static Vector3 MuzzleOf(string id)
        {
            if (Muzzles.TryGetValue(id, out var m)) return m;
            var model = Model(id);
            if (model == null) return Vector3.zero;
            var v = model.vertices;
            var front = model.bounds.max.x;
            var reach = model.bounds.size.x * 0.03f;
            var sum = Vector3.zero;
            var n = 0;
            foreach (var p in v)
            {
                if (p.x < front - reach) continue;
                sum += p;
                n++;
            }
            m = n > 0 ? new Vector3(front, sum.y / n, sum.z / n) : new Vector3(front, model.bounds.center.y, 0f);
            Muzzles[id] = m;
            return m;
        }

        /// <summary>
        /// Adds the model to a kit at <paramref name="place"/> (position, turn, size), exempt from the kit's
        /// RemoveHiddenFaces; for static map pieces merged into chunks (fence panels).
        /// </summary>
        public static bool AddTo(MeshKit kit, string id, Matrix4x4 place)
        {
            var model = Model(id);
            if (model == null) return false;
            kit.AddMesh(model, place, keepAllFaces: true);
            return true;
        }

        /// <summary>The model sized and turned as given, every face kept (cached).</summary>
        /// <param name="flat">Laid on its side (a gun lying on the ground): its thin side down.</param>
        public static Mesh Visible(string id, float size, float yaw, bool flat = false)
        {
            var model = Model(id);
            if (model == null) return null;
            var turn = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 15f) % 24;
            var key = (id, Mathf.RoundToInt(size * 100f), turn, flat);
            if (Culled.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var kit = new MeshKit();
            var place = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, turn * 15f, 0f), Vector3.one * size);
            if (flat)
            {
                var b = model.bounds;
                place *= Matrix4x4.Translate(new Vector3(0f, b.extents.z, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f)) * Matrix4x4.Translate(new Vector3(0f, -b.center.y, 0f));
            }
            kit.AddMesh(model, place, keepAllFaces: true);

            mesh = kit.ToMesh($"{id} visible");
            Culled[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// A child object showing the model: placed un-turned in the parent's space at <paramref name="local"/>, with the
        /// parent's world turn baked in for culling (the object itself is not rotated, so the culling stays right).
        /// Returns null when the model isn't in the pack (callers keep their placeholder then).
        /// </summary>
        public static GameObject Show(Transform parent, string id, float size, Vector3 local, float extraYaw = 0f, bool flat = false)
        {
            var yaw = parent.eulerAngles.y + extraYaw;
            var mesh = Visible(id, size, yaw, flat);
            if (mesh == null) return null;
            var go = new GameObject($"Model {id}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            // Undo the parent's turn: the mesh already carries it.
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ArtMaterials.Stylized;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
