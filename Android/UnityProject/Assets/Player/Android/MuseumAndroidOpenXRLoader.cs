using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Management;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.Management;
#endif

namespace AniqueMuseum
{
    // The native gate forwards Unity's unchanged provider only on known XR
    // hardware (or explicit ForceXR), before Unity creates its graphics device.
    // Ordinary phones/ForceFlat never load the native XR provider at boot.
    public sealed class MuseumAndroidOpenXRLoader : OpenXRLoader
#if UNITY_EDITOR
        , IXRLoaderPreInit
#endif
    {
#if UNITY_EDITOR
        string IXRLoaderPreInit.GetPreInitLibraryName(BuildTarget target, BuildTargetGroup group)
            => target == BuildTarget.Android ? "WorldRelicXRPreInit" : "UnityOpenXR";
#endif
    }
}
