using UnityEngine;

namespace VRC.SDK3.Dynamics.PhysBone.Components
{
    public sealed class VRCPhysBoneCollider : MonoBehaviour
    {
        public Transform rootTransform;
        public int shapeType;
        public bool insideBounds;
        public float radius;
        public float height;
        public Vector3 position;
        public Quaternion rotation;
    }
}
