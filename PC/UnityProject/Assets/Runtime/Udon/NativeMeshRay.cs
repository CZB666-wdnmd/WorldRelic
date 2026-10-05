using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalVrcw.Udon
{
    // Read-only geometry queries in an isolated physics scene. Native broad
    // instruction/sensor colliders never become a pickup's selectable surface.
    public sealed class NativeMeshRay
    {
        readonly Scene queryScene;
        readonly Dictionary<Mesh, MeshCollider> colliders = new Dictionary<Mesh, MeshCollider>();
        public NativeMeshRay() => queryScene = SceneManager.CreateScene("Independent Mesh Query " + System.Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        public bool TryHit(Ray worldRay, Transform source, Mesh mesh, float maximum, out Vector3 contact, out float distance)
        {
            contact = default; distance = maximum;
            if (mesh == null || mesh.vertexCount == 0) return false;
            mesh = RuntimeMeshReadback.Readable(mesh);
            if (!colliders.TryGetValue(mesh, out var collider))
            {
                var go = new GameObject("Private mesh query");
                SceneManager.MoveGameObjectToScene(go, queryScene);
                collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                colliders.Add(mesh, collider);
            }
            Vector3 origin = source.InverseTransformPoint(worldRay.origin);
            Vector3 direction = source.InverseTransformVector(worldRay.direction);
            if (direction.sqrMagnitude < 1e-12f) return false;
            bool backfaces = Physics.queriesHitBackfaces;
            bool hit;
            RaycastHit result;
            try
            {
                Physics.queriesHitBackfaces = true;
                hit = collider.Raycast(new Ray(origin, direction.normalized), out result, float.PositiveInfinity);
            }
            finally { Physics.queriesHitBackfaces = backfaces; }
            if (!hit) return false;
            contact = source.TransformPoint(result.point);
            distance = Vector3.Dot(contact - worldRay.origin, worldRay.direction.normalized);
            return distance >= 0 && distance <= maximum;
        }
        public void Dispose() { if (queryScene.IsValid() && queryScene.isLoaded) SceneManager.UnloadSceneAsync(queryScene); }
    }
}
