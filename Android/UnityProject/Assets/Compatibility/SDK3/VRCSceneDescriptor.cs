using UnityEngine;

namespace VRC.SDK3.Components
{
    // Independently authored data adapters. No official SDK implementation.
    public sealed class VRCSceneDescriptor : MonoBehaviour
    {
        public Transform[] spawns;
        public float spawnRadius;
        public int spawnOrder;
        public int spawnOrientation;
        public Camera ReferenceCamera;
        public float RespawnHeightY;
        public Vector3 gravity;
        public bool autoSpatializeAudioSources;
        public Material[] DynamicMaterials;
        public string unityVersion;
    }
}
