using System;
using UnityEngine;
using VRC.SDK3.Components;

namespace LocalVrcw.Udon
{
    // Local-only API equivalents. No VRChat client/SDK code or network service.
    public sealed class TrackingData { public Vector3 position; public Quaternion rotation; }
    public sealed class LocalPlayer
    {
        public static LocalPlayer Instance;
        public Camera camera;
        public AniqueMuseum.FPSCameraController controller;
        public Vector3 velocity { get => controller == null ? Vector3.zero : controller.GetVelocity(); set => controller?.SetVelocity(value); }
        public bool immobilized { get => controller != null && controller.immobilized; set => controller?.SetImmobilized(value); }
        public float walk { get => controller?.GetWalkSpeedForUdon() ?? 2; set => controller?.SetWalkSpeedFromUdon(value); }
        public float run { get => controller?.GetRunSpeedForUdon() ?? 4; set => controller?.SetRunSpeedFromUdon(value); }
        public float strafe { get => controller?.GetStrafeSpeedForUdon() ?? 2; set => controller?.SetStrafeSpeedFromUdon(value); }
        public float jump { get => controller?.GetJumpImpulseForUdon() ?? 3; set => controller?.SetJumpImpulseFromUdon(value); }
        public float gravity { get => controller == null ? 1 : controller.gravity / -20f; set { if (controller != null && !controller.FreeCameraActive) controller.gravity = -20f * value; } }
        public Vector3 Position => controller == null ? camera.transform.position - Vector3.up * 1.2f : controller.transform.position;
        public Quaternion Rotation => controller == null ? Quaternion.Euler(0, camera.transform.eulerAngles.y, 0) : controller.transform.rotation;
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (controller != null) controller.TeleportTo(position, rotation);
            else { camera.transform.SetPositionAndRotation(position + Vector3.up * 1.2f, rotation); camera.GetComponent<ProbeCamera>()?.ResetLook(); }
            velocity = Vector3.zero;
        }
        public TrackingData Tracking(int type)
        {
            Transform tracked = type == 1 ? VRCPickup.GetTrackedHand(1) : type == 2 ? VRCPickup.GetTrackedHand(0) : camera.transform;
            return new TrackingData { position = tracked != null ? tracked.position : type == 1 ? VRCPickup.GetLeftHandPosition(camera) : VRCPickup.GetRightHandPosition(camera), rotation = tracked != null ? tracked.rotation : camera.transform.rotation };
        }
    }
    public static partial class UdonExternImpl
    {
        internal static UdonRuntime FindRuntime(object value, UdonRuntime fallback, string member = null, bool preferEvent = false)
        {
            if (value is UdonRuntime direct) return direct;
            if (value is VRC.Udon.UdonBehaviour source) return source.RuntimeBridge as UdonRuntime;
            GameObject go = AGo(value);
            if (go == null) return fallback;
            var candidates = go.GetComponents<UdonRuntime>();
            foreach (var rt in candidates)
                if (member != null && (preferEvent ? rt.HasEvent(member) : rt.HasSymbol(member))) return rt;
            return candidates.Length == 1 ? candidates[0] : null;
        }
        internal static object VUdonEvent(string sig, UdonRuntime rt, object[] a)
        {
            bool explicitReceiver = a.Length > 1 && a[0] is UdonRuntime;
            var target = explicitReceiver ? (UdonRuntime)a[0] : rt;
            int offset = explicitReceiver ? 1 : 0;
            if (sig.Contains("__SendCustomEventDelayed"))
            {
                string name = AStr(a[offset]);
                if (sig.Contains("DelayedFrames")) target.SendCustomEventFrames(name, AInt(a[offset + 1]));
                else target.SendCustomEventDelayed(name, AFloat(a[offset + 1]));
                return null;
            }
            if (sig.Contains("__SendCustomEvent__")) { target.SendCustomEvent(AStr(a[offset])); return null; }
            if (sig.Contains("__SendCustomNetworkEvent")) { FindRuntime(a[0], rt)?.SendCustomEvent(AStr(a[2])); return null; }
            if (sig.Contains("__SetProgramVariable")) { FindRuntime(a[0], rt)?.SetSymbol(AStr(a[1]), a[2]); return null; }
            if (sig.Contains("__GetProgramVariable")) return FindRuntime(a[0], rt)?.GetSymbol(AStr(a[1]));
            if (sig.Contains("__RequestSerialization")) return null; // No network peers in this local research player.
            if (sig.Contains("__set_enabled")) { var host = FindRuntime(a[0], rt); host.Source.enabled = host.enabled = ABool(a[1]); return null; }
            if (sig.Contains("__get_enabled")) return FindRuntime(a[0], rt).Source.enabled;
            if (sig.Contains("__get_isActiveAndEnabled")) return FindRuntime(a[0], rt).Source.isActiveAndEnabled;
            if (sig.Contains("__get_InteractionText") || sig.Contains("__get_interactText")) return FindRuntime(a[0], rt)?.InteractionText;
            if (sig.Contains("__set_InteractionText")) { FindRuntime(a[0], rt).InteractionText = AStr(a[1]); return null; }
            return null;
        }
        internal static object VUdonBehaviour(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_gameObject")) return AGo(a[0]);
            if (sig.Contains("__get_transform")) return AGo(a[0])?.transform;
            return VUdonEvent(sig, rt, a);
        }
        internal static object VNetworking(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_LocalPlayer") || sig.Contains("__GetOwner")) return LocalPlayer.Instance;
            if (sig.Contains("__IsOwner")) return true;
            if (sig.Contains("__SetOwner")) return null;
            if (sig.Contains("__GetServerTimeInSeconds")) return Time.realtimeSinceStartupAsDouble;
            if (sig.Contains("__GetServerTimeInMilliseconds")) return unchecked((int)(Time.realtimeSinceStartupAsDouble * 1000));
            if (sig.Contains("__CalculateServerDeltaTime")) return ADouble(a[0]) - ADouble(a[1]);
            if (sig.Contains("__get_IsClogged")) return false;
            return null;
        }
        internal static object VPlayerApi(string sig, UdonRuntime rt, object[] a)
        {
            var player = a.Length > 0 ? a[0] as LocalPlayer : LocalPlayer.Instance;
            if (sig.Contains("__GetPlayerCount")) return 1;
            if (sig.Contains("__GetPlayers")) { var players = a[0] as Array; if (players?.Length > 0) players.SetValue(LocalPlayer.Instance, 0); return players; }
            if (sig.Contains("__get_isLocal") || sig.Contains("__IsValid")) return player != null;
            if (sig.Contains("__IsUserInVR")) return VRCPickup.IsVrActive;
            if (sig.Contains("__get_playerId")) return 1;
            if (sig.Contains("__GetPosition")) return player?.Position ?? Vector3.zero;
            if (sig.Contains("__GetRotation")) return player?.Rotation ?? Quaternion.identity;
            if (sig.Contains("__GetTrackingData")) return player?.Tracking(AInt(a[1]));
            if (sig.Contains("__GetBonePosition")) return player?.Tracking(AInt(a[1]) == (int)HumanBodyBones.LeftHand ? 1 : AInt(a[1]) == (int)HumanBodyBones.RightHand ? 2 : 0).position ?? Vector3.zero;
            if (sig.Contains("__GetBoneRotation")) return player?.Tracking(AInt(a[1]) == (int)HumanBodyBones.LeftHand ? 1 : AInt(a[1]) == (int)HumanBodyBones.RightHand ? 2 : 0).rotation ?? Quaternion.identity;
            if (sig.Contains("__GetAvatarEyeHeightAsMeters")) return player?.controller?.ActualEyeHeight ?? 1.2f;
            if (sig.Contains("__TeleportTo")) { if (NativePortalPolicy.Incomplete(rt)) return null; if (player?.controller != null) player.controller.TryTeleportFromUdon(AV3(a[1]), AQuat(a[2]), rt.programHash); else player?.Teleport(AV3(a[1]), AQuat(a[2])); return null; }
            if (sig.Contains("__Immobilize")) { if (player != null) player.immobilized = ABool(a[1]); return null; }
            if (sig.Contains("__GetVelocity")) return player?.velocity ?? Vector3.zero;
            if (sig.Contains("__SetVelocity")) { if (player != null) player.velocity = AV3(a[1]); return null; }
            if (sig.Contains("__GetPickupInHand")) return VRCPickup.GetPickupInHand(AInt(a[1]));
            if (player == null) return null;
            if (sig.Contains("__GetWalkSpeed")) return player.walk;
            if (sig.Contains("__SetWalkSpeed")) { player.walk = AFloat(a[1]); return null; }
            if (sig.Contains("__GetRunSpeed")) return player.run;
            if (sig.Contains("__SetRunSpeed")) { player.run = AFloat(a[1]); return null; }
            if (sig.Contains("__GetStrafeSpeed")) return player.strafe;
            if (sig.Contains("__SetStrafeSpeed")) { player.strafe = AFloat(a[1]); return null; }
            if (sig.Contains("__GetJumpImpulse")) return player.jump;
            if (sig.Contains("__SetJumpImpulse")) { player.jump = AFloat(a[1]); return null; }
            if (sig.Contains("__GetGravityStrength")) return player.gravity;
            if (sig.Contains("__SetGravityStrength")) { player.gravity = AFloat(a[1]); return null; }
            return null;
        }
        internal static object VTrackingData(string sig, UdonRuntime rt, object[] a)
        {
            var data = a[0] as TrackingData;
            return sig.Contains("__get_position") ? (object)(data?.position ?? Vector3.zero) : data?.rotation ?? Quaternion.identity;
        }
        internal static object VPickup(string sig, UdonRuntime rt, object[] a)
        {
            UdonExterns.pickupExternCalls++;
            var source = a[0] as VRCPickup;
            if (sig.Contains("__get_IsHeld")) return source != null && source.IsHeld;
            if (sig.Contains("__Drop")) { source?.Drop(); return null; }
            if (sig.Contains("__get_pickupable")) return source != null && source.pickupable;
            if (sig.Contains("__set_pickupable")) { if (source != null) source.pickupable = ABool(a[1]); return null; }
            if (sig.Contains("__get_currentHand")) return source?.currentHand ?? -1;
            if (sig.Contains("__get_DisallowTheft")) return source != null && source.DisallowTheft;
            return null;
        }
        internal static object VUtilities(string sig, UdonRuntime rt, object[] a) => a[0] != null && !(a[0] is UnityEngine.Object o && o == null);
        internal static object VInput(string sig, UdonRuntime rt, object[] a) => false;
        internal static object VEventTiming(string sig, UdonRuntime rt, object[] a) => 0;
        internal static object VSerialization(string sig, UdonRuntime rt, object[] a) => null;
        internal static object VSerializationResult(string sig, UdonRuntime rt, object[] a) => null;
        // Browser navigation is allowed only within the current explicit interact edge.
        public static int blockedExternalActions;
        internal static object VCustomAction(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Execute") && a.Length >= 2 && a[0] is global::VRCUrlLauncher launcher)
            { if (!launcher.Execute(AStr(a[1]))) blockedExternalActions++; }
            return null;
        }
        public static string lastOpenedGroupId, lastOpenedGroupUrl;
        internal static object VEconomyStore(string sig, UdonRuntime rt, object[] a)
        {
            // Local equivalent of the native group info sheet, not purchases
            // or a join action. Identifier comes from the user's raw program.
            if (sig.Contains("__OpenGroupPage") && a.Length == 1)
            {
                string id = AStr(a[0])?.Trim();
                if (id != null && id.StartsWith("grp_", StringComparison.Ordinal) && Guid.TryParseExact(id.Substring(4), "D", out _))
                {
                    string url = "https://vrchat.com/home/group/" + id;
                    if (ExternalUrlOpenGuard.TryOpen(url)) { lastOpenedGroupId = id; lastOpenedGroupUrl = url; return null; }
                }
            }
            blockedExternalActions++; return null;
        }
        internal static object Convert2(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("ToInt32")) return Convert.ToInt32(a[0]);
            if (sig.Contains("ToInt64")) return Convert.ToInt64(a[0]);
            if (sig.Contains("ToSingle")) return Convert.ToSingle(a[0]);
            if (sig.Contains("ToDouble")) return Convert.ToDouble(a[0]);
            if (sig.Contains("ToString")) return AStr(a[0]);
            if (sig.Contains("ToBoolean")) return ABool(a[0]);
            return null;
        }
    }
}
