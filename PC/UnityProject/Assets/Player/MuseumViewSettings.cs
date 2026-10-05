using UnityEngine;
using UnityEngine.InputSystem;

namespace AniqueMuseum
{
    [DefaultExecutionOrder(-90)]
    public sealed class MuseumViewSettings : MonoBehaviour
    {
        const string Key = "Museum.VerticalFOV";
        public const float DefaultFOV = 60, MinimumFOV = 40, MaximumFOV = 110;
        public static float FOV => Mathf.Clamp(PlayerPrefs.GetFloat(Key, DefaultFOV), MinimumFOV, MaximumFOV);
        static MuseumViewSettings instance;
        static int suppressFrame = -1;
        public static bool BlocksDesktopInput => (instance != null && instance.open) || suppressFrame == Time.frameCount;
        FPSCameraController player;
        bool open;
        float previousTimeScale;
        CursorLockMode previousLock;
        bool previousVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var player = FindAnyObjectByType<FPSCameraController>();
            if (player != null && player.GetComponent<MuseumViewSettings>() == null)
                player.gameObject.AddComponent<MuseumViewSettings>();
        }
        void Awake() { instance = this; player = GetComponent<FPSCameraController>(); }
        public static void SetFOV(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            PlayerPrefs.SetFloat(Key, Mathf.Clamp(value, MinimumFOV, MaximumFOV));
        }
        public static void Save() { PlayerPrefs.Save(); }
        void Update()
        {
            if (Application.platform == RuntimePlatform.Android || player == null || player.xrControlled) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.f2Key.wasPressedThisFrame || (open && keyboard.escapeKey.wasPressedThisFrame))) Toggle();
        }
        void LateUpdate()
        {
            if (player != null && player.playerCamera != null && !player.xrControlled && !player.playerCamera.stereoEnabled)
                player.playerCamera.fieldOfView = FOV;
        }
        void Toggle()
        {
            open = !open; suppressFrame = Time.frameCount;
            if (open)
            {
                VRC.SDK3.Components.VRCPickup.HeldPickup?.UseUp();
                previousTimeScale = Time.timeScale; Time.timeScale = 0;
                previousLock = Cursor.lockState; previousVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            else
            {
                Time.timeScale = previousTimeScale; Cursor.lockState = previousLock; Cursor.visible = previousVisible; Save();
            }
        }
        void OnGUI()
        {
            if (!open) return;
            float scale = Mathf.Max(.1f, Mathf.Min(Screen.width / 800f, Screen.height / 500f));
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 520 * scale) / 2, (Screen.height - 230 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUI.Box(new Rect(0, 0, 520, 230), "VIEW SETTINGS / F2");
            GUI.Label(new Rect(30, 42, 460, 30), "Vertical FOV: " + Mathf.RoundToInt(FOV) + " degrees");
            SetFOV(GUI.HorizontalSlider(new Rect(30, 88, 460, 26), FOV, MinimumFOV, MaximumFOV));
            GUI.Label(new Rect(30, 116, 460, 30), "40 - 110 degrees. Saved for flat-screen viewing.");
            if (GUI.Button(new Rect(30, 166, 210, 42), "RESET (60)")) SetFOV(DefaultFOV);
            if (GUI.Button(new Rect(280, 166, 210, 42), "RESUME")) Toggle();
            GUI.matrix = matrix;
        }
        void OnApplicationPause(bool paused) { if (paused) Save(); }
        void OnApplicationFocus(bool focused) { if (!focused) Save(); }
        void OnDisable() { if (open) Toggle(); Save(); if (instance == this) instance = null; }
    }
}
