using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // Soft camera attraction only while a flat-gamepad player aims. It never
    // dispatches an interaction, changes range, or replaces the real ray hit.
    public sealed class MuseumGamepadAimAssist : MonoBehaviour
    {
        public float captureDegrees = 3.5f;
        public float maxDegreesPerSecond = 8f;
        public Transform Target { get; private set; }
        readonly RaycastHit[] candidates = new RaycastHit[128];
        readonly RaycastHit[] blockers = new RaycastHit[128];
        readonly Collider[] nearCandidates = new Collider[64];
        Selectable[] controls = new Selectable[64];
        FPSCameraController player;
        UdonInteractor interactor;
        public void Initialize(FPSCameraController source, UdonInteractor interactions)
        { player = source; interactor = interactions; }
        public void Clear() { Target = null; }
        public void Apply(Vector2 stick, float deltaTime)
        {
            Target = null;
            if (player == null || interactor == null || player.playerCamera == null ||
                player.xrControlled || VRCPickup.IsVrActive || stick.sqrMagnitude < .0001f ||
                deltaTime <= 0 || MuseumViewSettings.BlocksDesktopInput) return;
            var view = player.playerCamera.transform;
            Vector3 origin = view.position, forward = view.forward;
            float range = interactor.maxDistance;
            float radius = Mathf.Tan(captureDegrees * Mathf.Deg2Rad) * range;
            float best = captureDegrees;
            Vector3 bestPoint = Vector3.zero;
            int count = Physics.SphereCastNonAlloc(origin, radius, forward, candidates, range,
                interactor.interactionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                ConsiderCollider(candidates[i].collider, origin, forward, stick, range, ref best, ref bestPoint);
            // SphereCast does not report bodies initially inside its volume.
            // Include near objects without changing the angular/range limits.
            int nearCount = Physics.OverlapSphereNonAlloc(origin, radius, nearCandidates,
                interactor.interactionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < nearCount; i++)
                ConsiderCollider(nearCandidates[i], origin, forward, stick, range, ref best, ref bestPoint);
            if (Selectable.allSelectableCount > controls.Length)
                controls = new Selectable[Selectable.allSelectableCount + 16];
            int uiCount = Selectable.AllSelectablesNoAlloc(controls);
            for (int i = 0; i < uiCount; i++)
            {
                var control = controls[i];
                if (control == null || !control.IsActive() || !control.IsInteractable() ||
                    !(control is Button || control is Toggle)) continue;
                var rect = control.transform as RectTransform;
                var canvas = control.GetComponentInParent<Canvas>();
                if (rect == null || canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;
                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
                var graphic = control.targetGraphic;
                if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget ||
                    graphic.depth < 0 || graphic.canvasRenderer.cull) continue;
                Vector3 projected = origin + forward * Vector3.Dot(rect.position - origin, forward);
                Vector3 local = rect.InverseTransformPoint(projected);
                Vector3 point = rect.TransformPoint(new Vector3(Mathf.Clamp(local.x, rect.rect.xMin, rect.rect.xMax),
                    Mathf.Clamp(local.y, rect.rect.yMin, rect.rect.yMax), 0));
                point = Vector3.MoveTowards(point, rect.TransformPoint(rect.rect.center), .012f);
                Consider(control.transform, point, origin, forward, stick, range, ref best, ref bestPoint);
            }
            if (Target == null || best < .03f) return;
            // Strong deliberate input wins; attraction fades instead of a snap.
            float rate = maxDegreesPerSecond * Mathf.Clamp01((1f - stick.magnitude) / .7f);
            Vector3 localDirection = view.InverseTransformDirection((bestPoint - origin).normalized);
            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float up = Mathf.Atan2(localDirection.y, Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z)) * Mathf.Rad2Deg;
            Vector2 correction = Vector2.ClampMagnitude(new Vector2(yaw, up), rate * Mathf.Min(deltaTime, .05f));
            player.ApplyFlatLook(correction);
        }
        void ConsiderCollider(Collider c, Vector3 origin, Vector3 forward, Vector2 stick, float range,
            ref float best, ref Vector3 bestPoint)
        {
            if (c == null || c.transform.IsChildOf(player.transform)) return;
            Transform target = interactor.ResolveTouchTarget(c, out _);
            if (target == null) return;
            Vector3 projected = origin + forward * Vector3.Dot(c.bounds.center - origin, forward);
            Vector3 edge = c.ClosestPoint(projected);
            // Aim inside the selectable silhouette, not at a point in air.
            Vector3 point = Vector3.MoveTowards(edge, c.bounds.center, .012f);
            Consider(target, point, origin, forward, stick, range, ref best, ref bestPoint);
        }
        void Consider(Transform target, Vector3 point, Vector3 origin, Vector3 forward, Vector2 stick,
            float range, ref float best, ref Vector3 bestPoint)
        {
            Vector3 delta = point - origin;
            float distance = delta.magnitude;
            if (distance < .08f || distance > range || Vector3.Dot(delta, forward) <= 0) return;
            float angle = Vector3.Angle(forward, delta);
            if (angle >= best || !Visible(origin, point, target)) return;
            Vector3 local = player.playerCamera.transform.InverseTransformDirection(delta.normalized);
            Vector2 toward = new Vector2(local.x, local.y).normalized;
            if (Vector2.Dot(stick.normalized, toward) < -.15f) return; // pushing away releases immediately
            // Keep an already-centred actual ray target, no pull to its centre.
            best = angle; bestPoint = point; Target = target;
        }
        bool Visible(Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 delta = point - origin;
            int count = Physics.RaycastNonAlloc(origin, delta.normalized, blockers, delta.magnitude,
                interactor.interactionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var c = blockers[i].collider;
                if (c == null || c.transform.IsChildOf(player.transform) || c.transform.IsChildOf(target)) continue;
                var pickup = c.GetComponentInParent<VRCPickup>();
                if (pickup != null && pickup.IsHeld) continue;
                if (UdonInteractor.IsInteractionSensor(c, pickup)) continue;
                var control = interactor.ResolveTouchTarget(c, out _);
                if (c.isTrigger && control == null) continue;
                if (control == target || (control != null && target.IsChildOf(control))) continue;
                if (blockers[i].distance < delta.magnitude - .005f) return false;
            }
            return true;
        }
    }
}
