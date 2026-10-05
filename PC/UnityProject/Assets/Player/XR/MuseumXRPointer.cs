using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // A controller ray, rather than a screen-centre mouse, drives existing uGUI.
    // Source Buttons, Toggles, Sliders and their UnityEvent -> Udon bindings remain intact.
    public class MuseumXRPointer : MonoBehaviour
    {
        public int pointerId = -101;
        public GameObject Target { get; private set; }
        PointerEventData data;
        GameObject hover, press, drag;
        readonly RaycastHit[] blockers = new RaycastHit[128];

        // World-space UI has no physics collider. Resolve a touched graphic
        // against its plane/rectangle, then feed the same pointer event route.
        public float UpdateTouch(Vector3 position, float radius, Camera camera)
        {
            if (EventSystem.current == null) new GameObject("XR EventSystem").AddComponent<EventSystem>();
            if (data == null) data = new PointerEventData(EventSystem.current) { pointerId = pointerId, button = PointerEventData.InputButton.Left };
            float closest = radius;
            int depth = -1;
            GameObject target = null;
            GraphicRaycaster module = null;
            Vector3 point = position, normal = Vector3.forward;
            foreach (var baseRaycaster in RaycasterManager.GetRaycasters())
            {
                var raycaster = baseRaycaster as GraphicRaycaster;
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
                var canvas = raycaster.GetComponent<Canvas>();
                if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace) continue;
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                for (int i = 0; i < graphics.Count; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.isActiveAndEnabled || !g.raycastTarget || g.depth < 0 || g.canvasRenderer.cull) continue;
                    var control = g.GetComponentInParent<Selectable>();
                    if (control == null || !control.IsActive() || !control.IsInteractable()) continue;
                    var rect = g.rectTransform;
                    Vector3 local = rect.InverseTransformPoint(position);
                    Vector3 p = rect.TransformPoint(new Vector3(Mathf.Clamp(local.x, rect.rect.xMin, rect.rect.xMax),
                        Mathf.Clamp(local.y, rect.rect.yMin, rect.rect.yMax), 0));
                    float d = Vector3.Distance(position, p);
                    if (d > closest || !g.Raycast(camera.WorldToScreenPoint(p), camera)) continue;
                    if (Mathf.Abs(d - closest) < 0.001f && g.depth < depth) continue;
                    target = g.gameObject; closest = d; depth = g.depth; point = p; normal = rect.forward; module = raycaster;
                }
            }
            SetTarget(target, closest, camera.WorldToScreenPoint(point), point, normal, module);
            return target == null ? float.PositiveInfinity : closest;
        }

        void SetTarget(GameObject target, float distance, Vector2 position, Vector3 point, Vector3 normal, GraphicRaycaster module)
        {
            Target = target;
            data.delta = position - data.position; data.position = position;
            data.pointerCurrentRaycast = new RaycastResult { gameObject = target, module = module, worldPosition = point,
                worldNormal = normal, distance = distance, screenPosition = position };
            if (hover != target)
            {
                if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
                hover = target; data.pointerEnter = target;
                if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerEnterHandler);
            }
            if (drag != null)
            {
                if (!data.dragging && data.delta.sqrMagnitude > 0)
                { ExecuteEvents.Execute(drag, data, ExecuteEvents.beginDragHandler); data.dragging = true; }
                if (data.dragging) ExecuteEvents.Execute(drag, data, ExecuteEvents.dragHandler);
            }
        }

        public float UpdateRay(Ray ray, float maxDistance, Camera camera, Transform playerRoot)
        {
            if (EventSystem.current == null) new GameObject("XR EventSystem").AddComponent<EventSystem>();
            if (data == null) data = new PointerEventData(EventSystem.current) { pointerId = pointerId, button = PointerEventData.InputButton.Left };
            float closest = maxDistance;
            int count = Physics.RaycastNonAlloc(ray, blockers, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = blockers[i].collider;
                if (c == null || c.transform.IsChildOf(playerRoot)) continue;
                var pickup = c.GetComponentInParent<VRCPickup>();
                if (pickup != null && pickup.IsHeld) continue;
                closest = Mathf.Min(closest, blockers[i].distance);
            }
            GameObject target = null; int depth = -1;
            GraphicRaycaster module = null;
            Vector3 point = ray.GetPoint(closest), normal = -ray.direction;
            foreach (var baseRaycaster in RaycasterManager.GetRaycasters())
            {
                var raycaster = baseRaycaster as GraphicRaycaster;
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
                var canvas = raycaster.GetComponent<Canvas>();
                if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace) continue;
                var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
                for (int graphicIndex = 0; graphicIndex < graphics.Count; graphicIndex++)
                {
                    var g = graphics[graphicIndex];
                    if (g == null || !g.isActiveAndEnabled || !g.raycastTarget || g.depth < 0 || g.canvasRenderer.cull) continue;
                    var rect = g.rectTransform;
                    var plane = new Plane(rect.forward, rect.position);
                    if (!plane.Raycast(ray, out float d) || d < 0 || d > closest + 0.001f) continue;
                    Vector3 p = ray.GetPoint(d);
                    if (!rect.rect.Contains((Vector2)rect.InverseTransformPoint(p))) continue;
                    if (!g.Raycast(camera.WorldToScreenPoint(p), camera)) continue;
                    if (Mathf.Abs(d - closest) < 0.001f && g.depth < depth) continue;
                    target = g.gameObject; closest = d; depth = g.depth; point = p; normal = rect.forward; module = raycaster;
                }
            }
            Target = target;
            Vector2 position = camera.WorldToScreenPoint(point);
            data.delta = position - data.position; data.position = position;
            data.pointerCurrentRaycast = new RaycastResult { gameObject = target, module = module, worldPosition = point, worldNormal = normal, distance = closest, screenPosition = position };
            if (hover != target)
            {
                if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
                hover = target; data.pointerEnter = target;
                if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerEnterHandler);
            }
            if (drag != null)
            {
                if (!data.dragging && data.delta.sqrMagnitude > 0)
                {
                    ExecuteEvents.Execute(drag, data, ExecuteEvents.beginDragHandler); data.dragging = true;
                }
                if (data.dragging) ExecuteEvents.Execute(drag, data, ExecuteEvents.dragHandler);
            }
            return closest;
        }

        public bool Press()
        {
            if (Target == null || data == null) return false;
            data.pressPosition = data.position; data.pointerPressRaycast = data.pointerCurrentRaycast;
            data.eligibleForClick = true; data.useDragThreshold = false;
            press = ExecuteEvents.ExecuteHierarchy(Target, data, ExecuteEvents.pointerDownHandler);
            if (press == null) press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(Target);
            data.pointerPress = press; data.rawPointerPress = Target;
            drag = ExecuteEvents.GetEventHandler<IDragHandler>(Target); data.pointerDrag = drag;
            if (drag != null) ExecuteEvents.Execute(drag, data, ExecuteEvents.initializePotentialDrag);
            return true;
        }

        public void Release()
        {
            GameObject releasedPress = press;
            global::ExternalUrlOpenGuard.RunUserGesture(CompleteRelease);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == releasedPress)
                EventSystem.current.SetSelectedGameObject(null);
        }

        void CompleteRelease()
        {
            if (data == null) return;
            if (press != null) ExecuteEvents.Execute(press, data, ExecuteEvents.pointerUpHandler);
            if (press != null && press == ExecuteEvents.GetEventHandler<IPointerClickHandler>(Target) && data.eligibleForClick)
                ExecuteEvents.Execute(press, data, ExecuteEvents.pointerClickHandler);
            if (drag != null && data.dragging) ExecuteEvents.Execute(drag, data, ExecuteEvents.endDragHandler);
            press = drag = null; data.pointerPress = data.pointerDrag = null; data.dragging = false; data.eligibleForClick = false;
        }

        public void Cancel()
        {
            if (data != null) data.eligibleForClick = false;
            CompleteRelease();
            if (hover != null) ExecuteEvents.ExecuteHierarchy(hover, data, ExecuteEvents.pointerExitHandler);
            hover = Target = null;
        }
        void OnDisable() { Cancel(); }
    }
}
