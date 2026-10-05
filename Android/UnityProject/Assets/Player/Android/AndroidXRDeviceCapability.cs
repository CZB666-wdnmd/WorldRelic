using System;
using UnityEngine;

namespace AniqueMuseum
{
    /// <summary>
    /// Decides whether Android OpenXR may be initialized.  A failed OpenXR
    /// initialization can leave some phone graphics drivers without a usable
    /// non-XR swapchain, so ordinary phones must be rejected before touching
    /// XRManagerSettings.
    /// </summary>
    public static class AndroidXRDeviceCapability
    {
        public static bool ShouldAttemptXR(bool forceDesktop, bool forceVR,
            bool hasHeadTracking, bool hasHighPerformanceVr,
            string manufacturer, string brand, string model)
        {
            if (forceDesktop) return false;
            if (forceVR) return true;
            return hasHeadTracking || hasHighPerformanceVr ||
                LooksLikeStandaloneHeadset(manufacturer, brand, model);
        }

        public static bool LooksLikeStandaloneHeadset(string manufacturer, string brand, string model)
        {
            string identity = (manufacturer ?? "") + " " + (brand ?? "") + " " + (model ?? "");
            return Contains(identity, "pico") || Contains(identity, "oculus") || Contains(identity, "quest");
        }

        static bool Contains(string value, string token)
        {
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool CurrentDeviceSupportsXR(out string evidence)
        {
            bool headTracking = false;
            bool highPerformanceVr = false;
            string manufacturer = "";
            string brand = "";
            string model = SystemInfo.deviceModel ?? "";

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var build = new AndroidJavaClass("android.os.Build"))
                {
                    manufacturer = build.GetStatic<string>("MANUFACTURER") ?? "";
                    brand = build.GetStatic<string>("BRAND") ?? "";
                    model = build.GetStatic<string>("MODEL") ?? model;
                }
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    headTracking = packageManager.Call<bool>("hasSystemFeature", "android.hardware.vr.headtracking");
                    highPerformanceVr = packageManager.Call<bool>("hasSystemFeature", "android.hardware.vr.high_performance");
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[MuseumXR] Android capability query failed; treating device as flat-screen: " + error.Message);
            }
#endif

            bool supported = ShouldAttemptXR(false, false, headTracking, highPerformanceVr,
                manufacturer, brand, model);
            evidence = "manufacturer=" + manufacturer + "; brand=" + brand + "; model=" + model +
                "; headTracking=" + headTracking + "; highPerformanceVr=" + highPerformanceVr;
            return supported;
        }
    }
}
