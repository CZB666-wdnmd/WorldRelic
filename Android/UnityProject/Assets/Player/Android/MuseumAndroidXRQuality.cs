using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.OpenXR.Features.PICOSupport;

namespace AniqueMuseum
{
    public static class MuseumAndroidXRQuality
    {
        public readonly struct Profile
        {
            public readonly int msaa, anisotropy;
            public readonly float scale;
            public readonly FoveationFeature.FoveatedRenderingLevel foveation;
            public Profile(int aa, float resolution, int aniso, FoveationFeature.FoveatedRenderingLevel ffr)
            { msaa = aa; scale = resolution; anisotropy = aniso; foveation = ffr; }
        }
        public static Profile GetProfile(string name)
        {
            if (name == "performance") return new Profile(2, 0.90f, 4, FoveationFeature.FoveatedRenderingLevel.Medium);
            if (name == "clear") return new Profile(4, 1.10f, 8, FoveationFeature.FoveatedRenderingLevel.Off);
            return new Profile(4, 1.00f, 4, FoveationFeature.FoveatedRenderingLevel.Low);
        }
        static bool prepared;
        static int previousAA, previousLights, previousMipLimit;
        static ShadowQuality previousShadows;
        static AnisotropicFiltering previousAnisotropy;
        static float previousDisplayScale;
        static XRDisplaySubsystem configuredDisplay;
        static bool reportPending;
        static bool previousHDR, previousCameraMSAA;
        static readonly Dictionary<Texture2D, (FilterMode filter, int aniso)> previousFilters = new Dictionary<Texture2D, (FilterMode, int)>();
        static Camera camera;
        static void Report(string message)
        {
            // Ordinary play filters Debug.Log. Keep the four one-time render
            // diagnostics available without exposing debug interaction names.
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var log = new AndroidJavaClass("android.util.Log")) log.CallStatic<int>("i", "MuseumXRQuality", message);
#else
            Debug.Log("[MuseumXRQuality] " + message);
#endif
        }

        public static void Prepare(Camera head)
        {
            if (Application.platform != RuntimePlatform.Android || prepared) return;
            prepared = true; camera = head;
            previousAA = QualitySettings.antiAliasing; previousLights = QualitySettings.pixelLightCount;
            previousMipLimit = QualitySettings.globalTextureMipmapLimit;
            previousShadows = QualitySettings.shadows; previousAnisotropy = QualitySettings.anisotropicFiltering;
            previousHDR = head.allowHDR;
            previousCameraMSAA = head.allowMSAA;
            var profile = GetProfile(AndroidNativeSettings.XRQuality);
            QualitySettings.antiAliasing = profile.msaa;
            // Keep the authored baked maps/probes and one principal light;
            // mobile XR avoids realtime shadow maps and HDR intermediates.
            QualitySettings.pixelLightCount = 1;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            head.allowHDR = false; head.allowMSAA = true;
            var config = PICOProjectSetting.GetProjectConfig();
            if (config != null) config.foveatedRenderingLevel = profile.foveation;
            Report($"prepare profile={AndroidNativeSettings.XRQuality} MSAA={profile.msaa} scale={profile.scale} aniso={profile.anisotropy} shadows=off HDR=off");
        }

        public static void ConfigureDisplay(XRDisplaySubsystem display)
        {
            if (!prepared || display == null) return;
            configuredDisplay = display; previousDisplayScale = display.scaleOfAllRenderTargets;
            var profile = GetProfile(AndroidNativeSettings.XRQuality);
            // Apply to the loaded OpenXR display before StartSubsystems. The
            // legacy XRSettings getters can report 0x0/MultiPass for OpenXR.
            display.scaleOfAllRenderTargets = profile.scale;
            display.SetMSAALevel(profile.msaa);
            Report($"display configured scale={display.scaleOfAllRenderTargets} MSAA={profile.msaa}");
        }

        public static void Activated(Camera head)
        {
            if (!prepared) return;
            var profile = GetProfile(AndroidNativeSettings.XRQuality);
            int count = 0;
            // Preserve the source image payloads and mip chains; filtering
            // changes cost no extra full-resolution textures or readback.
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (texture.mipmapCount > 1 && texture.width >= 256)
                {
                    previousFilters[texture] = (texture.filterMode, texture.anisoLevel);
                    texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = profile.anisotropy; count++;
                }
            try
            {
                if (FoveationFeature.isExtensionEnable) FoveationFeature.foveatedRenderingLevel = profile.foveation;
                float hz = 0;
                bool requested = DisplayRefreshRateFeature.SetDisplayRefreshRate(SystemDisplayFrequency.RefreshRate72);
                DisplayRefreshRateFeature.GetDisplayRefreshRate(ref hz);
                Report($"refresh72Requested={requested} refresh={hz} FFRSupported={FoveationFeature.isExtensionEnable} FFR={FoveationFeature.foveatedRenderingLevel}");
            }
            catch (Exception e) { Debug.LogWarning("[MuseumXRQuality] Optional headset tuning unavailable: " + e.Message); }
            Report($"active requestedMSAA={QualitySettings.antiAliasing} filteredTextures={count}; waiting for first render pass");
            reportPending = true;
        }

        public static void ReportFirstRenderPass()
        {
            if (!reportPending || configuredDisplay == null || !configuredDisplay.running) return;
            int passes = configuredDisplay.GetRenderPassCount();
            if (passes == 0) return;
            reportPending = false;
            Report($"display layout={configuredDisplay.textureLayout} scale={configuredDisplay.scaleOfAllRenderTargets} passes={passes}");
            for (int index = 0; index < passes; index++)
            {
                configuredDisplay.GetRenderPass(index, out var pass);
                Report($"renderPass={index} views={pass.GetRenderParameterCount()} target={pass.renderTargetDesc.width}x{pass.renderTargetDesc.height} samples={pass.renderTargetDesc.msaaSamples}");
            }
        }

        public static void Restore()
        {
            if (!prepared) return;
            QualitySettings.antiAliasing = previousAA; QualitySettings.pixelLightCount = previousLights;
            QualitySettings.shadows = previousShadows; QualitySettings.globalTextureMipmapLimit = previousMipLimit;
            QualitySettings.anisotropicFiltering = previousAnisotropy;
            if (configuredDisplay != null)
            {
                configuredDisplay.scaleOfAllRenderTargets = previousDisplayScale;
                configuredDisplay.SetMSAALevel(previousAA == 0 ? 1 : previousAA);
            }
            configuredDisplay = null; reportPending = false;
            foreach (var item in previousFilters)
                if (item.Key != null) { item.Key.filterMode = item.Value.filter; item.Key.anisoLevel = item.Value.aniso; }
            previousFilters.Clear();
            if (camera != null) { camera.allowHDR = previousHDR; camera.allowMSAA = previousCameraMSAA; }
            prepared = false; camera = null;
        }
    }
}
