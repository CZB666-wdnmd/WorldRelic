using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using AniqueMuseum;

namespace LocalVrcw.Editor
{
    public static class WorldRelicAndroidBuild
    {
        public static void Build()
        {
            // Keep the editor's configured toolchain unless explicitly overridden by build.ps1.
            string sdk = Environment.GetEnvironmentVariable("WORLDRELIC_ANDROID_SDK");
            string ndk = Environment.GetEnvironmentVariable("WORLDRELIC_ANDROID_NDK");
            string jdk = Environment.GetEnvironmentVariable("WORLDRELIC_ANDROID_JDK");
            if (!string.IsNullOrEmpty(sdk)) AndroidExternalToolsSettings.sdkRootPath = sdk;
            if (!string.IsNullOrEmpty(ndk)) AndroidExternalToolsSettings.ndkRootPath = ndk;
            if (!string.IsNullOrEmpty(jdk)) AndroidExternalToolsSettings.jdkRootPath = jdk;
            PlayerSettings.companyName = "WorldRelic"; PlayerSettings.productName = "WorldRelic";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "local.worldrelic.player");
            PlayerSettings.bundleVersion = "0.6.0"; PlayerSettings.Android.bundleVersionCode = 3;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear; PlayerSettings.runInBackground = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            QualitySettings.antiAliasing = 4; QualitySettings.vSyncCount = 0;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            settings.FindProperty("activeInputHandler").intValue = 2; settings.ApplyModifiedPropertiesWithoutUndo();
            WorldRelicBuild.CreateIndependentTextSettings();
            ConfigureXR();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Bootstrap.unity");
            AssetDatabase.SaveAssets();
            WorldRelicBuild.VerifyContentAllowlist();
            var issues = new List<OpenXRFeature.ValidationRule>();
            UnityEditor.XR.OpenXR.OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            foreach (var issue in issues) Debug.Log("EXTERNAL_XR_VALIDATION error=" + issue.error + " " + issue.message);
            if (issues.Any(i => i.error)) throw new Exception("OpenXR validation failed");
            string output = Environment.GetEnvironmentVariable("WORLDRELIC_ANDROID_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new Exception("Missing external APK output");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/Bootstrap.unity" }, target = BuildTarget.Android,
                locationPathName = output, options = BuildOptions.StrictMode | BuildOptions.DetailedBuildReport | BuildOptions.CompressWithLz4HC
            });
            foreach (var packed in build.packedAssets)
                foreach (var item in packed.contents) Debug.Log("EXTERNAL_PACKED_ASSET " + item.sourceAssetPath);
            Debug.Log("EXTERNAL_ANDROID_BUILD result=" + build.summary.result + " errors=" + build.summary.totalErrors +
                " warnings=" + build.summary.totalWarnings + " bytes=" + build.summary.totalSize);
            EditorApplication.Exit(build.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        static void ConfigureXR()
        {
            const string path = "Assets/XR/XRGeneralSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null) { settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(settings, path); }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            var group = BuildTargetGroup.Android;
            if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = settings.SettingsForBuildTarget(group); general.InitManagerOnStart = false;
            var manager = general.Manager; manager.automaticLoading = manager.automaticRunning = false;
            const string loaderPath = "Assets/XR/Loaders/MuseumAndroidOpenXRLoader.asset";
            var loader = AssetDatabase.LoadAssetAtPath<XRLoader>(loaderPath);
            if (loader == null) { loader = ScriptableObject.CreateInstance<MuseumAndroidOpenXRLoader>(); AssetDatabase.CreateAsset(loader, loaderPath); }
            if (!manager.TrySetLoaders(new List<XRLoader> { loader })) throw new Exception("Loader assignment failed");
            FeatureHelpers.RefreshFeatures(group);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            xr.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.Legacy;
            string[] enabled = { "PICOFeature", "OpenXRExtensions", "PICONeo3ControllerProfile", "PICO4ControllerProfile", "PICO4UltraControllerProfile", "KHRSimpleControllerProfile", "FoveatedRenderingFeature", "MuseumFixedFoveationFeature", "DisplayRefreshRateFeature" };
            int count = 0;
            foreach (var feature in xr.GetFeatures())
            {
                feature.enabled = enabled.Contains(feature.GetType().Name);
                if (feature is FoveatedRenderingFeature)
                {
                    var data = new SerializedObject(feature); data.FindProperty("useEyeTracking").boolValue = false;
                    data.FindProperty("enableSubsampledLayout").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
                }
                if (feature.enabled) count++; EditorUtility.SetDirty(feature);
            }
            if (count != enabled.Length) throw new Exception("Missing XR features " + count);
            var pico = Unity.XR.OpenXR.Features.PICOSupport.PICOProjectSetting.GetProjectConfig();
            pico.displayFrequency = Unity.XR.OpenXR.Features.PICOSupport.SystemDisplayFrequency.RefreshRate72;
            pico.foveationEnable = true; pico.foveatedRenderingMode = FoveationFeature.FoveatedRenderingMode.FixedFoveatedRendering;
            pico.foveatedRenderingLevel = FoveationFeature.FoveatedRenderingLevel.Low; pico.isSubsampledEnabled = false;
            foreach (var asset in new UnityEngine.Object[] { xr, manager, general, settings, pico }) EditorUtility.SetDirty(asset);
        }
    }
}
