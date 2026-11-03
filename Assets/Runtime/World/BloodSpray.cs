using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Green blood spraying out where bullets hit zombies (owner, 2026-10-08: green, the infection's colour; a spray in the
    /// air, not marks on the ground). Each hit throws a burst of droplets from the zombie, most out the far side, some back
    /// towards the shot; they arc down, shrink, and are gone when they reach the ground (no marks are left). At most
    /// <see cref="Max"/> at once (the oldest go first). Each device makes its own (an online friend's when it sees a
    /// zombie's health drop), so nothing goes over the network.
    /// </summary>
    public class BloodSpray : MonoBehaviour
    {
        public const int Max = 180;
        public const float Gravity = 14f;
        public const float MaxLife = 0.7f;
        /// <summary>The infected blood (owner: green), in the zombies' infection colours. PLACEHOLDER shade.</summary>
        public static readonly Color Green = new Color(0.25f, 0.72f, 0.12f, 1f);

        private class Drop
        {
            public Transform T;
            public Vector3 V;
            public float Age, Life, Size;
            public bool Live;
        }

        private static BloodSpray _instance;
        private static Material _material;
        private static Mesh _quad;
        private readonly List<Drop> _drops = new List<Drop>();
        private readonly System.Random _rng = new System.Random(11);
        private int _next;

        /// <summary>Droplets in the air now.</summary>
        public static int InAir
        {
            get
            {
                if (_instance == null) return 0;
                var n = 0;
                foreach (var d in _instance._drops) if (d.Live) n++;
                return n;
            }
        }

        /// <summary>
        /// A burst from a zombie standing at <paramref name="at"/>, <paramref name="height"/> tall, hit along
        /// <paramref name="shot"/> (from the shooter towards it; zero: any way). <paramref name="size"/> scales the burst.
        /// </summary>
        public static void Burst(Vector3 at, float height, Vector3 shot, float size = 1f)
        {
            if (!Application.isPlaying) return;
            if (_instance == null)
            {
                var stage = GameSession.Instance != null ? GameSession.Instance.StageRoot : null;
                var go = new GameObject("Blood spray");
                if (stage != null) go.transform.SetParent(stage, false);
                _instance = go.AddComponent<BloodSpray>();
            }
            _instance.Add(at, height, shot, size);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void Add(Vector3 at, float height, Vector3 shot, float size)
        {
            if (_material == null) _material = MakeMaterial();
            if (_quad == null) _quad = Quad();
            shot.y = 0f;
            if (shot.sqrMagnitude < 0.01f)
            {
                var a = R(0f, Mathf.PI * 2f);
                shot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            shot.Normalize();
            var side = Vector3.Cross(Vector3.up, shot);
            // Where the shot went in: the side facing the shooter, about chest high.
            var origin = at - shot * 0.25f + Vector3.up * Mathf.Clamp(height * 0.62f, 0.25f, 2.4f);
            var count = Mathf.RoundToInt(R(14f, 21f) * Mathf.Clamp(size, 0.6f, 1.8f));
            for (var i = 0; i < count; i++)
            {
                // Mostly out the far side in a wide cone; a third back towards the shot.
                var back = _rng.NextDouble() < 0.3;
                var dir = (back ? -shot : shot) + side * R(-0.9f, 0.9f);
                dir.Normalize();
                var speed = R(2.2f, 6f) * (back ? 0.6f : 1f) * Mathf.Sqrt(Mathf.Clamp(size, 0.6f, 1.8f));
                var v = dir * speed + Vector3.up * R(0.4f, 3.2f);
                Spawn(origin + Random.insideUnitSphere * 0.08f, v, R(0.12f, 0.28f) * Mathf.Clamp(size, 0.7f, 1.6f), R(0.35f, MaxLife));
            }
        }

        private void Spawn(Vector3 p, Vector3 v, float size, float life)
        {
            Drop d;
            if (_drops.Count < Max)
            {
                var go = new GameObject("Drop");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                d = new Drop { T = go.transform };
                _drops.Add(d);
            }
            else
            {
                // Full: the oldest goes.
                d = _drops[_next];
                _next = (_next + 1) % _drops.Count;
            }
            d.V = v;
            d.Age = 0f;
            d.Life = life;
            d.Size = size;
            d.Live = true;
            // Facing the camera (it never turns).
            d.T.SetPositionAndRotation(p, Quaternion.LookRotation(-TileAssembler.ToCamera));
            d.T.localScale = Vector3.one * size;
            d.T.gameObject.SetActive(true);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            foreach (var d in _drops)
            {
                if (!d.Live) continue;
                d.Age += dt;
                d.V.y -= Gravity * dt;
                var p = d.T.position + d.V * dt;
                // Gone at the ground (no marks) or at the end of its life.
                if (p.y <= 0.05f || d.Age >= d.Life)
                {
                    d.Live = false;
                    d.T.gameObject.SetActive(false);
                    continue;
                }
                d.T.position = p;
                // Stretched a little along its flight, shrinking towards the end.
                var k = 1f - Mathf.Clamp01((d.Age / d.Life - 0.6f) / 0.4f);
                d.T.localScale = new Vector3(d.Size * k, d.Size * k * (1f + Mathf.Min(1.2f, d.V.magnitude * 0.12f)), 1f);
            }
        }

        private static Mesh Quad()
        {
            var mesh = new Mesh { name = "Drop" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            // Facing -z (towards the camera once turned to look along its view).
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material MakeMaterial()
        {
            var shader = Resources.Load<Shader>("BloodDecal");
            var material = new Material(shader != null ? shader : Shader.Find("Zombie/BloodDecal")) { name = "Blood spray" };
            material.SetTexture("_BaseMap", DropTexture());
            material.SetColor("_BaseColor", Green);
            return material;
        }

        /// <summary>A round drop, lighter towards its middle (wet), soft at the rim.</summary>
        public static Texture2D DropTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Blood drop" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f;
                var a = Mathf.Clamp01((1f - d) / 0.2f);
                var g = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(1.25f, 0.75f, Mathf.Clamp01(d)) / 1.25f);
                pixels[y * size + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(255f * a));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }
    }
}
