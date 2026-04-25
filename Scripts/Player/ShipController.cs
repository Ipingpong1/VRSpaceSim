using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;

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
    public InputActionProperty leftStickAction;
    public InputActionProperty rightStickAction;

    [Header("Orbit Runtime Overrides (Play Mode)")]
    [Tooltip("When enabled, writes the values below to RK4Orbit once, then auto-disables.")]
    public bool applyOrbitOverride;
    public Vector3 overrideOrbitPosition;
    public Vector3 overrideOrbitVelocity;

    [Header("Camera")]
    [Tooltip("Main camera near clip. Higher = better depth precision for distant planets. 0.5 is a good balance.")]
    [SerializeField] private float cameraNearClip = 0.5f;
    [Tooltip("Far clip plane in Unity units. Must exceed the furthest planet.")]
    [SerializeField] private float cameraFarClip = 1_500_000f;
    [Tooltip("Near clip for the ship overlay camera — can be tiny since it has its own depth buffer.")]
    [SerializeField] private float shipOverlayNearClip = 0.01f;
    [Tooltip("Far clip for the ship overlay camera. Only needs to cover the ship model and hands.")]
    [SerializeField] private float shipOverlayFarClip = 100f;

    [Header("God View")]
    [Tooltip("Offset from the ship applied to xrOrigin when god view is toggled on with A.")]
    [SerializeField] public Vector3 god_view_pos;
    [Tooltip("Persistent base offset always added to the xrOrigin position.")]
    [SerializeField] public Vector3 camera_base_offset;
    [Tooltip("Pan/zoom speed when navigating in god view (Unity units/s).")]
    [SerializeField] private float godViewMoveSpeed = 0.5f;
    [Tooltip("Acceleration for both pan movement and camera rotation (units/s² and deg/s²).")]
    [SerializeField] private float godViewLocomotionAcceleration = 2f;
    [Tooltip("Maximum pan velocity in any direction (units/s).")]
    [SerializeField] private float godViewMaxPanVelocity = 0.5f;
    [Tooltip("Maximum yaw/pitch rotation rate (degrees/s).")]
    [SerializeField] private float godViewMaxRotationVelocity = 45f;

    const float k_TriggerThreshold = 0.1f;
    bool rightTriggerPressed;
    bool leftTriggerPressed;
    float baseTimeScale;
    bool _godViewActive;
    bool _timeWarpActive;

    private Vector3 _currentPanVelocity = Vector3.zero;
    private float _currentYawVelocity = 0f;

    void Start()
    {
        SetupCameras();
    }

    void OnEnable()
    {
        if (orbitScript != null)
            baseTimeScale = orbitScript.timeScale;

        if (xrOrigin != null)
        {
            Camera cam = xrOrigin.Camera;
            if (cam != null)
            {
                cam.nearClipPlane = cameraNearClip;
                cam.farClipPlane = cameraFarClip;
            }

            // Disable template-supplied locomotion providers so they don't fight
            // ShipController for stick input (template prefab adds snap-turn + continuous-move)
            foreach (var lp in xrOrigin.GetComponentsInChildren<LocomotionProvider>(true))
                lp.enabled = false;
        }

        rightTriggerAction.action?.Enable();
        leftTriggerAction.action?.Enable();
        bAction.action?.Enable();
        aAction.action?.Enable();
        leftStickAction.action?.Enable();
        rightStickAction.action?.Enable();
    }

    void OnDisable()
    {
        rightTriggerAction.action?.Disable();
        leftTriggerAction.action?.Disable();
        bAction.action?.Disable();
        aAction.action?.Disable();
        leftStickAction.action?.Disable();
        rightStickAction.action?.Disable();
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

        if (_godViewActive && xrOrigin != null)
        {
            Vector2 ls = leftStickAction.action  != null ? leftStickAction.action.ReadValue<Vector2>()  : Vector2.zero;
            Vector2 rs = rightStickAction.action != null ? rightStickAction.action.ReadValue<Vector2>() : Vector2.zero;

            Camera hmd = xrOrigin.Camera;
            Vector3 camForward = hmd != null
                ? Vector3.ProjectOnPlane(hmd.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 camRight = hmd != null
                ? Vector3.ProjectOnPlane(hmd.transform.right, Vector3.up).normalized
                : Vector3.right;

            float dt = Time.deltaTime;
            float accelT = Mathf.Clamp01(godViewLocomotionAcceleration * dt);

            // Left stick — pan in orbit plane, plus right stick Y for vertical pan
            Vector3 targetPanVelocity = (camRight * ls.x + camForward * ls.y + Vector3.up * rs.y) * godViewMoveSpeed;
            _currentPanVelocity = Vector3.Lerp(_currentPanVelocity, targetPanVelocity, accelT);
            _currentPanVelocity = Vector3.ClampMagnitude(_currentPanVelocity, godViewMaxPanVelocity);
            god_view_pos += _currentPanVelocity * dt;

            // Right stick X — yaw rotation around world Y axis
            float targetYaw = rs.x * godViewMaxRotationVelocity;
            _currentYawVelocity = Mathf.Lerp(_currentYawVelocity, targetYaw, accelT);
            xrOrigin.transform.Rotate(0f, _currentYawVelocity * dt, 0f, Space.World);
        }
        else
        {
            // Decay velocities while god view is off so they don't carry over on re-entry
            _currentPanVelocity = Vector3.zero;
            _currentYawVelocity = 0f;
        }

        if (xrOrigin != null && orbitScript != null)
        {
            Vector3 offset = _godViewActive ? god_view_pos : Vector3.zero;
            xrOrigin.transform.position = camera_base_offset + offset;
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

    void SetupCameras()
    {
        if (xrOrigin == null) return;
        Camera baseCam = xrOrigin.Camera;
        if (baseCam == null) return;

        int shipLayer = LayerMask.NameToLayer("Ship");
        if (shipLayer < 0)
        {
            Debug.LogWarning("ShipController: 'Ship' layer not found in TagManager. Add it via Edit > Project Settings > Tags & Layers.");
            return;
        }

        // Put ship visual, body, and hands on the Ship layer so the overlay camera owns them
        if (orbitScript != null && orbitScript.ship != null)
            AssignLayer(orbitScript.ship.transform, shipLayer);
        AssignLayer(shipRigidbody != null ? shipRigidbody.transform : null, shipLayer);
        AssignLayer(rightController, shipLayer);
        AssignLayer(leftController, shipLayer);

        // Exclude Ship layer from the main camera — the overlay handles it exclusively
        baseCam.cullingMask &= ~(1 << shipLayer);

        // Create the ship overlay camera as a sibling of the main XR camera
        // so it tracks the HMD identically
        GameObject overlayObj = new GameObject("ShipOverlayCamera");
        overlayObj.transform.SetParent(baseCam.transform.parent);
        overlayObj.transform.localPosition = Vector3.zero;
        overlayObj.transform.localRotation = Quaternion.identity;
        overlayObj.transform.localScale = Vector3.one;

        Camera overlayCam = overlayObj.AddComponent<Camera>();
        overlayCam.nearClipPlane = shipOverlayNearClip;
        overlayCam.farClipPlane = shipOverlayFarClip;
        overlayCam.cullingMask = 1 << shipLayer;
        overlayCam.stereoTargetEye = StereoTargetEyeMask.Both;

        // Configure as URP overlay with depth clear so the ship always
        // renders in front using its own fresh depth buffer
        var overlayData = overlayObj.AddComponent<UniversalAdditionalCameraData>();
        overlayData.renderType = CameraRenderType.Overlay;

        // Register overlay in the base camera's stack
        var baseCamData = baseCam.GetComponent<UniversalAdditionalCameraData>();
        if (baseCamData == null)
            baseCamData = baseCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        baseCamData.cameraStack.Add(overlayCam);
    }

    void AssignLayer(Transform root, int layer)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            r.gameObject.layer = layer;
    }
}
