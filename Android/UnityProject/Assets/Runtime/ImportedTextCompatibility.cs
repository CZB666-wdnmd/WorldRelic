using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalVrcw
{
    public static class ImportedTextCompatibility
    {
        public static int RepairFontMaterials(Scene sourceScene)
        {
            // TMP 2022 stored the default material as "material"; TMP 6 stores
            // "m_Material". Runtime cross-version deserialization can lose that
            // renamed field. Recover from already imported material/atlas references,
            // never from bundled world data, generated images or external fonts.
            var texts = sourceScene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).ToArray();
            var materials = Resources.FindObjectsOfTypeAll<Material>();
            int repaired = 0;
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font.material != null) continue;
                Texture atlas = font.atlasTextures?.FirstOrDefault(t => t != null);
                Material material = materials.FirstOrDefault(m => IsFontMaterial(m, atlas) &&
                    m.name == font.name + " Material");
                if (material == null)
                    material = texts.Where(t => t.font == font).Select(t => t.fontSharedMaterial).FirstOrDefault(m => m != null);
                if (material == null)
                    material = materials.FirstOrDefault(m => IsFontMaterial(m, atlas));
                if (material == null) continue;
                font.material = material;
                repaired++;
            }
            Debug.Log("RAW_PROBE TMP_FONT_MATERIALS_REPAIRED=" + repaired);
            return repaired;
        }

        static bool IsFontMaterial(Material material, Texture atlas)
        {
            return material != null && atlas != null && material.shader != null &&
                material.shader.name.StartsWith("TextMeshPro/", StringComparison.Ordinal) &&
                material.HasProperty("_MainTex") && material.GetTexture("_MainTex") == atlas;
        }
    }
}
