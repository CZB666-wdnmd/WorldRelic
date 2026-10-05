using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.Udon;

namespace LocalVrcw
{
    public sealed class WorldRelicRuntime : MonoBehaviour
    {
        ExternalWorldProfile profile;
        string inputPath;
        string fontPath;
        string outputPath;
        string status = "No file imported";
        bool automated;
        bool runUdon = true;
        Udon.RuntimeDirector udonDirector;
        readonly ProbeReport report = new ProbeReport();
        readonly List<string> warnings = new List<string>();
        readonly List<ProgramDigest> programDigests = new List<ProgramDigest>();
        readonly List<string> variableDigests = new List<string>();
        AssetBundle loadedBundle;
        string privateImportedPath;
        bool privateCopyOwned;
        string privateFontPath;
        bool privateFontOwned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var host = new GameObject("WorldRelic Runtime");
            DontDestroyOnLoad(host);
            host.AddComponent<WorldRelicRuntime>();
        }

        void Awake()
        {
            Application.logMessageReceived += CaptureLog;
            Application.targetFrameRate = 60;
            try { if (Application.platform == RuntimePlatform.Android) outputPath = Path.Combine(Application.persistentDataPath, "external-runtime"); else ParseArgs(); }
            catch (Exception ex) { Fail("ARGUMENT_ERROR", ex.Message, 2); }
        }

        IEnumerator Start()
        {
            if (status.StartsWith("ERROR")) yield break;
            if (Application.platform == RuntimePlatform.Android)
            {
                status = "请选择 Android VRCW 和批准的字体文件";
                yield return AndroidExternalDocuments.Request();
                var selection = AndroidExternalDocuments.Selection;
                if (selection == null || !string.IsNullOrEmpty(selection.error))
                { Fail("IMPORT_CANCELLED", selection?.error ?? "No document result", 3); yield break; }
                inputPath = selection.world; fontPath = selection.font;
            }
            // Unity remembers the preceding build's windowed resolution. Use
            // native borderless fullscreen on every normal/probe launch too.
            if (Application.platform != RuntimePlatform.Android) Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
            yield return null;
            report.fullScreenMode = Screen.fullScreenMode.ToString();
            report.screenWidth = Screen.width; report.screenHeight = Screen.height;
            Debug.Log($"RAW_PROBE DISPLAY mode={report.fullScreenMode} size={report.screenWidth}x{report.screenHeight}");
            if (string.IsNullOrEmpty(inputPath))
            {
                if (automated) { Fail("FILE_REQUIRED", "--probe requires --vrcw", 2); yield break; }
                Fail("FILE_REQUIRED", "Place AniqueMuseum-windows.vrcw and the approved VL-PGothic-Regular.ttf beside the EXE, or use --vrcw <path> --font <path>. This player contains no world/font.", 3);
                yield break;
            }
            report.unityVersion = Application.unityVersion;
            report.graphicsDevice = SystemInfo.graphicsDeviceName;
            report.colorSpace = QualitySettings.activeColorSpace.ToString();
            report.input = Path.GetFullPath(inputPath);
            string imported = null;
            try
            {
                status = "Verifying user file";
                Directory.CreateDirectory(outputPath);
                // Unique private copy: the bytes loaded are the same bytes that were hashed.
                string cache = Path.Combine(outputPath, "user-cache");
                Directory.CreateDirectory(cache);
                imported = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".vrcw");
                privateImportedPath = imported;
                report.bytes = new FileInfo(report.input).Length;
                if (!ExternalWorldProfile.AcceptsLength(report.bytes)) throw new InvalidDataException("Unsupported Android VRCW file length/platform/build");
                using (var source = new FileStream(report.input, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destination = new FileStream(imported, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var hash = SHA256.Create())
                {
                    privateCopyOwned = true; // FileMode.CreateNew succeeded; this process owns this file.
                    var buffer = new byte[1024 * 1024];
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        destination.Write(buffer, 0, count);
                        hash.TransformBlock(buffer, 0, count, buffer, 0);
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    report.sha256 = BitConverter.ToString(hash.Hash).Replace("-", "");
                }
                profile = ExternalWorldProfile.Identify(report.sha256, new FileInfo(imported).Length);
                report.contentVersion = profile.Version;
                report.contentVariant = profile.Variant;
                report.expectedSha256 = profile.Sha256;
                report.hashAccepted = true;
                Debug.Log("EXTERNAL_WORLD_SELECTED version=" + profile.Version + " variant=" + profile.Variant + " sha=" + report.sha256);
            }
            catch (Exception ex)
            {
                if (privateCopyOwned && imported != null && File.Exists(imported)) File.Delete(imported);
                Fail("FILE_REJECTED", ex.ToString(), 3);
                yield break;
            }

            try
            {
                report.fontInput = ExternalInputPaths.ResolveFont(ExternalInputPaths.ExecutableDirectory, fontPath);
                // Hash the same immutable snapshot consumed by FontEngine, not a later re-read of user input.
                privateFontPath = Path.Combine(outputPath, "user-cache", Guid.NewGuid().ToString("N") + ".ttf");
                using (var source = new FileStream(report.fontInput, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destination = new FileStream(privateFontPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hash = SHA256.Create())
                {
                    privateFontOwned = true;
                    if (source.Length != ExternalInputPaths.FontBytes) throw new InvalidDataException("Unapproved external font length");
                    var buffer = new byte[65536]; int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        destination.Write(buffer, 0, count);
                        report.fontBytes += count;
                        hash.TransformBlock(buffer, 0, count, buffer, 0);
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    report.fontSha256 = BitConverter.ToString(hash.Hash).Replace("-", "");
                }
                if (report.fontBytes != ExternalInputPaths.FontBytes || report.fontSha256 != ExternalInputPaths.FontSha256)
                    throw new InvalidDataException("External font SHA256 is not approved");
                report.fontHashAccepted = true;
                fontPath = privateFontPath;
                Debug.Log("EXTERNAL_FONT_SELECTED sha=" + report.fontSha256);
            }
            catch (Exception ex) { Fail("FONT_REQUIRED", ex.Message, 3); yield break; }

            status = "Loading original AssetBundle";
            var bundleRequest = AssetBundle.LoadFromFileAsync(imported);
            yield return bundleRequest;
            loadedBundle = bundleRequest.assetBundle;
            if (loadedBundle == null) { Fail("BUNDLE_LOAD_FAILED", "Unity rejected the original bundle", 4); yield break; }
            report.bundleLoaded = true;
            report.streamedSceneBundle = loadedBundle.isStreamedSceneAssetBundle;
            report.scenes = loadedBundle.GetAllScenePaths();
            Debug.Log("RAW_PROBE BUNDLE_LOADED scenes=" + string.Join(",", report.scenes));
            if (report.scenes.Length != 1) { Fail("SCENE_LAYOUT_ERROR", "Expected exactly one source scene", 4); yield break; }

            status = "Loading original scene";
            var sceneRequest = SceneManager.LoadSceneAsync(report.scenes[0], LoadSceneMode.Additive);
            if (sceneRequest == null) { Fail("SCENE_LOAD_FAILED", "No scene request", 5); yield break; }
            yield return sceneRequest;
            Scene scene = SceneManager.GetSceneByPath(report.scenes[0]);
            if (!scene.IsValid() || !scene.isLoaded) { Fail("SCENE_LOAD_FAILED", "Scene not loaded", 5); yield break; }
            SceneManager.SetActiveScene(scene);
            report.sceneLoaded = true;
            try
            {
                report.fontMaterialsRepaired = ImportedTextCompatibility.RepairFontMaterials(scene);
                Inspect(scene);
                report.text = SystemTextFallback.Install(scene, fontPath);
                if (runUdon)
                {
                    udonDirector = gameObject.AddComponent<Udon.RuntimeDirector>();
                    udonDirector.Import(scene, cameraForProbe, profile);
                }
            }
            catch (Exception ex) { Fail("INSPECTION_FAILED", ex.ToString(), 6); yield break; }

            // Upload resources and tick the independent interpreter (default gameplay).
            for (int i = 0; i < 8; i++) yield return null;
            AudioSource audioCheck = null;
            var audioClip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.samples > 0);
            if (audioClip != null)
            {
                audioClip.LoadAudioData();
                for (int i = 0; i < 120 && audioClip.loadState == AudioDataLoadState.Loading; i++) yield return null;
                report.audioClipLoaded = audioClip.loadState == AudioDataLoadState.Loaded;
                audioCheck = gameObject.AddComponent<AudioSource>();
                audioCheck.clip = audioClip;
                audioCheck.volume = 0; // Silent decode/playback check, not a BGM implementation.
                audioCheck.spatialize = false;
                audioCheck.Play();
                yield return null;
                report.silentAudioPlayback = audioCheck.isPlaying;
                audioCheck.Stop();
                Destroy(audioCheck);
            }
            if (!automated)
            {
                status = runUdon ? "External " + profile.Version + " runtime: WASD/mouse, E interact/grab, Q drop, LMB held-use, C/B crouch, F2 settings. Todoroki: LMB fire / RMB ice. --vr enables OpenXR." :
                    profile.Version + " " + profile.Variant + " loaded. RMB look, WASD move, Shift faster, Esc unlock. Add --udon for interpreter research.";
                WriteReport();
                yield break;
            }
            try
            {
                Capture(Path.Combine(outputPath, "entrance.png"));
                report.vm = udonDirector?.Report();
                report.success = report.sceneLoaded && report.gameObjects == profile.GameObjects && report.udonBehaviours == profile.UdonHosts &&
                    report.pickups == profile.Pickups && report.programAssets == 62 && report.programsWithData == 62 &&
                    report.udonWithProgram == profile.UdonHosts && report.variableNonNullReferences == profile.ObjectReferences &&
                    report.missingScripts == 0 && report.unsupportedShaders.Length == 0 && report.exceptionCount == 0 &&
                    report.errorCount == 0 && report.audioClipLoaded && report.silentAudioPlayback &&
                    (!runUdon || (report.vm.loaded == profile.UdonHosts && report.vm.faults == 0 && report.vm.unimplemented == 0));
                status = report.success ? "Runtime resource import checks passed" : "Runtime resource import checks incomplete";
                WriteReport();
            }
            catch (Exception ex) { Fail("CAPTURE_FAILED", ex.ToString(), 7); yield break; }
            yield return null;
            Application.Quit(report.success ? 0 : 8);
        }

        Camera cameraForProbe;

        void OnApplicationFocus(bool focused)
        {
            if (focused && Application.platform == RuntimePlatform.Android && report.sceneLoaded && !AndroidExternalDocuments.Busy)
                StartCoroutine(RecheckDocumentAccess());
        }
        IEnumerator RecheckDocumentAccess()
        {
            // Provider reads run off the Unity thread, including remote/offline providers.
            var check = System.Threading.Tasks.Task.Run(AndroidExternalDocuments.Readable);
            while (!check.IsCompleted) yield return null;
            if (!check.IsFaulted && check.Result) yield break;
            float previous = Time.timeScale; Time.timeScale = 0;
            yield return AndroidExternalDocuments.Request();
            Time.timeScale = previous;
            if (AndroidExternalDocuments.Selection == null || !string.IsNullOrEmpty(AndroidExternalDocuments.Selection.error)) Application.Quit();
            // Current session keeps its immutable, verified snapshots. A newly
            // selected different version is picked up on the next cold launch.
        }

        void Inspect(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var udons = roots.SelectMany(r => r.GetComponentsInChildren<UdonBehaviour>(true)).ToArray();
            report.roots = roots.Length;
            report.gameObjects = transforms.Length;
            report.renderers = renderers.Length;
            report.meshFilters = roots.Sum(r => r.GetComponentsInChildren<MeshFilter>(true).Length);
            report.pickups = roots.Sum(r => r.GetComponentsInChildren<VRCPickup>(true).Length);
            report.udonBehaviours = udons.Length;
            report.udonWithProgram = udons.Count(u => u.serializedProgramAsset != null);
            report.udonWithVariables = udons.Count(u => !string.IsNullOrEmpty(u.serializedPublicVariablesBytesString));
            report.hostPrograms = udons.Where(u => u.serializedProgramAsset != null)
                .GroupBy(u => u.serializedProgramAsset.name).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ProgramHostDigest { name = g.Key, hosts = g.Count() }).ToArray();
            foreach (var udon in udons)
                variableDigests.Add(Digest(Convert.FromBase64String(udon.serializedPublicVariablesBytesString)));
            report.variableObjectReferences = udons.Sum(u => u.publicVariablesUnityEngineObjects?.Length ?? 0);
            report.variableNonNullReferences = udons.Sum(u => u.publicVariablesUnityEngineObjects?.Count(o => o != null) ?? 0);
            var programs = udons.Select(u => u.serializedProgramAsset).Where(p => p != null).Distinct().ToArray();
            report.programAssets = programs.Length;
            report.programsWithData = programs.Count(p => p.serializedProgramCompressedBytes?.Length > 0);
            foreach (var program in programs)
            {
                byte[] bytes = program.serializedProgramCompressedBytes;
                if (bytes == null || bytes.Length == 0) continue;
                using (var source = new MemoryStream(bytes))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var decoded = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (decoded.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Program too large");
                        decoded.Write(buffer, 0, count);
                    }
                    report.decompressedProgramBytes += decoded.Length;
                    report.gzipProgramsDecoded++;
                    programDigests.Add(new ProgramDigest
                    {
                        name = program.name,
                        compressedBytes = bytes.Length,
                        compressedSha256 = Digest(bytes),
                        decodedBytes = decoded.Length,
                        decodedSha256 = Digest(decoded.ToArray())
                    });
                }
            }
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            report.materialsReferenced = materials.Length;
            report.missingMaterialSlots = renderers.Sum(r => r.sharedMaterials.Count(m => m == null));
            report.shaders = materials.Where(m => m.shader != null).Select(m => m.shader.name).Distinct().OrderBy(n => n).ToArray();
            report.unsupportedShaders = materials.Where(m => m.shader == null || !m.shader.isSupported)
                .Select(m => m.name + ":" + (m.shader ? m.shader.name : "null")).ToArray();
            report.textureReferences = materials.Sum(m => m.GetTexturePropertyNames().Count(p => m.GetTexture(p) != null));
            report.audioSources = roots.Sum(r => r.GetComponentsInChildren<AudioSource>(true).Length);
            report.audioWithClip = roots.Sum(r => r.GetComponentsInChildren<AudioSource>(true).Count(a => a.clip != null));
            report.animators = roots.Sum(r => r.GetComponentsInChildren<Animator>(true).Length);
            report.animatorsWithController = roots.Sum(r => r.GetComponentsInChildren<Animator>(true).Count(a => a.runtimeAnimatorController != null));
            report.particleSystems = roots.Sum(r => r.GetComponentsInChildren<ParticleSystem>(true).Length);
            report.reflectionProbes = roots.Sum(r => r.GetComponentsInChildren<ReflectionProbe>(true).Length);
            report.probesWithTexture = roots.Sum(r => r.GetComponentsInChildren<ReflectionProbe>(true).Count(p => p.bakedTexture != null));
            report.lightmaps = LightmapSettings.lightmaps.Length;
            report.lightProbes = LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0;
            report.missingScripts = transforms.Sum(t => t.GetComponents<MonoBehaviour>().Count(c => c == null));

            var descriptor = roots.SelectMany(r => r.GetComponentsInChildren<VRCSceneDescriptor>(true)).FirstOrDefault();
            Transform spawn = descriptor && descriptor.spawns != null ? descriptor.spawns.FirstOrDefault(s => s != null) : null;
            report.spawnResolved = spawn != null;
            report.dynamicMaterials = descriptor?.DynamicMaterials?.Count(m => m != null) ?? 0;
            foreach (var sourceCamera in roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)))
            { sourceCamera.enabled = false; if (sourceCamera.CompareTag("MainCamera")) sourceCamera.tag = "Untagged"; }
            foreach (var listener in roots.SelectMany(r => r.GetComponentsInChildren<AudioListener>(true))) listener.enabled = false;
            foreach (var audio in roots.SelectMany(r => r.GetComponentsInChildren<AudioSource>(true))) audio.Stop();
            var cameraObject = new GameObject("Independent Probe Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraForProbe = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            if (!runUdon) cameraObject.AddComponent<ProbeCamera>();
            cameraForProbe.nearClipPlane = 0.05f;
            cameraForProbe.farClipPlane = 500;
            cameraForProbe.fieldOfView = 65;
            cameraForProbe.transform.SetPositionAndRotation(spawn ? spawn.position + Vector3.up * 1.2f : Vector3.up * 1.2f,
                spawn ? spawn.rotation : Quaternion.identity);
            report.spawnPosition = cameraForProbe.transform.position;
            Debug.Log("RAW_PROBE SCENE_LOADED go=" + report.gameObjects + " udon=" + report.udonBehaviours +
                " programs=" + report.programAssets + " pickups=" + report.pickups + " missingScripts=" + report.missingScripts);
        }

        void Capture(string path)
        {
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                cameraForProbe.targetTexture = target;
                cameraForProbe.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                cameraForProbe.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Destroy(target);
                Destroy(texture);
            }
        }

        void ParseArgs()
        {
            outputPath = Path.Combine(Application.persistentDataPath, "runtime");
            string[] args = Environment.GetCommandLineArgs();
            var seen = new HashSet<string>(); bool help = false;
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if ((arg.StartsWith("--") || arg == "-v") && !seen.Add(arg)) throw new ArgumentException("Repeated option: " + arg);
                if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    help = true; continue;
                }
                if (arg == "--probe") { automated = true; continue; }
                if (arg == "--resources-only") { runUdon = false; continue; }
                if (arg == "--debug") { AniqueMuseum.MuseumDebug.Enabled = true; continue; }
                if (arg == "--vr" || arg == "--desktop") continue;
                if (arg == "--free-camera") { AniqueMuseum.DesktopFreeCameraSettings.Requested = AniqueMuseum.DesktopFreeCameraMode.FreeCamera; continue; }
                if (arg == "--free-camera-noclip") { AniqueMuseum.DesktopFreeCameraSettings.Requested = AniqueMuseum.DesktopFreeCameraMode.FreeCameraNoCollision; continue; }
                if (arg == "--udon") { runUdon = true; continue; }
                if (arg == "--vrcw" || arg == "--output" || arg == "--font")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-")) throw new ArgumentException("Missing value: " + arg);
                    if (arg == "--vrcw")
                    {
                        if (inputPath != null) throw new ArgumentException("Repeated --vrcw");
                        inputPath = args[++i];
                    }
                    else if (arg == "--font") fontPath = Path.GetFullPath(args[++i]);
                    else outputPath = Path.GetFullPath(args[++i]);
                    continue;
                }
                if (arg == "-logFile" || arg == "-screen-width" || arg == "-screen-height" || arg == "-screen-fullscreen")
                {
                    if (++i >= args.Length) throw new ArgumentException("Missing Unity option value");
                    continue;
                }
                if (arg == "-batchmode" || arg == "-force-d3d11" || arg == "-nographics") continue;
                throw new ArgumentException("Unknown argument: " + arg);
            }
            if (seen.Contains("--vr") && seen.Contains("--desktop")) throw new ArgumentException("Conflicting --vr / --desktop");
            if (seen.Contains("--free-camera") && seen.Contains("--free-camera-noclip")) throw new ArgumentException("Conflicting free-camera modes");
            if (seen.Contains("--resources-only") && seen.Contains("--udon")) throw new ArgumentException("Conflicting resources-only / udon");
            if (inputPath == null && File.Exists(ExternalInputPaths.DefaultWorld)) inputPath = ExternalInputPaths.DefaultWorld;
            if (help)
            {
                Debug.Log("WorldRelic 0.6.0\n[--vrcw <approved Windows VRCW>] [--font <approved VL-PGothic-Regular.ttf>] [--vr|--desktop] [--free-camera|--free-camera-noclip] [--debug]\n" +
                    "Default: AniqueMuseum-windows.vrcw and the hash-approved font beside the EXE; not the current working directory. Font renaming is allowed.\n" +
                    "[--output <private cache/evidence>] [--probe] [--resources-only]\n" +
                    "WASD/mouse, Shift run, Space jump, C/B crouch, E interact/grab, Q drop, LMB held-use; F2 settings.\n" +
                    "Flat Todoroki cube: LMB fire / RMB ice. VR left/fire and right/ice remain.\n" +
                    "Gamepad: sticks move/look; A jump, B crouch, X interact, Y grab/drop, RT use, LB run. Todoroki: LT fire / RT ice.\n" +
                    "VR: grip grab, trigger operate, left Y hands/ray, right B crouch. Free camera uses WASD along view only.\n" +
                    "Windows build/version/variant is identified automatically by SHA256. No -v option. Unknown files are rejected, never substituted.\n" +
                    "Only font SHA256 " + ExternalInputPaths.FontSha256 + " is accepted. World and font are user-provided, not bundled.");
                status = "ERROR HELP_REQUESTED"; Application.Quit(0);
            }
        }

        void CaptureLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                report.warningCount++;
                if (type == LogType.Exception) report.exceptionCount++;
                if (type == LogType.Error || type == LogType.Assert) report.errorCount++;
                if (warnings.Count < 100) warnings.Add(type + ": " + message);
            }
        }

        void Fail(string code, string message, int exitCode)
        {
            status = "ERROR " + code + ": " + message;
            report.failure = status;
            Debug.LogError(status);
            WriteReport();
            Application.Quit(exitCode);
        }

        void WriteReport()
        {
            if (udonDirector != null) report.vm = udonDirector.Report();
            report.messages = warnings.ToArray();
            report.programs = programDigests.OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
            report.variableDigests = variableDigests.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (!string.IsNullOrEmpty(outputPath))
            {
                Directory.CreateDirectory(outputPath);
                File.WriteAllText(Path.Combine(outputPath, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("RAW_PROBE_REPORT " + JsonUtility.ToJson(report));
        }

        static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        void OnGUI()
        {
            if (report.sceneLoaded && !AniqueMuseum.MuseumDebug.Enabled) return;
            GUI.Label(new Rect(12, 12, Screen.width - 24, 70), status);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= CaptureLog;
            if (loadedBundle != null) loadedBundle.Unload(false);
            // Only the single generated, verified copy owned by this process.
            // Never enumerate/delete cached content or touch the user's input.
            if (privateCopyOwned && privateImportedPath != null)
                try { File.Delete(privateImportedPath); }
                catch (IOException ex) { Debug.LogWarning("Private import copy remains locked: " + ex.Message); }
            if (privateFontOwned && privateFontPath != null)
                try { File.Delete(privateFontPath); }
                catch (IOException ex) { Debug.LogWarning("Private font copy remains locked: " + ex.Message); }
        }
    }

    [Serializable]
    public sealed class ProbeReport
    {
        public string unityVersion, graphicsDevice, colorSpace, input, expectedSha256, sha256, failure, fullScreenMode, contentVersion, contentVariant;
        public string fontInput, fontSha256;
        public long fontBytes;
        public bool fontHashAccepted;
        public int screenWidth, screenHeight;
        public long bytes, decompressedProgramBytes;
        public bool hashAccepted, bundleLoaded, streamedSceneBundle, sceneLoaded, spawnResolved, success;
        public bool audioClipLoaded, silentAudioPlayback;
        public int roots, gameObjects, renderers, meshFilters, pickups, udonBehaviours, udonWithProgram,
            udonWithVariables, variableObjectReferences, variableNonNullReferences, programAssets, programsWithData,
            gzipProgramsDecoded, materialsReferenced, missingMaterialSlots, textureReferences, audioSources, audioWithClip,
            animators, animatorsWithController, particleSystems, reflectionProbes, probesWithTexture, lightmaps, lightProbes,
            missingScripts, dynamicMaterials, warningCount, exceptionCount, errorCount, fontMaterialsRepaired;
        public Vector3 spawnPosition;
        public string[] scenes, shaders, unsupportedShaders, messages;
        public ProgramDigest[] programs;
        public ProgramHostDigest[] hostPrograms;
        public string[] variableDigests;
        public Udon.VmReport vm;
        public TextImportReport text;
    }

    [Serializable]
    public sealed class ProgramHostDigest { public string name; public int hosts; }

    [Serializable]
    public sealed class ProgramDigest
    {
        public string name, compressedSha256, decodedSha256;
        public long compressedBytes, decodedBytes;
    }
}
