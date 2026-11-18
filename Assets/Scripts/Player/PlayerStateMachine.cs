using System;
using UnityEngine;

public enum PlayerLocomotionState
{
    Idle,
    Walk,
    Sprint,
    Crouch,
    CrouchWalk,
    Airborne,
    Dash,
    Mantle,
    Hang
}

/// <summary>
/// High-level player state (Idle, Walk, Sprint, Airborne...), derived from PlayerController.
/// Weapons read it to choose their locomotion animations. If you assign a body Animator,
/// states with the same names as <see cref="PlayerLocomotionState"/> are played automatically
/// (no transitions needed).
/// </summary>
[DefaultExecutionOrder(10)]
[RequireComponent(typeof(PlayerController))]
public class PlayerStateMachine : MonoBehaviour
{
    [Tooltip("Optional full-body / third-person Animator. States named like PlayerLocomotionState are played automatically.")]
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private float crossFadeTime = 0.15f;
    [Tooltip("Horizontal speed (m/s) above which the player counts as moving.")]
    [SerializeField] private float moveSpeedThreshold = 0.5f;

    public PlayerLocomotionState State => fsm.CurrentId;
    public PlayerLocomotionState PreviousState => fsm.PreviousId;
    public float TimeInState => fsm.TimeInState;

    /// <summary>(previous, next)</summary>
    public event Action<PlayerLocomotionState, PlayerLocomotionState> StateChanged;

    public bool IsMoving => State == PlayerLocomotionState.Walk || State == PlayerLocomotionState.Sprint
                            || State == PlayerLocomotionState.CrouchWalk;
    /// <summary>True while climbing or hanging: weapons can't be used.</summary>
    public bool IsBusy => State == PlayerLocomotionState.Mantle || State == PlayerLocomotionState.Hang;

    private PlayerController player;
    private LedgeGrab ledgeGrab;
    private StateMachine<PlayerLocomotionState> fsm;
    private AnimationPlayer bodyAnimation;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        ledgeGrab = GetComponent<LedgeGrab>();
        if (bodyAnimator != null) bodyAnimation = new AnimationPlayer(bodyAnimator);

        fsm = new StateMachine<PlayerLocomotionState>();
        foreach (PlayerLocomotionState id in (PlayerLocomotionState[])Enum.GetValues(typeof(PlayerLocomotionState)))
            fsm.Add(id, new LocomotionState(this, id.ToString()));

        fsm.StateChanged += (previous, next) => StateChanged?.Invoke(previous, next);
    }

    private void Start()
    {
        fsm.ChangeState(Evaluate());
    }

    private void Update()
    {
        PlayerLocomotionState next = Evaluate();
        if (!fsm.IsIn(next)) fsm.ChangeState(next);
        fsm.Tick(Time.deltaTime);
    }

    private PlayerLocomotionState Evaluate()
    {
        if (ledgeGrab != null)
        {
            if (ledgeGrab.IsHanging) return PlayerLocomotionState.Hang;
            if (ledgeGrab.IsMantling) return PlayerLocomotionState.Mantle;
        }
        if (player.IsDashing) return PlayerLocomotionState.Dash;
        if (!player.IsGrounded) return PlayerLocomotionState.Airborne;

        bool moving = player.HorizontalSpeed > moveSpeedThreshold && player.MoveInput.sqrMagnitude > 0.01f;

        if (player.IsCrouching)
            return moving ? PlayerLocomotionState.CrouchWalk : PlayerLocomotionState.Crouch;
        if (moving)
            return player.IsSprinting ? PlayerLocomotionState.Sprint : PlayerLocomotionState.Walk;
        return PlayerLocomotionState.Idle;
    }

    private sealed class LocomotionState : StateBase
    {
        private readonly PlayerStateMachine owner;
        private readonly string animationState;

        public LocomotionState(PlayerStateMachine owner, string animationState)
        {
            this.owner = owner;
            this.animationState = animationState;
        }

        public override void Enter()
        {
            owner.bodyAnimation?.PlayLoop(animationState, owner.crossFadeTime);
        }
    }
}
