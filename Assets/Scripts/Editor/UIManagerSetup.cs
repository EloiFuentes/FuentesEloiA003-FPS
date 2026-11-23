using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Menú Tools > FPS > Crear HUD (UIManager): munta el UIManager i el HUD a l'escena oberta.</summary>
public static class UIManagerSetup
{
    [MenuItem("Tools/FPS/Crear HUD (UIManager)", priority = 40)]
    public static void CreateHUD()
    {
        UIManager existing = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("[UIManagerSetup] Ja hi ha un UIManager a l'escena.", existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Font del HUD (la crea Tools > FPS > Convertir HUD a TextMeshPro). Si no hi és, la de TMP per defecte.
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HudTmpSetup.FontAssetPath);
        Material shadow = AssetDatabase.LoadAssetAtPath<Material>(HudTmpSetup.ShadowMaterialPath);

        // UIManager
        var managerGO = new GameObject("UIManager");
        Undo.RegisterCreatedObjectUndo(managerGO, "Crear HUD");
        UIManager manager = managerGO.AddComponent<UIManager>();

        // Canvas
        var canvasGO = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(managerGO.transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Panell de munició (a baix a la dreta)
        var panelGO = new GameObject("AmmoPanel", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvasGO.transform, false);
        panelGO.layer = canvasGO.layer;
        var panel = (RectTransform)panelGO.transform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-40f, 40f);
        panel.sizeDelta = new Vector2(420f, 160f);
        var bg = panelGO.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.35f);
        bg.raycastTarget = false;

        TextMeshProUGUI weaponName = MakeText(panel, "WeaponName", "ARMA", font, shadow, 30, FontStyle.Bold, TextAnchor.UpperRight,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(-32f, 36f),
            new Color(0.85f, 0.85f, 0.85f));
        TextMeshProUGUI current = MakeText(panel, "CurrentAmmo", "30", font, shadow, 72, FontStyle.Bold, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-130f, 34f), new Vector2(240f, 80f),
            Color.white);
        TextMeshProUGUI max = MakeText(panel, "MaxAmmo", "/ 30", font, shadow, 36, FontStyle.Normal, TextAnchor.LowerLeft,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 42f), new Vector2(110f, 50f),
            new Color(0.85f, 0.85f, 0.85f));
        TextMeshProUGUI reserve = MakeText(panel, "Reserve", "RESERVA 90", font, shadow, 22, FontStyle.Normal, TextAnchor.LowerRight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 8f), new Vector2(300f, 28f),
            new Color(0.7f, 0.7f, 0.7f));

        // AmmoHUD + referències
        AmmoHUD hud = canvasGO.AddComponent<AmmoHUD>();
        var so = new SerializedObject(hud);
        so.FindProperty("root").objectReferenceValue = panelGO;
        so.FindProperty("weaponNameText").objectReferenceValue = weaponName;
        so.FindProperty("currentAmmoText").objectReferenceValue = current;
        so.FindProperty("maxAmmoText").objectReferenceValue = max;
        so.FindProperty("reserveText").objectReferenceValue = reserve;
        so.ApplyModifiedPropertiesWithoutUndo();

        var mso = new SerializedObject(manager);
        mso.FindProperty("weaponManager").objectReferenceValue = Object.FindAnyObjectByType<WeaponManager>();
        mso.FindProperty("ammoHUD").objectReferenceValue = hud;
        mso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(managerGO.scene);
        Selection.activeGameObject = managerGO;
        Debug.Log("[UIManagerSetup] HUD creat. Guarda l'escena (Ctrl+S).", managerGO);
    }

    private static TextMeshProUGUI MakeText(RectTransform parent, string name, string value, TMP_FontAsset font, Material material,
        int size, FontStyle style, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
        Vector2 sizeDelta, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = sizeDelta;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        if (material != null) text.fontSharedMaterial = material;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = font != null ? FontStyles.Normal : (style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal);
        text.alignment = HudTmpSetup.ToTmp(align);
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
