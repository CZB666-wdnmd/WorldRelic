using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalVrcw.Udon
{
    public static class ClassroomCompatibility
    {
        public static void Apply(Scene scene, RuntimeDirector director)
        {
            int drawers = 0, behaviours = 0, desks = 0;
            // Same source UniversalToggleSync interaction volumes as the old
            // reconstruction. Do not enable arbitrary disabled sensors/props.
            foreach (var rt in director.runtimes.Where(r => r.programHash == "ddb09549e3db53a40a5021d7c0d37598"))
            {
                var box = rt.GetComponent<BoxCollider>();
                if (box != null && !box.enabled) { box.enabled = true; drawers++; }
                // In raw v135 these same drawer drivers are disabled too.
                // Restore only configured drawer drivers, not state-only
                // receivers, templates or unrelated inactive world programs.
                if (rt.name == "300_Drawer_Root" && box != null &&
                    rt.GetSymbol("animators") is System.Array targets &&
                    targets.Cast<object>().OfType<Animator>().Any(a => a.runtimeAnimatorController != null) && !rt.Source.enabled)
                { rt.Source.enabled = true; rt.enabled = true; behaviours++; }
            }
            var group = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "300_DESK_CHAIR_ALL" && t.parent != null && t.parent.name == "Area_300_Classroom");
            foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == "300_Desk_Mesh")
                {
                    var box = filter.GetComponent<BoxCollider>();
                    if (box != null && !box.enabled && !box.isTrigger) { box.enabled = true; desks++; }
                }
                // Source chairs have no solid physics: nineteen have no
                // Collider, Uraraka has only her original interaction trigger.
                // Do not invent collisions for the visible seat/back/legs.
            }
            Physics.SyncTransforms();
            Debug.Log($"RAW_PROBE CLASSROOM_COLLISIONS drawers={drawers} behaviours={behaviours} desks={desks} addedChairs=0");
        }
    }
}
