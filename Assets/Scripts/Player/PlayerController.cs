using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Fast, responsive rigidbody first-person controller (Doom Eternal style):
/// snappy ground acceleration, strong air control, sprint, crouch, coyote time,
/// jump buffering, double jump and dash with charges.
/// Camera feel (head bob, tilt, landing, FOV) lives in FirstPersonCameraEffects.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(GameInputs))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance;

    [Header("References")]
    [Tooltip("Camera holder that follows the body. The Camera should be a child of it (head bob is applied to the child).")]
    [SerializeField] private GameObject playerCamera;

    [Header("Speeds (m/s)")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float sprintSpeed = 11f;
    [SerializeField] private float crouchSpeed = 3.5f;
    [Tooltip("Sprint only works when the input is mostly forward (1 = only straight forward, -1 = any direction).")]
    [SerializeField, Range(-1f, 1f)] private float sprintForwardThreshold = 0.3f;

    [Header("Ground Acceleration (m/s²)")]
    [SerializeField] private float groundAcceleration = 80f;
    [SerializeField] private float groundDeceleration = 60f;
    [Tooltip("Acceleration multiplier when reversing direction. Makes direction changes snappy.")]
    [SerializeField] private float turnAroundMultiplier = 1.6f;

    [Header("Air Control (m/s²)")]
    [SerializeField] private float airAcceleration = 35f;
    [Tooltip("How fast speed above the normal cap (e.g. after a dash) bleeds off in the air.")]
    [SerializeField] private float airDrag = 3f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private bool allowDoubleJump = true;
    [SerializeField] private float doubleJumpHeight = 1.3f;
    [Tooltip("Time after walking off a ledge during which you can still jump.")]
    [SerializeField] private float coyoteTime = 0.12f;
    [Tooltip("Time a jump press is remembered before landing.")]
    [SerializeField] private float jumpBufferTime = 0.15f;

    [Header("Gravity")]
    [SerializeField] private float gravity = 30f;
    [Tooltip("Extra gravity when falling. Gives the heavy, punchy arc of modern shooters.")]
    [SerializeField] private float fallGravityMultiplier = 1.4f;
    [SerializeField] private float maxFallSpeed = 55f;

    [Header("Dash")]
    [SerializeField] private bool allowDash = true;
    [SerializeField] private float dashSpeed = 24f;
    [SerializeField] private float dashDuration = 0.16f;
    [SerializeField] private int dashCharges = 2;
    [Tooltip("Seconds to recharge one dash charge.")]
    [SerializeField] private float dashRechargeTime = 1.25f;

    [Header("Crouch")]
    [FormerlySerializedAs("charcterHeight")]
    [SerializeField] private float standingHeight = 2f;
    [FormerlySerializedAs("crouchHeight")]
    [SerializeField] private float crouchHeightReduction = 0.6f;
    [FormerlySerializedAs("crouchedCameraHeight")]
    [SerializeField] private float crouchCameraDrop = 0.6f;
    [SerializeField] private float crouchTransitionSpeed = 12f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("How far below the feet we still count as grounded (also snaps you down slopes and small steps).")]
    [SerializeField] private float groundSnapDistance = 0.25f;
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 50f;
    [SerializeField] private float groundStickVelocity = 2f;

    [Header("Footstep Cadence (meters per step)")]
    [SerializeField] private float walkStepLength = 2.1f;
    [SerializeField] private float sprintStepLength = 2.9f;
    [SerializeField] private float crouchStepLength = 1.1f;

    [Header("Debug")]
    [SerializeField] private bool showDebug;

    [Header("State Variables")]
    public bool jumping;
    public bool crouched;
    public bool onGrounded;
    public string groundType = "";

    // ---- Events (used by camera effects and audio) ----
    public event Action Footstep;
    /// <summary>Impact speed in m/s.</summary>
    public event Action<float> Landed;
    /// <summary>true = double (air) jump.</summary>
    public event Action<bool> Jumped;
    /// <summary>Dash direction in world space.</summary>
    public event Action<Vector3> Dashed;

    // ---- Public read-only state ----
    public bool IsGrounded => onGrounded;
    public bool IsCrouching => crouched;
    public bool IsSprinting { get; private set; }
    public bool IsDashing => dashTimer > 0f;
    public float HorizontalSpeed { get; private set; }
    public Vector3 Velocity => rb.linearVelocity;
    public Vector3 LocalVelocity => Quaternion.Inverse(YawRotation) * rb.linearVelocity;
    public float StridePhase { get; private set; }
    public float WalkSpeed => moveSpeed;
    public float SprintSpeed => sprintSpeed;
    public float DashChargesAvailable => dashChargesCurrent;
    public Quaternion YawRotation => Quaternion.Euler(0f, gameInputs.Yaw, 0f);
    public Camera PlayerCam { get; private set; }

    // ---- Set by other systems (e.g. WeaponManager while aiming) ----
    /// <summary>Prevents sprinting (e.g. while aiming).</summary>
    public bool SprintBlocked { get; set; }
    /// <summary>Multiplies the move speed (e.g. slower while aiming).</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    // ---- Used by movement add-ons (e.g. LedgeGrab) ----
    public bool IsExternallyControlled { get; private set; }
    public float StandingHeight => standingHeight;
    public float ColliderRadius => capsule.radius;
    public Vector3 BaseColliderCenter => baseColliderCenter;
    public Vector3 FeetPosition => transform.TransformPoint(baseColliderCenter) - Vector3.up * (standingHeight * 0.5f);
    public Vector2 MoveInput => moveInput;
    public GameInputs Inputs => gameInputs;
    public LayerMask GroundMask => groundMask;
    public float MaxSlopeAngle => maxSlopeAngle;
    public CapsuleCollider Capsule => capsule;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private GameInputs gameInputs;
    private Transform cameraHolder;

    private Vector3 eyeOffset;
    private Vector3 baseColliderCenter;
    private float currentCameraDrop;

    private Vector2 moveInput;
    private bool sprintHeld;
    private bool crouchHeld;
    private bool dashRequested;

    private float jumpBufferTimer;
    private float coyoteTimer;
    private float timeSinceJump = 10f;
    private int airJumpsUsed;

    private float dashTimer;
    private Vector3 dashDirection;
    private float dashChargesCurrent;

    private Vector3 groundNormal = Vector3.up;
    private bool wasGrounded;
    private float lastVelocityY;
    private int lastStepIndex;

    private readonly RaycastHit[] castHits = new RaycastHit[16];
    private readonly Collider[] overlapHits = new Collider[16];

    private void Awake()
    {
        Instance = this;

        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        gameInputs = GetComponent<GameInputs>();

        // Smooth rendering between physics steps + no tunnelling while dashing.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;
        rb.useGravity = false;  // custom gravity
        rb.linearDamping = 0f;

        // Frictionless so we never stick to walls; all friction is handled in code.
        capsule.sharedMaterial = new PhysicsMaterial("PlayerFrictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        baseColliderCenter = capsule.center;
        dashChargesCurrent = dashCharges;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        cameraHolder = playerCamera.transform;
        eyeOffset = cameraHolder.position - transform.position;

        gameInputs.xRotation = transform.eulerAngles.y;
        gameInputs.yRotation = 0f;

        PlayerCam = cameraHolder.GetComponentInChildren<Camera>();
        if (PlayerCam != null && PlayerCam.GetComponent<FirstPersonCameraEffects>() == null)
            PlayerCam.gameObject.AddComponent<FirstPersonCameraEffects>();
    }

    private void Update()
    {
        moveInput = Vector2.ClampMagnitude(new Vector2(gameInputs.xAxisValue, gameInputs.yAxisValue), 1f);
        sprintHeld = gameInputs.run != null && gameInputs.run.IsPressed();
        crouchHeld = gameInputs.crouch != null && gameInputs.crouch.IsPressed();

        if (!IsExternallyControlled)
        {
            if (gameInputs.jump != null && gameInputs.jump.WasPressedThisFrame())
                jumpBufferTimer = jumpBufferTime;

            if (allowDash && gameInputs.dash != null && gameInputs.dash.WasPressedThisFrame())
                dashRequested = true;
        }

        IsSprinting = sprintHeld && !crouched && !SprintBlocked && moveInput.sqrMagnitude > 0.01f
                      && moveInput.normalized.y > sprintForwardThreshold;

        UpdateStride();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        jumpBufferTimer -= dt;
        timeSinceJump += dt;
        if (dashChargesCurrent < dashCharges)
            dashChargesCurrent = Mathf.Min(dashCharges, dashChargesCurrent + dt / dashRechargeTime);

        if (IsExternallyControlled)
        {
            HorizontalSpeed = 0f;
            rb.MoveRotation(YawRotation);
            return;
        }

        CrouchVoid();
        CheckGround();

        if (onGrounded)
        {
            coyoteTimer = coyoteTime;
            airJumpsUsed = 0;
            if (!wasGrounded) OnLand(Mathf.Max(0f, -lastVelocityY));
        }
        else
        {
            coyoteTimer -= dt;
        }
        wasGrounded = onGrounded;

        Vector3 v = rb.linearVelocity;
        Vector3 wishDir = YawRotation * new Vector3(moveInput.x, 0f, moveInput.y);
        float targetSpeed = (crouched ? crouchSpeed : IsSprinting ? sprintSpeed : moveSpeed) * Mathf.Max(0f, SpeedMultiplier);

        if (dashRequested)
        {
            dashRequested = false;
            TryDash(wishDir);
        }

        if (IsDashing)
        {
            v = DashMove(targetSpeed, dt);
        }
        else
        {
            v = onGrounded ? GroundMove(v, wishDir, targetSpeed, dt) : AirMove(v, wishDir, targetSpeed, dt);
            v = JumpVoid(v, wishDir, targetSpeed);
        }

        lastVelocityY = v.y;
        rb.linearVelocity = v;
        rb.MoveRotation(YawRotation);
        HorizontalSpeed = new Vector2(v.x, v.z).magnitude;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        float targetDrop = crouched ? crouchCameraDrop : 0f;
        currentCameraDrop = Mathf.Lerp(currentCameraDrop, targetDrop, 1f - Mathf.Exp(-crouchTransitionSpeed * dt));

        // Body is interpolated, so following it directly is smooth and lag-free.
        cameraHolder.position = transform.position + eyeOffset + Vector3.down * currentCameraDrop;
        cameraHolder.rotation = Quaternion.Euler(gameInputs.Pitch, gameInputs.Yaw, 0f);
    }

    // ---------------- Movement ----------------

    private Vector3 GroundMove(Vector3 v, Vector3 wishDir, float targetSpeed, float dt)
    {
        Vector3 planar = Vector3.ProjectOnPlane(v, groundNormal);

        Vector3 target = Vector3.zero;
        if (wishDir.sqrMagnitude > 1e-4f)
            target = Vector3.ProjectOnPlane(wishDir, groundNormal).normalized * (targetSpeed * wishDir.magnitude);

        float rate;
        if (target.sqrMagnitude < 1e-4f)
            rate = groundDeceleration;
        else if (Vector3.Dot(planar, target) < 0f)
            rate = groundAcceleration * turnAroundMultiplier;
        else if (planar.sqrMagnitude > target.sqrMagnitude)
            rate = groundDeceleration; // shedding extra speed (e.g. landing out of a dash)
        else
            rate = groundAcceleration;

        planar = Vector3.MoveTowards(planar, target, rate * dt);

        // Keep contact on slopes / when walking off small bumps.
        return planar - groundNormal * groundStickVelocity;
    }

    private Vector3 AirMove(Vector3 v, Vector3 wishDir, float targetSpeed, float dt)
    {
        Vector3 horizontal = new Vector3(v.x, 0f, v.z);
        float speed = horizontal.magnitude;

        if (wishDir.sqrMagnitude > 1e-4f)
        {
            // Steer freely but keep momentum above the normal cap, bleeding it slowly.
            float cap = Mathf.Max(targetSpeed, speed - airDrag * dt);
            horizontal = Vector3.MoveTowards(horizontal, wishDir.normalized * cap, airAcceleration * dt);
        }
        else
        {
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, airDrag * dt);
        }

        float g = gravity * (v.y < 0f ? fallGravityMultiplier : 1f);
        float vy = Mathf.Max(v.y - g * dt, -maxFallSpeed);

        return new Vector3(horizontal.x, vy, horizontal.z);
    }

    private Vector3 JumpVoid(Vector3 v, Vector3 wishDir, float targetSpeed)
    {
        if (jumpBufferTimer <= 0f) return v;

        if (onGrounded || coyoteTimer > 0f)
        {
            v.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            ConsumeJump();
            Jumped?.Invoke(false);
        }
        else if (allowDoubleJump && airJumpsUsed < 1)
        {
            airJumpsUsed++;
            v.y = Mathf.Sqrt(2f * gravity * doubleJumpHeight);

            // Double jump lets you redirect your momentum, like in Doom Eternal.
            if (wishDir.sqrMagnitude > 1e-4f)
            {
                float speed = Mathf.Max(new Vector2(v.x, v.z).magnitude, targetSpeed);
                Vector3 h = wishDir.normalized * speed;
                v.x = h.x;
                v.z = h.z;
            }
            ConsumeJump();
            Jumped?.Invoke(true);
        }
        return v;
    }

    private void ConsumeJump()
    {
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        timeSinceJump = 0f;
        onGrounded = false;
        wasGrounded = false;
        jumping = true;
    }

    private void TryDash(Vector3 wishDir)
    {
        if (!allowDash || dashChargesCurrent < 1f) return;

        dashDirection = wishDir.sqrMagnitude > 1e-4f ? wishDir.normalized : YawRotation * Vector3.forward;
        dashTimer = dashDuration;
        dashChargesCurrent -= 1f;
        Dashed?.Invoke(dashDirection);
    }

    private Vector3 DashMove(float targetSpeed, float dt)
    {
        dashTimer -= dt;

        Vector3 dir = onGrounded ? Vector3.ProjectOnPlane(dashDirection, groundNormal).normalized : dashDirection;

        if (dashTimer <= 0f)
        {
            // Exit with a bit of carried momentum; it bleeds off naturally.
            return dashDirection * Mathf.Max(targetSpeed, sprintSpeed);
        }
        return dir * dashSpeed; // no gravity during the dash
    }

    // ---------------- External control (mantling, hanging, cutscenes...) ----------------

    /// <summary>Stops normal movement; the body becomes kinematic and is moved with MoveExternally.</summary>
    public void BeginExternalControl()
    {
        if (IsExternallyControlled) return;
        IsExternallyControlled = true;

        dashTimer = 0f;
        dashRequested = false;
        jumpBufferTimer = 0f;
        onGrounded = false;
        wasGrounded = false;
        jumping = false;
        HorizontalSpeed = 0f;

        rb.linearVelocity = Vector3.zero;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // required for kinematic bodies
        rb.isKinematic = true;
    }

    /// <summary>Moves the body (interpolated) while externally controlled.</summary>
    public void MoveExternally(Vector3 bodyPosition)
    {
        if (IsExternallyControlled) rb.MovePosition(bodyPosition);
    }

    /// <summary>Gives control back to the player with the given velocity.</summary>
    public void EndExternalControl(Vector3 exitVelocity)
    {
        if (!IsExternallyControlled) return;

        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = exitVelocity;
        lastVelocityY = exitVelocity.y;
        airJumpsUsed = 0; // fresh double jump after a ledge
        IsExternallyControlled = false;
    }

    /// <summary>Body (transform) position that puts the standing capsule's feet at the given point.</summary>
    public Vector3 BodyPositionFromFeet(Vector3 feet)
    {
        return feet + Vector3.up * (standingHeight * 0.5f) - transform.TransformVector(baseColliderCenter);
    }

    // ---------------- Ground / Crouch ----------------

    private void CheckGround()
    {
        float radius = capsule.radius * 0.95f;
        Vector3 center = transform.TransformPoint(capsule.center);
        float halfHeight = capsule.height * 0.5f;
        float snap = wasGrounded ? groundSnapDistance : 0.06f;
        float castDistance = halfHeight - radius + snap;

        bool justJumped = timeSinceJump < 0.2f && rb.linearVelocity.y > 0f;

        onGrounded = false;
        groundNormal = Vector3.up;
        if (justJumped || IsDashing && !wasGrounded) return;

        int count = Physics.SphereCastNonAlloc(center, radius, Vector3.down, castHits, castDistance,
                                               groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];
            if (hit.collider == capsule || hit.distance <= 0f) continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeAngle) continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                groundNormal = hit.normal;
                groundType = hit.collider.tag;
                onGrounded = true;
            }
        }
    }

    private void CrouchVoid()
    {
        bool wantCrouch = crouchHeld;
        if (!wantCrouch && crouched && !CanStand()) wantCrouch = true; // don't stand up into a ceiling

        crouched = wantCrouch;
        float reduction = crouched ? crouchHeightReduction : 0f;
        capsule.height = standingHeight - reduction;
        capsule.center = baseColliderCenter + Vector3.down * (reduction * 0.5f);
    }

    private bool CanStand()
    {
        float r = capsule.radius * 0.95f;
        Vector3 feet = transform.TransformPoint(baseColliderCenter) - Vector3.up * (standingHeight * 0.5f);
        Vector3 p0 = feet + Vector3.up * (r + 0.05f);
        Vector3 p1 = feet + Vector3.up * (standingHeight - r);

        int count = Physics.OverlapCapsuleNonAlloc(p0, p1, r, overlapHits, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (overlapHits[i] != capsule) return false;
        return true;
    }

    private void OnLand(float impactSpeed)
    {
        jumping = false;
        // Next footstep comes half a step after landing.
        StridePhase = lastStepIndex * Mathf.PI + Mathf.PI * 0.5f;
        Landed?.Invoke(impactSpeed);
    }

    // ---------------- Stride (drives head bob + footsteps) ----------------

    private void UpdateStride()
    {
        if (!onGrounded || IsDashing || HorizontalSpeed < 0.5f) return;

        float stepLength = crouched
            ? crouchStepLength
            : Mathf.Lerp(walkStepLength, sprintStepLength, Mathf.InverseLerp(moveSpeed, sprintSpeed, HorizontalSpeed));

        // One footstep every PI radians.
        StridePhase += HorizontalSpeed / stepLength * Mathf.PI * Time.deltaTime;

        int step = Mathf.FloorToInt(StridePhase / Mathf.PI);
        if (step != lastStepIndex)
        {
            lastStepIndex = step;
            Footstep?.Invoke();
        }
    }

    // ---------------- Helpers ----------------

    /// <summary>Topmost player object (contains the body, camera holder and weapons).</summary>
    public Transform Root => transform.parent != null ? transform.parent : transform;

    /// <summary>True if the collider belongs to the player (used to ignore ourselves in raycasts).</summary>
    public bool OwnsCollider(Collider other)
    {
        return other != null && (other == capsule || other.transform.IsChildOf(Root));
    }

    private void OnGUI()
    {
        if (!showDebug) return;
        GUI.Label(new Rect(10, 10, 300, 20), $"speed: {HorizontalSpeed:0.0} m/s  vy: {rb.linearVelocity.y:0.0}");
        GUI.Label(new Rect(10, 30, 300, 20), $"grounded: {onGrounded}  ground: {groundType}");
        GUI.Label(new Rect(10, 50, 300, 20), $"sprint: {IsSprinting}  crouch: {crouched}  dash charges: {dashChargesCurrent:0.0}");
    }
}
