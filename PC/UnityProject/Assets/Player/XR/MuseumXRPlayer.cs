using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Input;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // The original player root remains the feet/teleport/trigger owner.
    // Only tracking and input are supplied by OpenXR; all exhibit actions use Udon.
    [DefaultExecutionOrder(-150)]
    public class MuseumXRPlayer : MonoBehaviour
    {
        public bool startInVR;
        public float snapTurnDegrees = 30f;
        public float nearGrabRadius = 0.085f;
        public float headLeanAllowance = 0.15f;
        public float headCollisionClearance = 0.20f;
        public bool RayInteractionMode { get; private set; }
        public float AppliedCrouchOffset { get; private set; }
        public bool Active { get; private set; }
        public string Status { get; private set; } = "Desktop";
        public Transform TrackingOrigin { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightHand { get; private set; }
        public Camera Head => player.playerCamera;
        FPSCameraController player;
        UdonInteractor interactor;
        XRManagerSettings manager;
        readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        InputActionMap actions;
        InputAction headPosition, headRotation, headTracked;
        HandInput left, right;
        Transform previousCameraParent;
        Vector3 previousCameraPosition;
        Quaternion previousCameraRotation;
        float previousNearClip;
        float previousRadius;
        int previousFrameRate;
        bool capsuleCaptured;
        MuseumXRKoseiPresentation koseiPresentation;
        Material pointerMaterial;
        bool floorTracking, turnLatched, suspended, simulated;
        float displayLostSince = -1;
        readonly Dictionary<Canvas, Camera> canvasCameras = new Dictionary<Canvas, Camera>();
        readonly List<BaseInputModule> disabledInputModules = new List<BaseInputModule>();

        class HandInput
        {
            public int hand;
            public Transform gripTransform, aimTransform;
            public InputAction position, rotation, aimPosition, aimRotation, tracked, grip, trigger, axis, primary, secondary, axisClick, haptic;
            public bool gripWasDown, triggerWasDown, trackedLast, secondaryWasDown;
            public LineRenderer line;
            public MuseumXRPointer pointer;
            public MuseumXRHandTouch touch;
            public MuseumXRHandVisual visual;
            public Transform rayTarget;
            public bool rayUi;
        }

        void Awake()
        {
            player = GetComponent<FPSCameraController>();
            interactor = GetComponent<UdonInteractor>();
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.Exists(args, a => a == "--desktop")) yield break;
            if (!startInVR && !Array.Exists(args, a => a == "--vr")) yield break;
            yield return StartXR();
        }

        IEnumerator StartXR()
        {
            Status = "Starting OpenXR";
            BuildRig();
            CreateActions();
            actions.Enable();
            manager = XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;
            if (manager == null) { Fail("OpenXR settings missing"); yield break; }
            yield return manager.InitializeLoader();
            if (manager.activeLoader == null) { Fail("No OpenXR runtime/headset. Start SteamVR and select it as the OpenXR runtime, then relaunch with --vr."); yield break; }
            manager.StartSubsystems();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!DisplayRunning() && Time.realtimeSinceStartup < deadline) yield return null;
            if (!DisplayRunning()) { Fail("OpenXR headset did not start within 30 seconds"); yield break; }
            SubsystemManager.GetSubsystems(inputs);
            floorTracking = false;
            foreach (var input in inputs)
            {
                if (input.running && input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor)) floorTracking = true;
                input.trackingOriginUpdated += OnOriginChanged;
            }
            Activate();
            Status = "OpenXR active";
            Debug.Log("[MuseumXR] OpenXR active; floor=" + floorTracking + "; runtime=" + UnityEngine.XR.OpenXR.OpenXRRuntime.name);
        }

        void OnOriginChanged(XRInputSubsystem input)
        {
            floorTracking = input.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor;
            VRCPickup.ResetHeldMotion();
        }

        bool DisplayRunning()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays) if (display.running) return true;
            return false;
        }

        void BuildRig()
        {
            if (TrackingOrigin != null) return;
            if (player == null) player = GetComponent<FPSCameraController>();
            if (interactor == null) interactor = GetComponent<UdonInteractor>();
            previousCameraParent = Head.transform.parent;
            previousCameraPosition = Head.transform.localPosition;
            previousCameraRotation = Head.transform.localRotation;
            previousNearClip = Head.nearClipPlane;
            TrackingOrigin = Child("XR Tracking Origin", transform);
            LeftHand = Child("XR Left Grip", TrackingOrigin);
            RightHand = Child("XR Right Grip", TrackingOrigin);
            pointerMaterial = new Material(Shader.Find("Sprites/Default"));
            pointerMaterial.name = "XR Pointer Runtime";
            left = MakeHand(1, LeftHand, new Color(0.25f, 0.75f, 1f));
            right = MakeHand(0, RightHand, new Color(1f, 0.75f, 0.25f));
        }

        HandInput MakeHand(int hand, Transform grip, Color color)
        {
            var h = new HandInput { hand = hand, gripTransform = grip, aimTransform = Child(hand == 1 ? "Left Aim" : "Right Aim", TrackingOrigin) };
            h.line = h.aimTransform.gameObject.AddComponent<LineRenderer>();
            h.line.sharedMaterial = pointerMaterial;
            h.line.startColor = h.line.endColor = color;
            h.line.startWidth = 0.002f; h.line.endWidth = 0.001f;
            h.line.positionCount = 2;
            h.line.enabled = false;
            h.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h.line.receiveShadows = false;
            h.pointer = h.aimTransform.gameObject.AddComponent<MuseumXRPointer>();
            h.pointer.pointerId = hand == 1 ? -101 : -102;
            h.touch = grip.gameObject.AddComponent<MuseumXRHandTouch>();
            h.touch.Initialize(h.pointer, interactor, transform);
            h.visual = Child(hand == 1 ? "Left Glove" : "Right Glove", grip).gameObject.AddComponent<MuseumXRHandVisual>();
            h.visual.Initialize(hand == 1);
            h.visual.SetPose(0, false, 0);
            return h;
        }

        static Transform Child(string name, Transform parent)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
        }

        InputAction Action(string name, string binding, string type = "Axis")
        {
            return actions.AddAction(name, InputActionType.Value, binding, expectedControlLayout: type);
        }

        void CreateActions()
        {
            if (actions != null) return;
            actions = new InputActionMap("MuseumVR");
            headPosition = Action("HeadPosition", "<XRHMD>/centerEyePosition", "Vector3");
            headRotation = Action("HeadRotation", "<XRHMD>/centerEyeRotation", "Quaternion");
            headTracked = Action("HeadTracked", "<XRHMD>/isTracked", "Button");
            BindHand(left, "LeftHand"); BindHand(right, "RightHand");
        }

        void BindHand(HandInput h, string usage)
        {
            string p = "<XRController>{" + usage + "}/";
            h.position = Action(usage + "Position", p + "devicePosition", "Vector3");
            h.rotation = Action(usage + "Rotation", p + "deviceRotation", "Quaternion");
            h.aimPosition = Action(usage + "AimPosition", p + "pointerPosition", "Vector3");
            h.aimRotation = Action(usage + "AimRotation", p + "pointerRotation", "Quaternion");
            h.tracked = Action(usage + "Tracked", p + "isTracked", "Button");
            h.grip = Action(usage + "Grip", p + "grip"); h.grip.AddBinding(p + "gripPressed");
            h.trigger = Action(usage + "Trigger", p + "trigger"); h.trigger.AddBinding(p + "triggerPressed");
            h.axis = Action(usage + "MoveTurn", p + "primary2DAxis", "Vector2");
            h.primary = Action(usage + "Primary", p + "primaryButton", "Button");
            h.secondary = Action(usage + "Secondary", p + "secondaryButton", "Button");
            h.axisClick = Action(usage + "AxisClick", p + "{Primary2DAxisClick}", "Button");
            h.haptic = actions.AddAction(usage + "Haptic", InputActionType.PassThrough, p + "haptic", expectedControlLayout: "Haptic");
        }

        void Activate()
        {
            Active = true;
            previousFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = -1; // The XR compositor controls pacing, not the flat 60fps cap.
            player.xrControlled = true;
            var cc = player.GetComponent<CharacterController>();
            previousRadius = cc.radius; capsuleCaptured = true;
            cc.radius = Mathf.Min(cc.radius, 0.22f);
            Head.transform.SetParent(TrackingOrigin, false);
            Head.transform.localPosition = Vector3.zero;
            Head.transform.localRotation = Quaternion.identity;
            Head.nearClipPlane = 0.05f;
            Head.stereoTargetEye = StereoTargetEyeMask.Both;
            VRCPickup.SetTrackedHands(true, LeftHand, RightHand);
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (canvas.renderMode == RenderMode.WorldSpace) { canvasCameras[canvas] = canvas.worldCamera; canvas.worldCamera = Head; }
            foreach (var module in UnityEngine.Object.FindObjectsByType<BaseInputModule>())
                if (module.enabled) { disabledInputModules.Add(module); module.enabled = false; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
            Application.onBeforeRender += BeforeRender;
            koseiPresentation = GetComponent<MuseumXRKoseiPresentation>();
            if (koseiPresentation == null) koseiPresentation = gameObject.AddComponent<MuseumXRKoseiPresentation>();
            koseiPresentation.Initialize(this);
        }

        void Update()
        {
            if (!Active || simulated) return;
            if (!DisplayRunning())
            {
                if (displayLostSince < 0) displayLostSince = Time.realtimeSinceStartup;
                SuspendHands();
                player.SetXRInput(Vector2.zero, false, false);
                if (Time.realtimeSinceStartup - displayLostSince > 5f) Fail("OpenXR session stopped; desktop controls restored");
                return;
            }
            displayLostSince = -1;
            if (headTracked.ReadValue<float>() < 0.5f)
            {
                SuspendHands(); player.SetXRInput(Vector2.zero, false, false); return;
            }
            suspended = false;
            UpdatePoses();
            FollowRoomScale();
            Vector2 move = left.trackedLast ? Axis(left) : Vector2.zero;
            Vector2 turn = right.trackedLast ? Axis(right) : Vector2.zero;
            bool jump = right.primary.WasPressedThisFrame() || (IsVive(right) && right.axisClick.WasPressedThisFrame() && turn.y > 0.7f);
            player.SetXRInput(move.magnitude < 0.15f ? Vector2.zero : move, jump, left.axisClick.IsPressed());
            if (Mathf.Abs(turn.x) < 0.3f) turnLatched = false;
            if (Mathf.Abs(turn.x) > 0.7f && !turnLatched && !player.immobilized)
            {
                SnapTurn(Mathf.Sign(turn.x) * snapTurnDegrees); turnLatched = true;
            }
            ProcessHand(left, left.grip.ReadValue<float>(), left.trigger.ReadValue<float>() > 0.55f, left.trackedLast);
            ProcessHand(right, right.grip.ReadValue<float>(), right.trigger.ReadValue<float>() > 0.55f, right.trackedLast);
        }

        static bool IsVive(HandInput h) => h.axis.activeControl != null && h.axis.activeControl.device.layout == "ViveController";
        static Vector2 Axis(HandInput h) => IsVive(h) && !h.axisClick.IsPressed() ? Vector2.zero : h.axis.ReadValue<Vector2>();

        void BeforeRender() { if (Active && !simulated && !suspended) UpdatePoses(); }

        void UpdatePoses()
        {
            if (headTracked.ReadValue<float>() < 0.5f) return;
            Vector3 head = headPosition.ReadValue<Vector3>();
            // Device/Local tracking supplies eye-relative y; Floor supplies physical height.
            float originHeight = floorTracking ? 0 : player.eyeHeight;
            // Combining a physical crouch with B must not push the view
            // below the floor. Keep the actual HMD rotation/tracking pose.
            AppliedCrouchOffset = player.IsCrouched ?
                Mathf.Min(player.CrouchOffset, Mathf.Max(0, originHeight + head.y - .35f)) : 0;
            TrackingOrigin.localPosition = new Vector3(TrackingOrigin.localPosition.x,
                originHeight - AppliedCrouchOffset,
                TrackingOrigin.localPosition.z);
            Head.transform.SetLocalPositionAndRotation(head, headRotation.ReadValue<Quaternion>());
            ReadHand(left); ReadHand(right);
        }

        void ReadHand(HandInput h)
        {
            h.trackedLast = h.tracked.ReadValue<float>() > 0.5f;
            if (!h.trackedLast) { h.line.enabled = false; h.visual.SetPose(0, false, 0); return; }
            h.gripTransform.SetLocalPositionAndRotation(h.position.ReadValue<Vector3>(), h.rotation.ReadValue<Quaternion>());
            bool aim = h.aimRotation.controls.Count > 0;
            h.aimTransform.SetLocalPositionAndRotation(aim ? h.aimPosition.ReadValue<Vector3>() : h.gripTransform.localPosition,
                aim ? h.aimRotation.ReadValue<Quaternion>() : h.gripTransform.localRotation);
        }

        public void FollowRoomScale()
        {
            var cc = player.GetComponent<CharacterController>();
            float height = Mathf.Clamp(Vector3.Dot(Head.transform.position - transform.position, Vector3.up) - headCollisionClearance, cc.radius * 2, 3f);
            cc.height = height; cc.center = new Vector3(0, height * 0.5f, 0);
            Vector3 delta = Vector3.ProjectOnPlane(Head.transform.position - transform.position, Vector3.up);
            if (delta.magnitude > headLeanAllowance && cc.enabled)
            {
                Vector3 before = transform.position;
                cc.Move(delta.normalized * (delta.magnitude - headLeanAllowance));
                // Do not pull the physical view backwards by the blocked
                // part of a requested move. This also retains leaning space.
                TrackingOrigin.position -= transform.position - before;
            }
        }

        public void RefreshCrouchPose()
        {
            if (!Active || actions == null) return;
            UpdatePoses();
            FollowRoomScale();
            VRCPickup.ResetHeldMotion();
        }

        public Quaternion TeleportRootRotation(Quaternion destination)
        {
            float headYaw = Mathf.DeltaAngle(transform.eulerAngles.y, Head.transform.eulerAngles.y);
            return Quaternion.Euler(0, destination.eulerAngles.y - headYaw, 0);
        }

        public void CenterTrackingOrigin()
        {
            if (TrackingOrigin == null) return;
            TrackingOrigin.position -= Vector3.ProjectOnPlane(Head.transform.position - transform.position, Vector3.up);
        }

        public void SnapTurn(float angle)
        {
            Vector3 pivot = Head.transform.position;
            transform.RotateAround(pivot, Vector3.up, angle);
            Physics.SyncTransforms();
            VRCPickup.ResetHeldMotion();
        }

        void ProcessHand(HandInput h, float gripValue, bool trigger, bool tracked)
        {
            if (!tracked)
            {
                VRCPickup.GetPickupInHand(h.hand)?.Drop(); h.pointer.Cancel(); h.line.enabled = false;
                h.touch.Clear(); h.visual.SetPose(0, false, 0);
                h.rayTarget = null; h.rayUi = false;
                h.gripWasDown = h.triggerWasDown = h.secondaryWasDown = false; return;
            }
            bool secondary = h.secondary != null && h.secondary.IsPressed();
            if (h.hand == 1 && secondary && !h.secondaryWasDown) ToggleInteractionMode();
            float rayDistance = interactor.maxDistance;
            Ray ray = new Ray(h.aimTransform.position, h.aimTransform.forward);
            if (RayInteractionMode)
            {
                h.rayTarget = interactor.FindInteractionRayTarget(ray, out rayDistance);
                float uiDistance = h.pointer.UpdateRay(ray, interactor.maxDistance, Head, transform);
                var control = h.pointer.Target == null ? null : h.pointer.Target.GetComponentInParent<Selectable>();
                h.rayUi = control != null && control.IsActive() && control.IsInteractable() && uiDistance <= rayDistance + 0.005f;
                if (h.rayUi) { h.rayTarget = control.transform; rayDistance = uiDistance; }
            }
            else h.touch.Refresh(nearGrabRadius, Head);
            var held = VRCPickup.GetPickupInHand(h.hand);
            bool grip = gripValue > (h.gripWasDown ? 0.25f : 0.65f);
            if (!RayInteractionMode && grip && !h.gripWasDown)
            {
                if (held != null && MuseumXRKoseiPresentation.IsKoseiPickup(held)) held.Drop();
                else if (held == null && h.touch.Grab(h.hand)) Pulse(h);
            }
            held = VRCPickup.GetPickupInHand(h.hand);
            if (!RayInteractionMode && !grip && held != null && !MuseumXRKoseiPresentation.IsKoseiPickup(held)) held.Drop();
            held = VRCPickup.GetPickupInHand(h.hand);
            if (trigger && !h.triggerWasDown)
            {
                if (RayInteractionMode)
                {
                    if (h.rayUi ? h.pointer.Press() : interactor.TryInteractTouch(h.rayTarget) > 0) Pulse(h);
                }
                else if (held != null)
                {
                    koseiPresentation.ApplyNow(); held.UseDown(); koseiPresentation.ApplyNow();
                }
                else if (h.touch.Operate()) Pulse(h);
            }
            if (!trigger && h.triggerWasDown)
            {
                if (held != null) held.UseUp();
                h.touch.ReleaseTrigger();
            }
            if (h.hand == 0 && secondary && !h.secondaryWasDown) player.ToggleCrouch();
            h.secondaryWasDown = secondary;
            h.gripWasDown = grip; h.triggerWasDown = trigger;
            h.line.enabled = RayInteractionMode;
            if (h.line.enabled)
            {
                h.line.startColor = h.line.endColor = h.rayTarget == null ? new Color(0.5f, 0.65f, 0.8f, 0.6f) : new Color(0.1f, 0.55f, 1f, 1f);
                h.line.SetPosition(0, ray.origin); h.line.SetPosition(1, ray.GetPoint(rayDistance));
            }
            bool holding = VRCPickup.GetPickupInHand(h.hand) != null;
            h.visual.SetPose(holding || grip ? Mathf.Max(0.85f, gripValue) : gripValue, !RayInteractionMode, simulated ? 0 : Time.unscaledDeltaTime);
            if (!RayInteractionMode && holding) h.touch.Clear();
        }

        public void ToggleInteractionMode()
        {
            if (!Active) return;
            RayInteractionMode = !RayInteractionMode;
            foreach (var h in new[] { left, right })
            {
                VRCPickup.GetPickupInHand(h.hand)?.UseUp();
                h.touch.Clear(); h.rayTarget = null; h.rayUi = false;
                h.line.enabled = false; h.visual.SetPose(0, false, 0);
                // Keep pressed trigger edges latched so a mode switch cannot
                // complete an old UI click or create a second Interact.
            }
            Debug.Log("[MuseumXR] interaction=" + (RayInteractionMode ? "Ray (Interact only)" : "Hands"));
        }

        public bool TryNearGrab(int hand, Vector3 position)
        {
            var h = hand == 1 ? left : right;
            h.touch.Refresh(nearGrabRadius, Head);
            return h.touch.Grab(hand);
        }

        public Transform LeftTouchTarget => RayInteractionMode ? left?.rayTarget : left?.touch.Focus;
        public Transform RightTouchTarget => RayInteractionMode ? right?.rayTarget : right?.touch.Focus;

        void Pulse(HandInput h)
        {
            if (!simulated) OpenXRInput.SendHapticImpulse(h.haptic, 0.25f, 0.04f);
        }

        void SuspendHands()
        {
            suspended = true;
            foreach (var h in new[] { left, right })
                if (h != null)
                {
                    VRCPickup.GetPickupInHand(h.hand)?.Drop();
                    if (h.pointer != null) h.pointer.Cancel();
                    if (h.line != null) h.line.enabled = false;
                    h.touch?.Clear();
                    h.rayTarget = null; h.rayUi = false;
                    h.visual?.SetPose(0, false, 0);
                    h.gripWasDown = h.triggerWasDown = h.secondaryWasDown = false;
                }
        }

        void Fail(string reason)
        {
            Debug.LogWarning("[MuseumXR] " + reason);
            StopXR(); Status = reason;
        }

        public void StopXR(bool restorePlayer = true)
        {
            Application.onBeforeRender -= BeforeRender;
            SuspendHands();
            koseiPresentation?.RestoreAll();
            VRCPickup.SetTrackedHands(false, null, null);
            foreach (var pair in canvasCameras) if (pair.Key != null) pair.Key.worldCamera = pair.Value;
            canvasCameras.Clear();
            foreach (var module in disabledInputModules) if (module != null) module.enabled = true;
            disabledInputModules.Clear();
            actions?.Disable();
            if (restorePlayer && Active && Head != null)
            {
                Head.transform.SetParent(previousCameraParent, false);
                Head.transform.SetLocalPositionAndRotation(previousCameraPosition, previousCameraRotation);
                Head.nearClipPlane = previousNearClip;
            }
            if (Active) Application.targetFrameRate = previousFrameRate;
            Active = false;
            AppliedCrouchOffset = 0;
            RayInteractionMode = false;
            if (capsuleCaptured && player != null)
            { player.GetComponent<CharacterController>().radius = previousRadius; capsuleCaptured = false; }
            if (player != null)
            {
                player.xrControlled = false;
                if (restorePlayer) player.ApplyEyeHeight();
                player.SetXRInput(Vector2.zero, false, false);
            }
            if (manager != null && manager.activeLoader != null) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
            foreach (var input in inputs) input.trackingOriginUpdated -= OnOriginChanged;
            if (restorePlayer && Application.isPlaying && player != null && player.lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void OnDestroy()
        {
            StopXR(false); actions?.Dispose();
            if (pointerMaterial != null) Destroy(pointerMaterial);
        }

        void OnGUI()
        {
            if (!Active && Status != "Desktop") GUI.Label(new Rect(12, 45, 1000, 50), Status);
        }

        public void BeginSimulation()
        {
            if (!LocalVrcw.RuntimeTestGate.Enabled) throw new InvalidOperationException("Simulation requires explicit regression mode");
            BuildRig(); CreateActions(); actions.Enable(); simulated = true; floorTracking = true; Activate();
        }
        public void SimulateHand(int hand, float grip, bool trigger, bool tracked)
        {
            var h = hand == 1 ? left : right;
            h.aimTransform.SetPositionAndRotation(h.gripTransform.position, h.gripTransform.rotation);
            ProcessHand(h, grip, trigger, tracked);
        }
        public InputActionMap SimulationActions => actions;
        public void SampleSimulationTracking() { UpdatePoses(); }
    }
}
