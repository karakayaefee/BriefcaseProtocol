using UnityEngine;

[DisallowMultipleComponent]
public sealed class LockDialController : MonoBehaviour
{
    private const int DigitCount = 10;

    [SerializeField] private Transform dialTransform;
    [SerializeField, Range(0, DigitCount - 1)] private int currentValue;
    [SerializeField, Min(0.01f)] private float stepAngle = 36f;
    [SerializeField] private Vector3 rotationAxis = Vector3.right;
    [SerializeField, Range(-1, 1)] private int rotationDirection = 1;
    [SerializeField, Min(0f)] private float rotationDuration = 0.15f;
    [SerializeField] private BriefcaseLockController lockController;

    private Quaternion originalLocalRotation;
    private Quaternion animationStartRotation;
    private Quaternion animationTargetRotation;
    private float animationElapsed;
    private int targetValue;
    private bool initialized;
    private bool awaitingNetworkValue;

    public int CurrentValue => currentValue;
    public float StepAngle => stepAngle;
    public Vector3 RotationAxis => rotationAxis;
    public int RotationDirection => rotationDirection;
    public Quaternion OriginalLocalRotation => originalLocalRotation;
    public float RotationSpeed => rotationDuration;
    public bool IsAnimating { get; private set; }
    public bool CanAcceptInput =>
        initialized &&
        !IsAnimating &&
        !awaitingNetworkValue &&
        lockController != null &&
        lockController.CanAdjustDials;

    public Vector3 VisualCenterWorld
    {
        get
        {
            Renderer dialRenderer = dialTransform != null
                ? dialTransform.GetComponent<Renderer>()
                : null;
            return dialRenderer != null
                ? dialRenderer.bounds.center
                : (dialTransform != null ? dialTransform.position : transform.position);
        }
    }

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        if (!IsAnimating || dialTransform == null)
        {
            return;
        }

        animationElapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.0001f, rotationDuration);
        float progress = Mathf.Clamp01(animationElapsed / duration);
        float smoothedProgress = progress * progress * (3f - 2f * progress);
        dialTransform.localRotation = Quaternion.Slerp(
            animationStartRotation,
            animationTargetRotation,
            smoothedProgress);

        if (progress >= 1f)
        {
            CompleteStep();
        }
    }

    private void OnDisable()
    {
        if (!IsAnimating || dialTransform == null)
        {
            return;
        }

        dialTransform.localRotation = animationTargetRotation;
        currentValue = targetValue;
        IsAnimating = false;
        awaitingNetworkValue = false;
    }

    private void OnValidate()
    {
        currentValue = WrapDigit(currentValue);
        stepAngle = Mathf.Max(0.01f, stepAngle);
        rotationDuration = Mathf.Max(0f, rotationDuration);
        if (rotationAxis.sqrMagnitude <= 0.000001f)
        {
            rotationAxis = Vector3.right;
        }

        rotationDirection = rotationDirection < 0 ? -1 : 1;
    }

    public void SetLockController(BriefcaseLockController owner)
    {
        lockController = owner;
    }

    public bool TryIncrease()
    {
        return TryStep(1);
    }

    public bool TryDecrease()
    {
        return TryStep(-1);
    }

    private bool TryStep(int valueDelta)
    {
        if (!CanAcceptInput || valueDelta == 0)
        {
            return false;
        }

        if (lockController != null)
        {
            awaitingNetworkValue = true;
            if (lockController.TryHandleDialInput(this, valueDelta))
            {
                return true;
            }

            awaitingNetworkValue = false;
        }

        ApplyNetworkValue(WrapDigit(currentValue + valueDelta), true);
        return true;
    }

    public void ApplyNetworkValue(int value, bool animate)
    {
        Initialize();
        awaitingNetworkValue = false;
        int wrappedValue = WrapDigit(value);
        if (!IsAnimating && currentValue == wrappedValue)
        {
            return;
        }

        targetValue = wrappedValue;
        animationElapsed = 0f;
        animationStartRotation = dialTransform.localRotation;
        animationTargetRotation = GetRotationForValue(targetValue);
        IsAnimating = animate && rotationDuration > 0f;

        if (!IsAnimating)
        {
            dialTransform.localRotation = animationTargetRotation;
            CompleteStep();
        }
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        if (dialTransform == null)
        {
            dialTransform = transform;
        }

        originalLocalRotation = dialTransform.localRotation;
        currentValue = WrapDigit(currentValue);
        rotationAxis = rotationAxis.sqrMagnitude > 0.000001f
            ? rotationAxis.normalized
            : Vector3.right;
        rotationDirection = rotationDirection < 0 ? -1 : 1;
        dialTransform.localRotation = GetRotationForValue(currentValue);
        initialized = true;
    }

    private void CompleteStep()
    {
        dialTransform.localRotation = animationTargetRotation;
        currentValue = targetValue;
        IsAnimating = false;
        lockController?.NotifyDialMovementComplete(this);
    }

    private Quaternion GetRotationForValue(int value)
    {
        float angle = value * stepAngle * rotationDirection;
        return originalLocalRotation * Quaternion.AngleAxis(angle, rotationAxis);
    }

    private static int WrapDigit(int value)
    {
        return (value % DigitCount + DigitCount) % DigitCount;
    }
}
