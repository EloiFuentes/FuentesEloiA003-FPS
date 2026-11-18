using UnityEngine;

/// <summary>
/// A weapon lying in the world. Look at it and press E to pick it up.
/// If you already carry a weapon of the same slot it is dropped here instead.
/// If it's the same weapon you carry, you take its ammo.
/// The prefab needs a collider (and usually a Rigidbody).
/// </summary>
public class WeaponPickup : MonoBehaviour, IInteractable
{
    [SerializeField] private WeaponData weapon;
    [Tooltip("-1 = full magazine.")]
    [SerializeField] private int magazineAmmo = -1;
    [Tooltip("-1 = the weapon's starting reserve.")]
    [SerializeField] private int reserveAmmo = -1;

    public WeaponData Weapon => weapon;

    public string InteractionPrompt
    {
        get
        {
            if (weapon == null) return string.Empty;
            WeaponManager manager = cachedManager;
            Weapon current = manager != null ? manager.GetWeapon(weapon.slot) : null;
            if (current == null) return $"Pick up {weapon.displayName}";
            if (current.Data == weapon) return $"Take {weapon.displayName} ammo";
            return $"Swap {current.Data.displayName} for {weapon.displayName}";
        }
    }

    private WeaponManager cachedManager;

    /// <summary>Sets which weapon this pickup gives (used when a weapon is dropped).</summary>
    public void SetWeapon(WeaponData data) => weapon = data;

    public void SetAmmo(int magazine, int reserve)
    {
        magazineAmmo = magazine;
        reserveAmmo = reserve;
    }

    /// <summary>Dropped weapons shouldn't push the player around.</summary>
    public void IgnorePlayer(Collider playerCollider)
    {
        if (playerCollider == null) return;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(c, playerCollider);
    }

    public bool CanInteract(PlayerInteractor interactor)
    {
        cachedManager = interactor.Weapons;
        if (weapon == null || cachedManager == null || !cachedManager.WeaponsEnabled) return false;

        Weapon current = cachedManager.GetWeapon(weapon.slot);
        return current == null || current.Data != weapon || !current.IsReserveFull;
    }

    public void Interact(PlayerInteractor interactor)
    {
        WeaponManager manager = interactor.Weapons;
        if (manager == null) return;

        if (manager.GiveWeapon(weapon, magazineAmmo, reserveAmmo, equip: true))
            Destroy(gameObject);
    }
}
