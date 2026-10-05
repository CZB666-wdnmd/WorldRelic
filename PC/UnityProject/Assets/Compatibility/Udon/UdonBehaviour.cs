using UnityEngine;

// Independently authored, passive serialization adapter, not the VRChat SDK.
// Execution is supplied explicitly by the independently authored runtime bridge.
namespace VRC.Udon
{
    public sealed class UdonBehaviour : MonoBehaviour
    {
        public Vector3 interactTextPlacement;
        public string interactText;
        public GameObject interactTextGO;
        public float proximity;
        public bool SynchronizePosition;
        public bool AllowCollisionOwnershipTransfer;
        public bool Reliable;
        public int _syncMethod;
        public ProgramSources.SerializedUdonProgramAsset serializedProgramAsset;
        public string serializedPublicVariablesBytesString;
        public Object[] publicVariablesUnityEngineObjects;
        public int publicVariablesSerializationDataFormat;
        [System.NonSerialized] public IUdonRuntimeBridge RuntimeBridge;
        public void Interact() => RuntimeBridge?.SendCustomEvent("_interact");
        public void SendCustomEvent(string eventName) => RuntimeBridge?.SendCustomEvent(eventName);
    }
    public interface IUdonRuntimeBridge { void SendCustomEvent(string eventName); }
}
