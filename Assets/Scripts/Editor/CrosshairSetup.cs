using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tools > FPS > Crear mira: afegeix la mira (Crosshair) al centre del HUD i la connecta al UIManager.</summary>
public static class CrosshairSetup
{
    [MenuItem("Tools/FPS/Crear mira", priority = 43)]
    public static void Create()
    {
        UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
        Canvas canvas = manager != null ? manager.GetComponentInChildren<Canvas>(true) : null;
        if (canvas == null)
        {
            Debug.LogWarning("[CrosshairSetup] No trobo el HUD. Fes primer Tools > FPS > Crear HUD (UIManager).");
            return;
        }
        Crosshair existing = canvas.GetComponentInChildren<Crosshair>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[CrosshairSetup] La mira ja existeix.", existing);
            return;
        }

        var root = new GameObject("Crosshair", typeof(RectTransform), typeof(CanvasGroup), typeof(Crosshair));
        Undo.RegisterCreatedObjectUndo(root, "Crear mira");
        root.layer = canvas.gameObject.layer;
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(80f, 80f);

        RectTransform top = Line(rt, "Top", new Vector2(3f, 12f), new Vector2(0.5f, 0f));
        RectTransform bottom = Line(rt, "Bottom", new Vector2(3f, 12f), new Vector2(0.5f, 1f));
        RectTransform left = Line(rt, "Left", new Vector2(12f, 3f), new Vector2(1f, 0.5f));
        RectTransform right = Line(rt, "Right", new Vector2(12f, 3f), new Vector2(0f, 0.5f));
        Line(rt, "Dot", new Vector2(3f, 3f), new Vector2(0.5f, 0.5f));

        var so = new SerializedObject(root.GetComponent<Crosshair>());
        so.FindProperty("top").objectReferenceValue = top;
        so.FindProperty("bottom").objectReferenceValue = bottom;
        so.FindProperty("left").objectReferenceValue = left;
        so.FindProperty("right").objectReferenceValue = right;
        so.ApplyModifiedPropertiesWithoutUndo();

        var mso = new SerializedObject(manager);
        mso.FindProperty("crosshair").objectReferenceValue = root.GetComponent<Crosshair>();
        mso.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[CrosshairSetup] Mira creada al centre del HUD. Guarda l'escena (Ctrl+S).", root);
    }

    // pivot: el costat que queda més a prop del centre, perquè la línia creixi cap enfora
    private static RectTransform Line(RectTransform parent, string name, Vector2 size, Vector2 pivot)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.9f);
        image.raycastTarget = false;

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);
        return rt;
    }
}
