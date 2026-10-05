using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;

#endif
namespace AniqueMuseum
{
    // PICO's generic feature requires eye-tracking and Vulkan extensions too.
    // Neo 3/GLES only needs these three fixed-foveation extensions. Do not
    // modify the SDK or require eye tracking on ordinary controller hardware.
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "Museum PICO Fixed Foveation (GLES)", BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "Local Rebuild", Version = "1.0.0", FeatureId = "local.anique.fixed-foveation",
        OpenxrExtensionStrings = "XR_FB_foveation XR_FB_foveation_configuration XR_FB_swapchain_update_state")]
#endif
    public sealed class MuseumFixedFoveationFeature : FoveationFeature
    {
        public override string GetExtensionString() => "XR_FB_foveation XR_FB_foveation_configuration XR_FB_swapchain_update_state";
        public override void SessionCreate()
        {
            try { base.SessionCreate(); }
            catch (Exception e) { Debug.LogWarning("[MuseumXRQuality] Fixed foveation unavailable; normal rendering: " + e.Message); }
        }
    }
}
