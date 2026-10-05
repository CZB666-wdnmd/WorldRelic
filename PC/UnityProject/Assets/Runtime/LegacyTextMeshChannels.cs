using TMPro;
using UnityEngine;

namespace LocalVrcw
{
    // The user bundle's TMP 2022 shader consumes packed face UV / signed SDF
    // scale in TEXCOORD1. TMP 6 generates face UV there and scale in UV0.w.
    // Adapt only imported text meshes, retaining the user's compiled shader.
    [DisallowMultipleComponent]
    public sealed class LegacyTextMeshChannels : MonoBehaviour
    {
        TMP_Text text;
        void Awake() { text = GetComponent<TMP_Text>(); }
        void OnEnable() { if (text == null) text = GetComponent<TMP_Text>(); text.OnPreRenderText += Convert; Canvas.willRenderCanvases += SyncScale; }
        void OnDisable() { if (text != null) text.OnPreRenderText -= Convert; Canvas.willRenderCanvases -= SyncScale; }
        void Convert(TMP_TextInfo info)
        {
            foreach (var mesh in info.meshInfo)
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    var uv = mesh.uvs2[i];
                    mesh.uvs2[i] = new Vector2((int)(uv.x * 511) * 4096 + (int)(uv.y * 511), mesh.uvs0[i].w);
                }
        }
        void LateUpdate() => SyncScale();
        void SyncScale()
        {
            // TMP's canvas/transform scale fast path changes UV0.w without
            // rebuilding geometry or invoking OnPreRenderText.
            bool changed = false;
            if (text == null || text.textInfo == null) return;
            foreach (var mesh in text.textInfo.meshInfo)
                for (int i = 0; i < mesh.vertexCount; i++)
                    if (mesh.uvs2[i].y != mesh.uvs0[i].w)
                    { mesh.uvs2[i].y = mesh.uvs0[i].w; changed = true; }
            if (changed) text.UpdateVertexData(TMP_VertexDataUpdateFlags.Uv2);
        }
    }
}
