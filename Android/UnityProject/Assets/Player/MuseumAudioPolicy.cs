using System.Collections.Generic;
using System.Linq;
using LocalVrcw.Udon;
using UnityEngine;

namespace AniqueMuseum
{
    /// <summary>Separates non-positional area music from positional world SFX.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class MuseumAudioPolicy : MonoBehaviour
    {
        const string ManagerProgram = "4d7b173158d9dc049b82ee6f01c38a23";

        void Awake() { ApplyNow(); }
        void OnEnable() { ApplyNow(); }

        public static void ApplyNow()
        {
            var nonPositionalSources = NonPositionalSources();

            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            {
                if (source == null) continue;
                if (nonPositionalSources.Contains(source))
                {
                    source.spatialBlend = 0f;
                    source.spatialize = false;
                    source.spatializePostEffects = false;
                    source.dopplerLevel = 0f;
                }
                else if (source.clip != null)
                {
                    source.spatialBlend = 1f;
                    source.spatialize = true;
                    source.spatializePostEffects = false;
                }
            }
        }

        public static HashSet<AudioSource> NonPositionalSources()
        {
            var sources = new HashSet<AudioSource>();
            foreach (var runtime in FindObjectsByType<UdonRuntime>(FindObjectsInactive.Include))
            {
                if (runtime == null) continue;
                if (runtime.programHash == ManagerProgram)
                {
                    if (runtime.GetSymbol("audioSourceA") is AudioSource a) sources.Add(a);
                    if (runtime.GetSymbol("audioSourceB") is AudioSource b) sources.Add(b);
                }
                // These raw sources are authored 2D zone feedback, not point
                // ambience. In particular the spikes' AudioSource is at the
                // scene origin, ~300m from the player: forcing 3D loses its cue.
                if (runtime.GetSymbol("passageLoopAudio") is AudioSource loop) sources.Add(loop);
                if (runtime.programHash == "92bbd52a00bbcc047aff121236c62862" && runtime.GetSymbol("audioSource") is AudioSource cue) sources.Add(cue);
            }
            return sources;
        }
    }
}
