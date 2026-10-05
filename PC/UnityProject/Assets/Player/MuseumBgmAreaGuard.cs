using System.Collections.Generic;
using LocalVrcw.Udon;
using UnityEngine;

namespace AniqueMuseum
{
    /// <summary>
    /// Reconciles the standalone player's physical position with every
    /// authored BGMArea. VRChat supplies player-trigger callbacks itself;
    /// Unity's CharacterController can miss them after teleports or area
    /// activation changes, so the rebuild also polls the source colliders.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class MuseumBgmAreaGuard : MonoBehaviour
    {
        const string AreaProgram = "02eed703c1afc8143912eb99ccc5f2d4";
        const string ManagerProgram = "4d7b173158d9dc049b82ee6f01c38a23";

        readonly List<UdonRuntime> areas = new List<UdonRuntime>();
        readonly HashSet<UdonRuntime> physicallyInside = new HashSet<UdonRuntime>();
        CharacterController playerCollider;
        UdonRuntime manager;
        float nextRefreshTime;
        float silentTargetSince = -1f;

        void Awake() { playerCollider = GetComponent<CharacterController>(); RefreshSourceAreas(); }
        void OnEnable() { RefreshSourceAreas(); }
        void FixedUpdate() { ProbeNowForVerification(); }

        public void RefreshSourceAreas()
        {
            areas.Clear();
            manager = null;
            foreach (var runtime in FindObjectsByType<UdonRuntime>(FindObjectsInactive.Include))
            {
                if (runtime == null) continue;
                if (runtime.programHash == ManagerProgram) manager = runtime;
                else if (runtime.programHash == AreaProgram && runtime.GetComponent<Collider>() != null) areas.Add(runtime);
            }
            nextRefreshTime = Time.unscaledTime + 2f;
        }

        public void ProbeNowForVerification()
        {
            if (playerCollider == null) playerCollider = GetComponent<CharacterController>();
            if (playerCollider == null || !playerCollider.enabled) return;
            if (manager == null || Time.unscaledTime >= nextRefreshTime) RefreshSourceAreas();
            if (manager == null) return;

            physicallyInside.Clear();
            for (int i = 0; i < areas.Count; i++)
            {
                UdonRuntime area = areas[i];
                if (area == null || !area.enabled || !area.gameObject.activeInHierarchy) continue;
                Collider authoredCollider = area.GetComponent<Collider>();
                if (authoredCollider == null || !authoredCollider.enabled || !OverlapsPlayer(authoredCollider)) continue;
                physicallyInside.Add(area);
                if (!(area.GetSymbol("_localPlayerInside") is bool sourceInside) || !sourceInside)
                    area.SendCustomEvent("_onPlayerTriggerEnter", LocalVrcw.Udon.LocalPlayer.Instance, "onPlayerTriggerEnterPlayer");
                else
                    area.SendCustomEvent("_onPlayerTriggerStay", LocalVrcw.Udon.LocalPlayer.Instance, "onPlayerTriggerStayPlayer");
            }

            for (int i = 0; i < areas.Count; i++)
            {
                UdonRuntime area = areas[i];
                if (area == null || physicallyInside.Contains(area)) continue;
                if (area.GetSymbol("_localPlayerInside") is bool sourceInside && sourceInside)
                    area.SendCustomEvent("_onPlayerTriggerExit", LocalVrcw.Udon.LocalPlayer.Instance, "onPlayerTriggerExitPlayer");
            }
            EnforceCompletedSilentFade();
        }

        bool OverlapsPlayer(Collider authoredCollider)
        {
            if (!authoredCollider.bounds.Intersects(playerCollider.bounds)) return false;
            if (Physics.ComputePenetration(
                    playerCollider, playerCollider.transform.position, playerCollider.transform.rotation,
                    authoredCollider, authoredCollider.transform.position, authoredCollider.transform.rotation,
                    out _, out _)) return true;
            Vector3 center = playerCollider.bounds.center;
            return (authoredCollider.ClosestPoint(center) - center).sqrMagnitude <= 0.000001f;
        }

        void EnforceCompletedSilentFade()
        {
            bool targetSilent = manager.GetSymbol("_targetIsSilent") is bool silent && silent;
            if (!targetSilent) { silentTargetSince = -1f; return; }
            if (silentTargetSince < 0f) silentTargetSince = Time.unscaledTime;
            float fadeDuration = manager.GetSymbol("_fadeDuration") is float duration ? Mathf.Max(0f, duration) : 2f;
            if (Time.unscaledTime - silentTargetSince < fadeDuration + 0.25f) return;
            StopSource(manager.GetSymbol("audioSourceA") as AudioSource);
            StopSource(manager.GetSymbol("audioSourceB") as AudioSource);
            manager.SetSymbol("_fadeActive", false);
            manager.SetSymbol("_currentIsSilent", true);
            manager.SetSymbol("_currentClip", null);
            manager.SetSymbol("_targetClip", null);
        }

        static void StopSource(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.volume = 0f;
            source.clip = null;
        }
    }
}
