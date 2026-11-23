using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

/// <summary>
/// Tools > FPS > Convertir HUD a TextMeshPro
///  1) Crea el Font Asset (SDF) de la font del HUD i un material amb ombra (Underlay).
///  2) Canvia els Text antics del HUD per TextMeshProUGUI, conservant text, mida, color, alineació i posició.
///  3) Torna a connectar els textos al component AmmoHUD.
/// </summary>
public static class HudTmpSetup
{
    public const string SourceFontPath = "Assets/Fonts/Anton-Regular.ttf";
    public const string FontAssetPath = "Assets/Fonts/Anton SDF.asset";
    public const string ShadowMaterialPath = "Assets/Fonts/Anton SDF - Ombra.mat";
    private const string Characters =
        " 0123456789/-.:%ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzÀÁÈÉÍÏÒÓÚÜÇÑàáèéíïòóúüçñ·";

    [MenuItem("Tools/FPS/Convertir HUD a TextMeshPro", priority = 42)]
    public static void Convert()
    {
        TMP_FontAsset font = GetOrCreateFontAsset();
        Material shadow = GetOrCreateShadowMaterial(font);
        if (font == null) return;

        AmmoHUD hud = Object.FindAnyObjectByType<AmmoHUD>(FindObjectsInactive.Include);
        if (hud == null)
        {
            Debug.LogWarning("[HudTmpSetup] No hi ha cap AmmoHUD a l'escena. Fes primer Tools > FPS > Crear HUD (UIManager).");
            return;
        }

        // Textos antics -> TextMeshPro
        int converted = 0;
        foreach (Text old in hud.GetComponentsInChildren<Text>(true))
        {
            GameObject go = old.gameObject;
            string value = old.text;
            int size = old.fontSize;
            TextAnchor align = old.alignment;
            Color color = old.color;

            foreach (Shadow s in go.GetComponents<Shadow>()) Undo.DestroyObjectImmediate(s);
            Undo.DestroyObjectImmediate(old);

            var tmp = Undo.AddComponent<TextMeshProUGUI>(go);
            tmp.text = value;
            tmp.fontSize = size;
            tmp.alignment = ToTmp(align);
            tmp.color = color;
            converted++;
        }

        // Estil comú a tots els textos del HUD
        var so = new SerializedObject(hud);
        foreach (TextMeshProUGUI tmp in hud.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            Undo.RecordObject(tmp, "HUD TextMeshPro");
            tmp.font = font;
            if (shadow != null) tmp.fontSharedMaterial = shadow;
            tmp.fontStyle = FontStyles.Normal;            // l'Anton ja és gruixuda
            tmp.characterSpacing = 2f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            EditorUtility.SetDirty(tmp);

            string n = tmp.gameObject.name.ToLowerInvariant();
            string field = n.Contains("weapon") ? "weaponNameText"
                         : n.Contains("current") ? "currentAmmoText"
                         : n.Contains("max") ? "maxAmmoText"
                         : n.Contains("reserv") ? "reserveText" : null;
            if (field != null) so.FindProperty(field).objectReferenceValue = tmp;
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        Selection.activeGameObject = hud.gameObject;
        Debug.Log($"[HudTmpSetup] {converted} textos convertits a TextMeshPro amb la font {font.name}. Guarda l'escena (Ctrl+S).", hud);
    }

    private static TMP_FontAsset GetOrCreateFontAsset()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null) return existing;

        var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (source == null)
        {
            Debug.LogError("[HudTmpSetup] No trobo la font " + SourceFontPath);
            return null;
        }

        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                           AtlasPopulationMode.Dynamic, true);
        font.name = "Anton SDF";
        font.TryAddCharacters(Characters);

        AssetDatabase.CreateAsset(font, FontAssetPath);
        font.material.name = "Anton SDF Material";
        AssetDatabase.AddObjectToAsset(font.material, font);
        for (int i = 0; i < font.atlasTextures.Length; i++)
        {
            font.atlasTextures[i].name = "Anton SDF Atlas " + i;
            AssetDatabase.AddObjectToAsset(font.atlasTextures[i], font);
        }
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        return font;
    }

    private static Material GetOrCreateShadowMaterial(TMP_FontAsset font)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
        if (existing != null || font == null) return existing;

        var mat = new Material(font.material) { name = "Anton SDF - Ombra" };
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.65f));
        mat.SetFloat("_UnderlayOffsetX", 0.5f);
        mat.SetFloat("_UnderlayOffsetY", -0.5f);
        mat.SetFloat("_UnderlaySoftness", 0.25f);
        AssetDatabase.CreateAsset(mat, ShadowMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    public static TextAlignmentOptions ToTmp(TextAnchor a)
    {
        switch (a)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            default: return TextAlignmentOptions.BottomRight;
        }
    }
}
