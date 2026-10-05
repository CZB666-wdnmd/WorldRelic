using System;
using UnityEngine;

/// <summary>
/// Standalone reconstruction of the URL action embedded in the source VRCW.
/// The original component serializes an array whose entries contain one
/// string field named "url"; keeping that exact shape lets Unity retain the
/// authored shop destination instead of silently dropping it.
/// </summary>
public sealed class VRCUrlLauncher : MonoBehaviour
{
    [Serializable]
    public struct UrlEntry
    {
        public string url;
    }

    public UrlEntry[] urls;

    [NonSerialized] public string lastOpenedUrl;
    [NonSerialized] public int executeAttemptCount;
    [NonSerialized] public int acceptedOpenCount;

    public string GetUrl(int index)
    {
        if (urls == null || index < 0 || index >= urls.Length) return null;
        return urls[index].url;
    }

    public bool Execute(string parameter)
    {
        if (!int.TryParse(parameter, out int index)) return false;
        string target = GetUrl(index);
        if (string.IsNullOrWhiteSpace(target)) return false;
        executeAttemptCount++;
        if (!ExternalUrlOpenGuard.TryOpen(target)) return false;
        lastOpenedUrl = target;
        acceptedOpenCount++;
        return true;
    }
}

/// <summary>
/// Prevents one physical UI edge from queuing duplicate shell launches.  The
/// browser can steal focus between two listeners, making the second request
/// appear much later even though both originated from one click.
/// </summary>
public static class ExternalUrlOpenGuard
{
    const float DuplicateWindowSeconds = 1.0f;
    public static bool SuppressShellForTests;
    static string lastTarget;
    static float nextAllowedTime = -1f;
    static int userGestureDepth;
    public static int AcceptedCount { get; private set; }
    public static int SuppressedCount { get; private set; }
    public static int RejectedUngesturedCount { get; private set; }

    /// <summary>
    /// External navigation is a privileged edge: it may only occur while a
    /// real interact/pointer release is being dispatched. This prevents a
    /// selected uGUI Button, delayed Udon callback, or Space/Submit binding
    /// from reopening a previously used URL later.
    /// </summary>
    public static void RunUserGesture(Action action)
    {
        userGestureDepth++;
        try { action?.Invoke(); }
        finally { userGestureDepth--; }
    }

    public static bool TryOpen(string target)
    {
        if (userGestureDepth <= 0)
        {
            RejectedUngesturedCount++;
            Debug.LogWarning("[ExternalUrl] rejected launch outside current user gesture: " + target);
            return false;
        }
        float now = Time.realtimeSinceStartup;
        if (string.Equals(lastTarget, target, StringComparison.Ordinal) && now < nextAllowedTime)
        {
            SuppressedCount++;
            Debug.LogWarning("[ExternalUrl] suppressed duplicate launch: " + target);
            return false;
        }
        lastTarget = target;
        nextAllowedTime = now + DuplicateWindowSeconds;
        AcceptedCount++;
        if (!Application.isBatchMode && !SuppressShellForTests) Application.OpenURL(target);
        return true;
    }

    public static void ResetForTests()
    {
        lastTarget = null;
        nextAllowedTime = -1f;
        AcceptedCount = 0;
        SuppressedCount = 0;
        RejectedUngesturedCount = 0;
        userGestureDepth = 0;
    }
}
