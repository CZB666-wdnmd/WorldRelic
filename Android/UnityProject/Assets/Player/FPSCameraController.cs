using UnityEngine;

namespace AniqueMuseum
{
    /// <summary>
    /// Simple first-person controller for the rebuilt museum.
    /// WASD move, mouse look (hold right mouse or always), space jump, gravity, sprint.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-40)]
    public class FPSCameraController : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 2.0f;
        public float runSpeed = 4.0f;
        // These are the desktop equivalents of the source VRCPlayerApi
        // locomotion properties.  Source Udon programs such as SlowZone
        // modify them independently, so do not collapse lateral movement
        // into walkSpeed.
        public float strafeSpeed = 2.0f;
        public float jumpImpulse = 3.0f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;

        [Header("Desktop Free Camera")]
        public float freeCameraSpeed = 6f;
        public float freeCameraBoostSpeed = 18f;

        [Header("Look")]
        public float mouseSensitivity = 2.0f;
        public float minPitch = -85f;
        public float maxPitch = 85f;
        public bool lockCursor = true;

        [Header("Camera")]
        public Camera playerCamera;
        [Tooltip("Desktop avatar eye height for comparison; the source world does not force an avatar size.")]
        [Range(0.2f, 5f)] public float eyeHeight = 1.2f;
        public float ActualEyeHeight => playerCamera == null ? eyeHeight :
            Vector3.Dot(playerCamera.transform.position - transform.position, transform.up);

        private CharacterController cc;
        MuseumGamepadInput gamepadInput;
        public bool IsCrouched { get; private set; }
        public float CrouchOffset => Mathf.Min(.55f, eyeHeight * .45f);
        readonly Collider[] standingObstacles = new Collider[64];
        public bool ToggleCrouch() => SetCrouched(!IsCrouched);
        public bool SetCrouched(bool value)
        {
            if (value == IsCrouched) return true;
            if (value && FreeCameraActive) return false;
            if (!value && !CanStand()) return false;
            IsCrouched = value;
            if (xrControlled) GetComponent<MuseumXRPlayer>()?.RefreshCrouchPose();
            else { ApplyEyeHeight(); VRC.SDK3.Components.VRCPickup.ResetHeldMotion(); }
            return true;
        }
        bool CanStand()
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            if (cc == null || !cc.enabled) return true;
            float height = Mathf.Max(cc.radius * 2f, eyeHeight * (1.8f / 1.6f));
            if (xrControlled && playerCamera != null)
                height = Mathf.Clamp(ActualEyeHeight + (GetComponent<MuseumXRPlayer>()?.AppliedCrouchOffset ?? CrouchOffset) -
                    (GetComponent<MuseumXRPlayer>()?.headCollisionClearance ?? .20f), cc.radius * 2f, 3f);
            Vector3 feet = transform.TransformPoint(new Vector3(cc.center.x, 0, cc.center.z));
            float radius = Mathf.Max(.01f, cc.radius - .02f);
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (cc.radius + .02f),
                feet + Vector3.up * Mathf.Max(cc.radius + .02f, height - cc.radius - .02f),
                radius, standingObstacles, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = standingObstacles[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                var p = c.GetComponentInParent<VRC.SDK3.Components.VRCPickup>();
                if (p != null && p.IsHeld) continue;
                return false;
            }
            return count < standingObstacles.Length;
        }
        public void ApplyFlatLook(Vector2 degrees)
        {
            if (xrControlled) return;
            pitch = Mathf.Clamp(pitch - degrees.y, minPitch, maxPitch);
            transform.Rotate(0, degrees.x, 0, Space.Self);
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
        private float pitch;
        private Vector3 velocity;
        private bool movementBaselinesReady;
        private float baselineWalkSpeed;
        private float baselineRunSpeed;
        private float baselineStrafeSpeed;
        private float baselineJumpImpulse;
        private DesktopFreeCameraMode? verificationMode;
        // Udon's VRCPlayerApi.Immobilize controls locomotion, not the user's
        // view. Source sequences such as 600_HighJumpGimmick keep calling
        // TeleportTo while this is true, so desktop WASD movement must not
        // compete with those source-driven positions.
        public bool immobilized { get; private set; }
        public bool xrControlled { get; set; }
        public bool flatControlled { get; set; }
        Vector2 flatMove, flatLook;
        bool flatJump, flatRun;
        public void SetFlatInput(Vector2 move, Vector2 lookDegrees, bool jump, bool run)
        {
            flatControlled = true; flatMove = Vector2.ClampMagnitude(move, 1);
            flatLook = lookDegrees; flatJump = jump; flatRun = run;
        }
        public DesktopFreeCameraMode CameraMode
        {
            get
            {
                if (xrControlled || VRC.SDK3.Components.VRCPickup.IsVrActive)
                    return DesktopFreeCameraMode.Default;
                return verificationMode ?? DesktopFreeCameraSettings.Requested;
            }
        }
        public bool FreeCameraActive => CameraMode != DesktopFreeCameraMode.Default;
        public bool FreeCameraNoCollision => CameraMode == DesktopFreeCameraMode.FreeCameraNoCollision;
        Vector2 xrMove;
        bool xrJump, xrRun;

        public void SetXRInput(Vector2 move, bool jump, bool run)
        {
            xrMove = Vector2.ClampMagnitude(move, 1f);
            xrJump = jump;
            xrRun = run;
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            CaptureMovementBaselines();
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();
            ApplyEyeHeight();
            gamepadInput = GetComponent<MuseumGamepadInput>();
            if (gamepadInput == null) gamepadInput = gameObject.AddComponent<MuseumGamepadInput>();
            ApplyCameraModeState();
        }

        public void ApplyEyeHeight()
        {
            if (xrControlled) return;
            eyeHeight = Mathf.Clamp(eyeHeight, 0.2f, 5f);
            if (cc == null) cc = GetComponent<CharacterController>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera != null)
            {
                Vector3 offset = playerCamera.transform.localPosition;
                offset.y = Mathf.Max(.2f, eyeHeight - (IsCrouched ? CrouchOffset : 0));
                playerCamera.transform.localPosition = offset;
            }
            if (cc != null)
            {
                // Keep the same feet/root reference used by source teleports.
                // Preserve horizontal clearance and the former body/eye ratio.
                cc.height = Mathf.Max(cc.radius * 2f, (eyeHeight - (IsCrouched ? CrouchOffset : 0)) * (1.8f / 1.6f));
                cc.center = new Vector3(cc.center.x, cc.height * 0.5f, cc.center.z);
            }
        }

        void Start()
        {
            if (FreeCameraActive)
                Debug.Log("[FreeCamera] mode=" + CameraMode +
                          " controls=WASD-facing-direction/Shift-boost; interactions=enabled; teleports=" +
                          (FreeCameraNoCollision ? "blocked" : "enabled"));
            if (lockCursor && !xrControlled && !flatControlled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            if (MuseumViewSettings.BlocksDesktopInput) return;
            HandleLook();
            HandleMove();
            xrJump = false; flatJump = false;
            if (xrControlled || flatControlled) return;
            if (Input.GetKeyDown(KeyCode.Escape) && lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Input.GetMouseButtonDown(0) && lockCursor && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void HandleLook()
        {
            if (xrControlled) return;
            if (flatControlled)
            {
                ApplyFlatLook(flatLook + (gamepadInput != null ? gamepadInput.LookDegrees : Vector2.zero));
                gamepadInput?.ApplyAimAssist(Time.unscaledDeltaTime, flatLook.sqrMagnitude > .0001f);
                return;
            }
            Vector2 mouse = Cursor.lockState == CursorLockMode.Locked ?
                new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity : Vector2.zero;
            ApplyFlatLook(mouse + (gamepadInput != null ? gamepadInput.LookDegrees : Vector2.zero));
            gamepadInput?.ApplyAimAssist(Time.unscaledDeltaTime, mouse.sqrMagnitude > .0001f);
        }

        Vector3 DesktopMovementInput()
        {
            // Legacy InputManager includes duplicate joystick axes and a
            // Y=Jump binding. The explicit gamepad route must not add twice.
            Vector2 keys = gamepadInput != null && gamepadInput.DevicePresent ? MuseumGamepadInput.KeyboardMove() :
                new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            Vector2 combined = Vector2.ClampMagnitude(keys + (gamepadInput != null ? gamepadInput.Move : Vector2.zero), 1f);
            return new Vector3(combined.x, 0, combined.y);
        }

        void HandleMove()
        {
            if (cc == null)
                return;

            bool jumpRequested = xrControlled ? xrJump :
                ((gamepadInput != null && gamepadInput.Jump) || (flatControlled ? flatJump : Input.GetKeyDown(KeyCode.Space)));
            if (jumpRequested && IsCrouched && !SetCrouched(false)) jumpRequested = false;

            ApplyCameraModeState();
            if (FreeCameraActive)
            {
                HandleFreeCameraMove();
                return;
            }

            if (immobilized)
            {
                velocity = Vector3.zero;
                return;
            }

            float forwardSpeed = (xrControlled ? xrRun : (flatControlled ? flatRun : Input.GetKey(KeyCode.LeftShift)) || (gamepadInput != null && gamepadInput.Run)) ? runSpeed : walkSpeed;
            Vector3 movementInput = xrControlled ? new Vector3(xrMove.x, 0, xrMove.y) :
                flatControlled ? new Vector3(flatMove.x, 0, flatMove.y) +
                    (gamepadInput != null ? new Vector3(gamepadInput.Move.x, 0, gamepadInput.Move.y) : Vector3.zero) : DesktopMovementInput();
            // Normalize input before applying the independently controlled
            // source VRCPlayerApi speeds.  This preserves the prior full
            // walk/run speed for diagonal input when strafe and forward match,
            // while still allowing SlowZone to change strafe separately.
            if (movementInput.sqrMagnitude > 1f)
                movementInput.Normalize();
            Vector3 forward = xrControlled && playerCamera != null ?
                Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized : transform.forward;
            if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 move = right * movementInput.x * strafeSpeed + forward * movementInput.z * forwardSpeed;

            if (cc.isGrounded)
            {
                velocity.y = -1f;
                if (jumpRequested)
                    velocity.y = jumpImpulse;
            }
            else
            {
                velocity.y += gravity * Time.deltaTime;
            }

            Vector3 motion = move + velocity;
            cc.Move(motion * Time.deltaTime);
        }

        void HandleFreeCameraMove()
        {
            immobilized = false;
            velocity = Vector3.zero;
            Transform view = playerCamera != null ? playerCamera.transform : transform;
            Vector3 input = DesktopMovementInput();
            // W/S follows the complete camera forward vector, including its
            // pitch. Looking up/down is therefore the only way to change
            // altitude; there are deliberately no separate rise/fall keys.
            Vector3 direction = view.right * input.x + view.forward * input.z;
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || (gamepadInput != null && gamepadInput.Run)
                ? freeCameraBoostSpeed : freeCameraSpeed;
            Vector3 delta = direction * speed * Time.deltaTime;

            if (!FreeCameraNoCollision)
            {
                cc.Move(delta);
                return;
            }

            // CharacterController.Move may still depenetrate even with
            // detectCollisions disabled. A one-write disabled capsule is the
            // deterministic no-clip path; UdonTriggerRouter polls the enabled
            // capsule in FixedUpdate afterwards so area/BGM/exhibit triggers
            // continue to work at the destination.
            bool enabled = cc.enabled;
            if (enabled) cc.enabled = false;
            transform.position += delta;
            if (enabled) cc.enabled = true;
            Physics.SyncTransforms();
        }

        void CaptureMovementBaselines()
        {
            if (movementBaselinesReady) return;
            movementBaselinesReady = true;
            baselineWalkSpeed = walkSpeed;
            baselineRunSpeed = runSpeed;
            baselineStrafeSpeed = strafeSpeed;
            baselineJumpImpulse = jumpImpulse;
        }

        void ApplyCameraModeState()
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            if (cc != null) cc.detectCollisions = !FreeCameraNoCollision;
            if (!FreeCameraActive) return;
            CaptureMovementBaselines();
            walkSpeed = baselineWalkSpeed;
            runSpeed = baselineRunSpeed;
            strafeSpeed = baselineStrafeSpeed;
            jumpImpulse = baselineJumpImpulse;
            immobilized = false;
            velocity = Vector3.zero;
        }

        public void SetCameraModeForVerification(DesktopFreeCameraMode mode)
        {
            verificationMode = mode;
            ApplyCameraModeState();
        }

        public void ClearCameraModeVerification()
        {
            verificationMode = null;
            ApplyCameraModeState();
        }

        public float GetWalkSpeedForUdon() { CaptureMovementBaselines(); return FreeCameraActive ? baselineWalkSpeed : walkSpeed; }
        public float GetRunSpeedForUdon() { CaptureMovementBaselines(); return FreeCameraActive ? baselineRunSpeed : runSpeed; }
        public float GetStrafeSpeedForUdon() { CaptureMovementBaselines(); return FreeCameraActive ? baselineStrafeSpeed : strafeSpeed; }
        public float GetJumpImpulseForUdon() { CaptureMovementBaselines(); return FreeCameraActive ? baselineJumpImpulse : jumpImpulse; }
        public void SetWalkSpeedFromUdon(float value) { if (!FreeCameraActive) walkSpeed = value; }
        public void SetRunSpeedFromUdon(float value) { if (!FreeCameraActive) runSpeed = value; }
        public void SetStrafeSpeedFromUdon(float value) { if (!FreeCameraActive) strafeSpeed = value; }
        public void SetJumpImpulseFromUdon(float value) { if (!FreeCameraActive) jumpImpulse = value; }

        public bool TryTeleportFromUdon(Vector3 position, Quaternion rotation, string sourceProgramHash)
        {
            if (FreeCameraNoCollision) return false;
            if (CameraMode == DesktopFreeCameraMode.FreeCamera &&
                sourceProgramHash == "3c4069e16a55ee34ba5db539522368ee") return false;
            // HighJump uses TeleportTo every frame for its scripted arc.
            // Re-aligning the HMD on every sample cancels physical head yaw.
            // Keep the XR rig orientation and room offset during this motion;
            // ordinary portals still perform their authored view alignment.
            bool xrScriptedLaunch = xrControlled && sourceProgramHash == "3c4069e16a55ee34ba5db539522368ee";
            TeleportToInternal(position, xrScriptedLaunch ? transform.rotation : rotation, !xrScriptedLaunch);
            return true;
        }

        /// <summary>
        /// The standalone replacement for VRCPlayerApi.TeleportTo.  Moving a
        /// live CharacterController by assigning transform.position leaves
        /// the controller's native overlap state intact on some Player
        /// backends; the next Move can then restore the old capsule position.
        /// Disable it for the single transform write, reset vertical motion,
        /// and resync physics just as a VRC teleport changes locomotion state.
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            TeleportToInternal(position, rotation, true);
        }

        void TeleportToInternal(Vector3 position, Quaternion rotation, bool alignXRView)
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            bool controllerWasEnabled = cc != null && cc.enabled;
            if (controllerWasEnabled) cc.enabled = false;
            var xr = xrControlled ? GetComponent<MuseumXRPlayer>() : null;
            if (xr != null && alignXRView) rotation = xr.TeleportRootRotation(rotation);
            transform.SetPositionAndRotation(position, rotation);
            if (xr != null && alignXRView) xr.CenterTrackingOrigin();
            velocity = Vector3.zero;
            if (controllerWasEnabled) cc.enabled = true;
            Physics.SyncTransforms();
            if (xr != null) VRC.SDK3.Components.VRCPickup.ResetHeldMotion();
        }

        public void SetImmobilized(bool value)
        {
            if (FreeCameraActive)
            {
                immobilized = false;
                velocity = Vector3.zero;
                return;
            }
            immobilized = value;
            if (value) velocity = Vector3.zero;
        }

        // VRCPlayerApi.SetVelocity is used by the original bed and Decay
        // teleport scripts to clear residual player motion immediately before
        // releasing control.  Retain the complete vector so that the desktop
        // bridge has the same observable velocity state as the source API.
        public Vector3 GetVelocity() { return FreeCameraActive ? Vector3.zero : velocity; }
        public void SetVelocity(Vector3 value) { if (!FreeCameraActive) velocity = value; }

        void OnGUI()
        {
            if (xrControlled || !MuseumDebug.Enabled) return;
            var pos = transform.position;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(12, 12, 600, 30), string.Format("Pos: {0:F2}, {1:F2}, {2:F2}   Yaw: {3:F0}", pos.x, pos.y, pos.z, transform.eulerAngles.y), style);
            if (FreeCameraActive)
                GUI.Label(new Rect(12, 34, 800, 30), "Camera: " + CameraMode + " | WASD follows view / Shift boost", style);
        }
    }
}
