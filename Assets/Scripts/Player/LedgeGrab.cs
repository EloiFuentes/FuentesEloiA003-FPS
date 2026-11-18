using System;
using UnityEngine;

/// <summary>
/// Ledge grabbing / mantling for PlayerController.
/// AutoMantle (default, Doom Eternal style): jump at a ledge while moving toward it and you are
/// pulled up onto it in one quick motion.
/// HangThenClimb: low ledges are vaulted; high ledges are grabbed and you hang until you
/// climb (Jump / Forward), drop (Crouch) or leap away (Back + Jump).
/// Add it to the same GameObject as PlayerController.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class LedgeGrab : MonoBehaviour
{
    public enum GrabMode { AutoMantle, HangThenClimb }

    [Header("Mode")]
    [SerializeField] private GrabMode mode = GrabMode.AutoMantle;
    [Tooltip("Only grab when the player is pressing a direction toward the ledge.")]
    [SerializeField] private bool requireMoveInput = true;

    [Header("Reach (meters above the feet)")]
    [SerializeField] private float minLedgeHeight = 0.4f;
    [SerializeField] private float maxLedgeHeight = 2.5f;
    [Tooltip("HangThenClimb only: ledges higher than this are grabbed (hang); lower ones are vaulted.")]
    [SerializeField] private float hangThreshold = 1.4f;
    [Tooltip("How far beyond the capsule we look for a wall.")]
    [SerializeField] private float wallCheckDistance = 0.45f;
    [Tooltip("Max angle between your move direction and the wall for a grab.")]
    [SerializeField, Range(0f, 90f)] private float maxApproachAngle = 55f;

    [Header("Mantle")]
    [SerializeField] private float mantleBaseDuration = 0.18f;
    [SerializeField] private float mantleDurationPerMeter = 0.1f;
    [SerializeField] private float mantleMaxDuration = 0.45f;
    [Tooltip("Extra height above the ledge the arc goes through, so the feet clear the edge.")]
    [SerializeField] private float mantleArcHeight = 0.15f;
    [Tooltip("Forward speed you keep after mantling (if still holding a direction).")]
    [SerializeField] private float mantleExitSpeed = 4f;

    [Header("Hang (HangThenClimb mode)")]
    [Tooltip("How far below the ledge the feet hang. ~1.35 puts the eyes just over the edge.")]
    [SerializeField] private float hangFeetBelowLedge = 1.35f;
    [SerializeField] private float hangSnapDuration = 0.1f;
    [SerializeField] private bool climbWithForward = true;
    [Tooltip("Time hanging before holding Forward climbs (so you don't climb instantly).")]
    [SerializeField] private float forwardClimbDelay = 0.25f;
    [SerializeField] private float leapAwaySpeed = 5f;
    [SerializeField] private float leapAwayUpSpeed = 6f;

    [Header("Timing")]
    [Tooltip("Time after a mantle/drop before another grab can happen.")]
    [SerializeField] private float regrabCooldown = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos;

    /// <summary>Height climbed (m).</summary>
    public event Action<float> MantleStarted;
    public event Action MantleFinished;
    public event Action LedgeGrabbed;
    public event Action LedgeReleased;

    private enum State { None, MovingToHang, Hanging, Mantling }

    private struct Ledge
    {
        public Vector3 Top;        // point on the ledge top surface
        public Vector3 WallNormal; // horizontal, pointing out of the wall toward the player
        public Vector3 StandFeet;  // where the feet end up after the mantle
        public float Height;       // ledge height above the feet when detected
    }

    public bool IsHanging => state == State.Hanging || state == State.MovingToHang;
    public bool IsMantling => state == State.Mantling;

    private PlayerController player;
    private State state;
    private Ledge ledge;

    private Vector3 moveStart;
    private Vector3 moveControl;
    private Vector3 moveEnd;
    private float moveDuration;
    private float moveT;

    private float cooldown;
    private float hangTime;
    private bool jumpPressed;
    private bool crouchPressed;

    private readonly Collider[] overlapHits = new Collider[16];

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    private void Update()
    {
        // Latch button presses so FixedUpdate never misses them.
        GameInputs inputs = player.Inputs;
        if (inputs == null) return;
        if (inputs.jump != null && inputs.jump.WasPressedThisFrame()) jumpPressed = true;
        if (inputs.crouch != null && inputs.crouch.WasPressedThisFrame()) crouchPressed = true;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        switch (state)
        {
            case State.None:
                jumpPressed = crouchPressed = false;
                if (cooldown > 0f) { cooldown -= dt; return; }
                TryGrab();
                break;

            case State.Mantling:
            case State.MovingToHang:
                StepMove(dt);
                break;

            case State.Hanging:
                UpdateHang(dt);
                break;
        }
    }

    // ---------------- Detection ----------------

    private void TryGrab()
    {
        if (player.IsGrounded || player.IsExternallyControlled) return;
        if (requireMoveInput && player.MoveInput.sqrMagnitude < 0.01f) return;
        if (!FindLedge(out Ledge found)) return;

        ledge = found;
        player.BeginExternalControl();

        if (mode == GrabMode.HangThenClimb && ledge.Height > hangThreshold)
            StartHang();
        else
            StartMantle();
    }

    private bool FindLedge(out Ledge result)
    {
        result = default;

        Vector3 up = Vector3.up;
        Vector3 feet = player.FeetPosition;
        Vector2 input = player.MoveInput;
        Vector3 probeDir = input.sqrMagnitude > 0.01f
            ? (player.YawRotation * new Vector3(input.x, 0f, input.y)).normalized
            : player.YawRotation * Vector3.forward;

        float radius = player.ColliderRadius;
        int mask = player.GroundMask;

        // 1) Find a wall in front, scanning from the shins up to max reach.
        RaycastHit wall = default;
        bool foundWall = false;
        for (float h = 0.2f; h <= maxLedgeHeight; h += 0.25f)
        {
            if (Physics.Raycast(feet + up * h, probeDir, out wall, radius + wallCheckDistance, mask, QueryTriggerInteraction.Ignore)
                && wall.collider != player.Capsule)
            {
                foundWall = true;
                break;
            }
        }
        if (!foundWall) return false;

        Vector3 wallNormal = Vector3.ProjectOnPlane(wall.normal, up);
        if (wallNormal.sqrMagnitude < 0.25f) return false; // too flat to be a wall
        wallNormal.Normalize();
        if (Vector3.Angle(-wallNormal, probeDir) > maxApproachAngle) return false;

        // 2) Find the top surface just behind the wall face.
        Vector3 inside = wall.point - wallNormal * 0.15f;
        float startY = feet.y + maxLedgeHeight + 0.05f;
        Vector3 topStart = new Vector3(inside.x, startY, inside.z);
        if (!Physics.Raycast(topStart, Vector3.down, out RaycastHit top, startY - (feet.y + minLedgeHeight),
                             mask, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Angle(top.normal, up) > player.MaxSlopeAngle) return false;
        if (top.point.y < wall.point.y - 0.05f) return false;

        float height = top.point.y - feet.y;
        if (height < minLedgeHeight || height > maxLedgeHeight) return false;

        // 3) The ledge must be deep enough to stand on.
        Vector3 standXZ = wall.point - wallNormal * (radius + 0.15f);
        Vector3 standProbe = new Vector3(standXZ.x, top.point.y + 0.5f, standXZ.z);
        if (!Physics.Raycast(standProbe, Vector3.down, out RaycastHit floor, 0.8f, mask, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Angle(floor.normal, up) > player.MaxSlopeAngle) return false;
        Vector3 standFeet = new Vector3(standXZ.x, floor.point.y, standXZ.z);

        // 4) Room to stand up there, and head room to rise from where we are.
        if (!CapsuleIsFree(standFeet + up * 0.05f, player.StandingHeight)) return false;
        Vector3 raisedFeet = new Vector3(feet.x, Mathf.Max(feet.y, standFeet.y) + 0.05f, feet.z);
        if (!CapsuleIsFree(raisedFeet, player.StandingHeight)) return false;

        result = new Ledge
        {
            Top = top.point,
            WallNormal = wallNormal,
            StandFeet = standFeet,
            Height = height
        };
        return true;
    }

    private bool CapsuleIsFree(Vector3 feet, float height)
    {
        float r = player.ColliderRadius * 0.9f;
        Vector3 p0 = feet + Vector3.up * (r + 0.02f);
        Vector3 p1 = feet + Vector3.up * (height - r);
        int count = Physics.OverlapCapsuleNonAlloc(p0, p1, r, overlapHits, player.GroundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (overlapHits[i] != player.Capsule) return false;
        return true;
    }

    // ---------------- Mantle ----------------

    private void StartMantle()
    {
        Vector3 start = transform.position;
        Vector3 end = player.BodyPositionFromFeet(ledge.StandFeet + Vector3.up * 0.02f);

        // Quadratic arc: rise along the wall first, then roll over the edge.
        moveStart = start;
        moveEnd = end;
        moveControl = new Vector3(start.x, end.y + mantleArcHeight, start.z);

        float climb = Mathf.Max(0f, end.y - start.y);
        moveDuration = Mathf.Clamp(mantleBaseDuration + climb * mantleDurationPerMeter, mantleBaseDuration, mantleMaxDuration);
        moveT = 0f;
        state = State.Mantling;

        MantleStarted?.Invoke(climb);
    }

    private void FinishMantle()
    {
        Vector3 exit = Vector3.zero;
        if (player.MoveInput.sqrMagnitude > 0.01f)
            exit = -ledge.WallNormal * mantleExitSpeed;

        state = State.None;
        cooldown = regrabCooldown;
        player.EndExternalControl(exit);
        MantleFinished?.Invoke();
    }

    // ---------------- Hang ----------------

    private void StartHang()
    {
        Vector3 hangXZ = ledge.Top + ledge.WallNormal * (player.ColliderRadius + 0.05f);
        Vector3 hangFeet = new Vector3(hangXZ.x, ledge.Top.y - hangFeetBelowLedge, hangXZ.z);

        moveStart = transform.position;
        moveEnd = player.BodyPositionFromFeet(hangFeet);
        moveControl = Vector3.Lerp(moveStart, moveEnd, 0.5f);
        moveDuration = Mathf.Max(0.01f, hangSnapDuration);
        moveT = 0f;
        hangTime = 0f;
        jumpPressed = crouchPressed = false;
        state = State.MovingToHang;

        LedgeGrabbed?.Invoke();
    }

    private void UpdateHang(float dt)
    {
        hangTime += dt;
        player.MoveExternally(moveEnd);

        Vector2 input = player.MoveInput;
        // Input relative to the wall, so "forward" means toward the ledge wherever you look.
        Vector3 worldInput = player.YawRotation * new Vector3(input.x, 0f, input.y);
        float towardWall = Vector3.Dot(worldInput, -ledge.WallNormal);

        if (crouchPressed)
        {
            Release(Vector3.down);
        }
        else if (jumpPressed && towardWall < -0.3f)
        {
            Release(ledge.WallNormal * leapAwaySpeed + Vector3.up * leapAwayUpSpeed);
        }
        else if (jumpPressed || (climbWithForward && hangTime >= forwardClimbDelay && towardWall > 0.5f))
        {
            StartMantle();
        }

        jumpPressed = crouchPressed = false;
    }

    private void Release(Vector3 velocity)
    {
        state = State.None;
        cooldown = regrabCooldown;
        player.EndExternalControl(velocity);
        LedgeReleased?.Invoke();
    }

    // ---------------- Shared movement ----------------

    private void StepMove(float dt)
    {
        moveT = Mathf.Min(1f, moveT + dt / moveDuration);
        float t = 1f - (1f - moveT) * (1f - moveT); // ease out: fast pull, soft finish

        float u = 1f - t;
        Vector3 pos = u * u * moveStart + 2f * u * t * moveControl + t * t * moveEnd;
        player.MoveExternally(pos);

        if (moveT >= 1f)
        {
            if (state == State.Mantling) FinishMantle();
            else state = State.Hanging;
        }
    }

    private void OnDisable()
    {
        if (state != State.None && player != null)
        {
            state = State.None;
            player.EndExternalControl(Vector3.zero);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || state == State.None) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(ledge.Top, 0.05f);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(ledge.StandFeet, 0.1f);
        Gizmos.DrawLine(moveStart, moveControl);
        Gizmos.DrawLine(moveControl, moveEnd);
    }
}
