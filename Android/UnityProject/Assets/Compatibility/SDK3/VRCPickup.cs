using System.Collections.Generic;
using UnityEngine;

namespace VRC.SDK3.Components
{
    /// <summary>
    /// Standalone implementation of the VRChat component serialized in the
    /// source VRCW. The approved v135 bundle contains 123 of these components, but not the
    /// VRC SDK implementation itself; keeping the original type and fields
    /// lets the recovered Udon programs observe the same pickup state.
    /// </summary>
    public class VRCPickup : MonoBehaviour
    {
        // Decoupled presentation hook, after source activation but before its
        // recovered particles restart (also after source drop cleanup).
        public static event System.Action<VRCPickup> PresentationChanged;
        // Names and defaults match the common source pickup record.
        public int MomentumTransferMethod = 0;
        public bool DisallowTheft = false;
        public Transform ExactGun;
        public Transform ExactGrip;
        public bool allowManipulationWhenEquipped = false;
        public int orientation = 0;
        public int AutoHold = 0;
        public string InteractionText = "";
        public string UseText = "Use";
        public int useEventBroadcastType = 0;
        public string UseDownEventName = "";
        public string UseUpEventName = "";
        public int pickupDropEventBroadcastType = 0;
        public string PickupEventName = "";
        public string DropEventName = "";
        public float ThrowVelocityBoostMinSpeed = 1f;
        public float ThrowVelocityBoostScale = 1f;
        public Component currentlyHeldBy;
        public bool pickupable = true;
        public float proximity = 2f;
        public int version = 1;

        static VRCPickup heldPickup;
        static VRCPickup leftHeldPickup;
        static Transform trackedLeftHand, trackedRightHand;
        public static bool IsVrActive { get; private set; }
        int holdingHand = -1;
        Vector3 heldPositionOffset;
        Vector3 grabbedLocalPoint;
        Quaternion previousHeldRotation;
        Vector3 releaseAngularVelocity;
        Rigidbody body;
        bool originalKinematic;
        bool originalUseGravity;
        bool originalStateCaptured;
        bool initialized;
        Quaternion heldRotationOffset = Quaternion.identity;
        Vector3 previousHandPosition;
        Vector3 releaseVelocity;
        float previousHeldTime;
        bool usePressed;
        static readonly Dictionary<MonoBehaviour, int> recoveredUrarakaBits = new Dictionary<MonoBehaviour, int>();

        public bool IsHeld => heldPickup == this || leftHeldPickup == this;
        // Actual contact/grip, not the potentially remote imported root pivot.
        public Vector3 GrabAnchorPosition => transform.TransformPoint(grabbedLocalPoint);
        // The source b066 pickup ASM compares PickupHand value 1 to its
        // left-hand branch; therefore this VRC SDK enum is Right=0, Left=1.
        // Desktop defaults to the right hand; OpenXR maintains both hands independently.
        public int currentHand => IsHeld ? holdingHand : -1;
        public static VRCPickup HeldPickup => heldPickup;

        void Awake()
        {
            EnsureInitialized();
        }

        void OnDisable()
        {
            if (IsHeld) Drop();
        }

        void LateUpdate() { UpdateHeldPose(Time.unscaledTime); }

        public void UpdateHeldPose(float now)
        {
            if (!IsHeld) return;
            Camera camera = Camera.main;
            Transform tracked = IsVrActive ? GetTrackedHand(holdingHand) : null;
            if (tracked == null && camera == null) return;
            Quaternion handRotation = tracked != null ? tracked.rotation : camera.transform.rotation;
            Vector3 handPosition = tracked != null ? tracked.position : GetRightHandPosition(camera);
            Quaternion rotation = handRotation * heldRotationOffset;
            Vector3 position = handPosition + handRotation * heldPositionOffset;
            float elapsed = now - previousHeldTime;
            if (elapsed > 0f && elapsed < 0.25f)
            {
                // Measure the controller, not the imported object's pivot.
                // Rotating an off-center pivot must not manufacture a huge throw.
                releaseVelocity = (handPosition - previousHandPosition) / elapsed;
                Quaternion delta = rotation * Quaternion.Inverse(previousHeldRotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                releaseAngularVelocity = Mathf.Abs(angle) < 0.001f ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / elapsed);
            }
            previousHandPosition = handPosition;
            previousHeldRotation = rotation;
            previousHeldTime = now;
            transform.SetPositionAndRotation(position, rotation);
        }

        public static Transform GetTrackedHand(int hand) => hand == 1 ? trackedLeftHand : trackedRightHand;

        public static void SetTrackedHands(bool active, Transform left, Transform right)
        {
            if (IsVrActive != active) { heldPickup?.Drop(); leftHeldPickup?.Drop(); }
            IsVrActive = active;
            trackedLeftHand = left;
            trackedRightHand = right;
        }

        public static void ResetHeldMotion()
        {
            foreach (var pickup in new[] { heldPickup, leftHeldPickup })
                if (pickup != null)
                {
                    pickup.UpdateHeldPose(Time.unscaledTime);
                    pickup.releaseVelocity = Vector3.zero;
                    pickup.releaseAngularVelocity = Vector3.zero;
                }
        }

        public bool TryPickup() { return TryPickup(0); }

        public bool TryPickup(int hand)
        { return TryPickupAt(hand, DefaultGrabPoint()); }

        Vector3 DefaultGrabPoint()
        {
            // Direct callers have no ray contact. Prefer actual geometry owned
            // by this pickup, never a large child proximity trigger.
            foreach (var mesh in GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh != null && mesh.GetComponentInParent<VRCPickup>(true) == this)
                    return mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
            var collider = GetComponent<Collider>();
            return collider != null ? collider.bounds.center : transform.position;
        }

        public bool TryPickupAt(int hand, Vector3 contactPoint)
        {
            EnsureInitialized();
            if (!pickupable || !isActiveAndEnabled || hand < 0 || hand > 1) return false;
            if (IsHeld && holdingHand == hand) return true;
            if (IsHeld) { if (DisallowTheft) return false; Drop(); }
            GetPickupInHand(hand)?.Drop();

            CaptureBodyState();
            // Any orientation with no authored grip preserves the pose the
            // player actually grabbed. Snapping every object to camera.rotation
            // showed the blank BACK pass of both classroom boards.
            Camera camera = Camera.main;
            Transform tracked = IsVrActive ? GetTrackedHand(hand) : null;
            Quaternion handRotation = tracked != null ? tracked.rotation : (camera == null ? Quaternion.identity : camera.transform.rotation);
            Vector3 handPosition = tracked != null ? tracked.position : (camera == null ? transform.position : GetRightHandPosition(camera));
            heldRotationOffset = Quaternion.Inverse(handRotation) * transform.rotation;
            heldPositionOffset = Vector3.zero;
            Transform grip = orientation == 1 ? ExactGun : ExactGrip;
            if (grip != null)
            {
                heldRotationOffset = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * grip.rotation);
                heldPositionOffset = -(heldRotationOffset * (Quaternion.Inverse(transform.rotation) * (grip.position - transform.position)));
            }
            else if (tracked != null)
                // Hand contact can be far from a Blender/exported root pivot.
                // Preserve the complete grabbed pose; distance is not a valid
                // reason to snap that invisible pivot onto the controller.
                heldPositionOffset = Quaternion.Inverse(handRotation) * (transform.position - handPosition);
            else
                // Desktop brings the touched surface into the hand slot.
                // Keep the original orientation and visible contact rather
                // than snapping an arbitrary Blender pivot to the camera.
                heldPositionOffset = Quaternion.Inverse(handRotation) * (transform.position - contactPoint);
            grabbedLocalPoint = transform.InverseTransformPoint(grip != null ? grip.position :
                (tracked != null ? handPosition : contactPoint));
            previousHandPosition = handPosition;
            previousHeldRotation = handRotation * heldRotationOffset;
            previousHeldTime = Time.unscaledTime;
            releaseVelocity = releaseAngularVelocity = Vector3.zero;
            holdingHand = hand;
            if (hand == 1) leftHeldPickup = this; else heldPickup = this;
            currentlyHeldBy = tracked != null ? tracked : (camera == null ? null : camera.transform);
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }
            Dispatch("_onPickup");
            if (!string.IsNullOrEmpty(PickupEventName)) Dispatch(PickupEventName);
            return true;
        }

        public void Drop()
        {
            if (!IsHeld) return;
            if (usePressed) UseUp();
            Vector3 velocity = releaseVelocity;
            if (velocity.magnitude >= ThrowVelocityBoostMinSpeed)
                velocity *= ThrowVelocityBoostScale;
            if (heldPickup == this) heldPickup = null;
            if (leftHeldPickup == this) leftHeldPickup = null;
            holdingHand = -1;
            currentlyHeldBy = null;
            if (body != null)
            {
                body.isKinematic = originalKinematic;
                body.useGravity = originalUseGravity;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            Dispatch("_onDrop");
            if (!string.IsNullOrEmpty(DropEventName)) Dispatch(DropEventName);
            PresentationChanged?.Invoke(this);
            // Source Mogimogi _onDrop turns its initially kinematic body into
            // a dynamic one, then reads velocity on the next fixed frame.
            // Transfer the measured hand motion after this callback; do not
            // invent a fixed launch speed or overwrite source respawn states.
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = velocity;
                if (IsVrActive) body.angularVelocity = releaseAngularVelocity;
            }
        }

        public void UseDown()
        {
            if (!IsHeld) return;
            usePressed = true;
            Dispatch("_onPickupUseDown");
            PresentationChanged?.Invoke(this);
            RestartRecoveredKoseiParticles();
            if (!string.IsNullOrEmpty(UseDownEventName)) Dispatch(UseDownEventName);
        }

        // These source programs activate an authored effect GameObject on
        // Use. A one-shot ParticleSystem that already ran while its parent
        // area was preloaded does not restart merely because SetActive(true)
        // is repeated. Preserve the source-selected effect branch, but reset
        // and play the ParticleSystems below that active branch explicitly.
        void RestartRecoveredKoseiParticles()
        {
            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().FullName != "LocalVrcw.Udon.UdonRuntime") continue;
                string hash = behaviour.GetType().GetProperty("programHash")?.GetValue(behaviour) as string;
                if (hash != "1a4371244d153c540bbeaa893438f6d2" &&
                    hash != "eaf6dddf79795bb4c9ab4e060a57bbef" &&
                    hash != "b0665ca267dc6f742a3c4c58a01eafa8") continue;
                var getSymbol = behaviour.GetType().GetMethod("GetSymbol", new[] { typeof(string) });
                if (getSymbol == null) continue;
                foreach (string symbol in new[] { "koseiEffect", "rightKoseiEffect", "leftKoseiEffect" })
                {
                    if (hash == "b0665ca267dc6f742a3c4c58a01eafa8")
                    {
                        bool left = getSymbol.Invoke(behaviour, new object[] { "_isLeftHand" }) is bool selectedLeft && selectedLeft;
                        // Fire/ice have independent source timers. Starting one
                        // must not restart the other already-running emission.
                        if (symbol != (left ? "leftKoseiEffect" : "rightKoseiEffect")) continue;
                    }
                    object value = getSymbol.Invoke(behaviour, new object[] { symbol });
                    GameObject effect = value as GameObject ?? (value as Component)?.gameObject;
                    if (effect == null || !effect.activeInHierarchy) continue;
                    foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        particles.Play(true);
                    }
                }
            }
        }

        public void UseUp()
        {
            if (!IsHeld) return;
            usePressed = false;
            Dispatch("_onPickupUseUp");
            if (!string.IsNullOrEmpty(UseUpEventName)) Dispatch(UseUpEventName);
        }

        public static VRCPickup GetPickupInHand(int hand)
        {
            return hand == 0 ? heldPickup : hand == 1 ? leftHeldPickup : null;
        }

        public static Vector3 GetRightHandPosition(Camera camera)
        {
            if (IsVrActive && trackedRightHand != null) return trackedRightHand.position;
            return camera.transform.position + camera.transform.forward * 0.55f +
                   camera.transform.right * 0.22f + Vector3.down * 0.18f;
        }

        public static Vector3 GetLeftHandPosition(Camera camera)
        {
            if (IsVrActive && trackedLeftHand != null) return trackedLeftHand.position;
            return camera.transform.position + camera.transform.forward * 0.55f -
                   camera.transform.right * 0.22f + Vector3.down * 0.18f;
        }

        void CaptureBodyState()
        {
            if (body == null || originalStateCaptured) return;
            originalKinematic = body.isKinematic;
            originalUseGravity = body.useGravity;
            originalStateCaptured = true;
        }

        void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            body = GetComponent<Rigidbody>();
            CaptureBodyState();
        }

        void Dispatch(string eventName)
        {
            // VRCSDK3 is a separate Unity assembly, so identify the rebuilt
            // Udon runtime by its recovered type without taking an assembly
            // dependency on it. Unity SendMessage only supports a zero- or
            // one-argument method, whereas UdonRuntime's public API has its
            // optional parameter pair in its CLR signature; invoke that API
            // explicitly so the real Udon event is not silently skipped.
            // Most source pickup callbacks are on this same GameObject. The
            // VRCW additionally has 26 InfoPanelHoverDisplay children whose
            // serialized `autoFindPickupInParents` explicitly binds them to
            // this parent Pickup. Forward only that source-program family to
            // its child Udon host, so holding an exhibit hides its own source
            // info panel without turning this into a broad hierarchy event.
            var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null && behaviours[i] != this &&
                    behaviours[i].GetType().FullName == "LocalVrcw.Udon.UdonRuntime")
                {
                    bool sameObject = behaviours[i].gameObject == gameObject;
                    string programHash = behaviours[i].GetType().GetProperty("programHash")?.GetValue(behaviours[i]) as string;
                    bool isSourceInfoChild = !sameObject && programHash != null &&
                                             programHash.StartsWith("449d3a88", System.StringComparison.Ordinal);
                    if (!sameObject && !isSourceInfoChild) continue;
                    var getSymbol = behaviours[i].GetType().GetMethod("GetSymbol", new[] { typeof(string) });
                    var method = behaviours[i].GetType().GetMethod(
                        "SendCustomEvent",
                        new[] { typeof(string), typeof(object), typeof(string) });
                    method?.Invoke(behaviours[i], new object[] { eventName, null, null });
                    RecoverUrarakaUseState(behaviours[i], eventName, getSymbol, method);
                }
        }

        // ec421's source ASM performs `bits XOR (1 << FindUseTargetIndex())`.
        // Its source ray/target lookup runs correctly in UdonRuntime, but a
        // nested-return limitation can overwrite the resulting bits with a
        // later temporary. Restore exactly that recorded source transition
        // in the standalone bridge.
        static void RecoverUrarakaUseState(MonoBehaviour behaviour, string eventName,
                                           System.Reflection.MethodInfo getSymbol,
                                           System.Reflection.MethodInfo sendEvent)
        {
            if (behaviour.GetType().GetProperty("programHash")?.GetValue(behaviour) as string != "ec421216ec98e40499e685c840096244")
                return;
            MonoBehaviour stateKey = behaviour;
            if (eventName == "_onDrop")
            {
                recoveredUrarakaBits.Remove(stateKey);
                return;
            }
            if (eventName != "_onPickupUseDown" || getSymbol == null || sendEvent == null) return;
            object foundValue = getSymbol.Invoke(behaviour, new object[] { "__0_FindUseTargetIndex__ret" });
            if (foundValue == null) return;
            int targetIndex;
            try { targetIndex = System.Convert.ToInt32(foundValue); }
            catch { return; }
            if (targetIndex < 0 || targetIndex >= 2) return;
            object afterValue = getSymbol.Invoke(behaviour, new object[] { "_floatingStateBits" });
            // The serialized source default is zero. The VM's private
            // temporary address is overwritten during nested returns before
            // this event starts, so only use its own recovered state during
            // the current hold cycle. The source mutation is exactly
            // `bits XOR (1 << targetIndex)` (ec421:0x2680).
            int before = recoveredUrarakaBits.TryGetValue(stateKey, out int priorBits) ? priorBits : 0;
            int restoredBits = before ^ (1 << targetIndex);
            var setSymbol = behaviour.GetType().GetMethod("SetSymbol", new[] { typeof(string), typeof(object) });
            if (setSymbol == null) return;
            int after = afterValue is int a ? a : 0;
            if (after != restoredBits)
                setSymbol.Invoke(behaviour, new object[] { "_floatingStateBits", restoredBits });
            recoveredUrarakaBits[stateKey] = restoredBits;
            ApplyUrarakaSourceAnimation(behaviour, getSymbol, targetIndex, (restoredBits & (1 << targetIndex)) != 0);
        }

        // Mirrors ec421's SetFloatingState visual branch (0x19FC-0x1D70)
        // after its state bit has been recovered. All objects and values are
        // read from that extracted Udon behaviour; no replacement assets or
        // invented animation names are introduced here.
        static void ApplyUrarakaSourceAnimation(MonoBehaviour behaviour,
                                                System.Reflection.MethodInfo getSymbol,
                                                int targetIndex, bool floating)
        {
            if (behaviour.GetType().GetProperty("programHash")?.GetValue(behaviour) as string != "ec421216ec98e40499e685c840096244")
                return;
            object targetsValue = getSymbol.Invoke(behaviour, new object[] { "targetAnimators" });
            object[] targets = targetsValue as object[];
            if (targets == null || targetIndex < 0 || targetIndex >= targets.Length) return;
            Animator animator = targets[targetIndex] as Animator ??
                                (targets[targetIndex] as GameObject)?.GetComponent<Animator>();
            if (animator == null) return;
            string stateName = getSymbol.Invoke(behaviour, new object[] { floating ? "floatingStateName" : "idleStateName" }) as string;
            if (string.IsNullOrEmpty(stateName)) return;
            float blend = System.Convert.ToSingle(getSymbol.Invoke(behaviour, new object[] { "animationBlendSeconds" }));
            int layer = System.Convert.ToInt32(getSymbol.Invoke(behaviour, new object[] { "animationLayer" }));
            if (blend > 0f) animator.CrossFadeInFixedTime(stateName, blend, layer, 0f);
            else animator.Play(stateName, layer, 0f);
        }

    }
}
