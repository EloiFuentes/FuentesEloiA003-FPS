using UnityEngine;

/// <summary>
/// Procedural first-person camera feel: footstep-synced head bob, idle breathing,
/// strafe roll, spring-based landing/jump/dash kicks and dynamic FOV.
/// Put it on the Camera (a child of the camera holder). PlayerController adds it
/// automatically at runtime if it's missing; add it yourself to tweak values in the Inspector.
/// Also layers weapon aim FOV, recoil kicks, explosion shake and weapon camera-bone animation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // after PlayerController positions the camera holder
[RequireComponent(typeof(Camera))]
public class FirstPersonCameraEffects : MonoBehaviour
{
    /// <summary>The active first-person camera (used by explosions for shake).</summary>
    public static FirstPersonCameraEffects Active { get; private set; }

    [SerializeField] private PlayerController player;

    [Header("Head Bob")]
    [SerializeField] private bool enableHeadBob = true;
    [Tooltip("x = side sway, y = vertical bounce (meters).")]
    [SerializeField] private Vector2 walkBobAmplitude = new Vector2(0.03f, 0.045f);
    [SerializeField] private Vector2 sprintBobAmplitude = new Vector2(0.05f, 0.08f);
    [SerializeField] private float crouchBobMultiplier = 0.6f;
    [Tooltip("Degrees of roll that follow the side sway.")]
    [SerializeField] private float bobRoll = 0.7f;
    [Tooltip("Degrees the view nods down on each footstep.")]
    [SerializeField] private float bobPitch = 0.5f;
    [SerializeField] private float bobBlendSpeed = 10f;

    [Header("Idle Breathing")]
    [SerializeField] private bool enableBreathing = true;
    [SerializeField] private float breathAmplitude = 0.006f;
    [SerializeField] private float breathPitch = 0.25f;
    [SerializeField] private float breathFrequency = 0.3f;

    [Header("Strafe Tilt")]
    [SerializeField] private bool enableStrafeTilt = true;
    [SerializeField] private float strafeTiltAngle = 1.5f;
    [SerializeField] private float strafeTiltSpeed = 8f;

    [Header("Landing / Jump / Dash Kicks")]
    [SerializeField] private bool enableKicks = true;
    [Tooltip("Impacts slower than this (m/s) are ignored.")]
    [SerializeField] private float minLandingSpeed = 2f;
    [SerializeField] private float landingDip = 0.11f;
    [SerializeField] private float maxLandingDip = 2.5f;
    [SerializeField] private float landingPitch = 3.5f;
    [SerializeField] private float maxLandingPitch = 70f;
    [SerializeField] private float jumpPitchKick = 12f;
    [SerializeField] private float doubleJumpPitchKick = -18f;
    [SerializeField] private float dashRollKick = 40f;
    [Tooltip("Pitch kick (deg/s) when pulling up onto a ledge.")]
    [SerializeField] private float mantlePitchKick = 30f;
    [SerializeField] private float mantleRollKick = 15f;
    [Tooltip("Downward jolt when catching a ledge to hang.")]
    [SerializeField] private float ledgeGrabJolt = 1.2f;
    [Tooltip("Spring stiffness / damping. Lower damping = more bounce.")]
    [SerializeField] private float springStiffness = 120f;
    [SerializeField] private float springDamping = 15f;

    [Header("Field Of View")]
    [SerializeField] private bool enableFovEffects = true;
    [SerializeField] private float sprintFovIncrease = 6f;
    [SerializeField] private float dashFovIncrease = 12f;
    [SerializeField] private float fovSpeed = 8f;
    [SerializeField] private float dashFovSpeed = 20f;

    [Header("Weapons")]
    [Tooltip("Head bob multiplier when fully aimed.")]
    [SerializeField, Range(0f, 1f)] private float aimBobMultiplier = 0.3f;
    [Tooltip("Stiffness of the recoil springs (higher = snappier recoil recovery).")]
    [SerializeField] private float recoilStiffness = 220f;
    [SerializeField] private float recoilDamping = 20f;

    [Header("Shake (explosions)")]
    [SerializeField] private float maxShakeAngle = 6f;
    [SerializeField] private float maxShakeOffset = 0.06f;
    [SerializeField] private float shakeFrequency = 22f;
    [Tooltip("How fast shake fades (trauma per second).")]
    [SerializeField] private float shakeDecay = 1.4f;

    /// <summary>Weapon camera bone animation (set by WeaponManager).</summary>
    public ICameraAnimationSource CameraAnimationSource { get; set; }
    /// <summary>Camera animation offset applied this frame (WeaponManager cancels it on the arms).</summary>
    public Vector3 AppliedAnimationPosition { get; private set; }
    public Quaternion AppliedAnimationRotation { get; private set; } = Quaternion.identity;

    private struct Spring
    {
        public float Value;
        public float Velocity;

        public void Step(float stiffness, float damping, float dt)
        {
            Velocity += (-stiffness * Value - damping * Velocity) * dt;
            Value += Velocity * dt;
        }
    }

    private Camera cam;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private float baseFov;

    private float bobBlend;
    private float sprintBlend;
    private float tilt;
    private float fovOffset;
    private float breathTime;

    private Spring heightSpring;
    private Spring pitchSpring;
    private Spring rollSpring;
    private Spring recoilPitch;
    private Spring recoilYaw;
    private Spring recoilRoll;

    private float aimAmount;
    private float aimFov;
    private float trauma;
    private float shakeSeed;

    private bool bound;
    private LedgeGrab ledgeGrab;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        baseFov = cam.fieldOfView;
        shakeSeed = Random.value * 100f;
    }

    private void OnEnable()
    {
        Active = this;
        TryBind();
    }

    private void Start() => TryBind();

    // ---------------- Public API ----------------

    /// <summary>Aim blend (0..1) and the field of view to reach when fully aimed. Call every frame.</summary>
    public void SetAim(float amount, float fov)
    {
        aimAmount = Mathf.Clamp01(amount);
        aimFov = fov;
    }

    /// <summary>Visual recoil impulse in deg/s (negative pitch = up). Springs back automatically.</summary>
    public void AddRecoil(float pitch, float yaw, float roll)
    {
        recoilPitch.Velocity += pitch;
        recoilYaw.Velocity += yaw;
        recoilRoll.Velocity += roll;
    }

    /// <summary>Adds screen shake (0..1 trauma; stacks, fades out).</summary>
    public void AddShake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        if (bound && player != null)
        {
            player.Landed -= OnLanded;
            player.Jumped -= OnJumped;
            player.Dashed -= OnDashed;
        }
        if (ledgeGrab != null)
        {
            ledgeGrab.MantleStarted -= OnMantleStarted;
            ledgeGrab.LedgeGrabbed -= OnLedgeGrabbed;
            ledgeGrab = null;
        }
        bound = false;
    }

    private bool TryBind()
    {
        if (bound) return true;
        if (player == null) player = PlayerController.Instance;
        if (player == null) return false;

        player.Landed += OnLanded;
        player.Jumped += OnJumped;
        player.Dashed += OnDashed;

        ledgeGrab = player.GetComponent<LedgeGrab>();
        if (ledgeGrab != null)
        {
            ledgeGrab.MantleStarted += OnMantleStarted;
            ledgeGrab.LedgeGrabbed += OnLedgeGrabbed;
        }
        bound = true;
        return true;
    }

    private void OnMantleStarted(float height)
    {
        if (!enableKicks) return;
        // Look down slightly as you haul yourself over, with a little random lean.
        pitchSpring.Velocity += mantlePitchKick * Mathf.Clamp(0.5f + height * 0.4f, 0.5f, 1.3f);
        rollSpring.Velocity += (Random.value < 0.5f ? -1f : 1f) * mantleRollKick;
    }

    private void OnLedgeGrabbed()
    {
        if (!enableKicks) return;
        heightSpring.Velocity -= ledgeGrabJolt;
        pitchSpring.Velocity += mantlePitchKick * 0.6f;
    }

    private void OnLanded(float impactSpeed)
    {
        if (!enableKicks || impactSpeed < minLandingSpeed) return;
        heightSpring.Velocity -= Mathf.Min(impactSpeed * landingDip, maxLandingDip);
        pitchSpring.Velocity += Mathf.Min(impactSpeed * landingPitch, maxLandingPitch);
    }

    private void OnJumped(bool doubleJump)
    {
        if (!enableKicks) return;
        pitchSpring.Velocity += doubleJump ? doubleJumpPitchKick : jumpPitchKick;
    }

    private void OnDashed(Vector3 worldDirection)
    {
        if (!enableKicks) return;
        Vector3 local = Quaternion.Inverse(player.YawRotation) * worldDirection;
        rollSpring.Velocity -= local.x * dashRollKick;            // lean into side dashes
        pitchSpring.Velocity -= local.z * dashRollKick * 0.25f;   // slight head-back on forward dash
    }

    private void LateUpdate()
    {
        if (!TryBind()) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float speed = player.HorizontalSpeed;

        // ---- Head bob (phase comes from the player's stride, so it is synced with footsteps) ----
        bool moving = player.IsGrounded && !player.IsDashing && speed > 0.3f;
        float targetBlend = moving ? Mathf.Clamp01(speed / Mathf.Max(0.01f, player.WalkSpeed)) : 0f;
        float blendT = 1f - Mathf.Exp(-bobBlendSpeed * dt);
        bobBlend = Mathf.Lerp(bobBlend, targetBlend, blendT);
        sprintBlend = Mathf.Lerp(sprintBlend, Mathf.InverseLerp(player.WalkSpeed, player.SprintSpeed, speed), blendT);

        Vector3 offset = Vector3.zero;
        float pitch = 0f;
        float roll = 0f;

        if (enableHeadBob)
        {
            float phase = player.StridePhase;
            float weight = bobBlend * (player.IsCrouching ? crouchBobMultiplier : 1f)
                           * Mathf.Lerp(1f, aimBobMultiplier, aimAmount);
            Vector2 amp = Vector2.Lerp(walkBobAmplitude, sprintBobAmplitude, sprintBlend) * weight;

            float side = Mathf.Sin(phase);           // one full sway every two steps
            float bounce = -Mathf.Cos(2f * phase);   // lowest point exactly on each footstep

            offset.x += side * amp.x;
            offset.y += bounce * amp.y;
            roll += -side * bobRoll * weight;
            pitch += -bounce * bobPitch * weight;
        }

        // ---- Idle breathing ----
        if (enableBreathing)
        {
            breathTime += dt;
            float idle = 1f - bobBlend;
            float breath = Mathf.Sin(breathTime * breathFrequency * 2f * Mathf.PI);
            offset.y += breath * breathAmplitude * idle;
            pitch += breath * breathPitch * idle;
        }

        // ---- Strafe tilt ----
        float targetTilt = 0f;
        if (enableStrafeTilt)
        {
            float lateral = Mathf.Clamp(player.LocalVelocity.x / Mathf.Max(0.01f, player.SprintSpeed), -1f, 1f);
            targetTilt = -lateral * strafeTiltAngle;
        }
        tilt = Mathf.Lerp(tilt, targetTilt, 1f - Mathf.Exp(-strafeTiltSpeed * dt));
        roll += tilt;

        // ---- Springs (sub-stepped so they stay stable at low framerates) ----
        int steps = Mathf.CeilToInt(dt / (1f / 120f));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            heightSpring.Step(springStiffness, springDamping, h);
            pitchSpring.Step(springStiffness, springDamping, h);
            rollSpring.Step(springStiffness, springDamping, h);
            recoilPitch.Step(recoilStiffness, recoilDamping, h);
            recoilYaw.Step(recoilStiffness, recoilDamping, h);
            recoilRoll.Step(recoilStiffness, recoilDamping, h);
        }
        offset.y += heightSpring.Value;
        pitch += pitchSpring.Value + recoilPitch.Value;
        roll += rollSpring.Value + recoilRoll.Value;
        float yaw = recoilYaw.Value;

        // ---- Shake (smooth Perlin noise scaled by trauma²) ----
        Vector3 shakeOffset = Vector3.zero;
        if (trauma > 0f)
        {
            float s = trauma * trauma;
            float t = Time.time * shakeFrequency;
            pitch += (Mathf.PerlinNoise(shakeSeed, t) * 2f - 1f) * maxShakeAngle * s;
            yaw += (Mathf.PerlinNoise(shakeSeed + 1f, t) * 2f - 1f) * maxShakeAngle * s;
            roll += (Mathf.PerlinNoise(shakeSeed + 2f, t) * 2f - 1f) * maxShakeAngle * 0.5f * s;
            shakeOffset = new Vector3(Mathf.PerlinNoise(shakeSeed + 3f, t) * 2f - 1f,
                                      Mathf.PerlinNoise(shakeSeed + 4f, t) * 2f - 1f, 0f) * (maxShakeOffset * s);
            trauma = Mathf.Max(0f, trauma - shakeDecay * dt);
        }

        // ---- FOV (sprint/dash, then weapon aim on top) ----
        float targetFov = 0f;
        if (enableFovEffects)
        {
            if (player.IsSprinting) targetFov += sprintFovIncrease * sprintBlend;
            if (player.IsDashing) targetFov += dashFovIncrease;
        }
        float fovT = player.IsDashing ? dashFovSpeed : fovSpeed;
        fovOffset = Mathf.Lerp(fovOffset, targetFov, 1f - Mathf.Exp(-fovT * dt));
        float fov = baseFov + fovOffset;
        if (aimAmount > 0f && aimFov > 0f) fov = Mathf.Lerp(fov, aimFov, aimAmount);
        cam.fieldOfView = fov;

        // Offsets are defined in yaw space (so vertical bob stays vertical when looking up/down).
        Vector3 worldOffset = player.YawRotation * offset;
        Vector3 localOffset = transform.parent != null ? transform.parent.InverseTransformDirection(worldOffset) : worldOffset;

        Vector3 effectsPosition = baseLocalPosition + localOffset + shakeOffset;
        Quaternion effectsRotation = baseLocalRotation * Quaternion.Euler(pitch, yaw, roll);

        // ---- Weapon camera bone animation (layered last, in camera space) ----
        Vector3 animPosition = Vector3.zero;
        Quaternion animRotation = Quaternion.identity;
        if (CameraAnimationSource != null && CameraAnimationSource.TryGetCameraAnimation(out Vector3 p, out Quaternion r))
        {
            animPosition = p;
            animRotation = r;
        }
        AppliedAnimationPosition = animPosition;
        AppliedAnimationRotation = animRotation;

        transform.localPosition = effectsPosition + effectsRotation * animPosition;
        transform.localRotation = effectsRotation * animRotation;
    }
}
