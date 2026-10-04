using System;
using UnityEngine;

namespace DeadAir
{
    // Shared by the menu, audio and first-person camera. Values persist between sessions.
    public static class DeadAirSettings
    {
        public static event Action Changed;
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
        public static int Quality { get => PlayerPrefs.GetInt("DeadAir.Quality", 360); set => SetInt("DeadAir.Quality", value); }
        static void SetFloat(string key, float value) { PlayerPrefs.SetFloat(key, value); Changed?.Invoke(); }
        static void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); Changed?.Invoke(); }
        public static void Save() => PlayerPrefs.Save();
    }
}
