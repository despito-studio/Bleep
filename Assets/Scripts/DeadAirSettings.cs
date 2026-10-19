using System;
using UnityEngine;

namespace DeadAir
{
    // Shared by the menu, audio and first-person camera. Values persist between sessions.
    public static class DeadAirSettings
    {
        public static event Action Changed;
        static bool applyingPreset, presetChanged;
        public static bool ArtReviewReflectionsOverride { get; } = ResolveArtReviewReflections(Environment.GetCommandLineArgs());
        public static bool ResolveArtReviewReflections(string[] arguments) => arguments != null &&
            Array.IndexOf(arguments, "-deadAirBlackpineCapture") >= 0 && Array.IndexOf(arguments, "-deadAirArtReviewReflections") >= 0;
        public static bool Fullscreen { get => PlayerPrefs.GetInt("DeadAir.Fullscreen", 1) != 0; set { SetInt("DeadAir.Fullscreen", value ? 1 : 0); ApplyDisplay(); } }
        public static void ApplyDisplay()
        {
            if(Application.isEditor || Application.isBatchMode)return;
            var display=Screen.currentResolution;
            Screen.SetResolution(Fullscreen?display.width:Mathf.Min(1280,display.width),Fullscreen?display.height:Mathf.Min(720,display.height),
                Fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        }
        public static float Volume { get => PlayerPrefs.GetFloat("DeadAir.Volume", .5f); set => SetFloat("DeadAir.Volume", Mathf.Clamp01(value)); }
        public static float AmbienceVolume { get => PlayerPrefs.GetFloat("DeadAir.AmbienceVolume", 1); set => SetFloat("DeadAir.AmbienceVolume", Mathf.Clamp01(value)); }
        public static float EffectsVolume { get => PlayerPrefs.GetFloat("DeadAir.EffectsVolume", 1); set => SetFloat("DeadAir.EffectsVolume", Mathf.Clamp01(value)); }
        public static float Sensitivity { get => PlayerPrefs.GetFloat("DeadAir.MouseSensitivity", 1); set => SetFloat("DeadAir.MouseSensitivity", Mathf.Clamp(value, .3f, 2)); }
        public static bool Muted { get => PlayerPrefs.GetInt("DeadAir.Muted", 0) != 0; set => SetInt("DeadAir.Muted", value ? 1 : 0); }
        // A fresh opt-in key also clears the old always-on VHS default for existing saves.
        public static bool Grain { get => PlayerPrefs.GetInt("DeadAir.FilmGrain", 0) != 0; set => SetInt("DeadAir.FilmGrain", value ? 1 : 0); }
        public static bool Flicker { get => PlayerPrefs.GetInt("DeadAir.Flicker", 1) != 0; set => SetInt("DeadAir.Flicker", value ? 1 : 0); }
        public static int GraphicsQuality { get => Mathf.Clamp(PlayerPrefs.GetInt("DeadAir.GraphicsQuality", 2),0,2); set => SetInt("DeadAir.GraphicsQuality",Mathf.Clamp(value,0,2)); }
        public static float RenderScale { get => Mathf.Clamp(PlayerPrefs.GetFloat("DeadAir.RenderScale",1),.5f,1); set => SetFloat("DeadAir.RenderScale",Mathf.Clamp(value,.5f,1)); }
        public static bool AmbientOcclusion { get => PlayerPrefs.GetInt("DeadAir.AmbientOcclusion",1)!=0; set => SetInt("DeadAir.AmbientOcclusion",value?1:0); }
        public static bool Bloom { get => PlayerPrefs.GetInt("DeadAir.Bloom",1)!=0; set => SetInt("DeadAir.Bloom",value?1:0); }
        public static bool Antialiasing { get => PlayerPrefs.GetInt("DeadAir.Antialiasing",1)!=0; set => SetInt("DeadAir.Antialiasing",value?1:0); }
        public static bool Reflections { get => ArtReviewReflectionsOverride || PlayerPrefs.GetInt("DeadAir.Reflections",1)!=0; set => SetInt("DeadAir.Reflections",value?1:0); }
        public static float Brightness { get => Mathf.Clamp(PlayerPrefs.GetFloat("DeadAir.Brightness",1),.8f,1.2f); set => SetFloat("DeadAir.Brightness",Mathf.Clamp(value,.8f,1.2f)); }
        public static int ShadowQuality { get => Mathf.Clamp(PlayerPrefs.GetInt("DeadAir.ShadowQuality",2),0,2); set => SetInt("DeadAir.ShadowQuality",Mathf.Clamp(value,0,2)); }
        public static void ApplyGraphicsPreset(int quality)
        {
            quality=Mathf.Clamp(quality,0,2);
            applyingPreset=true;
            try
            {
            GraphicsQuality=quality;
            RenderScale=quality==0?.65f:quality==1?.85f:1;
            ShadowQuality=quality;
            AmbientOcclusion=quality>0;
            Bloom=quality>0;
            Antialiasing=true;
            Reflections=quality>0;
            }
            finally
            {
                applyingPreset=false;
                if(presetChanged) { presetChanged=false; Changed?.Invoke(); }
            }
        }
        static void SetFloat(string key, float value)
        {
            if(PlayerPrefs.HasKey(key) && PlayerPrefs.GetFloat(key)==value)return;
            PlayerPrefs.SetFloat(key, value); NotifyChanged();
        }
        static void SetInt(string key, int value)
        {
            if(PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key)==value)return;
            PlayerPrefs.SetInt(key, value); NotifyChanged();
        }
        static void NotifyChanged()
        {
            if(applyingPreset)presetChanged=true;
            else Changed?.Invoke();
        }
        public static void Save() => PlayerPrefs.Save();
    }
}
