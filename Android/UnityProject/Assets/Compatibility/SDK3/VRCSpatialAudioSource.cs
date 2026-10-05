using UnityEngine;

namespace VRC.SDK3.Components
{
    public sealed class VRCSpatialAudioSource : MonoBehaviour
    {
        public float Gain;
        public float Far;
        public float Near;
        public float VolumetricRadius;
        public bool EnableSpatialization;
        public bool UseAudioSourceVolumeCurve;
    }
}
