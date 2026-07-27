using System.Collections;
using System.Collections.Generic;
using BriefcaseProtocol.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class FirstPersonCharacterController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4.5f;
    [SerializeField, Min(0f)] private float sprintSpeed = 7.5f;
    [SerializeField, Min(0f)] private float crouchSpeed = 2.25f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -25f;

    [Header("Stance")]
    [SerializeField, Min(0.1f)] private float standingHeight = 1.8f;
    [SerializeField, Min(0.1f)] private float crouchingHeight = 1.1f;
    [SerializeField, Min(0f)] private float standingEyeHeight = 1.65f;
    [SerializeField, Min(0f)] private float crouchingEyeHeight = 0.95f;
    [SerializeField, Min(0.1f)] private float stanceChangeSpeed = 5f;

    [Header("Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.08f;
    [SerializeField, Range(1f, 89f)] private float verticalLookLimit = 85f;

    [Header("Head Bob")]
    [SerializeField, Min(0f)] private float walkBobAmplitude = 0.035f;
    [SerializeField, Min(0f)] private float walkBobFrequency = 8.5f;
    [SerializeField, Min(0f)] private float sprintBobAmplitude = 0.06f;
    [SerializeField, Min(0f)] private float sprintBobFrequency = 12f;
    [SerializeField, Min(0f)] private float crouchBobAmplitude = 0.02f;
    [SerializeField, Min(0f)] private float crouchBobFrequency = 6f;
    [SerializeField, Min(0f)] private float bobSmoothing = 12f;

    [Header("Camera Mode")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 0.35f, -3.2f);
    [SerializeField, Min(0f)] private float cameraModeTransitionSpeed = 10f;
    [SerializeField, Min(0f)] private float thirdPersonCollisionRadius = 0.2f;
    [SerializeField, Min(0f)] private float cameraCollisionPadding = 0.1f;

    [Header("Interaction")]
    [SerializeField, Min(0.1f)] private float interactionDistance = 3f;
    [SerializeField] private LayerMask interactionLayers = ~0;

    [Header("Crosshair")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField, Min(4f)] private float crosshairSize = 18f;
    [SerializeField, Min(1f)] private float crosshairThickness = 2f;
    [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Animation")]
    [Tooltip("Place the imported character prefab under this transform.")]
    [SerializeField] private Transform characterModelRoot;
    [Tooltip("Optional. If left empty, the first Animator under CharacterModelRoot is used.")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private bool showModelInFirstPerson;
    [SerializeField] private bool hidePlaceholderCapsuleWhenModelAssigned = true;
    [SerializeField, Min(0f)] private float animationDampTime = 0.1f;

    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform capsuleVisual;

    private readonly Collider[] headroomHits = new Collider[16];
    private readonly RaycastHit[] cameraCollisionHits = new RaycastHit[16];
    private readonly RaycastHit[] interactionHits = new RaycastHit[16];

    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int MoveXParameter = Animator.StringToHash("MoveX");
    private static readonly int MoveYParameter = Animator.StringToHash("MoveY");
    private static readonly int VerticalSpeedParameter = Animator.StringToHash("VerticalSpeed");
    private static readonly int GroundedParameter = Animator.StringToHash("IsGrounded");
    private static readonly int CrouchingParameter = Animator.StringToHash("IsCrouching");
    private static readonly int SprintingParameter = Animator.StringToHash("IsSprinting");
    private static readonly int ThirdPersonParameter = Animator.StringToHash("IsThirdPerson");
    private static readonly int JumpParameter = Animator.StringToHash("Jump");

    private CharacterController characterController;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction crouchAction;
    private InputAction sprintAction;
    private InputAction cameraModeAction;
    private InputAction interactAction;
    private InputAction inspectAction;
    private InputAction inspectRotateAction;
    private InputAction inspectZoomAction;
    private IPlayerInspectable activeInspectable;
    private IInspectionPointerInteractable activeInspectionPointerInteraction;
    private Renderer[] modelRenderers = System.Array.Empty<Renderer>();
    private bool[] modelRendererDefaultStates = System.Array.Empty<bool>();
    private RuntimeAnimatorController cachedAnimatorController;
    private Texture2D crosshairTexture;
    private Camera playerCamera;
    private AudioListener playerAudioListener;
    private Coroutine spawnRoutine;
    private Vector3 cameraNeutralLocalPosition;
    private Vector3 currentBobOffset;
    private float verticalVelocity;
    private float pitch;
    private float currentEyeHeight;
    private float bobPhase;
    private bool isCrouching;
    private bool isSprinting;
    private bool isThirdPerson;
    private bool modelVisibilityInitialized;
    private bool modelIsVisible;
    private bool hasSpeedParameter;
    private bool hasMoveXParameter;
    private bool hasMoveYParameter;
    private bool hasVerticalSpeedParameter;
    private bool hasGroundedParameter;
    private bool hasCrouchingParameter;
    private bool hasSprintingParameter;
    private bool hasThirdPersonParameter;
    private bool hasJumpParameter;
    private bool hasLocalControl;
    private bool spawnReady = true;
    private bool sceneLoadSubscribed;

    public bool IsCrouching => isCrouching;
    public bool IsThirdPerson => isThirdPerson;
    public float StandingEyeHeight => standingEyeHeight;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraPivot == null)
        {
            cameraPivot = transform.Find("CameraPivot");
        }

        if (capsuleVisual == null)
        {
            capsuleVisual = transform.Find("Capsule");
        }

        if (cameraTransform == null && cameraPivot != null)
        {
            Camera childCamera = cameraPivot.GetComponentInChildren<Camera>(true);
            cameraTransform = childCamera != null ? childCamera.transform : null;
        }

        if (cameraTransform != null)
        {
            playerCamera = cameraTransform.GetComponent<Camera>();
            playerAudioListener = cameraTransform.GetComponent<AudioListener>();
        }

        ConfigureAnimationModel();

        if (cameraPivot != null)
        {
            cameraNeutralLocalPosition = cameraPivot.localPosition;
        }

        ConfigureCharacterController();
        CreateInputActions();
        CreateCrosshairTexture();
        ApplyStanceInstantly(false);
        RefreshLocalControlState();
    }

    private void OnEnable()
    {
        RefreshLocalControlState();
    }

    private void Start()
    {
        if (hasLocalControl)
        {
            LockCursor();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            spawnReady = false;
        }

        RefreshLocalControlState();

        if (!IsOwner)
        {
            return;
        }

        SubscribeToSceneLoads();
        ScheduleSpawn(SceneManager.GetActiveScene());
    }

    public override void OnNetworkDespawn()
    {
        CancelScheduledSpawn();
        UnsubscribeFromSceneLoads();
        ApplyLocalControlState(false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasLocalControl && hasFocus && isActiveAndEnabled && Application.isPlaying)
        {
            if (activeInspectable != null)
            {
                ShowInspectionCursor();
            }
            else
            {
                LockCursor();
            }
        }
    }

    private void Update()
    {
        if (!hasLocalControl || !spawnReady)
        {
            return;
        }

        ValidateActiveInspectionState();
        HandleCursorState();
        UpdateCameraModeInput();
        UpdateInspectionZoom();
        UpdateInspectionRotation();
        UpdateLook();
        UpdateStance();
        UpdateMovement();
        UpdateHeadBob();
        UpdateCameraModePosition();
        UpdateInteraction();
        UpdateAnimation();
    }

    private void OnDisable()
    {
        SetInputActionsEnabled(false);

        if (hasLocalControl && Application.isPlaying)
        {
            if (activeInspectionPointerInteraction != null)
            {
                activeInspectionPointerInteraction.ExitInspectionInteraction();
                activeInspectionPointerInteraction = null;
            }

            if (activeInspectable != null)
            {
                IPlayerInspectable inspectable = activeInspectable;
                activeInspectable = null;
                inspectable.ToggleInspection(cameraTransform);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public override void OnDestroy()
    {
        CancelScheduledSpawn();
        UnsubscribeFromSceneLoads();

        moveAction?.Dispose();
        lookAction?.Dispose();
        jumpAction?.Dispose();
        crouchAction?.Dispose();
        sprintAction?.Dispose();
        cameraModeAction?.Dispose();
        interactAction?.Dispose();
        inspectAction?.Dispose();
        inspectRotateAction?.Dispose();
        inspectZoomAction?.Dispose();

        if (crosshairTexture != null)
        {
            Destroy(crosshairTexture);
        }

        base.OnDestroy();
    }

    public void SetPrefabReferences(
        Transform newCameraPivot,
        Transform newCapsuleVisual,
        Transform newCharacterModelRoot)
    {
        cameraPivot = newCameraPivot;
        capsuleVisual = newCapsuleVisual;
        characterModelRoot = newCharacterModelRoot;
    }

    private void ConfigureCharacterController()
    {
        characterController.height = standingHeight;
        characterController.center = Vector3.up * (standingHeight * 0.5f);
        characterController.radius = Mathf.Min(characterController.radius, standingHeight * 0.5f);
    }

    private void CreateInputActions()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
        jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");

        crouchAction = new InputAction("Crouch", InputActionType.Button);
        crouchAction.AddBinding("<Keyboard>/leftCtrl");
        crouchAction.AddBinding("<Keyboard>/rightCtrl");

        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Keyboard>/rightShift");

        cameraModeAction = new InputAction("Camera Mode", InputActionType.Button, "<Keyboard>/t");
        interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        inspectAction = new InputAction("Inspect", InputActionType.Button, "<Mouse>/leftButton");
        inspectRotateAction = new InputAction(
            "Rotate Inspection",
            InputActionType.Button,
            "<Mouse>/rightButton");
        inspectZoomAction = new InputAction(
            "Zoom Inspection",
            InputActionType.Value,
            "<Mouse>/scroll/y");
    }

    private void UpdateInspectionZoom()
    {
        if (activeInspectable == null ||
            activeInspectionPointerInteraction != null ||
            inspectZoomAction == null)
        {
            return;
        }

        activeInspectable.ZoomInspection(inspectZoomAction.ReadValue<float>());
    }

    private void UpdateInspectionRotation()
    {
        if (!IsRotatingInspectedObject())
        {
            return;
        }

        activeInspectable.RotateInspection(lookAction.ReadValue<Vector2>());
    }

    private bool IsRotatingInspectedObject()
    {
        return activeInspectable != null &&
            activeInspectionPointerInteraction == null &&
            inspectRotateAction != null &&
            inspectRotateAction.IsPressed();
    }

    private void UpdateInteraction()
    {
        bool interactPressed = interactAction != null && interactAction.WasPressedThisFrame();
        bool inspectPressed = inspectAction != null && inspectAction.WasPressedThisFrame();

        if (cameraTransform == null || (!interactPressed && !inspectPressed))
        {
            return;
        }

        if (inspectPressed && activeInspectable != null)
        {
            if (activeInspectionPointerInteraction == null)
            {
                TryBeginInspectionPointerInteraction();
            }
            else
            {
                TryUseInspectionPointerInteraction();
            }

            return;
        }

        int hitCount = Physics.RaycastNonAlloc(
            cameraTransform.position,
            cameraTransform.forward,
            interactionHits,
            interactionDistance,
            interactionLayers,
            QueryTriggerInteraction.Collide);

        RaycastHit closestHit = default;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = interactionHits[i];
            if (hit.collider == null || hit.collider == characterController ||
                hit.collider.transform.IsChildOf(transform) || hit.distance >= closestDistance)
            {
                continue;
            }

            closestHit = hit;
            closestDistance = hit.distance;
        }

        if (closestHit.collider == null)
        {
            return;
        }

        MonoBehaviour[] behaviours =
            closestHit.collider.GetComponentsInParent<MonoBehaviour>(true);

        if (inspectPressed)
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPlayerInspectable inspectable)
                {
                    inspectable.ToggleInspection(cameraTransform);
                    activeInspectable = inspectable;
                    ShowInspectionCursor();
                    return;
                }
            }
        }

        if (!interactPressed)
        {
            return;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerInteractable interactable)
            {
                interactable.Interact();
                return;
            }
        }
    }

    private bool TryBeginInspectionPointerInteraction()
    {
        Camera playerCamera = cameraTransform != null
            ? cameraTransform.GetComponent<Camera>()
            : null;
        if (playerCamera == null || Mouse.current == null)
        {
            return false;
        }

        Vector2 pointerPosition = Mouse.current.position.ReadValue();
        Ray pointerRay = playerCamera.ScreenPointToRay(pointerPosition);
        int hitCount = Physics.RaycastNonAlloc(
            pointerRay,
            interactionHits,
            interactionDistance,
            interactionLayers,
            QueryTriggerInteraction.Collide);

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            Collider hitCollider = interactionHits[hitIndex].collider;
            if (hitCollider == null || hitCollider == characterController ||
                hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            MonoBehaviour[] behaviours =
                hitCollider.GetComponentsInParent<MonoBehaviour>(true);
            for (int behaviourIndex = 0; behaviourIndex < behaviours.Length; behaviourIndex++)
            {
                if (behaviours[behaviourIndex] is IInspectionPointerInteractable pointerTarget &&
                    pointerTarget.TryBeginInspectionInteraction(hitCollider, cameraTransform))
                {
                    activeInspectionPointerInteraction = pointerTarget;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryUseInspectionPointerInteraction()
    {
        Camera playerCamera = cameraTransform != null
            ? cameraTransform.GetComponent<Camera>()
            : null;
        if (playerCamera == null || Mouse.current == null ||
            activeInspectionPointerInteraction == null)
        {
            return false;
        }

        Ray pointerRay = playerCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        int hitCount = Physics.RaycastNonAlloc(
            pointerRay,
            interactionHits,
            interactionDistance,
            interactionLayers,
            QueryTriggerInteraction.Collide);

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            Collider hitCollider = interactionHits[hitIndex].collider;
            if (hitCollider == null || hitCollider == characterController ||
                hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (activeInspectionPointerInteraction.TryUseInspectionInteraction(hitCollider))
            {
                return true;
            }
        }

        return false;
    }

    private void OnGUI()
    {
        if (!hasLocalControl || !showCrosshair || crosshairTexture == null ||
            activeInspectable != null)
        {
            return;
        }

        float size = Mathf.Max(4f, crosshairSize);
        Rect crosshairRect = new Rect(
            (Screen.width - size) * 0.5f,
            (Screen.height - size) * 0.5f,
            size,
            size);

        Color previousColor = GUI.color;
        GUI.color = crosshairColor;
        GUI.DrawTexture(crosshairRect, crosshairTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;
    }

    private void CreateCrosshairTexture()
    {
        const int resolution = 64;
        crosshairTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false)
        {
            name = "Runtime Crosshair",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        float center = (resolution - 1) * 0.5f;
        float ringRadius = resolution * 0.38f;
        float scaledThickness = Mathf.Max(
            1f,
            crosshairThickness / Mathf.Max(4f, crosshairSize) * resolution);
        float halfThickness = scaledThickness * 0.5f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    new Vector2(center, center));
                float alpha = Mathf.Clamp01(
                    halfThickness + 1f - Mathf.Abs(distance - ringRadius));
                crosshairTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        crosshairTexture.Apply(false, true);
    }

    private void UpdateMovement()
    {
        bool movementEnabled = activeInspectable == null;
        bool isGrounded = characterController.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (movementEnabled && isGrounded && jumpAction.WasPressedThisFrame())
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            TriggerJumpAnimation();
        }

        Vector2 input = movementEnabled
            ? moveAction.ReadValue<Vector2>()
            : Vector2.zero;
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        isSprinting = movementEnabled && !isCrouching && sprintAction.IsPressed();
        float speed = isCrouching ? crouchSpeed : isSprinting ? sprintSpeed : walkSpeed;
        Vector3 horizontalVelocity = (transform.right * input.x + transform.forward * input.y) * speed;

        verticalVelocity += gravity * Time.deltaTime;
        horizontalVelocity.y = verticalVelocity;
        characterController.Move(horizontalVelocity * Time.deltaTime);
    }

    private void UpdateLook()
    {
        if (cameraPivot == null || Cursor.lockState != CursorLockMode.Locked ||
            activeInspectable != null)
        {
            return;
        }

        Vector2 lookDelta = lookAction.ReadValue<Vector2>() * mouseSensitivity;
        transform.Rotate(Vector3.up, lookDelta.x, Space.Self);

        pitch = Mathf.Clamp(pitch - lookDelta.y, -verticalLookLimit, verticalLookLimit);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void UpdateStance()
    {
        if (activeInspectable != null)
        {
            return;
        }

        bool wantsToCrouch = crouchAction.IsPressed();
        if (!wantsToCrouch && isCrouching && !HasStandingHeadroom())
        {
            wantsToCrouch = true;
        }

        isCrouching = wantsToCrouch;
        float targetHeight = isCrouching ? crouchingHeight : standingHeight;
        float targetEyeHeight = isCrouching ? crouchingEyeHeight : standingEyeHeight;
        float height = Mathf.MoveTowards(
            characterController.height,
            targetHeight,
            stanceChangeSpeed * Time.deltaTime);

        characterController.height = height;
        characterController.center = Vector3.up * (height * 0.5f);

        currentEyeHeight = Mathf.MoveTowards(
            currentEyeHeight,
            targetEyeHeight,
            stanceChangeSpeed * Time.deltaTime);

        UpdateCapsuleVisual(height);
    }

    private void UpdateHeadBob()
    {
        if (cameraPivot == null)
        {
            return;
        }

        Vector3 planarVelocity = characterController.velocity;
        planarVelocity.y = 0f;
        bool shouldBob = activeInspectable == null &&
            !isThirdPerson &&
            characterController.isGrounded &&
            planarVelocity.sqrMagnitude > 0.04f;
        Vector3 targetOffset = Vector3.zero;

        if (shouldBob)
        {
            float amplitude;
            float frequency;

            if (isCrouching)
            {
                amplitude = crouchBobAmplitude;
                frequency = crouchBobFrequency;
            }
            else if (isSprinting)
            {
                amplitude = sprintBobAmplitude;
                frequency = sprintBobFrequency;
            }
            else
            {
                amplitude = walkBobAmplitude;
                frequency = walkBobFrequency;
            }

            bobPhase += Time.deltaTime * frequency;
            targetOffset = new Vector3(
                Mathf.Sin(bobPhase * 0.5f) * amplitude * 0.5f,
                Mathf.Sin(bobPhase) * amplitude,
                0f);
        }
        else
        {
            bobPhase = 0f;
        }

        float smoothing = 1f - Mathf.Exp(-bobSmoothing * Time.deltaTime);
        currentBobOffset = Vector3.Lerp(currentBobOffset, targetOffset, smoothing);
        ApplyCameraPosition();
    }

    private void UpdateCameraModeInput()
    {
        if (activeInspectable == null && cameraModeAction.WasPressedThisFrame())
        {
            isThirdPerson = !isThirdPerson;
        }
    }

    private void UpdateCameraModePosition()
    {
        if (cameraTransform == null)
        {
            return;
        }

        Vector3 targetPosition = isThirdPerson ? GetThirdPersonCameraPosition() : Vector3.zero;
        float smoothing = 1f - Mathf.Exp(-cameraModeTransitionSpeed * Time.deltaTime);
        cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, targetPosition, smoothing);
    }

    private Vector3 GetThirdPersonCameraPosition()
    {
        float desiredDistance = thirdPersonOffset.magnitude;
        if (desiredDistance <= Mathf.Epsilon || cameraPivot == null)
        {
            return Vector3.zero;
        }

        Vector3 localDirection = thirdPersonOffset / desiredDistance;
        Vector3 worldDirection = cameraPivot.TransformDirection(localDirection);
        float availableDistance = desiredDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            cameraPivot.position,
            thirdPersonCollisionRadius,
            worldDirection,
            cameraCollisionHits,
            desiredDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = cameraCollisionHits[i];
            if (hit.collider == null || hit.collider == characterController ||
                hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            float collisionDistance = Mathf.Max(0.2f, hit.distance - cameraCollisionPadding);
            availableDistance = Mathf.Min(availableDistance, collisionDistance);
        }

        return localDirection * availableDistance;
    }

    private void ConfigureAnimationModel()
    {
        Transform localModelRoot = transform.Find("CharacterModelRoot");
        if (localModelRoot != null)
        {
            characterModelRoot = localModelRoot;
        }

        if (characterAnimator == null && characterModelRoot != null)
        {
            characterAnimator = characterModelRoot.GetComponentInChildren<Animator>(true);
        }

        if (characterModelRoot != null)
        {
            modelRenderers = characterModelRoot.GetComponentsInChildren<Renderer>(true);
            modelRendererDefaultStates = new bool[modelRenderers.Length];
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                modelRendererDefaultStates[i] = modelRenderers[i].enabled;
            }
        }

        bool hasModelVisual = modelRenderers.Length > 0;
        if (capsuleVisual != null && hidePlaceholderCapsuleWhenModelAssigned && hasModelVisual)
        {
            capsuleVisual.gameObject.SetActive(false);
        }

        if (characterAnimator != null)
        {
            characterAnimator.applyRootMotion = false;
            characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        CacheAnimatorParameters();
        UpdateModelVisibility(true);
    }

    private void UpdateAnimation()
    {
        UpdateModelVisibility(false);

        if (characterAnimator == null)
        {
            return;
        }

        if (cachedAnimatorController != characterAnimator.runtimeAnimatorController)
        {
            CacheAnimatorParameters();
        }

        Vector3 localVelocity = transform.InverseTransformDirection(characterController.velocity);
        float planarSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        float maximumSpeed = Mathf.Max(sprintSpeed, 0.01f);
        Vector2 normalizedMovement = activeInspectable == null
            ? moveAction.ReadValue<Vector2>()
            : Vector2.zero;
        if (normalizedMovement.sqrMagnitude > 1f)
        {
            normalizedMovement.Normalize();
        }

        if (hasSpeedParameter)
        {
            characterAnimator.SetFloat(
                SpeedParameter,
                Mathf.Clamp01(planarSpeed / maximumSpeed),
                animationDampTime,
                Time.deltaTime);
        }

        if (hasMoveXParameter)
        {
            characterAnimator.SetFloat(MoveXParameter, normalizedMovement.x, animationDampTime, Time.deltaTime);
        }

        if (hasMoveYParameter)
        {
            characterAnimator.SetFloat(MoveYParameter, normalizedMovement.y, animationDampTime, Time.deltaTime);
        }

        if (hasVerticalSpeedParameter)
        {
            characterAnimator.SetFloat(
                VerticalSpeedParameter,
                characterController.velocity.y,
                animationDampTime,
                Time.deltaTime);
        }

        if (hasGroundedParameter)
        {
            characterAnimator.SetBool(GroundedParameter, characterController.isGrounded);
        }

        if (hasCrouchingParameter)
        {
            characterAnimator.SetBool(CrouchingParameter, isCrouching);
        }

        if (hasSprintingParameter)
        {
            characterAnimator.SetBool(SprintingParameter, isSprinting && planarSpeed > 0.1f);
        }

        if (hasThirdPersonParameter)
        {
            characterAnimator.SetBool(ThirdPersonParameter, isThirdPerson);
        }
    }

    private void CacheAnimatorParameters()
    {
        hasSpeedParameter = false;
        hasMoveXParameter = false;
        hasMoveYParameter = false;
        hasVerticalSpeedParameter = false;
        hasGroundedParameter = false;
        hasCrouchingParameter = false;
        hasSprintingParameter = false;
        hasThirdPersonParameter = false;
        hasJumpParameter = false;

        if (characterAnimator == null)
        {
            cachedAnimatorController = null;
            return;
        }

        cachedAnimatorController = characterAnimator.runtimeAnimatorController;
        if (cachedAnimatorController == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in characterAnimator.parameters)
        {
            int hash = parameter.nameHash;
            AnimatorControllerParameterType type = parameter.type;
            hasSpeedParameter |= hash == SpeedParameter && type == AnimatorControllerParameterType.Float;
            hasMoveXParameter |= hash == MoveXParameter && type == AnimatorControllerParameterType.Float;
            hasMoveYParameter |= hash == MoveYParameter && type == AnimatorControllerParameterType.Float;
            hasVerticalSpeedParameter |= hash == VerticalSpeedParameter && type == AnimatorControllerParameterType.Float;
            hasGroundedParameter |= hash == GroundedParameter && type == AnimatorControllerParameterType.Bool;
            hasCrouchingParameter |= hash == CrouchingParameter && type == AnimatorControllerParameterType.Bool;
            hasSprintingParameter |= hash == SprintingParameter && type == AnimatorControllerParameterType.Bool;
            hasThirdPersonParameter |= hash == ThirdPersonParameter && type == AnimatorControllerParameterType.Bool;
            hasJumpParameter |= hash == JumpParameter && type == AnimatorControllerParameterType.Trigger;
        }
    }

    private void TriggerJumpAnimation()
    {
        if (characterAnimator == null)
        {
            return;
        }

        if (cachedAnimatorController != characterAnimator.runtimeAnimatorController)
        {
            CacheAnimatorParameters();
        }

        if (hasJumpParameter)
        {
            characterAnimator.SetTrigger(JumpParameter);
        }
    }

    private void UpdateModelVisibility(bool force)
    {
        bool shouldShowModel = !hasLocalControl || showModelInFirstPerson || isThirdPerson;
        if (!force && modelVisibilityInitialized && modelIsVisible == shouldShowModel)
        {
            return;
        }

        modelVisibilityInitialized = true;
        modelIsVisible = shouldShowModel;
        for (int i = 0; i < modelRenderers.Length; i++)
        {
            modelRenderers[i].enabled = modelRendererDefaultStates[i] && shouldShowModel;
        }
    }

    private bool HasStandingHeadroom()
    {
        float radius = characterController.radius * 0.95f;
        Vector3 bottom = transform.TransformPoint(Vector3.up * characterController.radius);
        Vector3 top = transform.TransformPoint(Vector3.up * (standingHeight - characterController.radius));
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            radius,
            headroomHits,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = headroomHits[i];
            if (hit != null && hit != characterController && !hit.transform.IsChildOf(transform))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyStanceInstantly(bool crouching)
    {
        isCrouching = crouching;
        float height = crouching ? crouchingHeight : standingHeight;
        currentEyeHeight = crouching ? crouchingEyeHeight : standingEyeHeight;
        currentBobOffset = Vector3.zero;
        characterController.height = height;
        characterController.center = Vector3.up * (height * 0.5f);

        ApplyCameraPosition();
        UpdateCapsuleVisual(height);
    }

    private void ApplyCameraPosition()
    {
        if (cameraPivot == null)
        {
            return;
        }

        Vector3 basePosition = cameraNeutralLocalPosition;
        basePosition.y = currentEyeHeight;
        cameraPivot.localPosition = basePosition + currentBobOffset;
    }

    private void UpdateCapsuleVisual(float height)
    {
        if (capsuleVisual == null)
        {
            return;
        }

        capsuleVisual.localPosition = Vector3.up * (height * 0.5f);
        capsuleVisual.localScale = new Vector3(
            characterController.radius * 2f,
            height * 0.5f,
            characterController.radius * 2f);
    }

    private static void LockCursor()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void ShowInspectionCursor()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HandleCursorState()
    {
        if (activeInspectionPointerInteraction != null &&
            !activeInspectionPointerInteraction.IsInspectionInteractionActive)
        {
            activeInspectionPointerInteraction = null;
        }

        bool escapePressed =
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame;

        if (activeInspectable != null)
        {
            ShowInspectionCursor();

            if (escapePressed)
            {
                if (activeInspectionPointerInteraction != null)
                {
                    activeInspectionPointerInteraction.ExitInspectionInteraction();
                    activeInspectionPointerInteraction = null;
                }
                else
                {
                    ReleaseActiveInspection();
                }
            }

            return;
        }

        if (escapePressed)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }
    }

    private void ValidateActiveInspectionState()
    {
        if (activeInspectable is Behaviour inspectableBehaviour &&
            (inspectableBehaviour == null || !inspectableBehaviour.isActiveAndEnabled))
        {
            activeInspectionPointerInteraction = null;
            activeInspectable = null;
            LockCursor();
        }
    }

    private void ReleaseActiveInspection()
    {
        if (activeInspectionPointerInteraction != null)
        {
            activeInspectionPointerInteraction.ExitInspectionInteraction();
            activeInspectionPointerInteraction = null;
        }

        IPlayerInspectable inspectable = activeInspectable;
        activeInspectable = null;
        inspectable.ToggleInspection(cameraTransform);
        LockCursor();
    }

    private void RefreshLocalControlState()
    {
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        bool networkSessionActive = manager != null && manager.IsListening;
        if (!networkSessionActive)
        {
            spawnReady = true;
        }

        bool shouldHaveLocalControl = !networkSessionActive || (IsSpawned && IsOwner);
        ApplyLocalControlState(shouldHaveLocalControl);
    }

    private void ApplyLocalControlState(bool shouldHaveLocalControl)
    {
        hasLocalControl = shouldHaveLocalControl;
        bool gameplayComponentsEnabled = hasLocalControl && spawnReady;

        if (playerCamera != null)
        {
            playerCamera.enabled = gameplayComponentsEnabled;
        }

        if (playerAudioListener != null)
        {
            playerAudioListener.enabled = gameplayComponentsEnabled;
        }

        if (characterController != null)
        {
            characterController.enabled = gameplayComponentsEnabled;
        }

        SetInputActionsEnabled(gameplayComponentsEnabled && isActiveAndEnabled);
        modelVisibilityInitialized = false;
        UpdateModelVisibility(true);
    }

    private void SetInputActionsEnabled(bool inputEnabled)
    {
        InputAction[] actions =
        {
            moveAction,
            lookAction,
            jumpAction,
            crouchAction,
            sprintAction,
            cameraModeAction,
            interactAction,
            inspectAction,
            inspectRotateAction,
            inspectZoomAction
        };

        for (int i = 0; i < actions.Length; i++)
        {
            if (inputEnabled)
            {
                actions[i]?.Enable();
            }
            else
            {
                actions[i]?.Disable();
            }
        }
    }

    private void SubscribeToSceneLoads()
    {
        if (sceneLoadSubscribed)
        {
            return;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneLoadSubscribed = true;
    }

    private void UnsubscribeFromSceneLoads()
    {
        if (!sceneLoadSubscribed)
        {
            return;
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        sceneLoadSubscribed = false;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        ScheduleSpawn(scene);
    }

    private void ScheduleSpawn(Scene scene)
    {
        if (!hasLocalControl || !IsSpawned || !IsOwner || !scene.isLoaded)
        {
            return;
        }

        CancelScheduledSpawn();
        spawnReady = false;
        ApplyLocalControlState(true);

        if (TryMoveToSceneSpawnPoint(scene))
        {
            CompleteSpawn();
            return;
        }

        spawnRoutine = StartCoroutine(MoveToSceneSpawnPoint(scene));
    }

    private IEnumerator MoveToSceneSpawnPoint(Scene scene)
    {
        const int maxFrameWait = 120;
        for (int frame = 0; frame < maxFrameWait; frame++)
        {
            yield return null;

            if (!hasLocalControl || !IsSpawned || !IsOwner || !scene.isLoaded)
            {
                spawnRoutine = null;
                yield break;
            }

            if (TryMoveToSceneSpawnPoint(scene))
            {
                CompleteSpawn();
                yield break;
            }
        }

        Debug.LogWarning(
            $"[PlayerSpawn] {scene.name} sahnesinde NetworkSpawnPoint bulunamadı; oyuncu kontrolü güvenlik için kapalı kaldı.",
            this);
        spawnRoutine = null;
    }

    private bool TryMoveToSceneSpawnPoint(Scene scene)
    {
        NetworkSpawnPoint[] allPoints =
            FindObjectsByType<NetworkSpawnPoint>(FindObjectsInactive.Exclude);
        List<NetworkSpawnPoint> scenePoints = new List<NetworkSpawnPoint>();
        for (int i = 0; i < allPoints.Length; i++)
        {
            if (allPoints[i] != null && allPoints[i].gameObject.scene == scene)
            {
                scenePoints.Add(allPoints[i]);
            }
        }

        scenePoints.Sort(NetworkSpawnPoint.Compare);
        if (scenePoints.Count == 0)
        {
            return false;
        }

        int pointIndex = (int)(OwnerClientId % (ulong)scenePoints.Count);
        Transform point = scenePoints[pointIndex].transform;
        transform.SetPositionAndRotation(point.position, point.rotation);
        verticalVelocity = 0f;
        pitch = 0f;
        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.identity;
        }

        Debug.Log(
            $"[PlayerSpawn] Client {OwnerClientId} -> {scene.name}/{point.name} @ {point.position}",
            this);
        return true;
    }

    private void CompleteSpawn()
    {
        spawnReady = true;
        ApplyLocalControlState(true);
        spawnRoutine = null;
    }

    private void CancelScheduledSpawn()
    {
        if (spawnRoutine == null)
        {
            return;
        }

        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
    }
}
