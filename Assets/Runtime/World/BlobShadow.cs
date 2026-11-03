using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// A dark elliptical shadow on the ground under a character (owner, 2026-09-30: simple ellipse shadows instead of
    /// real-time ones; 2026-10-07: characters and zombies keep these, dark, while buildings and props cast real shadows).
    /// By day it lies the way the real shadows fall (away from the sun, stretched with the height), so it shows beside
    /// the body instead of hiding under it; at night it sits under the feet. It never turns with the character.
    /// One shared material; one quad each.
    /// </summary>
    public static class BlobShadow
    {
        private static Material _material;
        private static Mesh _quad;

        /// <param name="height">How tall the character is (the shadow's length by day); 0: from its size.</param>
        public static void Attach(Transform owner, float width, float length, float height = 0f)
        {
            if (_material == null) _material = MakeMaterial();
            if (_quad == null) _quad = MakeQuad();
            var go = new GameObject("Blob shadow");
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            go.transform.localScale = new Vector3(width, 1f, length);
            go.AddComponent<BlobShadowPlacer>().Init(width, length, height > 0f ? height : Mathf.Max(width, length) * 1.2f);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Its own shader (Resources/BlobShadow): it darkens what is beneath by multiplying, so lighting, transparency and
        /// post-processing can't wash it out (2026-10-07: drawn as a lit, see-through texture it never showed).
        /// </summary>
        private static Material MakeMaterial()
        {
            var shader = Resources.Load<Shader>("BlobShadow");
            var material = new Material(shader != null ? shader : Shader.Find("Zombie/BlobShadow")) { name = "Blob shadow" };
            material.SetFloat("_Darkness", Darkness);
            return material;
        }

        /// <summary>How dark the ellipse is at its centre (0.7: the ground at 30 %). PLACEHOLDER.</summary>
        public const float Darkness = 0.7f;

        private static Mesh MakeQuad()
        {
            var mesh = new Mesh { name = "Blob shadow" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            // White, opaque vertex colours: vertex alpha below 1 would make it glow (the shader's glow mask).
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            return mesh;
        }
    }

    /// <summary>Keeps an ellipse shadow on the ground, unturned, on the side the real shadows fall (by day).</summary>
    public class BlobShadowPlacer : MonoBehaviour
    {
        /// <summary>How far the shadow reaches per metre of height by day (PLACEHOLDER, shorter than the sun's real one).</summary>
        public const float Reach = 0.45f;

        private static readonly Vector3 Away = new Vector3(-TileAssembler.ToSun.x, 0f, -TileAssembler.ToSun.z).normalized;
        private float _width, _length, _height;

        public void Init(float width, float length, float height)
        {
            _width = width;
            _length = length;
            _height = height;
        }

        // After the character has turned and moved this frame (before drawing).
        private void LateUpdate() => Place();

        private Transform _anchor;

        public void Place()
        {
            var owner = transform.parent;
            if (owner == null) return;
            // A character's shadow follows what is drawn of it (on the Host, the online friend's smooth playback).
            if (_anchor == null)
            {
                var survivor = owner.GetComponent<Survivor>();
                _anchor = survivor != null ? survivor.Visual : owner;
            }
            var day = !StageConditions.Dark;
            var reach = day ? _height * Reach : 0f;
            transform.SetPositionAndRotation(_anchor.position + Away * (reach * 0.5f) + Vector3.up * 0.03f,
                day ? Quaternion.LookRotation(Away, Vector3.up) : Quaternion.identity);
            // Its own scale in the world (the owner may be scaled): across, and along the shadow.
            var parentScale = owner.lossyScale;
            var across = Mathf.Min(_width, _length);
            var along = Mathf.Max(_width, _length) + reach;
            transform.localScale = new Vector3(across / Mathf.Max(0.01f, parentScale.x), 1f / Mathf.Max(0.01f, parentScale.y), along / Mathf.Max(0.01f, parentScale.z));
        }
    }
}
