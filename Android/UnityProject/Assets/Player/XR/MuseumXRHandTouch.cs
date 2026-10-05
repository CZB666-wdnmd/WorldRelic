using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // A small palm volume is shared by hover and action dispatch. No distant
    // ray fallback: the blue object is exactly the object the trigger will use.
    public sealed class MuseumXRHandTouch : MonoBehaviour
    {
        public Transform Focus { get; private set; }
        public VRCPickup Pickup { get; private set; }
        public bool UiFocused { get; private set; }
        public Vector3 ContactCenter => transform.TransformPoint(new Vector3(0, 0, 0.055f));
        readonly Collider[] overlaps = new Collider[256];
        readonly RaycastHit[] blockers = new RaycastHit[64];
        MuseumXRPointer pointer;
        UdonInteractor interactor;
        Transform playerRoot;

        public void Initialize(MuseumXRPointer uiPointer, UdonInteractor source, Transform root)
        { pointer = uiPointer; interactor = source; playerRoot = root; }

        public void Refresh(float radius, Camera camera)
        {
            Focus = null; Pickup = null; UiFocused = false;
            Vector3 center = ContactCenter;
            float best = float.PositiveInfinity;
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, interactor.interactionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var c = overlaps[i];
                if (c == null || c.transform.IsChildOf(playerRoot)) continue;
                Transform target = interactor.ResolveTouchTarget(c, out var pickup);
                if (target == null) continue;
                Vector3 point = c.ClosestPoint(center);
                if (pickup != null && interactor.surfaces != null && !interactor.surfaces.TryNear(pickup, center, radius, out point, out _)) continue;
                float distance = Vector3.Distance(center, point);
                if (distance > radius || distance >= best || !Reachable(center, point, target)) continue;
                best = distance; Focus = target; Pickup = pickup;
            }
            if (interactor.surfaces != null)
                foreach (var p in interactor.surfaces.Pickups)
                    if (p != null && p.isActiveAndEnabled && p.pickupable && !p.IsHeld &&
                        interactor.surfaces.TryNear(p, center, radius, out var point, out float distance) &&
                        distance < best && Reachable(center, point, p.transform))
                    { best = distance; Focus = p.transform; Pickup = p; }
            float uiDistance = pointer.UpdateTouch(center, radius, camera);
            if (pointer.Target != null && uiDistance <= best + 0.001f)
            {
                var control = pointer.Target.GetComponentInParent<Selectable>();
                Vector3 local = control.transform.InverseTransformPoint(center);
                local.z = 0;
                if (Reachable(center, control.transform.TransformPoint(local), control.transform))
                { Focus = control.transform; Pickup = null; UiFocused = true; }
            }
        }

        bool Reachable(Vector3 center, Vector3 point, Transform target)
        {
            Vector3 delta = point - center;
            if (delta.magnitude < 0.005f) return true;
            int count = Physics.RaycastNonAlloc(center, delta.normalized, blockers, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = blockers[i].collider;
                if (c == null || c.transform.IsChildOf(playerRoot) || c.transform.IsChildOf(target)) continue;
                var p = c.GetComponentInParent<VRCPickup>();
                if (p != null && p.IsHeld) continue;
                if (blockers[i].distance < delta.magnitude - 0.01f) return false;
            }
            return true;
        }

        public bool Grab(int hand) => Pickup != null && Pickup.TryPickup(hand);
        public bool Operate() => UiFocused ? pointer.Press() : interactor.TryInteractTouch(Focus) > 0;
        public void ReleaseTrigger() { pointer.Release(); }
        public void Clear()
        { Focus = null; Pickup = null; UiFocused = false; if (pointer != null) pointer.Cancel(); }
        void OnDisable() { Clear(); }
    }
}
