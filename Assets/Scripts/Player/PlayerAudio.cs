using UnityEngine;

/// <summary>
/// Plays footsteps in sync with the PlayerController stride (same cadence as the head bob),
/// plus jump and landing sounds scaled by impact.
/// </summary>
public class FootstepController : MonoBehaviour
{
    public AudioSourceArray[] footstepSources;
    [SerializeField] private AudioSource audioSource;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float walkVolume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float sprintVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float crouchVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.5f;
    [SerializeField, Range(0f, 1f)] private float maxLandingVolume = 1f;

    [Header("Variation")]
    [SerializeField] private float pitchVariation = 0.08f;
    [Tooltip("Landings slower than this (m/s) make no sound.")]
    [SerializeField] private float minLandingSpeed = 2.5f;

    private PlayerController player;
    private LedgeGrab ledgeGrab;
    private int lastClipIndex = -1;

    private void OnEnable()
    {
        player = GetComponent<PlayerController>();
        if (player == null) return;
        player.Footstep += OnFootstep;
        player.Landed += OnLanded;
        player.Jumped += OnJumped;

        ledgeGrab = GetComponent<LedgeGrab>();
        if (ledgeGrab != null) ledgeGrab.MantleFinished += OnMantleFinished;
    }

    private void OnDisable()
    {
        if (player == null) return;
        player.Footstep -= OnFootstep;
        player.Landed -= OnLanded;
        player.Jumped -= OnJumped;
        if (ledgeGrab != null) ledgeGrab.MantleFinished -= OnMantleFinished;
    }

    private void OnMantleFinished()
    {
        PlayRandomFootstepSound(walkVolume, 0.95f);
    }

    private void OnFootstep()
    {
        float volume = player.IsCrouching ? crouchVolume : player.IsSprinting ? sprintVolume : walkVolume;
        PlayRandomFootstepSound(volume, 1f);
    }

    private void OnJumped(bool doubleJump)
    {
        if (!doubleJump) PlayRandomFootstepSound(jumpVolume, 1.05f);
    }

    private void OnLanded(float impactSpeed)
    {
        if (impactSpeed < minLandingSpeed) return;
        float volume = Mathf.Lerp(0.4f, maxLandingVolume, Mathf.InverseLerp(minLandingSpeed, 15f, impactSpeed));
        PlayRandomFootstepSound(volume, 0.85f);
    }

    private int GetSourceIndex()
    {
        switch (player.groundType)
        {
            case "water": return 1;
            default: return 0;
        }
    }

    private void PlayRandomFootstepSound(float volume, float basePitch)
    {
        if (audioSource == null || footstepSources == null || footstepSources.Length == 0) return;

        int sourceIndex = Mathf.Min(GetSourceIndex(), footstepSources.Length - 1);
        AudioSourceArray source = footstepSources[sourceIndex];
        if (source == null) return;

        AudioClip[] clips = source.AudioClips;
        if (clips == null || clips.Length == 0) return;

        // Avoid playing the same clip twice in a row.
        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == lastClipIndex)
            index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        lastClipIndex = index;

        audioSource.pitch = basePitch + Random.Range(-pitchVariation, pitchVariation);
        audioSource.PlayOneShot(clips[index], volume);
    }
}
