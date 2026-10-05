using System.Collections.Generic;
using UnityEngine;

namespace AniqueMuseum
{
    /// <summary>
    /// Player trigger router: when the player (CharacterController) enters/exits
    /// a trigger collider that carries UdonRuntime(s), fire the matching Udon
    /// event with the player as the event parameter.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class UdonTriggerRouter : MonoBehaviour
    {
        readonly HashSet<LocalVrcw.Udon.UdonRuntime> sent = new HashSet<LocalVrcw.Udon.UdonRuntime>();
        readonly HashSet<Collider> activeTriggers = new HashSet<Collider>();
        readonly HashSet<Collider> overlapTriggers = new HashSet<Collider>();
        readonly List<Collider> overlapTriggerOrder = new List<Collider>();
        readonly List<Collider> triggerExitBuffer = new List<Collider>();
        readonly Dictionary<Collider, int> lastStayFrame = new Dictionary<Collider, int>();
        // A crowded authored area can overlap well over 128 colliders.  Keep
        // the poll non-allocating, but do not silently truncate the source
        // trigger set at the old diagnostic-sized buffer.
        readonly Collider[] overlapBuffer = new Collider[512];
        readonly RaycastHit[] sweepHitBuffer = new RaycastHit[512];
        readonly List<RaycastHit> orderedSweepHits = new List<RaycastHit>();
        CharacterController controller;
        bool initialOverlapChecked;
        bool hasPreviousCapsule;
        Vector3 previousCenter;
        Vector3 previousAxis;
        float previousRadius;

        // Normal player locomotion is only a fraction of a metre per physics
        // step.  This upper bound preserves fast physical crossings while
        // ensuring an authored TeleportTo does not fire every trigger between
        // two spatially separated museum sets.
        const float MaxPhysicalSweepDistance = 8f;

        void Awake() { controller = GetComponent<CharacterController>(); }

        void FixedUpdate()
        {
            // A CharacterController can be placed inside a trigger before the
            // first physics step. Unity does not consistently emit Enter for
            // that case, but the original world expects its enter event to
            // arm gimmicks such as 600_HighJumpGimmick.
            if (!initialOverlapChecked && Time.frameCount > 1)
            {
                initialOverlapChecked = true;
                ProbeInitialOverlaps();
            }

            // CharacterController trigger callbacks are not reliable across
            // Unity editor/player backends when the controller crosses a
            // trigger in a single Move. Poll the same capsule every physics
            // step so a compiled player cannot miss source gimmicks such as
            // 600_HighJumpGimmick.
            ProbeMovingOverlaps();
        }

        /// <summary>
        /// Regression hook for the editor smoke suite. It invokes the exact
        /// same original-collider overlap route used by FixedUpdate, rather
        /// than directly calling a Udon trigger entry.
        /// </summary>
        public void ProbeOriginalTriggersForVerification()
        {
            ProbeMovingOverlaps();
        }

        void OnTriggerEnter(Collider other) { RegisterEnter(other); }

        void OnTriggerStay(Collider other)
        {
            // Treat the first Stay as Enter as well. This covers scene-load
            // overlaps on Unity versions that only report Stay to a newly
            // enabled CharacterController.
            if (other != null && activeTriggers.Add(other))
                Send(other, "_onPlayerTriggerEnter", "onPlayerTriggerEnterPlayer");
            SendStay(other);
        }

        void OnTriggerExit(Collider other)
        {
            // The physics callback and the capsule poll can observe the same
            // transition in either order. Emit the source Exit event once.
            if (other == null || !activeTriggers.Remove(other)) return;
            lastStayFrame.Remove(other);
            Send(other, "_onPlayerTriggerExit", "onPlayerTriggerExitPlayer");
        }

        void RegisterEnter(Collider other)
        {
            if (other == null || !activeTriggers.Add(other)) return;
            Send(other, "_onPlayerTriggerEnter", "onPlayerTriggerEnterPlayer");
        }

        void ProbeInitialOverlaps()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return;

            Vector3 scale = transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(controller.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = transform.TransformPoint(controller.center);
            Vector3 axis = transform.up * (height * 0.5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(
                center - axis, center + axis, radius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other != null && other.isTrigger) RegisterEnter(other);
            }
        }

        void ProbeMovingOverlaps()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return;

            Vector3 scale = transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(controller.height * Mathf.Abs(scale.y), radius * 2f);
            Vector3 center = transform.TransformPoint(controller.center);
            Vector3 axis = transform.up * (height * 0.5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(
                center - axis, center + axis, radius, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Collide);

            overlapTriggers.Clear();
            overlapTriggerOrder.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == null || !other.isTrigger || other.transform.IsChildOf(transform)) continue;
                if (overlapTriggers.Add(other)) overlapTriggerOrder.Add(other);
            }

            CollectSweptTriggers(center, axis, radius);

            // A teleport can leave one authored area and enter another in the
            // same physics step. Source managers (FogChanger, AreaLoader and
            // BGM area) retain transition state, so the old Exit must be
            // applied before the new Enter. Doing this in the opposite order
            // made the old area's Exit overwrite the newly entered state.
            triggerExitBuffer.Clear();
            foreach (Collider active in activeTriggers)
                if (active == null || !overlapTriggers.Contains(active)) triggerExitBuffer.Add(active);
            for (int i = 0; i < triggerExitBuffer.Count; i++)
            {
                Collider other = triggerExitBuffer[i];
                activeTriggers.Remove(other);
                lastStayFrame.Remove(other);
                Send(other, "_onPlayerTriggerExit", "onPlayerTriggerExitPlayer");
            }

            // Deliver source trigger transitions crossed completely between
            // two physics samples.  Unity/VRChat emits OnPlayerTriggerEnter
            // for these during continuous player motion, whereas a pure
            // endpoint OverlapCapsule poll used by the standalone rebuild
            // could miss a thin authored collider entirely.
            for (int i = 0; i < orderedSweepHits.Count; i++)
            {
                Collider other = orderedSweepHits[i].collider;
                if (other == null || overlapTriggers.Contains(other) || other.transform.IsChildOf(transform)) continue;
                bool entered = activeTriggers.Add(other);
                if (entered) Send(other, "_onPlayerTriggerEnter", "onPlayerTriggerEnterPlayer");
                SendStay(other);
                if (activeTriggers.Remove(other))
                {
                    lastStayFrame.Remove(other);
                    Send(other, "_onPlayerTriggerExit", "onPlayerTriggerExitPlayer");
                }
            }

            for (int i = 0; i < overlapTriggerOrder.Count; i++)
            {
                Collider other = overlapTriggerOrder[i];
                if (activeTriggers.Add(other))
                    Send(other, "_onPlayerTriggerEnter", "onPlayerTriggerEnterPlayer");
                SendStay(other);
            }

            previousCenter = center;
            previousAxis = axis;
            previousRadius = radius;
            hasPreviousCapsule = true;
        }

        void CollectSweptTriggers(Vector3 center, Vector3 axis, float radius)
        {
            orderedSweepHits.Clear();
            if (!hasPreviousCapsule) return;

            Vector3 delta = center - previousCenter;
            float distance = delta.magnitude;
            if (distance <= 0.0001f || distance > MaxPhysicalSweepDistance) return;

            float sweepRadius = Mathf.Max(previousRadius, radius);
            Vector3 sweepAxis = previousAxis.sqrMagnitude >= axis.sqrMagnitude ? previousAxis : axis;
            int hitCount = Physics.CapsuleCastNonAlloc(
                previousCenter - sweepAxis, previousCenter + sweepAxis,
                sweepRadius, delta / distance, sweepHitBuffer, distance,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = sweepHitBuffer[i];
                Collider other = hit.collider;
                if (other == null || !other.isTrigger || other.transform.IsChildOf(transform)) continue;
                bool duplicate = false;
                for (int j = 0; j < orderedSweepHits.Count; j++)
                    if (orderedSweepHits[j].collider == other) { duplicate = true; break; }
                if (!duplicate) orderedSweepHits.Add(hit);
            }
            orderedSweepHits.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        void SendStay(Collider other)
        {
            if (other == null) return;
            int frame = Time.frameCount;
            int previous;
            if (lastStayFrame.TryGetValue(other, out previous) && previous == frame) return;
            lastStayFrame[other] = frame;
            Send(other, "_onPlayerTriggerStay", "onPlayerTriggerStayPlayer");
        }

        void Send(Collider other, string evt, string paramVar)
        {
            if (other == null || other.transform.IsChildOf(transform)) return;
            sent.Clear();
            var candidates = other.GetComponentsInParent<LocalVrcw.Udon.UdonRuntime>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                var rt = candidates[i];
                if (rt == null || !rt.enabled || !rt.gameObject.activeInHierarchy || !rt.HasEvent(evt)) continue;
                // The source AreaLoader owns this exact authored region box,
                // not the info/pickup/model triggers anywhere in its subtree.
                if (rt.programHash == "af99d91076c47bb4fba606b97cd8c8ad" &&
                    rt.transform != other.transform) continue;
                if (sent.Add(rt)) rt.SendCustomEvent(evt, LocalVrcw.Udon.LocalPlayer.Instance, paramVar);
            }
        }
    }
}
