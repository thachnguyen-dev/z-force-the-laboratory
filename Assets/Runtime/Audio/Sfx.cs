using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    public enum SoundId
    {
        PistolShot,
        ShotgunShot,
        ArShot,
        RifleShot,
        KnifeSwing,
        Explosion,
        Throw,
        ZombieGroan,
        ZombieAttack,
        ZombieDeath,
        PlayerHurt,
        PlayerDeath,
        Pickup,
        Drop,
        Heal,
        UiClick,
        // Owner's weapon sounds (2026-10-04); appended so the ids sent online stay the same.
        SmgShot,
        PistolReload,
        ShotgunReload,
        /// <summary>The AR and the SMG share one reload sound (owner).</summary>
        ArReload,
        RifleReload,
        /// <summary>One shotgun shell going in, and the pump at the end (cut from the owner's shotgun reload, 2026-10-05).</summary>
        ShotgunShell,
        ShotgunPump,
        // Owner's zombie voices and footsteps (2026-10-08); appended so the ids sent online stay the same.
        // ZombieGroan is the human zombies' voice, ZombieDeath their death.
        BossGroan,
        BearGroan,
        DogGroan,
        RatGroan,
        TigerGroan,
        /// <summary>Unused: the owner removed the footsteps (2026-10-08); kept so the ids sent online stay the same.</summary>
        Footstep,
    }

    /// <summary>
    /// ⚠ Placeholder audio: every sound is synthesised in code until the resource package arrives. Playback is 2D with
    /// simple distance falloff from the local player, through a small pool of sources.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float Audible = 45f;
        private static Sfx _instance;
        private static readonly Dictionary<SoundId, AudioClip> Clips = new Dictionary<SoundId, AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private int _next;

        public static float Volume = 1f;

        /// <summary>Plays a sound here and, on an online Host, on the other player's device too.</summary>
        public static void Play(SoundId id, Vector3 position, float volume = 1f)
        {
            NetSync.Sound(id, position, volume);
            // An online client hears game sounds only as the Host sends them.
            if (!World.Simulating) return;
            PlayLocal(id, position, volume);
        }

        /// <summary>Each zombie type's voice (owner, 2026-10-08): the human zombies share one, each animal and boss its own.</summary>
        public static SoundId VoiceOf(ZombieType type)
        {
            switch (type)
            {
                case ZombieType.Tank:
                case ZombieType.Boss:
                case ZombieType.SubBoss: return SoundId.BossGroan;
                case ZombieType.Bear: return SoundId.BearGroan;
                case ZombieType.Dog: return SoundId.DogGroan;
                case ZombieType.Rat: return SoundId.RatGroan;
                case ZombieType.Tiger: return SoundId.TigerGroan;
                default: return SoundId.ZombieGroan;
            }
        }

        /// <summary>How a zombie sounds dying: the human zombies their death cry, the animals and bosses their own voice.</summary>
        public static SoundId DeathOf(ZombieType type) => VoiceOf(type) == SoundId.ZombieGroan ? SoundId.ZombieDeath : VoiceOf(type);

        /// <summary>Menu clicks: local only, never sent to the other player.</summary>
        public static void PlayUi() => PlayLocal(SoundId.UiClick, Vector3.zero, 0.5f);

        /// <summary>Plays here only (an online client uses this for sounds the Host sent).</summary>
        public static void PlayLocal(SoundId id, Vector3 position, float volume)
        {
            if (_instance == null)
            {
                if (!Application.isPlaying) return;
                var go = new GameObject("Sfx");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Sfx>();
            }
            var listener = GameSession.Instance != null && GameSession.Instance.LocalSurvivor != null
                ? GameSession.Instance.LocalSurvivor.transform.position
                : position;
            var falloff = Mathf.Clamp01(1f - Vector3.Distance(listener, position) / Audible);
            var v = volume * falloff * Volume;
            if (v <= 0.01f) return;
            _instance.PlayClip(Clip(id), v);
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (_sources.Count < 16)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources.Add(source);
            }
            var s = _sources[_next];
            _next = (_next + 1) % 16;
            if (_next >= _sources.Count) _next = 0;
            s.pitch = Random.Range(0.94f, 1.06f);
            s.PlayOneShot(clip, volume);
        }

        private static AudioClip Clip(SoundId id)
        {
            if (Clips.TryGetValue(id, out var clip) && clip != null) return clip;
            // The owner's sounds (Resources/Audio/<id>, made by the editor's Import Owner Sounds) win over placeholders.
            clip = Resources.Load<AudioClip>($"Audio/{id}");
            if (clip != null)
            {
                Clips[id] = clip;
                return clip;
            }
            var samples = Synthesize(id);
            clip = AudioClip.Create($"placeholder_{id}", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Clips[id] = clip;
            return clip;
        }

        internal static float[] Synthesize(SoundId id)
        {
            var rng = new System.Random((int)id * 7919 + 17);
            switch (id)
            {
                case SoundId.PistolShot: return Mix(Noise(rng, 0.18f, 0.035f, 0.5f, 0.9f), Tone(160f, 0.18f, 0.03f, 0.6f));
                case SoundId.ShotgunShot: return Mix(Noise(rng, 0.45f, 0.11f, 0.25f, 1.2f), Tone(70f, 0.45f, 0.1f, 0.9f));
                case SoundId.ArShot: return Mix(Noise(rng, 0.14f, 0.03f, 0.45f, 0.8f), Tone(130f, 0.14f, 0.025f, 0.5f));
                case SoundId.RifleShot: return Mix(Noise(rng, 0.6f, 0.14f, 0.35f, 1.2f), Tone(90f, 0.6f, 0.12f, 0.8f), Tone(55f, 0.6f, 0.2f, 0.5f));
                case SoundId.KnifeSwing: return Noise(rng, 0.15f, 0.05f, 0.8f, 0.45f, attack: 0.04f);
                case SoundId.Explosion: return Mix(Noise(rng, 1.1f, 0.35f, 0.08f, 1.4f), Sweep(80f, 30f, 1.1f, 0.4f, 1f));
                case SoundId.Throw: return Noise(rng, 0.2f, 0.07f, 0.5f, 0.35f, attack: 0.05f);
                case SoundId.ZombieGroan: return Mix(Sweep(95f, 70f, 0.9f, 0.5f, 0.6f, attack: 0.2f), Noise(rng, 0.9f, 0.5f, 0.05f, 0.2f, attack: 0.2f));
                case SoundId.ZombieAttack: return Mix(Sweep(140f, 90f, 0.35f, 0.15f, 0.6f, attack: 0.03f), Noise(rng, 0.35f, 0.12f, 0.15f, 0.4f));
                case SoundId.ZombieDeath: return Sweep(120f, 45f, 0.6f, 0.3f, 0.7f, attack: 0.02f);
                case SoundId.PlayerHurt: return Mix(Tone(210f, 0.2f, 0.08f, 0.5f), Noise(rng, 0.2f, 0.05f, 0.2f, 0.5f));
                case SoundId.PlayerDeath: return Sweep(260f, 70f, 1f, 0.5f, 0.7f);
                case SoundId.Pickup: return Mix(Tone(660f, 0.12f, 0.05f, 0.35f), Tone(990f, 0.12f, 0.04f, 0.25f));
                case SoundId.Drop: return Mix(Tone(180f, 0.12f, 0.04f, 0.4f), Noise(rng, 0.12f, 0.03f, 0.2f, 0.3f));
                case SoundId.Heal: return Sweep(420f, 820f, 0.35f, 0.2f, 0.35f, attack: 0.03f);
                default: return Tone(880f, 0.06f, 0.02f, 0.3f);
            }
        }

        // ---- Tiny synth ----

        private static float Envelope(float t, float attack, float decay) =>
            (attack > 0f && t < attack ? t / attack : 1f) * Mathf.Exp(-Mathf.Max(0f, t - attack) / Mathf.Max(0.001f, decay));

        private static float[] Noise(System.Random rng, float length, float decay, float lowpass, float amp, float attack = 0.002f)
        {
            var n = (int)(length * SampleRate);
            var s = new float[n];
            var y = 0f;
            for (var i = 0; i < n; i++)
            {
                var x = (float)rng.NextDouble() * 2f - 1f;
                y += lowpass * (x - y);
                s[i] = y * amp * Envelope(i / (float)SampleRate, attack, decay);
            }
            return s;
        }

        private static float[] Tone(float hz, float length, float decay, float amp) => Sweep(hz, hz, length, decay, amp);

        private static float[] Sweep(float fromHz, float toHz, float length, float decay, float amp, float attack = 0.002f)
        {
            var n = (int)(length * SampleRate);
            var s = new float[n];
            var phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var hz = Mathf.Lerp(fromHz, toHz, t / length);
                phase += 2f * Mathf.PI * hz / SampleRate;
                s[i] = Mathf.Sin(phase) * amp * Envelope(t, attack, decay);
            }
            return s;
        }

        private static float[] Mix(params float[][] parts)
        {
            var n = 0;
            foreach (var p in parts) n = Mathf.Max(n, p.Length);
            var s = new float[n];
            foreach (var p in parts)
                for (var i = 0; i < p.Length; i++) s[i] += p[i];
            for (var i = 0; i < n; i++) s[i] = Mathf.Clamp(s[i], -1f, 1f);
            return s;
        }
    }
}
