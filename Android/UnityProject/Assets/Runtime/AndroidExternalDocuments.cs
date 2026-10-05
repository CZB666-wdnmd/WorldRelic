using System;
using System.Collections;
using UnityEngine;

namespace LocalVrcw
{
    [Serializable] public sealed class ExternalDocumentSelection { public string world, font, error; }
    public static class AndroidExternalDocuments
    {
        public static bool Busy { get; private set; }
        public static ExternalDocumentSelection Selection { get; private set; }
        const string Bridge = "local.worldrelic.files.ImportActivity";
        public static IEnumerator Request()
        {
            if (Busy) yield break;
            Busy = true;
            Selection = null;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var importer = new AndroidJavaClass(Bridge);
                importer.CallStatic("begin", activity);
                while (true)
                {
                    string result = importer.CallStatic<string>("poll");
                    if (!string.IsNullOrEmpty(result)) { Selection = JsonUtility.FromJson<ExternalDocumentSelection>(result); break; }
                    yield return null;
                }
            }
            finally { Busy = false; }
        }
        public static bool Readable()
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var importer = new AndroidJavaClass(Bridge);
                return importer.CallStatic<bool>("readable", activity);
            }
            catch (Exception e) { Debug.LogWarning("External document access check: " + e.Message); return false; }
            finally { AndroidJNI.DetachCurrentThread(); }
        }
    }
}
