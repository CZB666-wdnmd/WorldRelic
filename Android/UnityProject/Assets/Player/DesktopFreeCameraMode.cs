namespace AniqueMuseum
{
    public enum DesktopFreeCameraMode { Default, FreeCamera, FreeCameraNoCollision }
    // Options are populated by the single strict external-file parser before import.
    public static class DesktopFreeCameraSettings
    {
        public static DesktopFreeCameraMode Requested { get; internal set; }
    }
    public static class MuseumDebug { public static bool Enabled { get; internal set; } public static void SetEnabled(bool value) => Enabled = value; }
}
