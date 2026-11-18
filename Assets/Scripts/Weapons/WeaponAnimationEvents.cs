using UnityEngine;

/// <summary>
/// Receives animation events on the weapon's Animator object (added automatically).
/// In the Animation window add events with these function names:
///   PlaySound      (Object parameter = an AudioClip): magazine out/in, bolt, cloth...
///   ReleaseGrenade (no parameter): the moment the grenade leaves the hand in GrenadeThrow.
/// </summary>
public class WeaponAnimationEvents : MonoBehaviour
{
    private WeaponViewModel viewModel;

    private void Awake() => viewModel = GetComponentInParent<WeaponViewModel>(true);

    public void PlaySound(Object clip)
    {
        if (viewModel != null && clip is AudioClip audioClip) viewModel.RaiseSound(audioClip);
    }

    public void ReleaseGrenade()
    {
        if (viewModel != null) viewModel.RaiseGrenadeRelease();
    }
}
