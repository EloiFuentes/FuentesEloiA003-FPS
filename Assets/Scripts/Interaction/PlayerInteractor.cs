using System;
using UnityEngine;

/// <summary>
/// Finds the <see cref="IInteractable"/> in front of the camera and uses it when Interact (E) is pressed.
/// Put it on the same GameObject as PlayerController.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float interactDistance = 3f;
    [Tooltip("Small radius makes small pickups easier to target.")]
    [SerializeField] private float probeRadius = 0.12f;
    [SerializeField] private LayerMask interactMask = ~0;

    public PlayerController Player { get; private set; }
    public WeaponManager Weapons { get; private set; }

    /// <summary>What the player is currently looking at (null if nothing).</summary>
    public IInteractable Current { get; private set; }
    public string CurrentPrompt => Current != null ? Current.InteractionPrompt : string.Empty;

    /// <summary>Raised when the looked-at interactable changes (null = nothing). Hook a UI prompt here.</summary>
    public event Action<IInteractable> FocusChanged;

    private GameInputs inputs;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    private void Awake()
    {
        Player = GetComponent<PlayerController>();
        Weapons = GetComponent<WeaponManager>();
        inputs = GetComponent<GameInputs>();
    }

    private void Update()
    {
        UpdateFocus();

        if (Current != null && inputs.interact != null && inputs.interact.WasPressedThisFrame()
            && !Player.IsExternallyControlled && Current.CanInteract(this))
        {
            Current.Interact(this);
            UpdateFocus(); // the object may have been picked up / destroyed
        }
    }

    private void UpdateFocus()
    {
        IInteractable found = FindInteractable();
        if (!ReferenceEquals(found, Current))
        {
            Current = found;
            FocusChanged?.Invoke(Current);
        }
    }

    private IInteractable FindInteractable()
    {
        Transform view = Player.PlayerCam != null ? Player.PlayerCam.transform : null;
        if (view == null) return null;

        int count = Physics.SphereCastNonAlloc(view.position, probeRadius, view.forward, hits, interactDistance,
                                               interactMask, QueryTriggerInteraction.Collide);

        // Closest hit that isn't part of the player decides; walls block interaction.
        float best = float.MaxValue;
        Collider closest = null;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (Player.OwnsCollider(c)) continue;
            if (c.isTrigger && c.GetComponentInParent<IInteractable>() == null) continue; // ignore unrelated triggers
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                closest = c;
            }
        }
        if (closest == null) return null;

        IInteractable interactable = closest.GetComponentInParent<IInteractable>();
        if (interactable is Behaviour b && !b.isActiveAndEnabled) return null;
        return interactable;
    }
}
