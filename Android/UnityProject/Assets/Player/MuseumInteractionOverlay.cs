using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace AniqueMuseum
{
    // Extra camera pass: never swaps authored materials or changes renderers.
    [RequireComponent(typeof(Camera))]
    public sealed class MuseumInteractionOverlay : MonoBehaviour
    {
        public UdonInteractor interactor;
        Camera view;
        Material material;
        CommandBuffer commands;
        readonly HashSet<Transform> roots = new HashSet<Transform>();
        readonly HashSet<Renderer> drawn = new HashSet<Renderer>();
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Graphic> graphics = new List<Graphic>();
        readonly HashSet<Graphic> drawnUI = new HashSet<Graphic>();
        readonly HashSet<Collider> drawnZones = new HashSet<Collider>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly List<Material> sourceMaterials = new List<Material>();
        MaterialPropertyBlock uiProperties;
        Mesh wireBox;
        LocalVrcw.Udon.UdonRuntime[] runtimes;
        VRCPickup[] pickups;
        Selectable[] controls;
        float nextScan;
        public int HighlightedRendererCount { get; private set; }

        void OnEnable()
        {
            if (commands != null) return;
            uiProperties = new MaterialPropertyBlock();
            view = GetComponent<Camera>();
            wireBox = new Mesh { name = "Interaction volume bounds", hideFlags = HideFlags.HideAndDontSave };
            wireBox.vertices = new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            wireBox.SetIndices(new[] {0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7}, MeshTopology.Lines, 0);
            var shader = Resources.Load<Shader>("MuseumInteractionOverlay");
            if (shader == null) { Debug.LogError("Interaction overlay shader missing"); enabled = false; return; }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            commands = new CommandBuffer { name = "Museum interaction highlight" };
            view.AddCommandBuffer(CameraEvent.BeforeImageEffects, commands);
        }

        void OnPreCull()
        {
            Rebuild(MuseumDebug.Enabled);
        }

        // Also used by render regression tests; no debug override in player UI.
        public void Rebuild(bool debug)
        {
            if (commands == null) return;
            commands.Clear(); roots.Clear(); drawn.Clear(); drawnUI.Clear(); drawnZones.Clear(); HighlightedRendererCount = 0;
            if (interactor == null || !interactor.isActiveAndEnabled) return;
            if (VRCPickup.IsVrActive)
            {
                material.SetColor("_Color", new Color(0.02f, 0.35f, 1, 0.45f));
                var rig = interactor.GetComponent<MuseumXRPlayer>();
                if (rig != null && rig.Active)
                {
                    if (rig.LeftTouchTarget != null) roots.Add(rig.LeftTouchTarget);
                    if (rig.RightTouchTarget != null) roots.Add(rig.RightTouchTarget);
                }
            }
            else
            {
            material.SetColor("_Color", debug ? new Color(1, 0.015f, 0.015f, 0.42f) : new Color(0.02f, 0.35f, 1, 0.45f));
            if (debug)
            {
                if (runtimes == null || Time.unscaledTime >= nextScan)
                {
                    runtimes = FindObjectsByType<LocalVrcw.Udon.UdonRuntime>(FindObjectsInactive.Include);
                    pickups = FindObjectsByType<VRCPickup>(FindObjectsInactive.Include);
                    controls = FindObjectsByType<Selectable>(FindObjectsInactive.Include);
                    nextScan = Time.unscaledTime + 1;
                }
                foreach (var r in runtimes)
                {
                    if (r == null || !r.isActiveAndEnabled) continue;
                    if (r.HasEvent("_interact")) roots.Add(r.transform);
                    if (r.HasEvent("_onPlayerTriggerEnter") || r.HasEvent("_onPlayerTriggerExit") || r.HasEvent("_onPlayerTriggerStay"))
                    {
                        r.GetComponentsInChildren(false, colliders);
                        foreach (var c in colliders) if (c.enabled && c.isTrigger) DrawBounds(c);
                    }
                }
                foreach (var p in pickups) if (p != null && p.isActiveAndEnabled && p.pickupable) roots.Add(p.transform);
                foreach (var c in controls) if (c != null && c.IsActive() && c.IsInteractable()) roots.Add(c.transform);
            }
            else if (interactor.HoverTarget != null && interactor.HoverTarget.gameObject.activeInHierarchy)
                roots.Add(interactor.HoverTarget);
            }

            foreach (var root in roots)
            {
                root.GetComponentsInChildren(false, renderers);
                foreach (var r in renderers)
                {
                    if (r == null || !r.enabled || r.forceRenderingOff || !drawn.Add(r) || (view.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    r.GetSharedMaterials(sourceMaterials);
                    int count = sourceMaterials.Count;
                    for (int i = 0; i < count; i++) commands.DrawRenderer(r, material, i, 0);
                    HighlightedRendererCount++;
                }
                if (renderers.Count == 0)
                {
                    root.GetComponentsInChildren(false, colliders);
                    foreach (var c in colliders) if (c.enabled) DrawBounds(c);
                }
                root.GetComponentsInChildren(false, graphics);
                foreach (var g in graphics)
                {
                    if (!drawnUI.Add(g) || !g.isActiveAndEnabled || g.canvas == null || g.canvas.renderMode != RenderMode.WorldSpace || g.canvasRenderer.cull) continue;
                    var mesh = g.canvasRenderer.GetMesh();
                    if (mesh != null && mesh.vertexCount > 0)
                    {
                        uiProperties.Clear(); uiProperties.SetTexture("_MainTex", g.mainTexture);
                        var tint = material.GetColor("_Color"); tint.a *= g.color.a;
                        uiProperties.SetColor("_Color", tint);
                        commands.DrawMesh(mesh, g.transform.localToWorldMatrix, material, 0, 0, uiProperties);
                    }
                }
            }
        }

        void DrawBounds(Collider c)
        {
            if (!drawnZones.Add(c) || (view.cullingMask & (1 << c.gameObject.layer)) == 0) return;
            if (c is BoxCollider box)
                commands.DrawMesh(wireBox, c.transform.localToWorldMatrix * Matrix4x4.TRS(box.center, Quaternion.identity, box.size), material);
            else commands.DrawMesh(wireBox, Matrix4x4.TRS(c.bounds.center, Quaternion.identity, c.bounds.size), material);
        }

        void OnDisable()
        {
            if (commands != null) { if (view != null) view.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commands); commands.Release(); commands = null; }
            if (material != null) DestroyImmediate(material);
            if (wireBox != null) DestroyImmediate(wireBox);
        }
    }
}
