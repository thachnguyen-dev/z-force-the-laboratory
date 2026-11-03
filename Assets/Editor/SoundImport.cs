using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// The owner's weapon sounds (2026-10-04, D:\Game\assets for zombie\sound) into Resources/Audio/&lt;SoundId&gt;.wav, which
    /// Sfx plays instead of its synthesised placeholders. Each file is decoded, trailing silence is trimmed, and the AR
    /// and SMG shots are made much louder (owner: "the volume should be increased significantly") with a soft limiter so
    /// the boost doesn't clip harshly. The source files are kept as delivered.
    /// </summary>
    public static class SoundImport
    {
        public const string SourceFolder = "Assets/Zombie/Audio/Source";
        public const string OutFolder = "Assets/Zombie/Resources/Audio";

        /// <summary>Source file → sound id, and the loudness boost (1 = as delivered).</summary>
        /// <summary>
        /// Pieces cut from a source (seconds): one shell going into the shotgun and its pump, so the shotgun can load
        /// shell by shell (2026-10-05). Measured on the owner's file: a shell every 0.38 s from 0.50 s, the pump at 3.43 s.
        /// </summary>
        private static readonly (string file, SoundId id, float from, float to, float gain)[] Cuts =
        {
            ("shotgun_reload.wav", SoundId.ShotgunShell, 0.48f, 0.86f, 1f),
            ("shotgun_reload.wav", SoundId.ShotgunPump, 3.38f, 4.08f, 1f),
            // Owner, 2026-10-16: the zombies' groan from a CC0 recording (19 s): its strongest groan, at the start, about
            // as long as the old one.
            ("z_zombie_growling.wav", SoundId.ZombieGroan, 0.05f, 1.10f, GroanGain),
        };

        /// <summary>The new groan brought to the old one's loudness (measured 2026-10-16).</summary>
        public const float GroanGain = 0.8f;

        private static readonly (string file, SoundId id, float gain)[] Sounds =
        {
            ("pistol.wav", SoundId.PistolShot, 1f),
            // Owner, 2026-10-16: the shotgun, sniper, AR, bear and helicopter replaced with CC0 recordings (the earlier ones
            // were NonCommercial, or needed credit); each gain brings the new file to the old one's loudness.
            ("shotgun.wav", SoundId.ShotgunShot, 0.49f),
            ("AR.wav", SoundId.ArShot, ArSmgGain),
            ("smg.wav", SoundId.SmgShot, ArSmgGain * 1.8f),  // its file is quieter and already near full scale
            ("sniper.wav", SoundId.RifleShot, 2f),
            ("pistol_reload.wav", SoundId.PistolReload, 1f),
            ("shotgun_reload.wav", SoundId.ShotgunReload, 1f),
            ("AR and SMG_reload.mp3", SoundId.ArReload, 1f),
            ("sniper_reload.wav", SoundId.RifleReload, 1f),
            // Owner, 2026-10-08: the zombies' voices and their death (the footsteps were removed again the same day).
            ("z_die of humman zombie.wav", SoundId.ZombieDeath, DeathGain), // 2026-10-16: a CC0 recording
            ("z_boss01.wav", SoundId.BossGroan, 1f),
            ("z_bear.wav", SoundId.BearGroan, 0.73f),
            ("z_dog.wav", SoundId.DogGroan, 1f),
            ("z_rat.ogg", SoundId.RatGroan, 2f),           // delivered quiet (peak -10 dB)
            ("z_tiger.wav", SoundId.TigerGroan, 1f),
        };

        /// <summary>
        /// The background loops (owner, 2026-10-08): night and rain. Delivered as 4.5 and 6 minutes of 48 kHz stereo
        /// (about 230 MB together, over GitHub's file limit); converted outside Unity to mono 22 kHz 16-bit before going
        /// into the source folder. Streamed when played, brought up to a peak of -6 dB.
        /// </summary>
        private static readonly (string file, string name, float level)[] Ambient =
        {
            ("bg_night.wav", "AmbientNight", 1f),
            ("bg_rain.wav", "AmbientRain", 1f),
            // Owner, 2026-10-08: the helicopter (looped while its rotor turns); the 2026-10-16 recording is denser, so a
            // little lower to sound as loud as the old one.
            ("helicopter.wav", "HelicopterLoop", 0.72f),
        };

        /// <summary>About +10 dB before the limiter (PLACEHOLDER; the owner asked for a significant increase).</summary>
        public const float ArSmgGain = 3.2f;

        /// <summary>The 2026-10-16 death sound brought to the old one's loudness.</summary>
        public const float DeathGain = 2.4f;

        [MenuItem("Zombie/Developer (Claude)/Import Owner Sounds")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(OutFolder);
            foreach (var (file, id, gain) in Sounds)
            {
                var path = $"{SourceFolder}/{file}";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[ZOMBIE-SOUND] missing {path}");
                    continue;
                }
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                var (peak, rms) = Measure(data);
                if (gain != 1f)
                    for (var i = 0; i < data.Length; i++) data[i] = Limit(data[i] * gain);
                var trimmed = TrimTail(data, clip.channels, clip.frequency);
                var (peak2, rms2) = Measure(trimmed);
                var outPath = $"{OutFolder}/{id}.wav";
                WriteWav(outPath, trimmed, clip.channels, clip.frequency);
                AssetDatabase.ImportAsset(outPath);
                var outImporter = (AudioImporter)AssetImporter.GetAtPath(outPath);
                var s = outImporter.defaultSampleSettings;
                // Short, frequent sounds: decoded once, so firing never waits on decompression.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                outImporter.defaultSampleSettings = s;
                outImporter.forceToMono = true;
                outImporter.SaveAndReimport();
                Debug.Log($"[ZOMBIE-SOUND] {file} -> {id}: {clip.length:0.00}s -> {trimmed.Length / (float)clip.channels / clip.frequency:0.00}s, " +
                          $"peak {Db(peak):0.0} dB rms {Db(rms):0.0} dB -> peak {Db(peak2):0.0} dB rms {Db(rms2):0.0} dB");
            }
            foreach (var (file, name, level) in Ambient)
            {
                var path = $"{SourceFolder}/{file}";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[ZOMBIE-SOUND] missing {path}");
                    continue;
                }
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                var (peak, rms) = Measure(data);
                var gain = (peak > 1e-5f ? 0.5f / peak : 1f) * level;
                for (var i = 0; i < data.Length; i++) data[i] *= gain;
                var outPath = $"{OutFolder}/{name}.wav";
                WriteWav(outPath, data, clip.channels, clip.frequency);
                AssetDatabase.ImportAsset(outPath);
                var outImporter = (AudioImporter)AssetImporter.GetAtPath(outPath);
                var s = outImporter.defaultSampleSettings;
                // Long loops: streamed from storage, never decoded whole into memory.
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.45f;
                outImporter.defaultSampleSettings = s;
                outImporter.forceToMono = true;
                outImporter.SaveAndReimport();
                Debug.Log($"[ZOMBIE-SOUND] {file} -> {name}: {clip.length:0.0}s, peak {Db(peak):0.0} dB rms {Db(rms):0.0} dB, gain {Db(gain):+0.0;-0.0} dB");
            }
            foreach (var (file, id, from, to, gain) in Cuts)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SourceFolder}/{file}");
                if (clip == null) continue;
                clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                var ch = clip.channels;
                var a = Mathf.Clamp(Mathf.RoundToInt(from * clip.frequency), 0, clip.samples);
                var b = Mathf.Clamp(Mathf.RoundToInt(to * clip.frequency), a + 1, clip.samples);
                var cut = new float[(b - a) * ch];
                System.Array.Copy(data, a * ch, cut, 0, cut.Length);
                // Short fades so the cut never clicks.
                var fadeIn = clip.frequency / 200;
                var fadeOut = clip.frequency / 50;
                var frames = b - a;
                for (var i = 0; i < frames; i++)
                {
                    var k = Mathf.Min(1f, i / (float)fadeIn) * Mathf.Min(1f, (frames - i) / (float)fadeOut);
                    for (var c = 0; c < ch; c++) cut[i * ch + c] = gain != 1f ? Limit(cut[i * ch + c] * k * gain) : cut[i * ch + c] * k;
                }
                var outPath = $"{OutFolder}/{id}.wav";
                WriteWav(outPath, cut, ch, clip.frequency);
                AssetDatabase.ImportAsset(outPath);
                var outImporter = (AudioImporter)AssetImporter.GetAtPath(outPath);
                var s = outImporter.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                outImporter.defaultSampleSettings = s;
                outImporter.forceToMono = true;
                outImporter.SaveAndReimport();
                Debug.Log($"[ZOMBIE-SOUND] {file} {from:0.00}-{to:0.00}s -> {id}");
            }
            AssetDatabase.SaveAssets();
        }

        private static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(1e-6f, v));

        private static (float peak, float rms) Measure(float[] data)
        {
            float peak = 0f, sum = 0f;
            foreach (var v in data)
            {
                peak = Mathf.Max(peak, Mathf.Abs(v));
                sum += v * v;
            }
            return (peak, Mathf.Sqrt(sum / Mathf.Max(1, data.Length)));
        }

        /// <summary>Soft limiter: unchanged below 0.6, then eases towards ±0.98 instead of clipping.</summary>
        private static float Limit(float v)
        {
            var a = Mathf.Abs(v);
            if (a <= 0.6f) return v;
            var over = (a - 0.6f) / 0.38f;
            var eased = 0.6f + 0.38f * (float)System.Math.Tanh(over);
            return Mathf.Sign(v) * eased;
        }

        /// <summary>Cuts trailing near-silence (below -55 dB), keeping a short fade.</summary>
        private static float[] TrimTail(float[] data, int channels, int rate)
        {
            const float floor = 0.0018f;
            var last = data.Length - 1;
            while (last > 0 && Mathf.Abs(data[last]) < floor) last--;
            var end = Mathf.Min(data.Length, last + 1 + rate / 50 * channels);
            end -= end % channels;
            var result = new float[Mathf.Max(channels, end)];
            System.Array.Copy(data, result, result.Length);
            var fade = Mathf.Min(result.Length / channels, rate / 50);
            for (var i = 0; i < fade; i++)
                for (var ch = 0; ch < channels; ch++)
                    result[result.Length - (i + 1) * channels + ch] *= i / (float)fade;
            return result;
        }

        private static void WriteWav(string path, float[] data, int channels, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                var bytes = data.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (var v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }
    }
}
