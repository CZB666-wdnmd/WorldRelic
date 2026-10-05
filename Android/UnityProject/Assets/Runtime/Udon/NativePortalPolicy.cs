using UnityEngine;

namespace LocalVrcw.Udon
{
    public static class NativePortalPolicy
    {
        public static bool Incomplete(UdonRuntime runtime)
        {
            if (runtime.programHash != "8339a8bafb7429341916c8080b94b586" &&
                runtime.programHash != "9a890c3c749ea4a49a3d99e7b39a3945") return false;
            return ObjectOf(runtime.GetSymbol("teleportPoint")) == null || ObjectOf(runtime.GetSymbol("FadeObject")) == null;
        }
        static GameObject ObjectOf(object value) => value as GameObject ?? (value as Component)?.gameObject;
        public static void Apply(UdonRuntime runtime)
        {
            // A null authored destination is not a target at world origin.
            // Do not let an incomplete duplicate arm a fade or race the real
            // portal referenced by the raw's unlock checkpoint. Keep visuals,
            // colliders and every native reference; never invent a destination.
            if (!Incomplete(runtime)) return;
            runtime.Source.enabled = runtime.enabled = false;
            Debug.LogWarning("[NativePortal] inactive incomplete route: " + runtime.name);
        }
    }
}
