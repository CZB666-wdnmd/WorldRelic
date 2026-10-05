using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace LocalVrcw.Udon
{
    public sealed class RuntimeInteractor : MonoBehaviour
    {
        public Camera view;
        public UdonRuntime target;
        public VRCPickup pickup;
        Vector3 contact;
        public VRCPickup[] candidates;
        NativeMeshRay geometry;
        public GraphicRaycaster[] uiRaycasters;
        readonly System.Collections.Generic.List<RaycastResult> uiHits = new System.Collections.Generic.List<RaycastResult>();
        Selectable uiTarget;
        void Awake() => geometry = new NativeMeshRay();
        void OnDestroy() => geometry?.Dispose();
        void Update()
        {
            target = null; pickup = null;
            uiTarget = null;
            Ray ray = new Ray(view.transform.position, view.transform.forward);
            float blocked = 3;
            foreach (var hit in Physics.RaycastAll(ray, 3, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                var hosts = hit.collider.GetComponentsInParent<UdonRuntime>();
                target = hosts.FirstOrDefault(r => r.HasEvent("_interact"));
                blocked = hit.distance + 0.03f;
                if (target != null) break;
                if (hit.collider.GetComponentInParent<VRCPickup>() == null) break;
            }
            if (EventSystem.current != null && uiRaycasters != null)
            {
                var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width / 2f, Screen.height / 2f), button = PointerEventData.InputButton.Left };
                uiHits.Clear();
                foreach (var raycaster in uiRaycasters) if (raycaster && raycaster.isActiveAndEnabled) raycaster.Raycast(pointer, uiHits);
                foreach (var hit in uiHits.OrderBy(h => h.distance))
                {
                    var selectable = hit.gameObject.GetComponentInParent<Selectable>();
                    if (selectable != null && selectable.IsInteractable() && hit.distance >= 0 && hit.distance <= blocked)
                    { uiTarget = selectable; blocked = hit.distance; break; }
                }
            }
            if (RuntimePickup.Held == null)
                foreach (var candidate in candidates)
                {
                    if (!candidate || !candidate.gameObject.activeInHierarchy || !candidate.pickupable) continue;
                    foreach (var renderer in candidate.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!renderer.enabled || !renderer.bounds.IntersectRay(ray)) continue;
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null && geometry.TryHit(ray, renderer.transform, filter.sharedMesh, blocked, out var point, out float distance))
                        { pickup = candidate; contact = point; blocked = distance; }
                    }
                }
            if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
            {
                if (uiTarget is Button button) button.onClick.Invoke();
                else if (uiTarget is Toggle toggle) toggle.isOn = !toggle.isOn;
                else if (uiTarget == null) target?.Interact();
            }
            if (Input.GetKeyDown(KeyCode.F)) { if (RuntimePickup.Held != null) RuntimePickup.Drop(); else if (pickup != null) RuntimePickup.Grab(pickup, contact); }
            if (Input.GetKeyDown(KeyCode.R)) RuntimePickup.Dispatch("_onPickupUseDown");
            if (Input.GetKeyUp(KeyCode.R)) RuntimePickup.Dispatch("_onPickupUseUp");
        }
        void OnGUI()
        {
            GUI.color = target != null || pickup != null || uiTarget != null ? Color.cyan : Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 6, Screen.height / 2 - 9, 20, 20), "+"); GUI.color = Color.white;
        }
    }
}
