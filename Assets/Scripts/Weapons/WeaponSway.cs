using System;
using UnityEngine;

/// <summary>
/// Procedural first-person weapon motion: look sway (the weapon lags behind the view),
/// movement offset and tilt, footstep-synced weapon bob, and a spring-based recoil kick.
/// </summary>
[Serializable]
public class WeaponSway
{
    [Header("Look Sway")]
    [Tooltip("Degrees of sway per degree/second of look speed.")]
    public float lookSwayAmount = 0.012f;
    public float maxLookSway = 5f;
    [Tooltip("Roll added when turning (fraction of yaw sway).")]
    public float lookSwayRoll = 0.8f;
    public float lookSwaySmoothing = 9f;

    [Header("Move Sway")]
    [Tooltip("Meters of offset per m/s of movement (weapon lags opposite to movement).")]
    public float moveOffsetAmount = 0.0025f;
    public float maxMoveOffset = 0.03f;
    [Tooltip("Meters of vertical lag per m/s of vertical speed (jumping/falling).")]
    public float verticalOffsetAmount = 0.003f;
    public float maxVerticalOffset = 0.05f;
    [Tooltip("Roll (degrees) when strafing at sprint speed.")]
    public float strafeTilt = 4f;
    public float moveSmoothing = 8f;

    [Header("Weapon Bob")]
    [Tooltip("x = side, y = vertical (meters), synced with footsteps.")]
    public Vector2 bobAmount = new Vector2(0.01f, 0.008f);
    public float bobRoll = 1.2f;
    public float sprintBobMultiplier = 1.6f;

    [Header("Recoil Kick")]
    public float kickStiffness = 260f;
    public float kickDamping = 22f;

    private Vector3 lookSway;
    private Vector3 moveOffset;
    private float moveTilt;
    private float bobWeight;

    private float kickBack, kickBackVelocity;
    private float kickPitch, kickPitchVelocity;

    public struct Context
    {
        public float DeltaTime;
        public Vector2 LookDelta;        // degrees this frame (x yaw, y pitch)
        public Vector3 LocalVelocity;    // player velocity in view-yaw space
        public float WalkSpeed;
        public float SprintSpeed;
        public float StridePhase;
        public bool Grounded;
        public float SwayMultiplier;
        public float BobMultiplier;
    }

    /// <summary>Recoil kick for the view model (meters back, degrees up).</summary>
    public void AddKick(float back, float pitchUp)
    {
        // Impulses: the springs turn them into a snappy kick that settles back.
        kickBackVelocity -= back * kickStiffness * 0.08f;
        kickPitchVelocity -= pitchUp * kickStiffness * 0.08f;
    }

    public void Reset()
    {
        lookSway = moveOffset = Vector3.zero;
        moveTilt = bobWeight = 0f;
        kickBack = kickBackVelocity = kickPitch = kickPitchVelocity = 0f;
    }

    public void Evaluate(in Context c, out Vector3 position, out Quaternion rotation)
    {
        float dt = Mathf.Max(c.DeltaTime, 1e-5f);

        // ---- Look sway (from look speed, so it's framerate independent) ----
        Vector2 lookSpeed = c.LookDelta / dt;
        float yaw = Mathf.Clamp(-lookSpeed.x * lookSwayAmount, -maxLookSway, maxLookSway);
        float pitch = Mathf.Clamp(-lookSpeed.y * lookSwayAmount, -maxLookSway, maxLookSway);
        Vector3 lookTarget = new Vector3(pitch, yaw, yaw * lookSwayRoll) * c.SwayMultiplier;
        lookSway = Vector3.Lerp(lookSway, lookTarget, 1f - Mathf.Exp(-lookSwaySmoothing * dt));

        // ---- Movement offset / tilt ----
        Vector3 v = c.LocalVelocity;
        Vector3 moveTarget = new Vector3(
            Mathf.Clamp(-v.x * moveOffsetAmount, -maxMoveOffset, maxMoveOffset),
            Mathf.Clamp(-v.y * verticalOffsetAmount, -maxVerticalOffset, maxVerticalOffset),
            Mathf.Clamp(-v.z * moveOffsetAmount * 0.5f, -maxMoveOffset, maxMoveOffset)) * c.SwayMultiplier;
        float tiltTarget = -Mathf.Clamp(v.x / Mathf.Max(0.01f, c.SprintSpeed), -1f, 1f) * strafeTilt * c.SwayMultiplier;
        float moveT = 1f - Mathf.Exp(-moveSmoothing * dt);
        moveOffset = Vector3.Lerp(moveOffset, moveTarget, moveT);
        moveTilt = Mathf.Lerp(moveTilt, tiltTarget, moveT);

        // ---- Bob (shares the player's stride phase, so it matches footsteps and head bob) ----
        float horizontalSpeed = new Vector2(v.x, v.z).magnitude;
        float bobTarget = c.Grounded ? Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, c.WalkSpeed)) : 0f;
        bobWeight = Mathf.Lerp(bobWeight, bobTarget, 1f - Mathf.Exp(-10f * dt));
        float sprintBlend = Mathf.InverseLerp(c.WalkSpeed, c.SprintSpeed, horizontalSpeed);
        float bob = bobWeight * c.BobMultiplier * Mathf.Lerp(1f, sprintBobMultiplier, sprintBlend);
        float side = Mathf.Sin(c.StridePhase);
        float bounce = -Mathf.Cos(2f * c.StridePhase);
        Vector3 bobOffset = new Vector3(side * bobAmount.x, bounce * bobAmount.y, 0f) * bob;
        float bobRollAngle = side * bobRoll * bob;

        // ---- Recoil kick springs (sub-stepped for stability) ----
        int steps = Mathf.CeilToInt(dt / (1f / 240f));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            kickBackVelocity += (-kickStiffness * kickBack - kickDamping * kickBackVelocity) * h;
            kickBack += kickBackVelocity * h;
            kickPitchVelocity += (-kickStiffness * kickPitch - kickDamping * kickPitchVelocity) * h;
            kickPitch += kickPitchVelocity * h;
        }

        position = moveOffset + bobOffset + new Vector3(0f, 0f, kickBack);
        rotation = Quaternion.Euler(lookSway.x + kickPitch, lookSway.y, lookSway.z + moveTilt + bobRollAngle);
    }
}
