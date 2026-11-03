using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The team's helicopter (owner, 2026-10-08): at the start it comes down from above with the players aboard, its rotor
    /// spinning, and lands on the pad; they step out. When the mission is done, its rotor spins up again and every living
    /// player must get back to it; then it takes off and the stage is complete. The owner's model is one mesh; its rotor
    /// (the hub and four blades on top, separate pieces of the mesh) is split off here and spun about the hub, so no rig
    /// is needed. Each device runs the landing itself; the take-off is the Host's (sent in the snapshot).
    /// </summary>
    public class Helicopter : MonoBehaviour
    {
        public enum Phase : byte { Parked, Landing, Landed, Ready, TakingOff, Gone }

        public static Helicopter Current { get; private set; }

        /// <summary>The players are in the helicopter (landing, taking off, flown away): hidden, untouchable, not playing.</summary>
        public static bool Aboard => Current != null && (Current.State == Phase.Landing || Current.State == Phase.TakingOff || Current.State == Phase.Gone);

        public const float LandTime = 4f;
        public const float TakeOffTime = 4f;
        /// <summary>How high it starts above the pad, how far it flies off (forward) while climbing. PLACEHOLDER.</summary>
        public const float Height = 28f;
        public const float FlyOff = 18f;
        /// <summary>Rotor speed in flight (degrees a second), and how long it takes to wind down after landing.</summary>
        public const float RotorFlight = 1260f;
        public const float WindDown = 3f;

        public Phase State { get; private set; } = Phase.Parked;
        /// <summary>How far through the current landing or take-off (0-1).</summary>
        public float Progress => State == Phase.Landing ? Mathf.Clamp01(_t / LandTime) : State == Phase.TakingOff ? Mathf.Clamp01(_t / TakeOffTime) : State == Phase.Gone ? 1f : 0f;

        private Transform _rotor;
        private AudioSource _sound;

        /// <summary>The engine and rotor sound (owner's, 2026-10-08): loudest in flight, heard this far. PLACEHOLDER.</summary>
        public const float SoundVolume = 0.8f, SoundRange = 70f;
        private Vector3 _ground;
        private Vector3 _nose;
        private float _t;
        private float _spin;
        private float _rotorAngle;

        /// <summary>
        /// The helicopter object at <paramref name="ground"/>: the model placed by <paramref name="place"/> (turn, pose, size)
        /// split into its body (static map art: only the faces the camera or the sun reach) and its rotor (every face, it spins).
        /// </summary>
        public static Helicopter Create(Transform root, Mesh model, Matrix4x4 place, Vector3 ground, float yaw)
        {
            var go = new GameObject("Model helicopter");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(ground, Quaternion.identity);
            var heli = go.AddComponent<Helicopter>();
            heli._ground = ground;
            heli._nose = Quaternion.Euler(0f, yaw, 0f) * Vector3.left;
            var (body, rotor, hub) = Split(model, place);
            Show(go.transform, "Body", body, Vector3.zero);
            if (rotor != null) heli._rotor = Show(go.transform, "Rotor", rotor, hub);
            Current = heli;
            return heli;
        }

        private static Transform Show(Transform parent, string name, Mesh mesh, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Art.ArtMaterials.Stylized;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>
        /// The model's separate pieces (joined by triangles, and by vertices at the same spot); those whose centre is in the
        /// top 15 % of its height are the rotor. Returns the body, the rotor around its hub (null if there is none), and the hub.
        /// </summary>
        public static (Mesh body, Mesh rotor, Vector3 hub) Split(Mesh model, Matrix4x4 place)
        {
            var v = model.vertices;
            var t = model.triangles;
            var world = new Vector3[v.Length];
            for (var i = 0; i < v.Length; i++) world[i] = place.MultiplyPoint3x4(v[i]);
            var parent = new int[v.Length];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }
            void Join(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) parent[a] = b;
            }
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                Join(t[i], t[i + 1]);
                Join(t[i], t[i + 2]);
            }
            var spot = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 10000f);
                if (spot.TryGetValue(k, out var o)) Join(i, o);
                else spot[k] = i;
            }
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in world)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            var sums = new Dictionary<int, (Vector3 sum, int n)>();
            for (var i = 0; i < v.Length; i++)
            {
                var r = Find(i);
                sums.TryGetValue(r, out var s);
                sums[r] = (s.sum + world[i], s.n + 1);
            }
            var cut = minY + (maxY - minY) * 0.85f;
            var isRotor = new bool[v.Length];
            var rotorBounds = new Bounds();
            var any = false;
            for (var i = 0; i < v.Length; i++)
            {
                var s = sums[Find(i)];
                if (s.sum.y / s.n <= cut) continue;
                isRotor[i] = true;
                if (!any) rotorBounds = new Bounds(world[i], Vector3.zero);
                else rotorBounds.Encapsulate(world[i]);
                any = true;
            }
            var hub = any ? new Vector3(rotorBounds.center.x, rotorBounds.min.y, rotorBounds.center.z) : Vector3.zero;
            var body = Part(model, place, i => !isRotor[i], Vector3.zero, true, "Helicopter body");
            var rotor = any ? Part(model, place, i => isRotor[i], hub, false, "Helicopter rotor") : null;
            return (body, rotor, hub);
        }

        /// <summary>The triangles whose vertices all pass <paramref name="keep"/>, placed, moved so <paramref name="origin"/> is 0.</summary>
        private static Mesh Part(Mesh model, Matrix4x4 place, System.Func<int, bool> keep, Vector3 origin, bool cull, string name)
        {
            var v = model.vertices;
            var n = model.normals;
            var c = model.colors;
            var t = model.triangles;
            var uv = new List<Vector4>[4];
            for (var ch = 0; ch < 4; ch++)
            {
                if (!model.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch)) continue;
                uv[ch] = new List<Vector4>();
                model.GetUVs(ch, uv[ch]);
                if (uv[ch].Count != v.Length) uv[ch] = null;
            }
            var normalMatrix = place.inverse.transpose;
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector4>[4];
            for (var ch = 0; ch < 4; ch++) if (uv[ch] != null) uvs[ch] = new List<Vector4>();
            var tris = new List<int>();
            int Index(int i)
            {
                if (map.TryGetValue(i, out var j)) return j;
                j = verts.Count;
                map[i] = j;
                verts.Add(place.MultiplyPoint3x4(v[i]) - origin);
                norms.Add(n.Length == v.Length ? normalMatrix.MultiplyVector(n[i]).normalized : Vector3.up);
                if (c.Length == v.Length) cols.Add(c[i]);
                for (var ch = 0; ch < 4; ch++) if (uvs[ch] != null) uvs[ch].Add(uv[ch][i]);
                return j;
            }
            for (var i = 0; i + 2 < t.Length; i += 3)
            {
                if (!keep(t[i]) || !keep(t[i + 1]) || !keep(t[i + 2])) continue;
                if (cull)
                {
                    Vector3 a = place.MultiplyPoint3x4(v[t[i]]), b = place.MultiplyPoint3x4(v[t[i + 1]]), d = place.MultiplyPoint3x4(v[t[i + 2]]);
                    if (!Art.MeshCull.Keep(Vector3.Cross(b - a, d - a))) continue;
                }
                tris.Add(Index(t[i]));
                tris.Add(Index(t[i + 1]));
                tris.Add(Index(t[i + 2]));
            }
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            if (cols.Count == verts.Count) mesh.SetColors(cols);
            for (var ch = 0; ch < 4; ch++) if (uvs[ch] != null) mesh.SetUVs(ch, uvs[ch]);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>The start: from above, the players aboard.</summary>
        public void BeginLanding()
        {
            State = Phase.Landing;
            _t = 0f;
            _spin = RotorFlight;
            Place();
        }

        /// <summary>The mission is done: the rotor spins up, waiting for the players.</summary>
        public void MakeReady()
        {
            if (State == Phase.Parked || State == Phase.Landed) State = Phase.Ready;
        }

        public void BeginTakeOff()
        {
            if (State == Phase.TakingOff || State == Phase.Gone) return;
            State = Phase.TakingOff;
            _t = 0f;
        }

        /// <summary>Online friend: the Host's helicopter as it stands.</summary>
        public void ApplyReplica(Phase phase, float progress)
        {
            if (phase == Phase.Ready) MakeReady();
            if (phase == Phase.TakingOff || phase == Phase.Gone)
            {
                BeginTakeOff();
                _t = Mathf.Max(_t, progress * TakeOffTime);
            }
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            switch (State)
            {
                case Phase.Landing:
                    _t += dt;
                    _spin = RotorFlight;
                    if (_t >= LandTime) State = Phase.Landed;
                    break;
                case Phase.Landed:
                case Phase.Parked:
                    _spin = Mathf.MoveTowards(_spin, 0f, RotorFlight / WindDown * dt);
                    break;
                case Phase.Ready:
                    _spin = Mathf.MoveTowards(_spin, RotorFlight * 0.8f, RotorFlight / 2f * dt);
                    break;
                case Phase.TakingOff:
                    _t += dt;
                    _spin = Mathf.MoveTowards(_spin, RotorFlight, RotorFlight * dt);
                    if (_t >= TakeOffTime) State = Phase.Gone;
                    break;
            }
            Place();
            UpdateSound();
            if (_rotor != null)
            {
                _rotorAngle = Mathf.Repeat(_rotorAngle + _spin * dt, 360f);
                _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);
            }
        }

        /// <summary>
        /// The owner's helicopter sound, looping while the rotor turns: as loud and as high as the rotor is fast (it dies
        /// away as the rotor winds down after landing, and comes back when it spins up to leave), quieter with distance.
        /// </summary>
        private void UpdateSound()
        {
            var turning = _spin / RotorFlight;
            if (_sound == null)
            {
                if (turning < 0.01f) return;
                var clip = Resources.Load<AudioClip>("Audio/HelicopterLoop");
                if (clip == null) return;
                _sound = gameObject.AddComponent<AudioSource>();
                _sound.clip = clip;
                _sound.loop = true;
                _sound.spatialBlend = 0f;
                _sound.playOnAwake = false;
            }
            var game = GameSession.Instance;
            var listener = game != null && game.LocalSurvivor != null ? game.LocalSurvivor.transform.position : transform.position;
            var near = Mathf.Clamp01(1f - Vector3.Distance(listener, transform.position) / SoundRange);
            _sound.volume = SoundVolume * Sfx.Volume * Mathf.Clamp01(turning * 1.2f) * near;
            _sound.pitch = Mathf.Lerp(0.7f, 1f, turning);
            if (turning > 0.01f && !_sound.isPlaying) _sound.Play();
            else if (turning <= 0.01f && _sound.isPlaying) _sound.Stop();
        }

        /// <summary>Where it is: coming down (slowing near the ground), on the pad, or climbing away nose first.</summary>
        private void Place()
        {
            var p = _ground;
            if (State == Phase.Landing)
            {
                var k = Mathf.Clamp01(_t / LandTime);
                var left = 1f - k;
                p += Vector3.up * (Height * left * left);
            }
            else if (State == Phase.TakingOff || State == Phase.Gone)
            {
                var k = Mathf.Clamp01(_t / TakeOffTime);
                p += Vector3.up * (Height * k * k) + _nose * (FlyOff * k * k * k);
            }
            transform.position = p;
        }
    }
}
