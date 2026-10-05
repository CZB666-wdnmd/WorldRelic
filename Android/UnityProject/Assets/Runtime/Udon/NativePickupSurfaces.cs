using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;

namespace LocalVrcw.Udon
{
    // Geometry from the user's live bundle, queried in private physics scenes.
    // Long import pivots, info triggers and empty mesh bounds are not grab surfaces.
    public sealed class NativePickupSurfaces
    {
        readonly Dictionary<VRCPickup, MeshFilter[]> meshes = new Dictionary<VRCPickup, MeshFilter[]>();
        readonly NativeMeshRay rayQuery = new NativeMeshRay();
        readonly Scene scene = SceneManager.CreateScene("Independent contact queries", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        readonly Dictionary<Transform, Transform> poses = new Dictionary<Transform, Transform>();
        readonly Dictionary<MeshFilter, MeshCollider> shapes = new Dictionary<MeshFilter, MeshCollider>();
        readonly SphereCollider palm;
        static readonly Vector3[] axes = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        public IEnumerable<VRCPickup> Pickups => meshes.Keys;
        public NativePickupSurfaces(IEnumerable<VRCPickup> pickups)
        {
            foreach (var p in pickups) Register(p);
            var go = new GameObject("Private sphere contact"); SceneManager.MoveGameObjectToScene(go, scene);
            palm = go.AddComponent<SphereCollider>();
        }
        public void Register(VRCPickup pickup)
        {
            meshes[pickup] = pickup.GetComponentsInChildren<MeshFilter>(true)
                .Where(m => m.sharedMesh != null && m.GetComponentInParent<VRCPickup>(true) == pickup).ToArray();
            foreach (var mesh in meshes[pickup].Select(m => m.sharedMesh).Distinct()) RuntimeMeshReadback.Readable(mesh);
        }
        bool Visible(MeshFilter m) => m != null && m.gameObject.activeInHierarchy &&
            m.TryGetComponent<Renderer>(out var r) && r.enabled && !r.forceRenderingOff;
        public bool TryRay(VRCPickup pickup, Ray ray, float maximum, out Vector3 point, out float distance)
        {
            point = default; distance = maximum; bool found = false;
            if (!meshes.TryGetValue(pickup, out var filters)) return false;
            foreach (var m in filters)
                if (Visible(m) && m.GetComponent<Renderer>().bounds.IntersectRay(ray) &&
                    rayQuery.TryHit(ray, m.transform, m.sharedMesh, distance, out var contact, out float d))
                { found = true; point = contact; distance = d; }
            return found;
        }
        Transform Pose(Transform source)
        {
            if (!poses.TryGetValue(source, out var clone))
            {
                clone = new GameObject("Private geometry pose").transform; SceneManager.MoveGameObjectToScene(clone.gameObject, scene);
                if (source.parent != null) clone.SetParent(Pose(source.parent), false);
                poses[source] = clone;
            }
            else if (source.parent != null) Pose(source.parent);
            clone.localPosition = source.localPosition; clone.localRotation = source.localRotation; clone.localScale = source.localScale;
            return clone;
        }
        public bool TryNear(VRCPickup pickup, Vector3 center, float radius, out Vector3 point, out float distance)
        {
            point = default; distance = radius; bool found = false;
            if (!meshes.TryGetValue(pickup, out var filters)) return false;
            palm.radius = radius;
            foreach (var m in filters)
            {
                if (!Visible(m) || (m.GetComponent<Renderer>().bounds.ClosestPoint(center) - center).sqrMagnitude > radius * radius) continue;
                Transform pose = Pose(m.transform);
                if (!shapes.TryGetValue(m, out var shape))
                { shape = pose.gameObject.AddComponent<MeshCollider>(); shape.sharedMesh = RuntimeMeshReadback.Readable(m.sharedMesh); shapes[m] = shape; }
                // Sphere/non-convex native mesh is a supported penetration pair.
                if (Physics.ComputePenetration(palm, center, Quaternion.identity, shape, pose.position, pose.rotation, out var direction, out float depth))
                {
                    float d = Mathf.Max(0, radius - depth);
                    if (d <= distance) { distance = d; point = center - direction * d; found = true; }
                }
                // ComputePenetration rejects triangle backfaces; ray tests cover
                // two-sided flat exhibit cards without enlarging physical reach.
                foreach (var axis in axes)
                    if (rayQuery.TryHit(new Ray(center, axis), m.transform, m.sharedMesh, distance, out var hit, out float d))
                    { distance = d; point = hit; found = true; }
            }
            return found;
        }
        public void Dispose() { rayQuery.Dispose(); if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene); RuntimeMeshReadback.Clear(); }
    }
}
