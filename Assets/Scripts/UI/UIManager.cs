using UnityEngine;

/// <summary>
/// Gestiona i actualitza la interfície d'usuari del joc.
/// Escolta el WeaponManager i passa les dades al HUD (AmmoHUD) i a la mira (Crosshair).
/// Més endavant aquí s'hi poden afegir altres parts de la UI (vida, menús...).
/// </summary>
[DefaultExecutionOrder(200)]
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Referències")]
    [Tooltip("Si és buit, el busca a l'escena.")]
    [SerializeField] private WeaponManager weaponManager;
    [Tooltip("Si és buit, el busca als fills.")]
    [SerializeField] private AmmoHUD ammoHUD;
    [Tooltip("Si és buit, la busca als fills.")]
    [SerializeField] private Crosshair crosshair;

    private Weapon shownWeapon;
    private int lastMagazine = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (weaponManager == null) weaponManager = FindAnyObjectByType<WeaponManager>();
        if (ammoHUD == null) ammoHUD = GetComponentInChildren<AmmoHUD>(true);
        if (crosshair == null) crosshair = GetComponentInChildren<Crosshair>(true);
    }

    private void OnEnable()
    {
        if (weaponManager == null) return;
        weaponManager.ActiveWeaponChanged += OnActiveWeaponChanged;
        weaponManager.AmmoChanged += OnAmmoChanged;
    }

    private void OnDisable()
    {
        if (weaponManager == null) return;
        weaponManager.ActiveWeaponChanged -= OnActiveWeaponChanged;
        weaponManager.AmmoChanged -= OnAmmoChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        RefreshWeaponHUD();
    }

    private void LateUpdate()
    {
        // Per si l'arma canvia sense passar per l'event (p. ex. en començar la partida).
        Weapon active = weaponManager != null ? weaponManager.ActiveWeapon : null;
        if (active != shownWeapon) RefreshWeaponHUD();

        if (crosshair != null)
        {
            // La mira es veu sempre igual, també apuntant (per això el valor d'apuntat és 0).
            bool moving = weaponManager != null && weaponManager.IsPlayerMoving;
            crosshair.Refresh(active != null && active.IsVisible, 0f, moving, Time.deltaTime);
        }
    }

    private void OnActiveWeaponChanged(Weapon weapon) => RefreshWeaponHUD();

    private void OnAmmoChanged(Weapon weapon)
    {
        if (weaponManager == null || weapon != weaponManager.ActiveWeapon) return;

        // Si el carregador baixa és que s'ha disparat: la mira s'obre una mica.
        if (crosshair != null && weapon == shownWeapon && weapon.Magazine < lastMagazine) crosshair.Kick();
        RefreshWeaponHUD();
    }

    /// <summary>Actualitza el HUD amb l'arma equipada i la seva munició.</summary>
    public void RefreshWeaponHUD()
    {
        shownWeapon = weaponManager != null ? weaponManager.ActiveWeapon : null;
        lastMagazine = shownWeapon != null ? shownWeapon.Magazine : -1;
        if (ammoHUD == null) return;

        if (shownWeapon == null)
        {
            ammoHUD.Hide();
            return;
        }

        WeaponData data = shownWeapon.Data;
        ammoHUD.Show(data.displayName, shownWeapon.Magazine, data.magazineSize,
                     shownWeapon.Reserve, data.infiniteReserveAmmo);
    }
}
