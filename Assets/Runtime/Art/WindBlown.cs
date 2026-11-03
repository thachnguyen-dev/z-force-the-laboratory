using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame.Art
{
    /// <summary>
    /// Wind-blown leaves and paper (owner, 2026-10-05: they mustn't stay still; now and then the wind blows them). Each
    /// leaf or sheet is its own small object sharing one mesh. They lie still most of the time; every 5–12 s a gust
    /// sweeps across the map from one side, and each bit it reaches lifts, tumbles and skids 1–3 m downwind, then
    /// settles flat again. The wind's direction drifts slowly. Bits stop at walls and cover, and never stray more than
    /// 7 m from where the stage placed them: one that has, and is off screen, is put back quietly. Normal stages only:
    /// the stage generator places them only when it isn't raining or dark, and they're skipped at runtime there too.
    /// </summary>
    public class WindBlown : MonoBehaviour
    {
        private class Bit
        {
            public Transform T;
            public Renderer Renderer;
            public bool Paper;
            public Vector3 Home;
            public float HomeYaw;
            public float Yaw;
            public float Start = -1f, Length;
            public float Distance, Hop, Spin, Flips, Side;
            public Vector3 Dir;
            public bool Moving;
        }

        /// <summary>How far a bit may stray from its spot (m).</summary>
        public const float Leash = 7f;
        private const float Lift = 0.014f;

        private static WindBlown _instance;
        private readonly List<Bit> _bits = new List<Bit>();
        private float _nextGust;
        private float _windAngle;
        private bool _everyBit;

        /// <summary>A gust right now (tests and checks); <paramref name="everyBit"/> lifts every bit instead of most.</summary>
        public static void GustNow(bool everyBit = false)
        {
            if (_instance == null) return;
            _instance._everyBit = everyBit;
            _instance._nextGust = 0f;
        }

        /// <summary>Builds one wind decoration's bits under <paramref name="root"/> and hands them to the wind.</summary>
        public static void Place(Transform root, MapDecoration d, int seed)
        {
            if (_instance == null)
            {
                var go = new GameObject("Wind");
                go.transform.SetParent(root, false);
                _instance = go.AddComponent<WindBlown>();
                _instance._windAngle = Random.Range(0f, 360f);
                _instance._nextGust = Time.time + Random.Range(2f, 5f);
            }
            var paper = d.id == "wind_paper";
            var turn = Quaternion.Euler(0f, d.rotation, 0f);
            var scale = d.scale > 0f ? d.scale : 1f;
            for (var i = 0; i < ArtLibrary.WindBits(d.id, seed); i++)
            {
                var (at, yaw, variant) = ArtLibrary.WindBit(d.id, seed, i);
                var go = new GameObject(paper ? "Paper" : "Leaf");
                go.transform.SetParent(root, false);
                var home = new Vector3(d.x, Lift + i * 0.0008f, d.z) + turn * (at * scale);
                var bitYaw = yaw + d.rotation;
                go.transform.SetPositionAndRotation(home, Quaternion.Euler(0f, bitYaw, 0f));
                go.transform.localScale = Vector3.one * (paper ? 1.15f : 1.6f);
                go.AddComponent<MeshFilter>().sharedMesh = paper ? ArtLibrary.PaperMesh(variant) : ArtLibrary.LeafMesh(variant);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ArtMaterials.Stylized;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _instance._bits.Add(new Bit { T = go.transform, Renderer = renderer, Paper = paper, Home = home, HomeYaw = bitYaw, Yaw = bitYaw });
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            var now = Time.time;
            // The wind's direction wanders slowly.
            _windAngle += (Mathf.PerlinNoise(now * 0.05f, 0.37f) - 0.5f) * 40f * Time.deltaTime;
            if (now >= _nextGust) Gust(now);
            foreach (var b in _bits)
                if (b.Moving) Blow(b, now);
        }

        /// <summary>A gust: a front sweeping downwind at about 9 m/s; each bit it reaches gets its own push.</summary>
        private void Gust(float now)
        {
            _nextGust = now + Random.Range(5f, 12f);
            var dir = Quaternion.Euler(0f, _windAngle + Random.Range(-25f, 25f), 0f) * Vector3.forward;
            var strength = Random.Range(0.6f, 1.15f);
            var min = float.MaxValue;
            foreach (var b in _bits) min = Mathf.Min(min, Vector3.Dot(b.T.position, dir));
            foreach (var b in _bits)
            {
                if (b.Moving) continue;
                var pos = b.T.position;
                var away = new Vector3(pos.x - b.Home.x, 0f, pos.z - b.Home.z);
                // Strayed too far: put it back when nobody sees; otherwise the wind leaves it be this time.
                if (away.magnitude > Leash)
                {
                    if (!b.Renderer.isVisible) Reset(b);
                    continue;
                }
                // Not every bit lifts every time: light paper goes more often than leaves.
                if (!_everyBit && Random.value > (b.Paper ? 0.85f : 0.7f)) continue;
                b.Dir = Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f) * dir;
                b.Start = now + (Vector3.Dot(pos, dir) - min) / 9f + Random.Range(0f, 0.35f);
                b.Length = Random.Range(0.9f, 1.8f) * (b.Paper ? 1.2f : 1f);
                b.Distance = Random.Range(1f, 3f) * strength * (b.Paper ? 1.2f : 1f);
                b.Hop = Random.Range(0.08f, b.Paper ? 0.55f : 0.35f) * strength;
                b.Spin = Random.Range(-360f, 360f);
                b.Flips = b.Paper ? Random.Range(1, 3) : Random.Range(1, 4);
                b.Side = Random.Range(-0.4f, 0.4f);
                b.Moving = true;
            }
            _everyBit = false;
        }

        private void Blow(Bit b, float now)
        {
            if (now < b.Start) return;
            var u = Mathf.Clamp01((now - b.Start) / b.Length);
            // Speed rises and falls through the push; the bit skids, hops and tumbles, then lands flat.
            var push = Mathf.Sin(u * Mathf.PI);
            var step = b.Distance * Mathf.PI * 0.5f * push * Time.deltaTime / b.Length;
            var sideways = Vector3.Cross(Vector3.up, b.Dir) * (b.Side * Mathf.Cos(u * Mathf.PI * 2f));
            var pos = b.T.position;
            var next = pos + (b.Dir + sideways) * step;
            if (Blocked(next) || new Vector2(next.x - b.Home.x, next.z - b.Home.z).magnitude > Leash) next = pos;
            b.Yaw += b.Spin * push * Time.deltaTime;
            var height = Lift + b.Hop * push + (b.Paper ? 0.04f * Mathf.Sin(u * Mathf.PI * 6f) * push : 0f);
            next.y = height;
            var tumble = Mathf.Sin(u * Mathf.PI * 2f * b.Flips) * (b.Paper ? 70f : 50f) * push;
            var axis = Vector3.Cross(Vector3.up, b.Dir);
            b.T.SetPositionAndRotation(next, Quaternion.AngleAxis(tumble, axis) * Quaternion.Euler(0f, b.Yaw, 0f));
            if (u >= 1f)
            {
                b.Moving = false;
                b.T.SetPositionAndRotation(new Vector3(next.x, b.Home.y, next.z), Quaternion.Euler(0f, b.Yaw, 0f));
            }
        }

        private static void Reset(Bit b)
        {
            b.Yaw = b.HomeYaw;
            b.T.SetPositionAndRotation(b.Home, Quaternion.Euler(0f, b.HomeYaw, 0f));
        }

        /// <summary>Walls, cover, the edge, and the map's outside stop a bit.</summary>
        private static bool Blocked(Vector3 p)
        {
            var map = StageGrid.Map;
            if (map == null) return false;
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z);
            if (x < 0 || z < 0 || x >= map.width || z >= map.height) return true;
            return Tiles.IsSolid(map[x, z]);
        }
    }
}
