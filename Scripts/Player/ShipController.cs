using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;

public class ShipController : MonoBehaviour
{
    [Header("Ship Parts")]
    public Rigidbody shipRigidbody;
    public Transform rightController;
    public Transform leftController;
    public RK4Orbit orbitScript;
    public XROrigin xrOrigin;

    [Header("Movement")]
    public float thrustPower = 25f;          // units/s² at full squeeze
    [SerializeField] private float brakingRate = 2f;  // fraction of velocity lost per second at full squeeze
    public float timeScaleStep = 10f;        // time warp multiplier toggled by B

    [Header("Controls")]
    public InputActionProperty rightTriggerAction;
    public InputActionProperty leftTriggerAction;
    public InputActionProperty bAction;
    public InputActionProperty aAction;

    [Header("Orbit Runtime Overrides (Play Mode)")]
    [Tooltip("When enabled, writes the values below to RK4Orbit once, then auto-disables.")]
    public bool applyOrbitOverride;
    public Vector3 overrideOrbitPosition;
    public Vector3 overrideOrbitVelocity;

    [Header("God View")]
    [Tooltip("Offset from the ship applied to xrOrigin when god view is toggled on with A.")]
    [SerializeField] public Vector3 god_view_pos;
    [Tooltip("Persistent base offset always added to the xrOrigin position.")]
    [SerializeField] public Vector3 camera_base_offset;

    const float k_TriggerThreshold = 0.1f;
    bool rightTriggerPressed;
    bool leftTriggerPressed;
    float baseTimeScale;
    bool _godViewActive;
    bool _timeWarpActive;

    void OnEnable()
    {
        if (orbitScript != null)
            baseTimeScale = orbitScript.timeScale;

        rightTriggerAction.action?.Enable();
        leftTriggerAction.action?.Enable();
        bAction.action?.Enable();
        aAction.action?.Enable();
    }

    void OnDisable()
    {
        rightTriggerAction.action?.Disable();
        leftTriggerAction.action?.Disable();
        bAction.action?.Disable();
        aAction.action?.Disable();
    }

    void Update()
    {
        float rightValue = rightTriggerAction.action != null ? rightTriggerAction.action.ReadValue<float>() : 0f;
        float leftValue  = leftTriggerAction.action  != null ? leftTriggerAction.action.ReadValue<float>()  : 0f;

        rightTriggerPressed = rightValue > k_TriggerThreshold;
        leftTriggerPressed  = leftValue  > k_TriggerThreshold;

        if (aAction.action != null && aAction.action.WasPressedThisFrame())
        {
            _godViewActive = !_godViewActive;
            if (orbitScript != null)
                orbitScript.godViewActive = _godViewActive;
        }

        // Toggle time warp on press rather than holding — checked in Update so
        // WasPressedThisFrame() can't be missed between FixedUpdate ticks
        if (bAction.action != null && bAction.action.WasPressedThisFrame())
        {
            _timeWarpActive = !_timeWarpActive;
            if (orbitScript != null)
                orbitScript.timeScale = _timeWarpActive ? baseTimeScale * timeScaleStep : baseTimeScale;
        }

        if (xrOrigin != null && orbitScript != null)
        {
            // Ship is always at world origin — camera just needs its own offsets.
            Vector3 offset = _godViewActive ? god_view_pos : Vector3.zero;
            xrOrigin.transform.position = offset + camera_base_offset;
        }
    }

    void FixedUpdate()
    {
        if (shipRigidbody == null || rightController == null || orbitScript == null)
            return;

        if (rightTriggerPressed && rightTriggerAction.action != null)
        {
            float squeeze = rightTriggerAction.action.ReadValue<float>();
            // thrustPower is units/s²; multiply by dt to get frame-rate-independent impulse
            orbitScript.currentVelocity += thrustPower * squeeze * Time.fixedDeltaTime * rightController.forward.normalized;
        }

        if (leftTriggerPressed && leftTriggerAction.action != null)
        {
            float squeeze = leftTriggerAction.action.ReadValue<float>();
            // Frame-rate-independent exponential velocity damping
            orbitScript.currentVelocity *= 1f - Mathf.Clamp01(brakingRate * squeeze * Time.fixedDeltaTime);
        }

        if (applyOrbitOverride)
        {
            orbitScript.currentPosition = overrideOrbitPosition;
            orbitScript.currentVelocity = overrideOrbitVelocity;
            applyOrbitOverride = false;
        }
    }
}
