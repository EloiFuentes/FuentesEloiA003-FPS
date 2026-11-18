using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays Animator states by name from code, so you never have to set up transitions.
/// Missing states are silently skipped (use fallbacks), so every animation is optional.
/// </summary>
public sealed class AnimationPlayer
{
    private readonly Animator animator;
    private readonly int layer;
    private readonly Dictionary<string, int> hashes = new Dictionary<string, int>();
    private readonly Dictionary<int, bool> stateExists = new Dictionary<int, bool>();

    private int currentLoopHash;

    public Animator Animator => animator;

    public AnimationPlayer(Animator animator, int layer = 0)
    {
        this.animator = animator;
        this.layer = layer;
    }

    public bool IsValid =>
        animator != null && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled;

    public bool HasState(string stateName)
    {
        if (!IsValid || string.IsNullOrEmpty(stateName)) return false;
        int hash = Hash(stateName);
        if (!stateExists.TryGetValue(hash, out bool exists))
        {
            exists = animator.HasState(layer, hash);
            stateExists[hash] = exists;
        }
        return exists;
    }

    /// <summary>
    /// Plays a looping state (Idle, Walk, Run...). Does nothing if it's already playing.
    /// </summary>
    public bool PlayLoop(string stateName, float fadeSeconds = 0.15f)
    {
        if (!HasState(stateName)) return false;
        int hash = Hash(stateName);
        if (hash == currentLoopHash) return true;

        animator.CrossFadeInFixedTime(hash, fadeSeconds, layer);
        currentLoopHash = hash;
        return true;
    }

    /// <summary>
    /// Plays a one-shot state (Fire, Reload, Equip...) from the start, even if it's already playing.
    /// </summary>
    public bool PlayAction(string stateName, float fadeSeconds = 0.05f)
    {
        if (!HasState(stateName)) return false;
        int hash = Hash(stateName);

        if (fadeSeconds <= 0f || IsCurrentState(hash))
            animator.Play(hash, layer, 0f);         // hard restart (needed for rapid fire)
        else
            animator.CrossFadeInFixedTime(hash, fadeSeconds, layer, 0f);

        currentLoopHash = 0; // next PlayLoop call must fade back in
        return true;
    }

    /// <summary>Plays the first state that exists. Returns the played name, or null.</summary>
    public string PlayLoop(float fadeSeconds, params string[] candidates)
    {
        foreach (string name in candidates)
            if (PlayLoop(name, fadeSeconds)) return name;
        return null;
    }

    /// <summary>Plays the first one-shot state that exists. Returns the played name, or null.</summary>
    public string PlayAction(float fadeSeconds, params string[] candidates)
    {
        foreach (string name in candidates)
            if (PlayAction(name, fadeSeconds)) return name;
        return null;
    }

    public void ResetLoopTracking() => currentLoopHash = 0;

    private bool IsCurrentState(int hash)
    {
        return animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == hash
               || (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).shortNameHash == hash);
    }

    private int Hash(string stateName)
    {
        if (!hashes.TryGetValue(stateName, out int hash))
        {
            hash = Animator.StringToHash(stateName);
            hashes[stateName] = hash;
        }
        return hash;
    }
}
