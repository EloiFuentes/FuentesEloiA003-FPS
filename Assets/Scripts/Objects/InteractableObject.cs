using UnityEngine;

/// <summary>
/// Base class for simple interactable objects (doors, switches...). Override Interact().
/// Works with PlayerInteractor (E key).
/// </summary>
public class InteractableObject : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactionPrompt = "Use";

    public virtual string InteractionPrompt => interactionPrompt;

    public virtual bool CanInteract(PlayerInteractor interactor) => true;

    public void Interact(PlayerInteractor interactor) => Interact();

    public virtual void Interact()
    {
        Debug.Log($"Interacted with {name}");
    }
}
