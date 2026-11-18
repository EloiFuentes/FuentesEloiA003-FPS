using UnityEngine;
using UnityEngine.InputSystem;

// Runs before PlayerController so movement/look always uses this frame's input.
[DefaultExecutionOrder(-100)]
public class GameInputs : MonoBehaviour
{
    // Actions
    private InputAction xAxis;
    private InputAction yAxis;
    private InputAction camRotX;
    private InputAction camRotY;
    [HideInInspector] public InputAction jump;
    [HideInInspector] public InputAction crouch;
    [HideInInspector] public InputAction run;
    [HideInInspector] public InputAction interact;
    [HideInInspector] public InputAction dash; // Optional: null if the asset has no "Dash" action

    // Weapons (all optional: null if the action is missing from the asset)
    [HideInInspector] public InputAction fire;
    [HideInInspector] public InputAction aim;
    [HideInInspector] public InputAction reload;
    [HideInInspector] public InputAction grenade;
    [HideInInspector] public InputAction primaryWeapon;
    [HideInInspector] public InputAction secondaryWeapon;
    [HideInInspector] public InputAction switchWeapon;

    /// <summary>Look rotation applied this frame, in degrees (x = yaw, y = pitch). Used by weapon sway.</summary>
    public Vector2 LookDelta { get; private set; }

    // Rotation (kept for compatibility: xRotation = yaw, yRotation = pitch)
    [HideInInspector] public float xRotation;
    [HideInInspector] public float yRotation;

    // Input Values
    [HideInInspector] public float xAxisValue;
    [HideInInspector] public float yAxisValue;
    [HideInInspector] public float camRotXValue;
    [HideInInspector] public float camRotYValue;

    [Header("Look")]
    [Tooltip("Degrees of rotation per mouse count. Mouse delta is already per-frame, so this is framerate independent.")]
    public float mouseSensitivity = 0.12f;
    public bool invertY;
    [SerializeField] private float minPitch = -89f;
    [SerializeField] private float maxPitch = 89f;

    public float Yaw => xRotation;
    public float Pitch => yRotation;

    void Start()
    {
        InputActionAsset actions = GetComponent<PlayerInput>().actions;
        xAxis = actions.FindAction("xAxis");
        yAxis = actions.FindAction("yAxis");
        camRotX = actions.FindAction("CamRotX");
        camRotY = actions.FindAction("CamRotY");
        jump = actions.FindAction("Jump");
        crouch = actions.FindAction("Crouch");
        run = actions.FindAction("Run");
        interact = actions.FindAction("Interact");
        dash = actions.FindAction("Dash");

        fire = actions.FindAction("Fire");
        aim = actions.FindAction("Aim");
        reload = actions.FindAction("Reload");
        grenade = actions.FindAction("Grenade");
        primaryWeapon = actions.FindAction("PrimaryWeapon");
        secondaryWeapon = actions.FindAction("SecondaryWeapon");
        switchWeapon = actions.FindAction("SwitchWeapon");
    }

    /// <summary>Adds rotation to the view (e.g. weapon recoil). Positive pitch looks down.</summary>
    public void AddLookRotation(float yawDegrees, float pitchDegrees)
    {
        xRotation = Mathf.Repeat(xRotation + yawDegrees, 360f);
        yRotation = Mathf.Clamp(yRotation + pitchDegrees, minPitch, maxPitch);
    }

    void Update()
    {
        if (xAxis == null) return;

        xAxisValue = xAxis.ReadValue<float>();
        yAxisValue = yAxis.ReadValue<float>();
        camRotXValue = camRotX.ReadValue<float>();
        camRotYValue = camRotY.ReadValue<float>();

        // Raw, unsmoothed look: no deltaTime (mouse delta is already a per-frame amount).
        LookDelta = new Vector2(camRotXValue * mouseSensitivity, camRotYValue * mouseSensitivity * (invertY ? -1f : 1f));
        xRotation = Mathf.Repeat(xRotation + LookDelta.x, 360f);
        yRotation = Mathf.Clamp(yRotation + LookDelta.y, minPitch, maxPitch);
    }
}
