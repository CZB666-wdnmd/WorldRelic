using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace LocalVrcw
{
    [Serializable] public sealed class TextImportReport
    {
        public string fontPath, family, missingCharacters;
        public int texts, primaryFontsReplaced, wrappingRestored, characters, atlases;
    }
    public static class SystemTextFallback
    {
        public static TMP_FontAsset ExternalFont { get; private set; }
        public static TextImportReport Install(Scene scene, string path)
        {
            var result = new TextImportReport { fontPath = Path.GetFullPath(path) };
            // The raw only includes the full Distance Field shader, not TMP's
            // mobile default. Both use the same SDF properties. Bind that
            // already imported shader to this known TMP-version factory slot;
            // don't add a source-world shader or font to the player assets.
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                var shader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name == "TextMeshPro/Distance Field");
                if (shader == null) throw new InvalidDataException("Imported TMP SDF shader unavailable");
                typeof(ShaderUtilities).GetField("k_ShaderRef_MobileSDF", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, shader);
            }
            // Dynamic loads by file path; DynamicOS would require installing
            // the font and could silently lose glyphs after the first load.
            var fallback = TMP_FontAsset.CreateFontAsset(path, 0, 90, 8, GlyphRenderMode.SDFAA, 2048, 2048);
            if (fallback == null) throw new InvalidDataException("Unable to read external TTF/OTF/TTC font: " + path);
            ExternalFont = fallback;
            fallback.name = "User external Japanese font";
            fallback.hideFlags = HideFlags.DontSave;
            fallback.material.hideFlags = HideFlags.DontSave;
            result.family = fallback.faceInfo.familyName;
            var texts = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).ToArray();
            var required = string.Concat(texts.Select(t => Regex.Replace(t.text ?? "", "<[^>]+>", "")));
            fallback.TryAddCharacters(required, out string missing);
            result.missingCharacters = new string((missing ?? "").Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).Distinct().ToArray());
            if (result.missingCharacters.Length != 0)
                throw new InvalidDataException("External font lacks world text characters: " + result.missingCharacters + ". Supply a Japanese font with broader glyph coverage via --font.");
            result.characters = fallback.characterTable.Count; result.atlases = fallback.atlasTextures.Length;
            if (!TMP_Settings.fallbackFontAssets.Contains(fallback)) TMP_Settings.fallbackFontAssets.Add(fallback);
            var legacyWrap = typeof(TMP_Text).GetField("m_enableWordWrapping", BindingFlags.Instance | BindingFlags.NonPublic);
            if (legacyWrap == null) throw new InvalidDataException("Legacy TMP bool-layout adapter missing");
            foreach (var text in texts)
            {
                // Replace the empty primary font's metrics/material as well,
                // instead of mixing its line heights with OS fallback advances.
                // Complete raw Latin atlases retain their authored advances and
                // line heights. Replacing Liberation Sans with the Japanese
                // fallback made narrow English headings wrap into their rules.
                if (text.font == null || text.font.characterTable.Count == 0)
                { text.font = fallback; text.fontSharedMaterial = fallback.material; result.primaryFontsReplaced++; }
                else
                {
                    if (text.font.fallbackFontAssetTable == null) text.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (!text.font.fallbackFontAssetTable.Contains(fallback)) text.font.fallbackFontAssetTable.Add(fallback);
                }
                text.textWrappingMode = (bool)legacyWrap.GetValue(text) ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                if (text.GetComponent<LegacyTextMeshChannels>() == null) text.gameObject.AddComponent<LegacyTextMeshChannels>();
                result.wrappingRestored++; result.texts++;
                text.SetAllDirty();
            }
            Debug.Log("RAW_PROBE EXTERNAL_FONT family=" + result.family + " primary=" + result.primaryFontsReplaced + " texts=" + result.texts + " wrap=" + result.wrappingRestored + " glyphs=" + result.characters);
            return result;
        }
    }
}
