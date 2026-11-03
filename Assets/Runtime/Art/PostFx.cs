using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieGame.Art
{
    /// <summary>
    /// The screen look (2026-10-02): bloom so the toxic-green lights glow, neutral tonemapping, a little contrast and
    /// saturation, and a soft vignette. Built in code (no profile asset to lose). The low quality level drops the bloom
    /// for slow phones.
    /// </summary>
    public static class PostFx
    {
        private static Volume _volume;

        public static void Setup(Camera camera, bool lowQuality)
        {
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            if (_volume == null)
            {
                var go = new GameObject("Post-processing");
                Object.DontDestroyOnLoad(go);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 10f;
                _volume.sharedProfile = BuildProfile();
            }
            if (_volume.sharedProfile.TryGet<Bloom>(out var bloom)) bloom.active = !lowQuality;
        }

        private static VolumeProfile BuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.75f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(0.85f, 1f, 0.85f));
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(5);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(18f);
            color.postExposure.Override(0f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.14f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.02f, 0.05f, 0.03f));
            return profile;
        }
    }
}
