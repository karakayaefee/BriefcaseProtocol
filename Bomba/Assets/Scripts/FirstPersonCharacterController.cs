using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class FirstPersonCharacterController : MonoBehaviour
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

    [Header("Animation")]
    [Tooltip("Place the imported character prefab under this transform.")]
    [SerializeField] private Transform characterModelRoot;
    [Tooltip("Optional. If left empty, the first Animator under CharacterModelRoot is used.")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private bool showModelInFirstPerson = true;
    [SerializeField] private bool hidePlaceholderCapsuleWhenModelAssigned = true;
    [SerializeField, Min(0f)] private float animationDampTime = 0.1f;

    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform capsuleVisual;

    private readonly Collider[] headroomHits = new Collider[16];
    private readonly RaycastHit[] cameraCollisionHits = new RaycastHit[16];

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
    private Renderer[] modelRenderers = System.Array.Empty<Renderer>();
    private bool[] modelRendererDefaultStates = System.Array.Empty<bool>();
    private RuntimeAnimatorController cachedAnimatorController;
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

        ConfigureAnimationModel();

        if (cameraPivot != null)
        {
            cameraNeutralLocalPosition = cameraPivot.localPosition;
        }

        ConfigureCharacterController();
        CreateInputActions();
        ApplyStanceInstantly(false);
    }

    private void OnEnable()
    {
        moveAction?.Enable();
        lookAction?.Enable();
        jumpAction?.Enable();
        crouchAction?.Enable();
        sprintAction?.Enable();
        cameraModeAction?.Enable();
    }

    private void Start()
    {
        LockCursor();
    }

    private void Update()
    {
        HandleCursorState();
        UpdateCameraModeInput();
        UpdateLook();
        UpdateStance();
        UpdateMovement();
        UpdateHeadBob();
        UpdateCameraModePosition();
        UpdateAnimation();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        lookAction?.Disable();
        jumpAction?.Disable();
        crouchAction?.Disable();
        sprintAction?.Disable();
        cameraModeAction?.Disable();

        if (Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
        lookAction?.Dispose();
        jumpAction?.Dispose();
        crouchAction?.Dispose();
        sprintAction?.Dispose();
        cameraModeAction?.Dispose();
    }

    public void SetPrefabReferences(Transform newCameraPivot, Transform newCapsuleVisual)
    {
        cameraPivot = newCameraPivot;
        capsuleVisual = newCapsuleVisual;
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
    }

    private void UpdateMovement()
    {
        bool isGrounded = characterController.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (isGrounded && jumpAction.WasPressedThisFrame())
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            TriggerJumpAnimation();
        }

        Vector2 input = moveAction.ReadValue<Vector2>();
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        isSprinting = !isCrouching && sprintAction.IsPressed();
        float speed = isCrouching ? crouchSpeed : isSprinting ? sprintSpeed : walkSpeed;
        Vector3 horizontalVelocity = (transform.right * input.x + transform.forward * input.y) * speed;

        verticalVelocity += gravity * Time.deltaTime;
        horizontalVelocity.y = verticalVelocity;
        characterController.Move(horizontalVelocity * Time.deltaTime);
    }

    private void UpdateLook()
    {
        if (cameraPivot == null || Cursor.lockState != CursorLockMode.Locked)
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
        bool shouldBob = !isThirdPerson && characterController.isGrounded && planarVelocity.sqrMagnitude > 0.04f;
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
        if (cameraModeAction.WasPressedThisFrame())
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
        if (characterModelRoot == null)
        {
            characterModelRoot = transform.Find("CharacterModelRoot");
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
        Vector2 normalizedMovement = moveAction.ReadValue<Vector2>();
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
        bool shouldShowModel = showModelInFirstPerson || isThirdPerson;
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

    private static void HandleCursorState()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }
    }
}
