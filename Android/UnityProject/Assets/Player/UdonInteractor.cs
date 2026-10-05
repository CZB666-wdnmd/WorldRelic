using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    /// <summary>
    /// Player interaction: raycast from camera center; press E to trigger
    /// "_interact" (and "_onPlayerTriggerEnter" zone logic is skipped here) on
    /// UdonRuntime components of the hit object.
    /// </summary>
    public class UdonInteractor : MonoBehaviour
    {
        [Header("Setup")]
        public Camera playerCamera;
        public float maxDistance = 5f;
        public LayerMask interactionMask = ~0;
        public KeyCode interactKey = KeyCode.E;
        public KeyCode dropKey = KeyCode.Q;

        string prompt = "";
        public string Prompt => prompt;
        public static string GamepadPrompt(string text) => text.Replace("[E] Pick up", "[Y] Pick up")
            .Replace("[E] Drop", "[Y] Drop").Replace("[E]", "[X]").Replace("[Q]", "[Y]")
            .Replace("[LMB] Fire / [RMB] Ice", "[LT] Fire / [RT] Ice").Replace("[LMB]", "[RT]");
        public Transform HoverTarget { get; private set; }

        readonly List<LocalVrcw.Udon.UdonRuntime> hitRuntimes = new List<LocalVrcw.Udon.UdonRuntime>();
        readonly RaycastHit[] rayHits = new RaycastHit[32];
        VRCPickup hitPickup;
        [System.NonSerialized] public LocalVrcw.Udon.NativePickupSurfaces surfaces;
        Vector3 hitPickupPoint;
        MuseumXRPointer desktopPointer;
        bool hitUI;

        void Awake()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            desktopPointer = gameObject.AddComponent<MuseumXRPointer>();
            desktopPointer.pointerId = -103;
            if (playerCamera != null && playerCamera.GetComponent<MuseumInteractionOverlay>() == null)
                playerCamera.gameObject.AddComponent<MuseumInteractionOverlay>().interactor = this;
        }

        void Update()
        {
            if (MuseumFlatInput.IsActive && MuseumFlatInput.Instance.BlocksGameplayInput) return;
            if (MuseumViewSettings.BlocksDesktopInput) return;
            if (VRCPickup.IsVrActive) return;
            if (playerCamera == null) return;
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            CollectInteractables(ray);
            if (VRCPickup.HeldPickup != null)
            {
                var held = VRCPickup.HeldPickup;
                bool usable = System.Array.Exists(held.GetComponents<LocalVrcw.Udon.UdonRuntime>(), r => r.HasEvent("_onPickupUseDown") || r.HasEvent("_onPickupUseUp"));
                string action = (usable ? (FindDualElementProgram(held) != null ? "[LMB] Fire / [RMB] Ice  " : "[LMB] Use  ") : "") + "[Q] Release (move hand to throw)";
                prompt = FormatPrompt(action, held.gameObject.name);
            }

            if (Input.GetKeyDown(dropKey)) TryDropHeldPickup();
            SampleMouseButtons();
            if (Input.GetKeyDown(interactKey))
            {
                if (hitUI) DispatchUI();
                else if (hitPickup != null) DispatchPickup();
                else if (hitRuntimes.Count > 0) DispatchInteractions();
            }
        }

        /// <summary>
        /// Run the same raycast/target selection used by Update and dispatch
        /// _interact immediately. Editor smoke tests use this to exercise the
        /// real input route without fabricating a direct Udon event call.
        /// </summary>
        public int TryInteractRay(Ray ray)
        {
            CollectInteractables(ray);
            if (hitUI) return DispatchUI();
            if (hitPickup != null) return DispatchPickup();
            return hitRuntimes.Count == 0 ? 0 : DispatchInteractions();
        }

        public int TryInteractRay(Ray ray, int hand, bool allowPickup)
        {
            CollectInteractables(ray);
            if (hitPickup != null) return allowPickup ? (hitPickup.TryPickupAt(hand, hitPickupPoint) ? 1 : 0) : 0;
            return hitRuntimes.Count == 0 ? 0 : DispatchInteractions();
        }

        public bool TryPickupRay(Ray ray, int hand)
        {
            CollectInteractables(ray);
            return hitPickup != null && hitPickup.TryPickupAt(hand, hitPickupPoint);
        }

        public int TryInteractOnlyRay(Ray ray)
        {
            CollectInteractables(ray, true);
            if (hitUI) return DispatchUI();
            if (hitPickup != null) return 0;
            return hitRuntimes.Count == 0 ? 0 : DispatchInteractions();
        }

        // The VR utility ray targets only authored Interact controls. Pickups
        // with no Interact event are blockers, never remote grab candidates.
        public Transform FindInteractionRayTarget(Ray ray, out float distance)
        {
            distance = maxDistance;
            int count = Physics.RaycastNonAlloc(ray, rayHits, maxDistance, interactionMask, QueryTriggerInteraction.Collide);
            Transform nearest = null;
            float blocker = maxDistance;
            for (int i = 0; i < count; i++)
            {
                var c = rayHits[i].collider;
                if (c == null || c.GetComponentInParent<FPSCameraController>() != null) continue;
                var pickup = c.GetComponentInParent<VRCPickup>();
                if (IsInteractionSensor(c, pickup)) continue;
                if (pickup != null && pickup.IsHeld) continue;
                Transform target = null;
                for (Transform scope = c.transform; scope != null; scope = scope.parent)
                {
                    foreach (var rt in scope.GetComponents<LocalVrcw.Udon.UdonRuntime>())
                        if (rt.isActiveAndEnabled && rt.HasEvent("_interact")) { target = scope; break; }
                    if (target != null) break;
                }
                if (target != null && rayHits[i].distance < distance)
                { nearest = target; distance = rayHits[i].distance; }
                if (!c.isTrigger) blocker = Mathf.Min(blocker, rayHits[i].distance);
            }
            if (distance > blocker + 0.005f) nearest = null;
            distance = Mathf.Min(distance, blocker);
            return nearest;
        }

        // Resolve the nearest authored control in a touched collider's hierarchy.
        // A child page button wins over the book's pickup/parent Interact event.
        public Transform ResolveTouchTarget(Collider collider, out VRCPickup pickup)
        {
            pickup = null;
            if (collider == null || !collider.enabled || collider.GetComponentInParent<FPSCameraController>() != null) return null;
            var candidatePickup = collider.GetComponentInParent<VRCPickup>();
            if (IsInteractionSensor(collider, candidatePickup)) return null;
            Transform control = null;
            for (Transform scope = collider.transform; scope != null; scope = scope.parent)
            {
                foreach (var rt in scope.GetComponents<LocalVrcw.Udon.UdonRuntime>())
                    if (rt.isActiveAndEnabled && rt.HasEvent("_interact")) { control = scope; break; }
                if (control != null) break;
            }
            if (candidatePickup != null && candidatePickup.isActiveAndEnabled && candidatePickup.pickupable)
            {
                if (control != null && control != candidatePickup.transform && control.IsChildOf(candidatePickup.transform)) return control;
                if (candidatePickup.IsHeld) return null;
                pickup = candidatePickup;
                return pickup.transform;
            }
            return control;
        }

        // InfoPanelHoverDisplay owns large source proximity triggers. They
        // must keep receiving source trigger events, but are not pickup bodies
        // and must never steal touch/ray selection from nearby small exhibits.
        public static bool IsPickupInfoVolume(Collider collider, VRCPickup pickup)
        {
            if (pickup == null) return false;
            for (var scope = collider.transform; scope != null && scope != pickup.transform; scope = scope.parent)
                if (scope.TryGetComponent<LocalVrcw.Udon.UdonRuntime>(out var runtime) &&
                    runtime.programHash != null && runtime.programHash.StartsWith("449d3a88", System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        // The source InsideDoorTrigger is an automatic door sensor, not the
        // door's physical Interact collider. Identify the source program, not
        // arbitrary trigger events: KaminariPhone intentionally has only an
        // InfoGimmick collider and must retain its parent Interact route.
        public static bool IsInteractionSensor(Collider collider, VRCPickup pickup)
        {
            if (IsPickupInfoVolume(collider, pickup)) return true;
            if (!collider.isTrigger) return false;
            for (var scope = collider.transform; scope != null && scope != pickup?.transform; scope = scope.parent)
            {
                foreach (var runtime in scope.GetComponents<LocalVrcw.Udon.UdonRuntime>())
                {
                    if (runtime.programHash == "14503e75b72ec4642a505413700118b5") return true;
                }
            }
            return false;
        }

        public int TryInteractTouch(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return 0;
            int sent = 0;
            var targets = target.GetComponents<LocalVrcw.Udon.UdonRuntime>();
            global::ExternalUrlOpenGuard.RunUserGesture(() =>
            {
                foreach (var rt in targets)
                    if (rt != null && rt.isActiveAndEnabled && rt.HasEvent("_interact"))
                    { rt.SendCustomEvent("_interact"); sent++; }
            });
            return sent;
        }

        // Kept as the one desktop input route for both Update and the
        // source-parity test.  A held pickup is already the target selected
        // by the preceding E ray interaction; mouse input never raycasts a
        // second object in the original standalone control scheme.
        // Flat-only mapping into the authored dual-element program. Never
        // change the actual pickup hand, tracking APIs or the original timers.
        static LocalVrcw.Udon.UdonRuntime FindDualElementProgram(VRCPickup pickup)
        {
            if (pickup == null) return null;
            foreach (var runtime in pickup.GetComponents<LocalVrcw.Udon.UdonRuntime>())
                if (runtime.programHash == "b0665ca267dc6f742a3c4c58a01eafa8" &&
                    runtime.GetSymbol("_isLeftHand") is bool) return runtime;
            return null;
        }

        public bool TryUseMouseHeldPickup(int button, bool pressed)
        {
            if (VRCPickup.IsVrActive || (button != 0 && button != 1)) return false;
            var pickup = VRCPickup.HeldPickup;
            if (pickup == null) return false;
            var program = FindDualElementProgram(pickup);
            if (program == null) return button == 0 && TryUseHeldPickup(pressed);
            if (!pressed) return TryUseHeldPickup(false);
            object originalHand = program.GetSymbol("_isLeftHand");
            try
            {
                program.SetSymbol("_isLeftHand", button == 0); // left/fire; right/ice
                return TryUseHeldPickup(true);
            }
            finally { program.SetSymbol("_isLeftHand", originalHand); }
        }

        public bool HeldPickupHasDualElement => FindDualElementProgram(VRCPickup.HeldPickup) != null;
        public bool TryUseGamepadHeldPickup(bool fire, bool pressed)
        {
            if (VRCPickup.IsVrActive || VRCPickup.HeldPickup == null) return false;
            if (!HeldPickupHasDualElement) return !fire && TryUseHeldPickup(pressed);
            return TryUseMouseHeldPickup(fire ? 0 : 1, pressed);
        }

        public void SampleMouseButtons()
        {
            if (VRCPickup.IsVrActive || MuseumViewSettings.BlocksDesktopInput || Time.timeScale <= 0f) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.added || !mouse.enabled) return;
            if (mouse.leftButton.wasPressedThisFrame) TryUseMouseHeldPickup(0, true);
            if (mouse.rightButton.wasPressedThisFrame) TryUseMouseHeldPickup(1, true);
            if (mouse.leftButton.wasReleasedThisFrame) TryUseMouseHeldPickup(0, false);
            if (mouse.rightButton.wasReleasedThisFrame) TryUseMouseHeldPickup(1, false);
        }

        public bool TryUseHeldPickup(bool pressed)
        {
            var pickup = VRCPickup.HeldPickup;
            if (pickup == null) return false;
            if (pressed) pickup.UseDown();
            else pickup.UseUp();
            return true;
        }

        public bool TryDropHeldPickup()
        {
            var pickup = VRCPickup.HeldPickup;
            if (pickup == null) return false;
            pickup.Drop();
            return true;
        }

        int CollectInteractables(Ray ray, bool ignoreHeld = false)
        {
            hitRuntimes.Clear();
            hitUI = false;
            float targetDistance = maxDistance;
            hitPickup = null;
            HoverTarget = null;
            prompt = "";

            int hitCount = Physics.RaycastNonAlloc(ray, rayHits, maxDistance, interactionMask,
                                                   QueryTriggerInteraction.Collide);
            // RaycastNonAlloc has no ordering guarantee. Process the closest
            // hit first so a nearby interactable wins over a distant trigger.
            for (int i = 0; i < hitCount; i++)
            {
                for (int j = i + 1; j < hitCount; j++)
                {
                    if (rayHits[j].distance < rayHits[i].distance)
                    {
                        RaycastHit tmp = rayHits[i];
                        rayHits[i] = rayHits[j];
                        rayHits[j] = tmp;
                    }
                }
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = rayHits[i].collider;
                if (collider == null || collider.GetComponentInParent<FPSCameraController>() != null) continue;

                var pickup = collider.GetComponentInParent<VRCPickup>(true);
                if (IsInteractionSensor(collider, pickup)) continue;
                if ((ignoreHeld || VRCPickup.IsVrActive) && pickup != null && pickup.IsHeld) continue;
                if (pickup != null && pickup.enabled && pickup.gameObject.activeInHierarchy && pickup.pickupable)
                {
                    if (surfaces != null && !surfaces.TryRay(pickup, ray, targetDistance, out hitPickupPoint, out _)) continue;
                    targetDistance = surfaces != null ? Vector3.Distance(ray.origin, hitPickupPoint) : rayHits[i].distance;
                    hitPickup = pickup;
                    if (surfaces == null) hitPickupPoint = rayHits[i].point;
                    HoverTarget = pickup.transform;
                    string detail = string.IsNullOrEmpty(pickup.InteractionText) ? pickup.gameObject.name : pickup.InteractionText;
                    prompt = FormatPrompt(pickup.IsHeld ? "[E] Drop  [Q] Drop" : "[E] Pick up", detail);
                    break;
                }

                // Pick only the nearest transform in the hit hierarchy that
                // owns an interact event. Child controls such as the hero
                // notebook's Next/Prev/Close buttons sit below an interactive
                // parent. Dispatching every parent used to turn the page and
                // immediately toggle the notebook in the same keypress.
                for (Transform scope = collider.transform; scope != null; scope = scope.parent)
                {
                    var candidates = scope.GetComponents<LocalVrcw.Udon.UdonRuntime>();
                    for (int j = 0; j < candidates.Length; j++)
                    {
                        var rt = candidates[j];
                        if (rt == null || !rt.enabled || !rt.gameObject.activeInHierarchy || !rt.HasEvent("_interact")) continue;
                        if (!hitRuntimes.Contains(rt)) hitRuntimes.Add(rt);
                    }
                    if (hitRuntimes.Count > 0) break;
                }
                if (hitRuntimes.Count > 0)
                {
                    targetDistance = rayHits[i].distance;
                    HoverTarget = hitRuntimes[0].transform;
                    string sourceText = hitRuntimes[0].InteractionText;
                    prompt = FormatPrompt("[E] Interact",
                        string.IsNullOrEmpty(sourceText) ? hitRuntimes[0].gameObject.name : sourceText);
                    break;
                }
                if (VRCPickup.IsVrActive && !collider.isTrigger) break;
            }
            if (surfaces != null)
            {
                foreach (var p in surfaces.Pickups)
                    if (p != null && p.pickupable && p.isActiveAndEnabled && !p.IsHeld &&
                        surfaces.TryRay(p, ray, targetDistance, out var point, out float d))
                    {
                        // Never let a mesh behind an actual solid wall win.
                        bool blocked = false;
                        for (int i = 0; i < hitCount; i++)
                        {
                            var c = rayHits[i].collider;
                            if (c != null && !c.isTrigger && c.GetComponentInParent<FPSCameraController>() == null &&
                                c.GetComponentInParent<VRCPickup>() == null && rayHits[i].distance < d - .005f)
                            { blocked = true; break; }
                        }
                        if (blocked) continue;
                        if (hitRuntimes.Count > 0 && hitRuntimes[0].transform != p.transform && hitRuntimes[0].transform.IsChildOf(p.transform)) continue;
                        hitPickup = p; hitPickupPoint = point; targetDistance = d; hitRuntimes.Clear();
                        HoverTarget = p.transform; prompt = FormatPrompt("[E] Pick up", p.InteractionText);
                    }
            }
            if (!VRCPickup.IsVrActive && desktopPointer != null && playerCamera != null)
            {
                float distance = desktopPointer.UpdateRay(ray, maxDistance, playerCamera, transform);
                var control = desktopPointer.Target != null ? desktopPointer.Target.GetComponentInParent<UnityEngine.UI.Selectable>() : null;
                if (control != null && control.IsActive() && control.IsInteractable() &&
                    (control is UnityEngine.UI.Button || control is UnityEngine.UI.Toggle) && distance <= targetDistance + 0.001f)
                {
                    hitUI = true; hitPickup = null; hitRuntimes.Clear();
                    HoverTarget = control.transform; prompt = FormatPrompt("[E] Interact", control.name);
                }
            }
            return hitRuntimes.Count;
        }

        // Object names and authored interaction labels are useful when
        // diagnosing reconstructed Udon graphs, but they are implementation
        // details in ordinary play. Keep the action hint stable and expose
        // the target detail only when the explicit debug mode is enabled.
        public static string FormatPrompt(string action, string targetDetail)
        {
            return FormatPrompt(action, targetDetail, MuseumDebug.Enabled);
        }

        public static string FormatPrompt(string action, string targetDetail, bool debug)
        {
            if (!debug || string.IsNullOrEmpty(targetDetail)) return action;
            return action + "  [" + targetDetail + "]";
        }

        int DispatchUI()
        {
            if (!hitUI || desktopPointer == null) return 0;
            desktopPointer.Press(); desktopPointer.Release(); return 1;
        }

        int DispatchPickup()
        {
            if (hitPickup == null) return 0;
            if (hitPickup.IsHeld) hitPickup.Drop();
            else if (!hitPickup.TryPickupAt(0, hitPickupPoint)) return 0;
            return 1;
        }

        int DispatchInteractions()
        {
            // Snapshot the list before dispatch: an interaction can
            // enable/disable objects and invalidate a live hierarchy.
            var targets = hitRuntimes.ToArray();
            int sent = 0;
            global::ExternalUrlOpenGuard.RunUserGesture(() =>
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] == null) continue;
                    targets[i].SendCustomEvent("_interact");
                    sent++;
                }
            });
            return sent;
        }

        void OnGUI()
        {
            if (VRCPickup.IsVrActive) return;
            var previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(Screen.width / 2f - 6, Screen.height / 2f - 2, 12, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width / 2f - 2, Screen.height / 2f - 6, 4, 12), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 1, 10, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width / 2f - 1, Screen.height / 2f - 5, 2, 10), Texture2D.whiteTexture);
            GUI.color = previous;
            if (MuseumDebug.Enabled) GUI.Label(new Rect(12, 42, 900, 25), "DEBUG | Red: interactables / wire: trigger bounds | Target: " + (HoverTarget != null ? HoverTarget.name : "-"));
            if (!string.IsNullOrEmpty(prompt))
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                var pad = GetComponent<MuseumGamepadInput>();
                GUI.Label(new Rect(Screen.width / 2f - 230f, Screen.height / 2f + 40f, 460f, 65f),
                    pad != null && pad.UsingGamepad ? GamepadPrompt(prompt) : prompt, style);
            }
        }
        void OnDestroy() => surfaces?.Dispose();
    }
}
