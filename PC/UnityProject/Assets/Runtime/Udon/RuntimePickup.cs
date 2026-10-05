using UnityEngine;
using VRC.SDK3.Components;
namespace LocalVrcw.Udon
{
    // Regression/API facade delegates to the same mature native pickup used in play.
    public sealed class RuntimePickup : MonoBehaviour
    {
        public Camera view;
        public static VRCPickup Held => VRCPickup.HeldPickup;
        public static bool Grab(VRCPickup pickup, Vector3 contact)
        { if (pickup == null || Held != null) return false; bool result = pickup.TryPickupAt(0, contact); pickup.UpdateHeldPose(Time.unscaledTime); return result; }
        public static void Drop() => Held?.Drop();
        public static void Dispatch(string name)
        { if (name == "_onPickupUseDown") Held?.UseDown(); else if (name == "_onPickupUseUp") Held?.UseUp(); else if (Held != null) foreach (var rt in Held.GetComponents<UdonRuntime>()) rt.SendCustomEvent(name); }
        public Vector3 Hand => VRCPickup.GetRightHandPosition(view);
        public Vector3 AnchorPosition => Held == null ? Hand : Held.GrabAnchorPosition;
        void OnDestroy() => Drop();
    }
}
