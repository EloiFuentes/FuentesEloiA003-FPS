using UnityEngine;

/// <summary>Grenades lying in the world. Look at them and press E.</summary>
public class GrenadePickup : MonoBehaviour, IInteractable
{
    [SerializeField] private GrenadeData grenade;
    [Min(1)] [SerializeField] private int amount = 1;

    public string InteractionPrompt =>
        grenade == null ? string.Empty : amount > 1 ? $"Pick up {amount}x {grenade.displayName}" : $"Pick up {grenade.displayName}";

    public bool CanInteract(PlayerInteractor interactor)
    {
        return grenade != null && interactor.Weapons != null && interactor.Weapons.CanTakeGrenades(grenade);
    }

    public void Interact(PlayerInteractor interactor)
    {
        amount -= interactor.Weapons.AddGrenades(grenade, amount);
        if (amount <= 0) Destroy(gameObject);
    }
}
