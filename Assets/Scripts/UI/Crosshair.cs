using UnityEngine;

/// <summary>
/// Mira central del HUD (quatre línies al voltant del centre).
/// Només mostra: el UIManager li diu cada frame si hi ha arma, si s'apunta i si el jugador es mou.
///  - Sense arma no es veu.
///  - En apuntar amb l'arma (zoom) s'esvaeix.
///  - En moure's o disparar s'obre, com la dispersió de les bales.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class Crosshair : MonoBehaviour
{
    [Header("Línies")]
    [SerializeField] private RectTransform top;
    [SerializeField] private RectTransform bottom;
    [SerializeField] private RectTransform left;
    [SerializeField] private RectTransform right;

    [Header("Obertura (píxels des del centre)")]
    [SerializeField] private float idleGap = 8f;
    [SerializeField] private float movingGap = 20f;
    [SerializeField] private float shotKick = 6f;
    [SerializeField] private float maxGap = 34f;
    [Tooltip("Com de ràpid s'obre i es tanca.")]
    [SerializeField] private float speed = 14f;

    private CanvasGroup group;
    private float gap;
    private float kick;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        gap = idleGap;
        ApplyGap();
    }

    /// <summary>Obre una mica la mira (es crida en cada tret).</summary>
    public void Kick() => kick = Mathf.Min(kick + shotKick, maxGap);

    /// <summary>Actualitza la mira. aimAmount: 0 = de maluc, 1 = apuntant.</summary>
    public void Refresh(bool hasWeapon, float aimAmount, bool moving, float deltaTime)
    {
        group.alpha = hasWeapon ? 1f - aimAmount : 0f;

        float target = Mathf.Min(maxGap, (moving ? movingGap : idleGap) + kick);
        kick = Mathf.MoveTowards(kick, 0f, shotKick * 5f * deltaTime);
        gap = Mathf.Lerp(gap, target, 1f - Mathf.Exp(-speed * deltaTime));
        ApplyGap();
    }

    private void ApplyGap()
    {
        if (top != null) top.anchoredPosition = new Vector2(0f, gap);
        if (bottom != null) bottom.anchoredPosition = new Vector2(0f, -gap);
        if (left != null) left.anchoredPosition = new Vector2(-gap, 0f);
        if (right != null) right.anchoredPosition = new Vector2(gap, 0f);
    }
}
