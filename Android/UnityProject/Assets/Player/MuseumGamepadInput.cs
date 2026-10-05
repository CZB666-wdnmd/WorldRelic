using UnityEngine;
using UnityEngine.InputSystem;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // One common non-XR gamepad route on Windows and Android. Touch and legacy
    // keyboard/mouse retain their own routes; no synthetic Submit/keyboard events.
    [DefaultExecutionOrder(-60)]
    public sealed class MuseumGamepadInput : MonoBehaviour
    {
        public float lookDegreesPerSecond = 105f;
        public bool aimAssistEnabled = true;
        public Vector2 Move { get; private set; }
        public Vector2 LookStick { get; private set; }
        public Vector2 LookDegrees { get; private set; }
        public bool Jump { get; private set; }
        public bool Run { get; private set; }
        public bool DevicePresent => Gamepad.current != null && Gamepad.current.added;
        public bool UsingGamepad => Time.unscaledTime - lastGamepadActivity < 2f && DevicePresent;
        public MuseumGamepadAimAssist Aim { get; private set; }
        FPSCameraController player;
        UdonInteractor interactor;
        bool interact, pickup, use, fire, focused = true, latchedUntilNeutral;
        Gamepad sampledDevice;
        VRCPickup usingPickup;
        int usingMask;
        float lastGamepadActivity = float.NegativeInfinity;
        public bool GameplayReady
        {
            get
            {
                if (!focused || player == null || player.xrControlled || VRCPickup.IsVrActive ||
                    Time.timeScale <= 0 || MuseumViewSettings.BlocksDesktopInput) return false;
#if UNITY_ANDROID
                if (!MuseumXRPlayer.MobileFlatReady || (MuseumFlatInput.IsActive && MuseumFlatInput.Instance.BlocksGameplayInput)) return false;
#endif
                return true;
            }
        }
        void Awake()
        {
            player = GetComponent<FPSCameraController>();
            interactor = GetComponent<UdonInteractor>();
            Aim = GetComponent<MuseumGamepadAimAssist>();
            if (Aim == null) Aim = gameObject.AddComponent<MuseumGamepadAimAssist>();
            Aim.Initialize(player, interactor);
        }
        void Update() { Sample(Time.unscaledDeltaTime); }
        // Also used by tests with the actual Input System device state buffer.
        public void Sample(float deltaTime)
        {
            Move = LookStick = LookDegrees = Vector2.zero;
            Jump = Run = interact = pickup = use = fire = false;
            var pad = Gamepad.current;
            bool keyboardCrouch = GameplayReady && Keyboard.current != null &&
                (Keyboard.current.cKey.wasPressedThisFrame || Keyboard.current.bKey.wasPressedThisFrame);
            if (keyboardCrouch)
                player.ToggleCrouch();
            if (!GameplayReady || pad == null || !pad.added)
            { latchedUntilNeutral = true; sampledDevice = pad; ReleaseUse(); return; }
            if (sampledDevice != pad)
            { sampledDevice = pad; latchedUntilNeutral = true; ReleaseUse(); }
            // Hot plug / focus recovery must not turn an already-held button
            // into a fresh action. Neutralise before accepting the new device.
            if (latchedUntilNeutral)
            {
                if (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed ||
                    pad.buttonNorth.isPressed || pad.rightTrigger.ReadValue() > .2f ||
                    pad.leftTrigger.ReadValue() > .2f) return;
                latchedUntilNeutral = false;
            }
            Move = FilterStick(pad.leftStick.ReadValue());
            LookStick = FilterStick(pad.rightStick.ReadValue());
            LookDegrees = LookStick * lookDegreesPerSecond * Mathf.Min(deltaTime, .05f);
            Jump = pad.buttonSouth.wasPressedThisFrame;
            interact = pad.buttonWest.wasPressedThisFrame;
            pickup = pad.buttonNorth.wasPressedThisFrame;
            use = pad.rightTrigger.ReadValue() > .2f;
            fire = pad.leftTrigger.ReadValue() > .2f;
            Run = pad.leftShoulder.isPressed || pad.leftStickButton.isPressed;
            if (Move.sqrMagnitude > 0 || LookStick.sqrMagnitude > 0 || Jump || interact || pickup ||
                use || fire || pad.buttonEast.wasPressedThisFrame) lastGamepadActivity = Time.unscaledTime;
            if (pad.buttonEast.wasPressedThisFrame && !keyboardCrouch) player.ToggleCrouch();
        }
        public static Vector2 FilterStick(Vector2 value)
        {
            // Unity already applies the platform stick deadzone. This small
            // residual guard avoids drift without a second large deadzone.
            return value.sqrMagnitude < .015f * .015f ? Vector2.zero : Vector2.ClampMagnitude(value, 1f);
        }
        public static Vector2 KeyboardMove()
        {
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            return Vector2.ClampMagnitude(new Vector2(
                (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0),
                (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0)), 1f);
        }
        public void ApplyAimAssist(float deltaTime, bool manualPointerLook)
        {
            if (!GameplayReady || !aimAssistEnabled || manualPointerLook || LookStick.sqrMagnitude < .0001f)
            { Aim?.Clear(); return; }
            Aim?.Apply(LookStick, deltaTime);
        }
        void LateUpdate() { DispatchActions(); }
        public void DispatchActions()
        {
            if (!GameplayReady || interactor == null || player.playerCamera == null)
            { ReleaseUse(); return; }
            var view = player.playerCamera.transform;
            var ray = new Ray(view.position, view.forward);
            // Y is explicitly grab/release; X never implicitly grabs a pickup.
            if (pickup)
            {
                ReleaseUse();
                if (!interactor.TryDropHeldPickup()) interactor.TryPickupRay(ray, 0);
            }
            if (interact) interactor.TryInteractOnlyRay(ray);
            var held = VRCPickup.HeldPickup;
            int mask = (use ? 2 : 0) | (fire && interactor.HeldPickupHasDualElement ? 1 : 0);
            if (mask == 0 || held != usingPickup) ReleaseUse();
            if (mask != 0 && held != null)
            {
                usingPickup = held;
                // Independent trigger edges: adding LT while RT is held must
                // not retrigger ice. Keep the original UseUp when both release.
                if ((mask & 1) != 0 && (usingMask & 1) == 0) interactor.TryUseGamepadHeldPickup(true, true);
                if ((mask & 2) != 0 && (usingMask & 2) == 0) interactor.TryUseGamepadHeldPickup(false, true);
                usingMask = mask;
            }
            interact = pickup = false;
        }
        void ReleaseUse() { if (usingPickup != null) usingPickup.UseUp(); usingPickup = null; usingMask = 0; }
        void OnApplicationFocus(bool value)
        { focused = value; if (!value) { latchedUntilNeutral = true; Clear(); } }
        void OnApplicationPause(bool value) { OnApplicationFocus(!value); }
        void Clear()
        { Move = LookStick = LookDegrees = Vector2.zero; Jump = Run = interact = pickup = use = fire = false; Aim?.Clear(); ReleaseUse(); }
        void OnDisable() { latchedUntilNeutral = true; Clear(); }
    }
}
