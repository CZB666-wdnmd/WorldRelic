using System;
using UnityEngine;

namespace AniqueMuseum
{
    public enum AndroidXRMode
    {
        Auto,
        ForceFlat,
        ForceXR
    }

    /// <summary>Reads preferences written by the native Android settings Activity.</summary>
    public static class AndroidNativeSettings
    {
        public const string PreferencesName = "worldrelic_settings";
        public const string WorldVersionKey = "world_version";
        public const string XRModeKey = "xr_mode";
        public const string XRQualityKey = "xr_quality";
        public static int WorldVersion { get; private set; } = 135;
        public static AndroidXRMode XRMode { get; private set; } = AndroidXRMode.Auto;
        public static string XRQuality { get; private set; } = "balanced";
        public static bool Loaded { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadBeforeScene()
        {
            WorldVersion = 135;
            XRMode = AndroidXRMode.Auto;
            XRQuality = "balanced";
            Loaded = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var preferences = activity.Call<AndroidJavaObject>("getSharedPreferences", PreferencesName, 0))
                {
                    WorldVersion = NormalizeWorldVersion(preferences.Call<int>("getInt", WorldVersionKey, 135));
                    XRMode = ParseXRMode(preferences.Call<string>("getString", XRModeKey, "auto"));
                    XRQuality = NormalizeXRQuality(preferences.Call<string>("getString", XRQualityKey, "balanced"));
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[AndroidSettings] Native preferences unavailable; using v135/auto: " + error.Message);
            }
#endif
            Loaded = true;
            Debug.Log("[AndroidSettings] world=v" + WorldVersion + "; xr=" + XRMode + "; quality=" + XRQuality);
        }

        public static int NormalizeWorldVersion(int value) => value == 134 ? 134 : 135;
        public static string NormalizeXRQuality(string value) => value == "performance" || value == "clear" ? value : "balanced";
        public static AndroidXRMode ParseXRMode(string value)
        {
            if (string.Equals(value, "flat", StringComparison.OrdinalIgnoreCase)) return AndroidXRMode.ForceFlat;
            if (string.Equals(value, "xr", StringComparison.OrdinalIgnoreCase)) return AndroidXRMode.ForceXR;
            return AndroidXRMode.Auto;
        }
    }
}
