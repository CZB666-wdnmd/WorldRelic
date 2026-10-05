using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Components;
using LocalVrcw.Udon;

namespace AniqueMuseum
{
    // Presentation-only flat/VR correction: keep tracking/bone APIs intact and
    // leave the authored activation, audio and world-target animations alone.
    [DefaultExecutionOrder(10000)]
    public sealed class MuseumXRKoseiPresentation : MonoBehaviour
    {
        sealed class Binding
        {
            public VRCPickup pickup;
            public Transform root;
            public Transform effect;
            public Vector3 originalLocal;
            public Quaternion originalRotation, worldRotation;
            public Vector3 playerOffset;
            public bool applied;
            public UdonRuntime source;
            public int effectBit;
        }
        readonly List<Binding> bindings = new List<Binding>();
        MuseumXRPlayer rig;

        public static bool IsKoseiPickup(VRCPickup pickup)
        {
            if (pickup == null) return false;
            switch (pickup.name)
            {
                case "KoseiCube_Bakugo_R": case "KoseiCube_Todoroki":
                case "KoseiCube_Deku": case "KoseiCube_Kaminari":
                case "KoseiCube_Uraraka": return true;
                default: return false;
            }
        }

        public void Initialize(MuseumXRPlayer owner)
        {
            rig = owner;
            RestoreAll(); bindings.Clear();
            foreach (var pickup in Object.FindObjectsByType<VRCPickup>(FindObjectsInactive.Include))
            {
                if (!IsKoseiPickup(pickup)) continue;
                foreach (var runtime in pickup.GetComponents<UdonRuntime>())
                    foreach (string symbol in new[] { "koseiEffect", "rightKoseiEffect", "leftKoseiEffect" })
                    {
                        object value = runtime.GetSymbol(symbol);
                        Transform root = value is GameObject go ? go.transform : value is Component c ? c.transform : null;
                        if (root == null || !root.IsChildOf(pickup.transform)) continue;
                        Transform effect = root;
                        // Ice uses a separately authored follow root, whereas
                        // fire's VM writes the nested FireBall itself. Override
                        // exactly that follow transform so a source LateUpdate
                        // cannot leave its child back at the physical hand.
                        if (symbol == "rightKoseiEffect" && runtime.GetSymbol("rightEffectFollowRoot") is Transform follow)
                            root = follow;
                        if (bindings.Exists(b => b.root == root)) continue;
                        bindings.Add(new Binding { pickup = pickup, root = root, effect = effect,
                            originalLocal = root.localPosition, originalRotation = root.localRotation, source = runtime,
                            effectBit = runtime.programHash == "b0665ca267dc6f742a3c4c58a01eafa8" ?
                                (symbol == "leftKoseiEffect" ? 1 : symbol == "rightKoseiEffect" ? 2 : 0) : 0 });
                    }
            }
            Application.onBeforeRender -= ApplyNow;
            Application.onBeforeRender += ApplyNow;
            VRCPickup.PresentationChanged -= OnPickupChanged;
            VRCPickup.PresentationChanged += OnPickupChanged;
        }

        void OnPickupChanged(VRCPickup pickup) { if (IsKoseiPickup(pickup)) ApplyNow(); }

        void LateUpdate() => ApplyNow();

        public void ApplyNow()
        {
            foreach (var b in bindings)
            {
                if (b.root == null || b.pickup == null) continue;
                // The extracted delta-state application can leave an inactive
                // branch's object enabled. Trust the original two-bit state,
                // not that stale flag, before restarting its one-shot particles.
                // Source activation, independent expiry and VR hand selection
                // still own the bits; presentation never writes those bits.
                if (b.effectBit != 0 && b.effect != null && b.source.GetSymbol("_effectStateBits") is int bits)
                {
                    bool active = (bits & b.effectBit) != 0;
                    if (b.effect.gameObject.activeSelf != active) b.effect.gameObject.SetActive(active);
                }
                if (b.pickup.IsHeld && b.effect != null && b.effect.gameObject.activeInHierarchy)
                {
                    if (!b.applied)
                    {
                        var eye = rig != null && rig.Active ? rig.Head.transform : GetComponent<FPSCameraController>().playerCamera.transform;
                        Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
                        if (forward.sqrMagnitude < .5f) forward = transform.forward;
                        Vector3 right = Vector3.Cross(Vector3.up, forward);
                        float side = b.pickup.currentHand == 1 ? -0.18f : 0.18f;
                        if (b.pickup.name == "KoseiCube_Todoroki")
                            side = b.root.name == "RightIceFollowRoot" ? 0.20f : -0.20f;
                        b.playerOffset = eye.position + forward * .65f - Vector3.up * .25f + right * side - transform.position;
                        b.worldRotation = b.root.rotation;
                        b.applied = true;
                    }
                    // Neither the camera nor held cube/hand can rotate or move
                    // this anchor. Only the character's world translation does.
                    b.root.SetPositionAndRotation(transform.position + b.playerOffset, b.worldRotation);
                }
                else if (b.applied)
                {
                    b.root.localPosition = b.originalLocal;
                    b.root.localRotation = b.originalRotation;
                    b.applied = false;
                }
            }
        }

        public void RestoreAll()
        {
            foreach (var b in bindings)
                if (b.applied && b.root != null)
                { b.root.localPosition = b.originalLocal; b.root.localRotation = b.originalRotation; b.applied = false; }
        }

        void OnDisable()
        { Application.onBeforeRender -= ApplyNow; VRCPickup.PresentationChanged -= OnPickupChanged; RestoreAll(); }
    }
}
