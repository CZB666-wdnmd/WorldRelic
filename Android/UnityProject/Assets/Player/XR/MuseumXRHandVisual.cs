using UnityEngine;
using UnityEngine.Rendering;

namespace AniqueMuseum
{
    // Lightweight glove with an articulated thumb and four three-joint fingers.
    // Its pose is driven only by controller grip; it does not require optical
    // hand tracking or a controller-specific skeletal input extension.
    public sealed class MuseumXRHandVisual : MonoBehaviour
    {
        readonly Transform[,] joints = new Transform[5, 3];
        Material glove, cuff;
        Mesh palmMesh;
        Transform thumb;
        float side;
        public float GripPose { get; private set; }
        public bool Visible => gameObject.activeSelf;

        public void Initialize(bool left)
        {
            side = left ? -1 : 1;
            var shader = Resources.Load<Shader>("MuseumXRHand");
            glove = new Material(shader) { name = "XR glove", hideFlags = HideFlags.DontSave };
            glove.SetColor("_Color", new Color(0.82f, 0.87f, 0.91f, 1));
            cuff = new Material(shader) { name = "XR glove cuff", hideFlags = HideFlags.DontSave };
            cuff.SetColor("_Color", new Color(0.14f, 0.23f, 0.32f, 1));
            var palm = new GameObject("Palm");
            palm.transform.SetParent(transform, false);
            palm.transform.localPosition = new Vector3(0, 0, 0.025f);
            palmMesh = RoundedPalm(new Vector3(0.041f, 0.0165f, 0.043f), 0.012f);
            palm.AddComponent<MeshFilter>().sharedMesh = palmMesh;
            var palmRenderer = palm.AddComponent<MeshRenderer>();
            palmRenderer.sharedMaterial = glove;
            palmRenderer.shadowCastingMode = ShadowCastingMode.Off; palmRenderer.receiveShadows = false;
            Shape("Wrist", PrimitiveType.Sphere, transform, new Vector3(0, 0, -0.027f), Quaternion.identity,
                new Vector3(0.058f, 0.036f, 0.042f), cuff);
            float[] lengths = { 0.070f, 0.080f, 0.075f, 0.058f };
            float[] starts = { 0.055f, 0.060f, 0.057f, 0.046f };
            for (int finger = 0; finger < 4; finger++)
            {
                Transform parent = transform;
                for (int joint = 0; joint < 3; joint++)
                {
                    float length = lengths[finger] * (joint == 0 ? 0.44f : joint == 1 ? 0.32f : 0.24f);
                    var pivot = new GameObject("Finger " + finger + " joint " + joint).transform;
                    pivot.SetParent(parent, false);
                    pivot.localPosition = joint == 0 ? new Vector3(side * (-0.028f + finger * 0.018f), 0, starts[finger]) :
                        new Vector3(0, 0, lengths[finger] * (joint == 1 ? 0.44f : 0.32f));
                    joints[finger, joint] = pivot;
                    Shape("Glove segment", PrimitiveType.Capsule, pivot, new Vector3(0, 0, length * 0.5f),
                        Quaternion.Euler(90, 0, 0), new Vector3(0.016f, (length + 0.008f) * 0.5f, 0.016f), glove);
                    parent = pivot;
                }
            }
            Transform thumbParent = transform;
            for (int joint = 0; joint < 3; joint++)
            {
                var pivot = new GameObject("Thumb joint " + joint).transform;
                pivot.SetParent(thumbParent, false);
                pivot.localPosition = joint == 0 ? new Vector3(-side * 0.033f, -0.005f, 0.014f) : new Vector3(0, 0, 0.023f);
                if (joint == 0) thumb = pivot;
                joints[4, joint] = pivot;
                Shape("Thumb segment", PrimitiveType.Capsule, pivot, new Vector3(0, 0, 0.0115f),
                    Quaternion.Euler(90, 0, 0), new Vector3(0.019f, 0.0155f, 0.019f), glove);
                thumbParent = pivot;
            }
            SetPose(0, true, 0);
        }

        static Mesh RoundedPalm(Vector3 extents, float rounding)
        {
            const int steps = 8, stride = steps + 1, faceVertices = stride * stride;
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left };
            Vector3[] rights = { Vector3.right, Vector3.left, Vector3.right, Vector3.right, Vector3.back, Vector3.forward };
            Vector3[] ups = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };
            var vertices = new Vector3[6 * faceVertices];
            var vertexNormals = new Vector3[vertices.Length];
            var triangles = new int[6 * steps * steps * 6];
            Vector3 inner = extents - Vector3.one * rounding;
            int index = 0;
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y <= steps; y++)
                    for (int x = 0; x <= steps; x++)
                    {
                        Vector3 raw = Vector3.Scale(normals[face] + rights[face] * (2f * x / steps - 1) + ups[face] * (2f * y / steps - 1), extents);
                        Vector3 core = new Vector3(Mathf.Clamp(raw.x, -inner.x, inner.x), Mathf.Clamp(raw.y, -inner.y, inner.y), Mathf.Clamp(raw.z, -inner.z, inner.z));
                        Vector3 normal = (raw - core).normalized;
                        int v = face * faceVertices + y * stride + x;
                        vertices[v] = core + normal * rounding; vertexNormals[v] = normal;
                    }
                for (int y = 0; y < steps; y++)
                    for (int x = 0; x < steps; x++)
                    {
                        int v = face * faceVertices + y * stride + x;
                        triangles[index++] = v; triangles[index++] = v + 1; triangles[index++] = v + stride;
                        triangles[index++] = v + 1; triangles[index++] = v + stride + 1; triangles[index++] = v + stride;
                    }
            }
            return new Mesh { name = "Rounded glove palm", vertices = vertices, normals = vertexNormals, triangles = triangles };
        }

        static void Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation); go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        public void SetPose(float grip, bool tracked, float deltaTime)
        {
            gameObject.SetActive(tracked);
            float target = Mathf.Clamp01(grip);
            GripPose = deltaTime <= 0 ? target : Mathf.MoveTowards(GripPose, target, deltaTime * 12);
            for (int finger = 0; finger < 4; finger++)
                for (int joint = 0; joint < 3; joint++)
                    joints[finger, joint].localRotation = Quaternion.Euler(Mathf.Lerp(4, joint == 0 ? 72 : joint == 1 ? 92 : 68, GripPose), 0, 0);
            thumb.localRotation = Quaternion.Euler(Mathf.Lerp(10, 48, GripPose), -side * Mathf.Lerp(55, 15, GripPose), -side * Mathf.Lerp(15, 40, GripPose));
            joints[4, 1].localRotation = Quaternion.Euler(Mathf.Lerp(5, 45, GripPose), 0, 0);
            joints[4, 2].localRotation = Quaternion.Euler(Mathf.Lerp(5, 55, GripPose), 0, 0);
        }

        void OnDestroy()
        {
            if (glove != null) Destroy(glove);
            if (cuff != null) Destroy(cuff);
            if (palmMesh != null) Destroy(palmMesh);
        }
    }
}
