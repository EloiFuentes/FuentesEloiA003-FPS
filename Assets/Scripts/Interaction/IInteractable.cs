/// <summary>
/// Anything the player can use with the Interact key (E): pickups, doors, switches...
/// Implement it on a MonoBehaviour that sits on (or above) the object's collider.
/// </summary>
public interface IInteractable
{
    /// <summary>Text for a future UI prompt, e.g. "Pick up Rifle".</summary>
    string InteractionPrompt { get; }

    bool CanInteract(PlayerInteractor interactor);

    void Interact(PlayerInteractor interactor);
}
