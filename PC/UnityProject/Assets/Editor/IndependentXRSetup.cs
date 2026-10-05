using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace LocalVrcw.Editor
{
    public static class IndependentXRSetup
    {
        public static void Configure()
        {
            Directory.CreateDirectory("Assets/XR");
            const string path = "Assets/XR/XRGeneralSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            var group = BuildTargetGroup.Standalone;
            if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = settings.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            var manager = general.Manager;
            manager.automaticLoading = false; manager.automaticRunning = false;
            if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                throw new Exception("Could not assign OpenXR loader");
            FeatureHelpers.RefreshFeatures(group);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (xr == null) throw new Exception("OpenXR feature settings missing");
            xr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            string[] profileNames = { "OculusTouchControllerProfile", "ValveIndexControllerProfile", "HTCViveControllerProfile", "MicrosoftMotionControllerProfile", "KHRSimpleControllerProfile" };
            int enabled = 0;
            foreach (var f in xr.GetFeatures())
            {
                if (f is OpenXRInteractionFeature)
                {
                    f.enabled = profileNames.Contains(f.GetType().Name);
                    if (f.enabled) { enabled++; Debug.Log("[MuseumXRSetup] profile=" + f.GetType().Name); }
                    EditorUtility.SetDirty(f);
                }
            }
            if (enabled != profileNames.Length) throw new Exception("An expected controller profile is absent: " + enabled);
            EditorUtility.SetDirty(xr); EditorUtility.SetDirty(manager); EditorUtility.SetDirty(general); EditorUtility.SetDirty(settings);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }
    }
}
