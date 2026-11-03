using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Owner, 2026-10-04: a simple muzzle flash when a gun fires. A flat glowing star (white-hot centre, yellow-orange
    /// points) at the muzzle, shown for a few frames at a random size and twist. One per shooter, reused.
    /// </summary>
    public class MuzzleFlash : MonoBehaviour
    {
        private const float Duration = 0.05f;
        private static Mesh _mesh;
        private float _left;

        public static MuzzleFlash Attach(Transform shooter)
        {
            var go = new GameObject("Muzzle flash");
            go.transform.SetParent(shooter, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ArtMaterials.Stylized;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var flash = go.AddComponent<MuzzleFlash>();
            go.SetActive(false);
            return flash;
        }

        /// <param name="local">The muzzle, in the shooter's space.</param>
        /// <param name="size">Bigger for heavier guns.</param>
        public void Fire(Vector3 local, float size)
        {
            transform.localPosition = local;
            transform.localRotation = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
            transform.localScale = Vector3.one * size * Random.Range(0.85f, 1.15f);
            _left = Duration;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            _left -= Time.deltaTime;
            if (_left <= 0f) gameObject.SetActive(false);
        }

        /// <summary>A star pointing forward (+z): one long point ahead, shorter ones to the sides, flat and facing up.</summary>
        private static Mesh Mesh()
        {
            if (_mesh != null) return _mesh;
            var k = new MeshKit();
            var core = MeshKit.Glow(new Color(1f, 0.97f, 0.8f), 1f);
            var tip = MeshKit.Glow(new Color(1f, 0.62f, 0.15f), 1f);
            var lengths = new[] { 1f, 0.35f, 0.55f, 0.3f, 0.25f, 0.3f, 0.55f, 0.35f };
            const int n = 8;
            for (var i = 0; i < n; i++)
            {
                var a = i / (float)n * Mathf.PI * 2f;
                var a0 = a - Mathf.PI / n * 0.55f;
                var a1 = a + Mathf.PI / n * 0.55f;
                var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.5f * lengths[i];
                var s0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * 0.17f;
                var s1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * 0.17f;
                // Clockwise seen from above, so the face points up at the camera.
                k.Quad(Vector3.zero, s0, p, s1, core, core, tip, core);
            }
            _mesh = k.ToMesh("Muzzle flash");
            return _mesh;
        }
    }
}
