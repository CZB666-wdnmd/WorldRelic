using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

namespace LocalVrcw.Udon
{
    public sealed class RuntimeDirector : MonoBehaviour
    {
        public readonly List<UdonRuntime> runtimes = new List<UdonRuntime>();
        public readonly Dictionary<VRC.Udon.ProgramSources.SerializedUdonProgramAsset, RawProgram> programs = new Dictionary<VRC.Udon.ProgramSources.SerializedUdonProgramAsset, RawProgram>();
        public readonly List<string> loadFailures = new List<string>();
        HashSet<Collider> inside = new HashSet<Collider>();
        public bool ready;
        public ExternalWorldProfile Profile { get; private set; }
        public void Import(Scene scene, Camera camera, ExternalWorldProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            LocalPlayer.Instance = new LocalPlayer { camera = camera };
            var sources = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UdonBehaviour>(true)).ToArray();
            foreach (var source in sources)
            {
                var runtime = source.gameObject.AddComponent<UdonRuntime>(); runtime.Attach(source); runtimes.Add(runtime);
            }
            foreach (var runtime in runtimes)
                try
                {
                    var asset = runtime.Source.serializedProgramAsset;
                    if (!programs.TryGetValue(asset, out var program)) { program = new RawProgram(asset); programs.Add(asset, program); }
                    runtime.Initialize(program);
                }
                catch (Exception ex) { loadFailures.Add(runtime.gameObject.name + ": " + ex); }
            if (loadFailures.Count != 0) throw new System.IO.InvalidDataException("Udon import failures: " + string.Join("\n", loadFailures.Take(5)));
            foreach (var runtime in runtimes) NativePortalPolicy.Apply(runtime);
            ClassroomCompatibility.Apply(scene, this);
            // The raw includes its own legacy StandaloneInputModule. Retain
            // native UI objects, but never let its Submit axis see Space/Jump.
            foreach (var module in UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.BaseInputModule>(FindObjectsInactive.Include)) module.enabled = false;
            foreach (var events in UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include))
            {
                events.sendNavigationEvents = false; events.firstSelectedGameObject = null;
                events.SetSelectedGameObject(null);
            }
            var player = new GameObject("Independent Local Player");
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.SetPositionAndRotation(camera.transform.position - Vector3.up * 1.2f, camera.transform.rotation);
            camera.transform.SetParent(player.transform, true);
            var controller = player.AddComponent<CharacterController>();
            controller.radius = .25f; controller.height = 1.35f; controller.center = Vector3.up * .675f;
            controller.stepOffset = .25f; controller.skinWidth = .025f;
            var interactions = player.AddComponent<AniqueMuseum.UdonInteractor>(); interactions.playerCamera = camera;
            interactions.surfaces = new NativePickupSurfaces(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VRC.SDK3.Components.VRCPickup>(true)));
            var locomotion = player.AddComponent<AniqueMuseum.FPSCameraController>(); locomotion.playerCamera = camera;
            LocalPlayer.Instance.controller = locomotion;
            player.AddComponent<AniqueMuseum.MuseumViewSettings>();
            var xr = player.AddComponent<AniqueMuseum.MuseumXRPlayer>();
            player.AddComponent<AniqueMuseum.MuseumXRKoseiPresentation>().Initialize(xr);
            player.AddComponent<AniqueMuseum.UdonTriggerRouter>();
            var hold = camera.gameObject.AddComponent<RuntimePickup>(); hold.view = camera;
            if (UnityEngine.EventSystems.EventSystem.current == null)
            { var events = gameObject.AddComponent<UnityEngine.EventSystems.EventSystem>(); events.sendNavigationEvents = false; }
            var uiRaycasters = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true))
                .Where(c => c.isRootCanvas).Select(c =>
                {
                    c.worldCamera = camera;
                    return c.GetComponent<UnityEngine.UI.GraphicRaycaster>() ?? c.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }).ToArray();
            // No StandaloneInputModule: Space/Jump is never mapped to UI Submit.
            ready = true;
            foreach (var runtime in runtimes) runtime.Begin();
            AniqueMuseum.MuseumAudioPolicy.ApplyNow();
            player.AddComponent<AniqueMuseum.MuseumBgmAreaGuard>();
            StartCoroutine(InitialRespawn());
        }
        System.Collections.IEnumerator InitialRespawn()
        {
            yield return null;
            foreach (var runtime in runtimes)
                if (runtime.Started && runtime.Source.isActiveAndEnabled)
                    runtime.SendCustomEvent("_onPlayerRespawn", LocalPlayer.Instance, "onPlayerRespawnPlayer");
        }
        void OnDestroy() { if (LocalPlayer.Instance?.controller != null) LocalPlayer.Instance.controller.GetComponent<AniqueMuseum.UdonInteractor>()?.surfaces?.Dispose(); }
        void Update()
        {
            if (!ready) return;
            foreach (var runtime in runtimes) runtime.Tick();
            if (LocalPlayer.Instance.controller != null) return; // Mature trigger router owns exact capsule transitions.
            Vector3 foot = LocalPlayer.Instance.Position;
            var current = new HashSet<Collider>(Physics.OverlapCapsule(foot + Vector3.up * 0.25f, foot + Vector3.up * 1.15f, 0.2f, ~0, QueryTriggerInteraction.Collide).Where(c => c.isTrigger));
            foreach (var collider in current)
                foreach (var runtime in Routes(collider))
                    runtime.SendCustomEvent(inside.Contains(collider) ? "_onPlayerTriggerStay" : "_onPlayerTriggerEnter", LocalPlayer.Instance,
                        inside.Contains(collider) ? "onPlayerTriggerStayPlayer" : "onPlayerTriggerEnterPlayer");
            foreach (var collider in inside)
                if (collider != null && !current.Contains(collider))
                    foreach (var runtime in Routes(collider)) runtime.SendCustomEvent("_onPlayerTriggerExit", LocalPlayer.Instance, "onPlayerTriggerExitPlayer");
            inside = current;
        }
        static UdonRuntime[] Routes(Collider collider)
        {
            for (var parent = collider.transform; parent != null; parent = parent.parent)
            { var hosts = parent.GetComponents<UdonRuntime>(); if (hosts.Length > 0) return hosts; }
            return Array.Empty<UdonRuntime>();
        }
        void LateUpdate() { if (ready) foreach (var rt in runtimes) if (rt.Started && rt.Source.isActiveAndEnabled) { rt.SendCustomEvent("_lateUpdate"); rt.SendCustomEvent("_postLateUpdate"); } }
        void FixedUpdate() { if (ready) foreach (var rt in runtimes) if (rt.Started && rt.Source.isActiveAndEnabled) rt.SendCustomEvent("_fixedUpdate"); }
        public VmReport Report()
        {
            return new VmReport
            {
                loaded = runtimes.Count(r => r.Loaded), programs = programs.Count, loadFailures = loadFailures.ToArray(),
                variables = runtimes.Sum(r => r.variableCount), boundReferences = runtimes.Sum(r => r.boundReferences),
                referenceIdentitiesVerified = runtimes.Sum(r => r.referenceIdentitiesVerified),
                started = runtimes.Count(r => r.Started), dispatched = runtimes.Sum(r => r.dispatchedEvents), faults = runtimes.Sum(r => r.faults),
                unimplemented = UdonExterns.unimplemented, blockedExternalActions = UdonExternImpl.blockedExternalActions,
                programTrees = programs.Select(p => new TreeDigest { name = p.Key.name, fingerprint = p.Value.document.Fingerprint(),
                    bytecodeSha256 = Hash(p.Value.bytecode), entries = p.Value.entryMap.Count, symbols = p.Value.symbolMap.Count, heap = p.Value.heapMap.Count }).OrderBy(p => p.name).ToArray(),
                variableTrees = runtimes.Where(r => r.Variables != null).Select(r => r.Variables.Fingerprint()).OrderBy(h => h, StringComparer.Ordinal).ToArray(),
                faultDetails = runtimes.Where(r => r.faults > 0).Select(r => r.gameObject.name + " [" + r.programHash + "]: " + string.Join("\n", r.faultMessages)).ToArray(),
                unsupportedSignatures = UdonExterns.unimplementedSigs.ToArray()
            };
        }
        static string Hash(byte[] b) { using var hash = System.Security.Cryptography.SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(b)).Replace("-", ""); }
    }
    [Serializable] public sealed class TreeDigest { public string name, fingerprint, bytecodeSha256; public int entries, symbols, heap; }
    [Serializable] public sealed class VmReport
    {
        public int loaded, programs, variables, boundReferences, referenceIdentitiesVerified, started, dispatched, faults, unimplemented, blockedExternalActions;
        public string[] loadFailures, variableTrees, faultDetails, unsupportedSignatures;
        public TreeDigest[] programTrees;
    }
}
