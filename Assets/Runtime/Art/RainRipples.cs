using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Raindrop rings on the brown puddles of rainy stages (owner, 2026-10-05: puddles that look wet). A small pool of
    /// thin pale rings: about 30 a second, each starting on a puddle near the camera and spreading to about 30 cm in
    /// 0.7 s before it goes. Looks only.
    /// </summary>
    public class RainRipples : MonoBehaviour
    {
        private const int Pool = 28;
        private const float Life = 0.7f;
        private const float PerSecond = 30f;
        private const float Range = 22f;

        private static RainRipples _instance;
        private static Mesh _ring;
        private readonly List<(Vector3 centre, float radius)> _puddles = new List<(Vector3, float)>();
        private readonly List<int> _near = new List<int>();
        private Transform[] _rings;
        private float[] _age;
        private int _next;
        private float _due;
        private float _nearCheck;

        /// <summary>Adds a puddle (centre, rough radius in metres) for the rain to fall on.</summary>
        public static void Add(Transform root, Vector3 centre, float radius)
        {
            if (_instance == null)
            {
                var go = new GameObject("Rain ripples");
                go.transform.SetParent(root, false);
                _instance = go.AddComponent<RainRipples>();
                _instance.Init();
            }
            _instance._puddles.Add((centre, radius));
        }

        private void Init()
        {
            if (_ring == null) _ring = RingMesh();
            _rings = new Transform[Pool];
            _age = new float[Pool];
            for (var i = 0; i < Pool; i++)
            {
                var go = new GameObject("Ripple");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _ring;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ArtMaterials.Stylized;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                _rings[i] = go.transform;
                _age[i] = Life;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            for (var i = 0; i < Pool; i++)
            {
                if (_age[i] >= Life) continue;
                _age[i] += dt;
                if (_age[i] >= Life)
                {
                    _rings[i].gameObject.SetActive(false);
                    continue;
                }
                var t = _age[i] / Life;
                var s = Mathf.Lerp(0.04f, 0.22f, Mathf.Sqrt(t));
                _rings[i].localScale = new Vector3(s, 1f, s);
            }
            // Which puddles are near the camera (checked twice a second).
            var cam = Camera.main;
            if (cam == null || _puddles.Count == 0) return;
            if (Time.time >= _nearCheck)
            {
                _nearCheck = Time.time + 0.5f;
                _near.Clear();
                var c = cam.transform.position;
                var forward = cam.transform.forward;
                // The point on the ground the camera looks at.
                var look = Mathf.Abs(forward.y) > 0.01f ? c + forward * (-c.y / forward.y) : c;
                for (var i = 0; i < _puddles.Count; i++)
                    if ((new Vector2(_puddles[i].centre.x - look.x, _puddles[i].centre.z - look.z)).sqrMagnitude < Range * Range) _near.Add(i);
            }
            if (_near.Count == 0) return;
            _due += dt * PerSecond;
            while (_due >= 1f)
            {
                _due -= 1f;
                var (centre, radius) = _puddles[_near[Random.Range(0, _near.Count)]];
                var spot = Random.insideUnitCircle * radius * 0.55f;
                var ring = _rings[_next];
                _age[_next] = 0f;
                ring.position = new Vector3(centre.x + spot.x, 0.03f, centre.z + spot.y);
                ring.localScale = new Vector3(0.04f, 1f, 0.04f);
                ring.gameObject.SetActive(true);
                _next = (_next + 1) % Pool;
            }
        }

        /// <summary>A thin pale ring of radius 1 lying flat, facing up.</summary>
        private static Mesh RingMesh()
        {
            var k = new MeshKit();
            const int n = 18;
            var colour = new Color(0.64f, 0.58f, 0.49f);
            for (var i = 0; i < n; i++)
            {
                var a0 = i / (float)n * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Vector3 P(float r, float a) => new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                k.Quad(P(0.8f, a0), P(1f, a0), P(1f, a1), P(0.8f, a1), colour);
            }
            return k.ToMesh("Ripple ring");
        }
    }
}
