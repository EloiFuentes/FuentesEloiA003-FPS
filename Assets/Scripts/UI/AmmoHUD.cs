using UnityEngine;
using TMPro;

/// <summary>
/// HUD de l'arma: nom de l'arma equipada, munició actual i munició màxima.
/// Només mostra dades; qui les hi passa és el UIManager.
/// </summary>
public class AmmoHUD : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text weaponNameText;
    [SerializeField] private TMP_Text currentAmmoText;
    [SerializeField] private TMP_Text maxAmmoText;
    [SerializeField] private TMP_Text reserveText;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color lowAmmoColor = new Color(1f, 0.35f, 0.25f);
    [Tooltip("Amb aquesta munició o menys, el número surt en vermell.")]
    [Min(0)] [SerializeField] private int lowAmmoCount = 5;

    public void Show(string weaponName, int current, int max, int reserve, bool infiniteReserve)
    {
        if (root != null && !root.activeSelf) root.SetActive(true);

        if (weaponNameText != null) weaponNameText.text = weaponName.ToUpperInvariant();

        if (currentAmmoText != null)
        {
            currentAmmoText.text = current.ToString();
            bool low = current <= lowAmmoCount;
            currentAmmoText.color = low ? lowAmmoColor : normalColor;
        }

        if (maxAmmoText != null) maxAmmoText.text = "/ " + max;

        if (reserveText != null) reserveText.text = infiniteReserve ? "RESERVA ∞" : "RESERVA " + reserve;
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
