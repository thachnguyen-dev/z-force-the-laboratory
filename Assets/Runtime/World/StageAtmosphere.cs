using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// How a stage's conditions look and sound (owner, 2026-09-30):
    /// darkness — the world goes nearly black; each player carries a flashlight lighting only the area in front of them
    /// (owner, 2026-10-07: not around the head), and street lights light a small pool beneath them;
    /// rain — ⚠ placeholder streaks over the screen, a blue tint and a rain hiss;
    /// night rain (both; owner, 2026-10-07) — lightning at random moments lights up the map, and thunder follows.
    /// Lives on the stage root; restores the lighting when the stage ends. Endless mode changes the weather as it goes
    /// (<see cref="Set"/>).
    /// </summary>
    public class StageAtmosphere : MonoBehaviour
    {
        private ConditionsTuning _c;
        private bool _dark;
        private bool _rain;
        private Light _sun;
        private float _sunIntensity;
        private Color _ambient;
        private Color _background;
        private Camera _camera;
        private readonly Dictionary<Survivor, GameObject> _lamps = new Dictionary<Survivor, GameObject>();
        private AudioSource _rainSound;
        private AudioSource _nightSound;
        private Vector2[] _drops;
        private System.Random _rng;

        // Street lights: where each lamp head is, and the few real lights handed to the nearest ones.
        private readonly List<Vector3> _streetLamps = new List<Vector3>();
        private Light[] _streetPool;
        private float _streetCheckLeft;

        // Lightning.
        private float _nextStrike;
        private float _strikeTime = -100f;
        private float _thunderAt = -1f;
        private AudioSource _thunder;
        private static AudioClip _thunderClip;

        /// <summary>How loud the background loops play (PLACEHOLDER).</summary>
        public const float RainVolume = 0.4f, NightVolume = 0.35f;

        /// <summary>Lightning strikes so far (tests).</summary>
        public int Strikes { get; private set; }
        /// <summary>How bright the lightning is right now, 0 to 1.</summary>
        public float Flash { get; private set; }
        public bool NightRain => _dark && _rain;
        /// <summary>Street lights with a real light on now.</summary>
        public int StreetLightsLit
        {
            get
            {
                var n = 0;
                if (_streetPool != null) foreach (var l in _streetPool) if (l.enabled) n++;
                return n;
            }
        }

        public static StageAtmosphere Attach(Transform stageRoot, StageLayout layout, GameTuning tuning)
        {
            var atmosphere = stageRoot.gameObject.AddComponent<StageAtmosphere>();
            atmosphere._c = tuning.conditions;
            atmosphere._dark = layout.Dark;
            atmosphere._rain = layout.Rain;
            atmosphere._rng = new System.Random(layout.Name != null ? layout.Name.GetHashCode() : 1);
            atmosphere.FindStreetLamps(layout);
            atmosphere.Begin();
            return atmosphere;
        }

        private void Begin()
        {
            _ambient = RenderSettings.ambientLight;
            _sun = FindSun();
            if (_sun != null)
            {
                _sunIntensity = _sun.intensity;
                // Owner, 2026-10-07: buildings and props cast real shadows. The sun's direction is fixed: map meshes keep
                // only the faces facing it or the camera.
                _sun.transform.rotation = TileAssembler.SunRotation;
                _sun.shadowStrength = 1f;
            }
            _camera = Camera.main;
            if (_camera != null) _background = _camera.backgroundColor;
            _nextStrike = Time.time + NextGap();
            Look();
        }

        /// <summary>Endless mode (owner, 2026-10-07): day, rain, night and night rain in turn.</summary>
        public void Set(bool rain, bool dark)
        {
            if (rain == _rain && dark == _dark) return;
            _rain = rain;
            _dark = dark;
            RenderSettings.ambientLight = _ambient;
            if (_sun != null) _sun.intensity = _sunIntensity;
            if (_camera != null) _camera.backgroundColor = _background;
            Flash = 0f;
            _nextStrike = Time.time + NextGap();
            Look();
        }

        private float DarkSun => 0.02f;
        private Color DarkAmbient => new Color(_c.darkAmbient, _c.darkAmbient, _c.darkAmbient * 1.3f);

        private void Look()
        {
            foreach (var lamp in _lamps.Values)
                if (lamp != null) lamp.SetActive(_dark);
            if (!_dark && _streetPool != null)
                foreach (var l in _streetPool) l.enabled = false;
            _streetCheckLeft = 0f;
            if (_rainSound != null)
            {
                if (_rain && !_rainSound.isPlaying) _rainSound.Play();
                if (!_rain) _rainSound.Stop();
            }
            // Owner, 2026-10-08: the night has its own background sound (with the rain's too in night rain).
            if (_dark && _nightSound == null)
            {
                var night = Resources.Load<AudioClip>("Audio/AmbientNight");
                if (night != null)
                {
                    _nightSound = gameObject.AddComponent<AudioSource>();
                    _nightSound.clip = night;
                    _nightSound.loop = true;
                    _nightSound.volume = NightVolume * Sfx.Volume;
                }
            }
            if (_nightSound != null)
            {
                if (_dark && !_nightSound.isPlaying) _nightSound.Play();
                if (!_dark) _nightSound.Stop();
            }
            // No shadow map at night: the sun is all but off then.
            if (_sun != null) _sun.shadows = _dark ? LightShadows.None : LightShadows.Soft;
            if (_dark)
            {
                RenderSettings.ambientLight = DarkAmbient;
                if (_sun != null) _sun.intensity = DarkSun;
                if (_camera != null) _camera.backgroundColor = Color.black;
            }
            else if (_rain)
            {
                RenderSettings.ambientLight = _ambient * 0.8f;
                if (_sun != null) _sun.intensity = _sunIntensity * 0.6f;
            }
            if (_rain && _rainSound == null)
            {
                _rainSound = gameObject.AddComponent<AudioSource>();
                // The owner's rain (2026-10-08); the made-up hiss only if it is missing.
                var rain = Resources.Load<AudioClip>("Audio/AmbientRain");
                _rainSound.clip = rain != null ? rain : RainClip();
                _rainSound.loop = true;
                _rainSound.volume = (rain != null ? RainVolume : 0.25f) * Sfx.Volume;
                _rainSound.Play();
                var rng = new System.Random(3);
                _drops = new Vector2[140];
                for (var i = 0; i < _drops.Length; i++) _drops[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            }
        }

        private static Light FindSun()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) return light;
            return null;
        }

        private void OnDestroy()
        {
            if (_sun != null) _sun.shadows = LightShadows.None;
            RenderSettings.ambientLight = _ambient;
            if (_sun != null) _sun.intensity = _sunIntensity;
            if (_camera != null) _camera.backgroundColor = _background;
        }

        private void Update()
        {
            if (!_dark) return;
            GiveLamps();
            UpdateStreetLights();
            UpdateLightning();
        }

        // ------------------------------------------------------------------ the flashlight

        /// <summary>
        /// Every player (and the AI teammates) carries a flashlight; the hostage doesn't. Owner, 2026-10-07: it lights only
        /// the area in front of the character: it sits at chest height just ahead of the body, so the cone starts in front
        /// and nothing lights the head or the ground around the character.
        /// </summary>
        private void GiveLamps()
        {
            foreach (var s in Survivor.All)
            {
                if (s.IsHostage || _lamps.ContainsKey(s)) continue;
                var go = new GameObject("Flashlight");
                go.transform.SetParent(s.Visual, false);
                go.transform.localPosition = new Vector3(0f, _c.lampHeight, _c.lampAhead);
                go.transform.localRotation = Quaternion.Euler(_c.lampPitch, 0f, 0f);
                var lamp = go.AddComponent<Light>();
                lamp.type = LightType.Spot;
                lamp.range = _c.lampRange;
                lamp.spotAngle = _c.lampAngle;
                lamp.innerSpotAngle = _c.lampAngle * 0.6f;
                lamp.intensity = _c.lampIntensity;
                lamp.color = new Color(1f, 0.95f, 0.85f);
                lamp.shadows = LightShadows.None;
                _lamps[s] = go;
            }
        }

        // ------------------------------------------------------------------ street lights

        /// <summary>The lamp head of every street light on the map (the piece's head is 5.8 m up, 1.85 m out along its arm).</summary>
        private void FindStreetLamps(StageLayout layout)
        {
            var map = layout.Map;
            if (map == null || map.props == null) return;
            foreach (var p in map.props)
            {
                if (p.id != "street_light") continue;
                var scale = p.scale > 0f ? p.scale : 1f;
                _streetLamps.Add(new Vector3(p.x, 0f, p.z) + Quaternion.Euler(0f, p.rotation, 0f) * new Vector3(0f, 5.75f, 1.85f) * scale);
            }
        }

        /// <summary>A few times a second: the real lights go to the street lights nearest the view.</summary>
        private void UpdateStreetLights()
        {
            if (_streetLamps.Count == 0) return;
            _streetCheckLeft -= Time.deltaTime;
            if (_streetCheckLeft > 0f) return;
            _streetCheckLeft = 0.25f;
            if (_streetPool == null)
            {
                _streetPool = new Light[Mathf.Max(1, _c.streetLightPool)];
                for (var i = 0; i < _streetPool.Length; i++)
                {
                    var go = new GameObject("Street light");
                    go.transform.SetParent(transform, false);
                    go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.range = _c.streetLightRange;
                    l.spotAngle = _c.streetLightAngle;
                    l.innerSpotAngle = _c.streetLightAngle * 0.45f;
                    l.intensity = _c.streetLightIntensity;
                    l.color = new Color(1f, 0.78f, 0.45f);
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    _streetPool[i] = l;
                }
            }
            var focus = ViewCentre();
            _streetLamps.Sort((a, b) => Flat(a - focus).CompareTo(Flat(b - focus)));
            for (var i = 0; i < _streetPool.Length; i++)
            {
                var on = i < _streetLamps.Count && Flat(_streetLamps[i] - focus) < 30f * 30f;
                _streetPool[i].enabled = on;
                if (on) _streetPool[i].transform.position = _streetLamps[i];
            }
        }

        private static float Flat(Vector3 d) => d.x * d.x + d.z * d.z;

        private Vector3 ViewCentre()
        {
            var game = GameSession.Instance;
            if (game != null && game.LocalSurvivor != null) return game.LocalSurvivor.transform.position;
            return _camera != null ? _camera.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------ lightning and thunder

        private float NextGap() => Mathf.Lerp(_c.lightningMinSeconds, _c.lightningMaxSeconds, (float)(_rng ?? new System.Random()).NextDouble());

        /// <summary>Night rain only: lightning at random moments lights the whole map for a moment; thunder follows.</summary>
        private void UpdateLightning()
        {
            if (!_rain || (GameSession.Instance != null && !GameSession.Instance.Playing)) return;
            if (Time.time >= _nextStrike) Strike();
            // A double flash: bright, a flicker down, bright again, then fading.
            var t = Time.time - _strikeTime;
            Flash = t < 0.08f ? 1f : t < 0.16f ? 0.25f : t < 0.3f ? 0.85f : Mathf.Max(0f, 0.85f * (1f - (t - 0.3f) / 0.45f));
            RenderSettings.ambientLight = Color.Lerp(DarkAmbient, new Color(0.7f, 0.76f, 0.95f), Flash);
            if (_sun != null) _sun.intensity = Mathf.Lerp(DarkSun, Mathf.Max(0.8f, _sunIntensity), Flash);
            if (_camera != null) _camera.backgroundColor = Color.Lerp(Color.black, new Color(0.35f, 0.38f, 0.5f), Flash);
            if (_thunderAt >= 0f && Time.time >= _thunderAt)
            {
                _thunderAt = -1f;
                if (_thunder == null)
                {
                    _thunder = gameObject.AddComponent<AudioSource>();
                    _thunder.playOnAwake = false;
                }
                _thunder.PlayOneShot(ThunderClip(), 0.8f * Sfx.Volume);
            }
        }

        /// <summary>A lightning strike now; the thunder comes 0.3–2 s later (the storm a little way off).</summary>
        public void Strike()
        {
            Strikes++;
            _strikeTime = Time.time;
            _nextStrike = Time.time + NextGap();
            _thunderAt = Time.time + Mathf.Lerp(0.3f, 2f, (float)_rng.NextDouble());
        }

        private void OnGUI()
        {
            if (GameSession.Instance == null || !GameSession.Instance.Playing) return;
            if (Flash > 0.01f && _dark)
            {
                Gui.Begin();
                Gui.Fill(new Rect(0f, 0f, Gui.Width, Gui.Height), new Color(0.85f, 0.9f, 1f, 0.18f * Flash));
            }
            if (!_rain || _drops == null) return;
            Gui.Begin();
            var w = Gui.Width;
            var h = Gui.Height;
            Gui.Fill(new Rect(0f, 0f, w, h), new Color(0.2f, 0.25f, 0.35f, 0.12f));
            var t = Time.time;
            var color = new Color(0.75f, 0.8f, 0.9f, 0.35f);
            for (var i = 0; i < _drops.Length; i++)
            {
                var d = _drops[i];
                var y = (d.y + t * (1.3f + d.x * 0.6f)) % 1f;
                var x = (d.x + y * 0.08f) % 1f;
                Gui.Fill(new Rect(x * w, y * h, 1.5f, 14f), color);
            }
        }

        /// <summary>⚠ Placeholder rain: two seconds of soft filtered noise that loops.</summary>
        private static AudioClip RainClip()
        {
            const int rate = 22050;
            var samples = new float[rate * 2];
            var rng = new System.Random(11);
            var y = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var x = (float)rng.NextDouble() * 2f - 1f;
                y += 0.35f * (x - y);
                samples[i] = y * 0.6f;
            }
            var clip = AudioClip.Create("placeholder_rain", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Thunder: the owner's recording (Resources/Audio/thunder). Fallback: a sharp crack, then a deep rumble that rolls in a few swells and dies away over about four
        /// seconds (low-passed noise).
        /// </summary>
        private static AudioClip ThunderClip()
        {
            if (_thunderClip != null) return _thunderClip;
            // The owner's thunder recording (2026-10-07); the made-up rumble below only if it is missing.
            _thunderClip = Resources.Load<AudioClip>("Audio/thunder");
            if (_thunderClip != null) return _thunderClip;
            const int rate = 22050;
            var samples = new float[rate * 4];
            var rng = new System.Random(23);
            float lowA = 0f, lowB = 0f, crack = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var x = (float)rng.NextDouble() * 2f - 1f;
                // Two low-passes in a row for the rumble; a lighter one for the crack.
                lowA += 0.03f * (x - lowA);
                lowB += 0.03f * (lowA - lowB);
                crack += 0.5f * (x - crack);
                var crackEnv = Mathf.Exp(-t * 18f);
                var swell = 0.6f + 0.4f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 1.7f + 0.5f);
                var rumbleEnv = Mathf.Clamp01(t * 6f) * Mathf.Exp(-t * 0.9f) * swell;
                samples[i] = Mathf.Clamp(crack * crackEnv * 0.7f + lowB * rumbleEnv * 9f, -1f, 1f);
            }
            _thunderClip = AudioClip.Create("placeholder_thunder", samples.Length, 1, rate, false);
            _thunderClip.SetData(samples, 0);
            return _thunderClip;
        }
    }
}
