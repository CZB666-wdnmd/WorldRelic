namespace AniqueMuseum
{
    // Uses unscaled time so holding a contact / pausing never loses the overlay.
    // Presence, not the most recently active input device, owns auto-hiding.
    public sealed class MuseumTouchOverlayState
    {
        public const float IdleSeconds = 5f;
        public bool Visible { get; private set; } = true;
        public bool GamepadConnected { get; private set; }
        float lastTouch = float.NegativeInfinity;

        public void Observe(bool gamepadConnected, bool touching, float now)
        {
            if (gamepadConnected != GamepadConnected)
            {
                GamepadConnected = gamepadConnected;
                Visible = !gamepadConnected;
                lastTouch = float.NegativeInfinity;
            }
            if (!gamepadConnected) { Visible = true; return; }
            if (touching) { lastTouch = now; Visible = true; }
            else if (now - lastTouch >= IdleSeconds) Visible = false;
        }
    }
}
