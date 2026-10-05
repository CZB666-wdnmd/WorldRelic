using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Udon;

namespace LocalVrcw.Udon
{
    // Each bridge maps one native behaviour, never just a GameObject or object name.
    public sealed class UdonRuntime : MonoBehaviour, IUdonRuntimeBridge
    {
        public UdonBehaviour Source { get; private set; }
        public RawProgram Program { get; private set; }
        public UdonVM VM { get; private set; }
        public OdinDocument Variables { get; private set; }
        public string programHash => Source.serializedProgramAsset.name;
        public string InteractionText { get => Source.interactText; set => Source.interactText = value; }
        public bool Loaded => VM != null;
        public int variableCount, boundReferences, dispatchedEvents, faults;
        public int referenceIdentitiesVerified;
        public bool Started { get; private set; }
        bool previouslyActive;
        readonly Dictionary<string, object> variables = new Dictionary<string, object>();
        public readonly List<string> faultMessages = new List<string>();
        readonly List<Timer> timers = new List<Timer>();
        sealed class Timer { public string name; public float time; public int frame; }
        static int depth, dispatches;
        static readonly HashSet<string> active = new HashSet<string>();

        public void Attach(UdonBehaviour source) { Source = source; source.RuntimeBridge = this; enabled = source.enabled; }
        public void Initialize(RawProgram program)
        {
            Program = program;
            Variables = new OdinDocument(Convert.FromBase64String(Source.serializedPublicVariablesBytesString));
            var reader = new OdinValues(Variables, Source.publicVariablesUnityEngineObjects, this);
            var field = Variables.nodes.Single(n => n.name == "Variables");
            foreach (var element in field.First.children.Where(n => n.arrayIndex >= 0))
            {
                var variable = element.First;
                string symbol = (string)variable.Named("SymbolName").scalar;
                if (!Program.symbolMap.ContainsKey(symbol)) throw new System.IO.InvalidDataException("Unknown public symbol " + symbol);
                variables.Add(symbol, reader.Read(variable.Named("Value"))); variableCount++;
            }
            boundReferences = Variables.nodes.Count(n => n.IsExternal);
            foreach (var reference in Variables.nodes.Where(n => n.IsExternal))
            {
                var original = Source.publicVariablesUnityEngineObjects[Convert.ToInt32(reference.scalar)];
                object actual = reader.Read(reference);
                bool identical = original is UdonBehaviour behaviour ?
                    actual is UdonRuntime bridge && ReferenceEquals(bridge.Source, behaviour) : ReferenceEquals(actual, original);
                if (!identical) throw new System.IO.InvalidDataException("Native reference identity changed");
                referenceIdentitiesVerified++;
            }
            VM = new UdonVM(this, program);
            ApplyVariables();
        }
        public void ApplyVariables() { foreach (var pair in variables) VM.Set(Program.symbolMap[pair.Key], pair.Value); }
        public object ConvertConst(uint address, OdinNode node) => constantReader.Read(node);
        OdinValues constantReader;
        public void ResetConstantReader() => constantReader = new OdinValues(Program.document, Source.serializedProgramAsset.programUnityEngineObjects, this);
        public bool HasEvent(string name) => Program != null && Program.entryMap.ContainsKey(name);
        public bool HasSymbol(string name) => Program != null && Program.symbolMap.ContainsKey(name);
        public object GetSymbol(string name) => HasSymbol(name) ? VM.Get(Program.symbolMap[name]) : null;
        public void SetSymbol(string name, object value) { if (HasSymbol(name)) VM.Set(Program.symbolMap[name], value); }
        public void Interact() => SendCustomEvent("_interact");
        public void Begin()
        {
            if (Started || !Loaded || !Source.isActiveAndEnabled) return;
            if (!previouslyActive) { previouslyActive = true; SendCustomEvent("_onEnable"); }
            Started = true; SendCustomEvent("_start");
            SendCustomEvent("_onPlayerJoined", LocalPlayer.Instance, "onPlayerJoinedPlayer");
        }
        public void SendCustomEvent(string name) => SendCustomEvent(name, null, null);
        public void SendCustomEvent(string name, object value, string parameter)
        {
            if (!HasEvent(name)) return;
            bool root = depth == 0;
            if (root) { active.Clear(); dispatches = 0; }
            string key = GetEntityId().ToString() + ":" + name;
            if (depth >= 64 || ++dispatches > 256 || !active.Add(key)) { LogError("Event recursion/budget " + name); return; }
            depth++;
            try
            {
                if (parameter != null) SetSymbol(parameter, value);
                dispatchedEvents++; VM.TryRun(name);
            }
            catch (Exception ex) { LogError(name + ": " + ex); }
            finally { depth--; active.Remove(key); }
        }
        public void SendCustomEventDelayed(string name, float seconds) => timers.Add(new Timer { name = name, time = Time.time + Mathf.Max(0, seconds), frame = Time.frameCount + 1 });
        public void SendCustomEventFrames(string name, int frames) => timers.Add(new Timer { name = name, time = 0, frame = Time.frameCount + Mathf.Max(1, frames) });
        public void Tick()
        {
            if (!Loaded) return;
            if (enabled != Source.enabled) enabled = Source.enabled;
            bool currentActive = enabled && Source.isActiveAndEnabled;
            if (currentActive != previouslyActive)
            { previouslyActive = currentActive; SendCustomEvent(currentActive ? "_onEnable" : "_onDisable"); }
            if (!currentActive) return;
            Begin();
            for (int i = timers.Count - 1; i >= 0; i--)
                if (timers[i].frame <= Time.frameCount && timers[i].time <= Time.time)
                { string name = timers[i].name; timers.RemoveAt(i); SendCustomEvent(name); }
            SendCustomEvent("_update");
        }
        void OnCollisionEnter(Collision collision) => SendCustomEvent("_onCollisionEnter", collision, "onCollisionEnterOther");
        void OnCollisionStay(Collision collision) => SendCustomEvent("_onCollisionStay", collision, "onCollisionStayOther");
        void OnCollisionExit(Collision collision) => SendCustomEvent("_onCollisionExit", collision, "onCollisionExitOther");
        void OnTriggerEnter(Collider collider) { if (collider.GetComponentInParent<AniqueMuseum.FPSCameraController>() == null) SendCustomEvent("_onTriggerEnter", collider, "onTriggerEnterOther"); }
        void OnTriggerStay(Collider collider) { if (collider.GetComponentInParent<AniqueMuseum.FPSCameraController>() == null) SendCustomEvent("_onTriggerStay", collider, "onTriggerStayOther"); }
        void OnTriggerExit(Collider collider) { if (collider.GetComponentInParent<AniqueMuseum.FPSCameraController>() == null) SendCustomEvent("_onTriggerExit", collider, "onTriggerExitOther"); }
        public void Log(string msg) => Debug.Log("[RawUdon:" + programHash + "] " + msg, this);
        public void LogError(string msg)
        {
            faults++; if (faultMessages.Count < 16) faultMessages.Add(msg);
            Debug.LogWarning("[RawUdon:" + programHash + "] " + msg, this);
        }
    }
}
