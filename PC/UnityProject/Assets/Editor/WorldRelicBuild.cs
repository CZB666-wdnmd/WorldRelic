using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

namespace LocalVrcw.Editor
{
    public static class WorldRelicBuild
    {
        public static void Build()
        {
            string output = Arg("--build-output");
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("Missing --build-output");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            Directory.CreateDirectory("Assets/Scenes");
            CreateIndependentTextSettings();
            IndependentXRSetup.Configure();
            var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            playerSettings.FindProperty("activeInputHandler").intValue = 2; // Both, without joystick Submit bindings.
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            var empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(empty, "Assets/Scenes/Bootstrap.unity");
            VerifyContentAllowlist();
            PlayerSettings.companyName = "WorldRelic";
            PlayerSettings.productName = "WorldRelic";
            PlayerSettings.bundleVersion = "0.6.0";
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,
                ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone,
                ManagedStrippingLevel.Disabled);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Bootstrap.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                extraScriptingDefines = Array.Empty<string>(),
                options = BuildOptions.DetailedBuildReport
            });
            Debug.Log("WORLDRELIC_BUILD result=" + build.summary.result + " bytes=" + build.summary.totalSize +
                " errors=" + build.summary.totalErrors + " warnings=" + build.summary.totalWarnings);
            foreach (var assetFile in build.packedAssets)
                foreach (var content in assetFile.contents)
                {
                    Debug.Log("PACKED_ASSET " + content.sourceAssetPath);
                    VerifyAssetPath(content.sourceAssetPath);
                }
            if (build.summary.result != BuildResult.Succeeded) throw new Exception("WorldRelic build failed");
        }

        static void VerifyContentAllowlist()
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path)) continue;
                VerifyAssetPath(path);
            }
        }

        static void VerifyAssetPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return;
            if (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal)) return;
            if (path == "Assets/Resources/MuseumXRHand.shader" || path == "Assets/Resources/MuseumInteractionOverlay.shader") return;
            // Generated generic Unity XR configuration, never imported world objects.
            if (path == "Assets/XR/XRGeneralSettings.asset" || path == "Assets/XR/Loaders/OpenXRLoader.asset" ||
                path == "Assets/XR/Settings/OpenXR Package Settings.asset" || path == "Assets/XR/Settings/OpenXR Editor Settings.asset") return;
            if (path == "Assets/Scenes/Bootstrap.unity" || path == "Assets/Resources/TMP Settings.asset" ||
                path == "Assets/Resources/m_leadingCharacters.asset" || path == "Assets/Resources/m_followingCharacters.asset") return;
            throw new InvalidOperationException("Asset not on independent-content allowlist: " + path);
        }

        static void CreateIndependentTextSettings()
        {
            // Empty generic TMP settings authored here; no source-world fonts/styles are copied.
            Directory.CreateDirectory("Assets/Resources");
            const string settingsPath = "Assets/Resources/TMP Settings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TMP_Settings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("assetVersion").stringValue = "2";
            serialized.FindProperty("m_fallbackFontAssets").arraySize = 0;
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = null;
            serialized.FindProperty("m_defaultStyleSheet").objectReferenceValue = null;
            string[] files = { "m_leadingCharacters", "m_followingCharacters" };
            foreach (string field in files)
            {
                string path = "Assets/Resources/" + field + ".asset";
                var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (text == null)
                {
                    text = new TextAsset("");
                    AssetDatabase.CreateAsset(text, path);
                }
                serialized.FindProperty(field).objectReferenceValue = text;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
