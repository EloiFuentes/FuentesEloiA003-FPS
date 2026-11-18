using System;
using System.Collections.Generic;

/// <summary>A state driven by a <see cref="StateMachine{TId}"/>.</summary>
public interface IState
{
    void Enter();
    void Tick(float deltaTime);
    void Exit();
}

/// <summary>Convenience base class: override only what you need.</summary>
public abstract class StateBase : IState
{
    public virtual void Enter() { }
    public virtual void Tick(float deltaTime) { }
    public virtual void Exit() { }
}

/// <summary>
/// Small, allocation-free finite state machine keyed by an enum.
/// </summary>
public sealed class StateMachine<TId> where TId : struct, Enum
{
    private readonly Dictionary<TId, IState> states = new Dictionary<TId, IState>();
    private static readonly EqualityComparer<TId> Comparer = EqualityComparer<TId>.Default;

    public TId CurrentId { get; private set; }
    public TId PreviousId { get; private set; }
    public IState Current { get; private set; }
    public float TimeInState { get; private set; }
    public bool IsRunning => Current != null;

    /// <summary>(previous, next)</summary>
    public event Action<TId, TId> StateChanged;

    public StateMachine<TId> Add(TId id, IState state)
    {
        states[id] = state ?? throw new ArgumentNullException(nameof(state));
        return this;
    }

    public bool IsIn(TId id) => Current != null && Comparer.Equals(CurrentId, id);

    public void ChangeState(TId id, bool allowReenter = false)
    {
        if (!states.TryGetValue(id, out IState next))
            throw new InvalidOperationException($"State '{id}' was not added to the state machine.");

        if (Current != null && Comparer.Equals(CurrentId, id) && !allowReenter) return;

        TId previous = CurrentId;
        Current?.Exit();

        PreviousId = previous;
        CurrentId = id;
        Current = next;
        TimeInState = 0f;

        next.Enter();
        StateChanged?.Invoke(previous, id);
    }

    public void Tick(float deltaTime)
    {
        if (Current == null) return;
        TimeInState += deltaTime;
        Current.Tick(deltaTime);
    }
}
