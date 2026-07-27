using BriefcaseProtocol.Core;
using UnityEngine;

public interface IPlayerInteractable
{
    void Interact();
}

public interface IPlayerInspectable
{
    void ToggleInspection(Transform viewTransform);
    void RotateInspection(Vector2 inputDelta);
    void ZoomInspection(float scrollDelta);
}

[DisallowMultipleComponent]
public sealed class BriefcaseInteractable : MonoBehaviour, IPlayerInteractable, IPlayerInspectable
{
    [SerializeField] private Animator animator;
    [SerializeField] private string openStateName =
        "Base Layer.Briefcase_LidRoot|Briefcase_Open";
    [SerializeField, Min(0f)] private float colliderPadding = 0.02f;
    [SerializeField] private BriefcaseLockController lockController;

    [Header("Inspection")]
    [SerializeField, Min(0.1f)] private float inspectionDistance = 0.95f;
    [SerializeField] private Vector3 inspectionOffset = new Vector3(0f, -0.12f, 0f);
    [SerializeField] private Vector3 inspectionEulerAngles = new Vector3(0f, 180f, 0f);
    [SerializeField, Min(0f)] private float inspectionMoveSpeed = 10f;
    [SerializeField, Min(0f)] private float inspectionRotationSensitivity = 0.2f;
    [SerializeField, Min(0.1f)] private float minimumInspectionDistance = 0.65f;
    [SerializeField, Min(0.1f)] private float maximumInspectionDistance = 1.5f;
    [SerializeField, Min(0f)] private float inspectionZoomSensitivity = 0.12f;

    private int openStateHash;
    private bool targetOpen;
    private bool isAnimating;
    private float animationDuration = 1f;
    private float normalizedProgress;
    private Transform inspectionView;
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalWorldPosition;
    private Quaternion originalWorldRotation;
    private Vector2 inspectionRotationAngles;
    private float currentInspectionDistance;
    private Vector2 savedInspectionRotationAngles;
    private float savedInspectionDistance;
    private Vector3 detailFocusLocalPoint;
    private float detailInspectionDistance;
    private Vector3 detailInspectionOffset;
    private bool isInspecting;
    private bool isReturningFromInspection;
    private bool isDetailInspecting;
    private bool isInspectionTransitioning;
    private BriefcaseNetworkState networkState;

    public bool IsInspectionActive => isInspecting;
    public bool IsDetailInspectionActive => isDetailInspecting;
    public bool IsOpen => networkState != null && networkState.IsSpawned
        ? networkState.IsOpen.Value
        : targetOpen;
    public bool IsInteractionAnimating =>
        isAnimating || isReturningFromInspection || isInspectionTransitioning;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (lockController == null)
        {
            lockController = GetComponent<BriefcaseLockController>();
        }

        openStateHash = Animator.StringToHash(openStateName);
        EnsureInteractionCollider();

        if (animator != null)
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller != null && controller.animationClips.Length > 0)
            {
                animationDuration = Mathf.Max(0.01f, controller.animationClips[0].length);
            }

            animator.enabled = false;
        }
    }

    private void OnEnable()
    {
        BriefcaseNetworkState.InstanceChanged += HandleNetworkStateInstanceChanged;
        BindNetworkState(BriefcaseNetworkState.Instance);
    }

    private void OnDisable()
    {
        BriefcaseNetworkState.InstanceChanged -= HandleNetworkStateInstanceChanged;
        BindNetworkState(null);
    }

    private void Update()
    {
        if (animator == null || !animator.enabled || !isAnimating)
        {
            return;
        }

        float targetProgress = targetOpen ? 1f : 0f;
        normalizedProgress = Mathf.MoveTowards(
            normalizedProgress,
            targetProgress,
            Time.deltaTime / animationDuration);
        SampleAnimation();

        isAnimating = !Mathf.Approximately(normalizedProgress, targetProgress);
    }

    private void LateUpdate()
    {
        if (isInspecting)
        {
            if (inspectionView == null)
            {
                BeginReturnFromInspection();
                return;
            }

            Quaternion inspectionRotation =
                Quaternion.AngleAxis(inspectionRotationAngles.y, Vector3.up) *
                Quaternion.AngleAxis(inspectionRotationAngles.x, Vector3.right);
            Quaternion targetRotation =
                inspectionView.rotation *
                inspectionRotation *
                Quaternion.Euler(inspectionEulerAngles);
            Vector3 targetPosition;

            if (isDetailInspecting)
            {
                Vector3 targetFocusPosition =
                    inspectionView.position +
                    inspectionView.forward * detailInspectionDistance +
                    inspectionView.TransformVector(detailInspectionOffset);
                Vector3 scaledFocusOffset = Vector3.Scale(
                    detailFocusLocalPoint,
                    transform.lossyScale);
                targetPosition =
                    targetFocusPosition - targetRotation * scaledFocusOffset;
            }
            else
            {
                targetPosition =
                    inspectionView.position +
                    inspectionView.forward * currentInspectionDistance +
                    inspectionView.TransformVector(inspectionOffset);
            }

            MoveTowardsInspectionPose(targetPosition, targetRotation);
            isInspectionTransitioning = !IsAtPose(targetPosition, targetRotation);
            return;
        }

        if (!isReturningFromInspection)
        {
            return;
        }

        GetOriginalWorldPose(out Vector3 targetWorldPosition, out Quaternion targetWorldRotation);
        MoveTowardsInspectionPose(targetWorldPosition, targetWorldRotation);

        if (Vector3.SqrMagnitude(transform.position - targetWorldPosition) <= 0.000001f &&
            Quaternion.Angle(transform.rotation, targetWorldRotation) <= 0.05f)
        {
            transform.SetPositionAndRotation(targetWorldPosition, targetWorldRotation);
            isReturningFromInspection = false;
            isInspectionTransitioning = false;
            inspectionView = null;
        }
    }

    public void Interact()
    {
        if (animator == null || isAnimating || isDetailInspecting)
        {
            return;
        }

        if (!IsOpen && lockController != null && !lockController.IsUnlocked)
        {
            lockController.NotifyLockedOpenAttempt();
            return;
        }

        if (networkState != null && networkState.IsSpawned)
        {
            networkState.RequestToggleOpen();
            return;
        }

        BeginOpenTransition(!targetOpen, false);
    }

    private void BeginOpenTransition(bool shouldOpen, bool immediate)
    {
        if (animator == null)
        {
            return;
        }

        animator.enabled = true;
        if (!animator.HasState(0, openStateHash))
        {
            animator.enabled = false;
            Debug.LogWarning(
                $"Briefcase animation state '{openStateName}' was not found.",
                this);
            return;
        }

        targetOpen = shouldOpen;
        animator.speed = 0f;

        if (immediate)
        {
            normalizedProgress = targetOpen ? 1f : 0f;
            isAnimating = false;
            SampleAnimation();
            return;
        }

        isAnimating = true;
        SampleAnimation();
    }

    public void ToggleInspection(Transform viewTransform)
    {
        if (isInspecting)
        {
            BeginReturnFromInspection();
            return;
        }

        if (viewTransform == null)
        {
            return;
        }

        if (!isReturningFromInspection)
        {
            originalParent = transform.parent;
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            originalWorldPosition = transform.position;
            originalWorldRotation = transform.rotation;
        }

        inspectionView = viewTransform;
        inspectionRotationAngles = Vector2.zero;
        currentInspectionDistance = Mathf.Clamp(
            inspectionDistance,
            Mathf.Min(minimumInspectionDistance, maximumInspectionDistance),
            Mathf.Max(minimumInspectionDistance, maximumInspectionDistance));
        isReturningFromInspection = false;
        isInspecting = true;
        isInspectionTransitioning = true;
    }

    public void RotateInspection(Vector2 inputDelta)
    {
        if (!isInspecting || isDetailInspecting || inputDelta.sqrMagnitude <= 0f)
        {
            return;
        }

        inspectionRotationAngles.x = Mathf.Repeat(
            inspectionRotationAngles.x + inputDelta.y * inspectionRotationSensitivity,
            360f);
        inspectionRotationAngles.y = Mathf.Repeat(
            inspectionRotationAngles.y - inputDelta.x * inspectionRotationSensitivity,
            360f);
    }

    public void ZoomInspection(float scrollDelta)
    {
        if (!isInspecting || isDetailInspecting || Mathf.Approximately(scrollDelta, 0f))
        {
            return;
        }

        float minimumDistance = Mathf.Min(
            minimumInspectionDistance,
            maximumInspectionDistance);
        float maximumDistance = Mathf.Max(
            minimumInspectionDistance,
            maximumInspectionDistance);
        currentInspectionDistance = Mathf.Clamp(
            currentInspectionDistance - Mathf.Sign(scrollDelta) * inspectionZoomSensitivity,
            minimumDistance,
            maximumDistance);
        isInspectionTransitioning = true;
    }

    public bool TryEnterDetailInspection(
        Transform focusRoot,
        float closeUpDistance,
        Vector3 closeUpOffset)
    {
        if (!isInspecting || isDetailInspecting || isInteractionBlockingDetailView() ||
            focusRoot == null)
        {
            return false;
        }

        Renderer[] focusRenderers = focusRoot.GetComponentsInChildren<Renderer>(true);
        if (focusRenderers.Length == 0)
        {
            return false;
        }

        Bounds focusBounds = focusRenderers[0].bounds;
        for (int i = 1; i < focusRenderers.Length; i++)
        {
            focusBounds.Encapsulate(focusRenderers[i].bounds);
        }

        savedInspectionDistance = currentInspectionDistance;
        savedInspectionRotationAngles = inspectionRotationAngles;
        detailFocusLocalPoint = transform.InverseTransformPoint(focusBounds.center);
        detailInspectionDistance = Mathf.Max(0.1f, closeUpDistance);
        detailInspectionOffset = closeUpOffset;
        inspectionRotationAngles = Vector2.zero;
        isDetailInspecting = true;
        isInspectionTransitioning = true;
        return true;
    }

    public void ExitDetailInspection()
    {
        if (!isDetailInspecting)
        {
            return;
        }

        inspectionRotationAngles = savedInspectionRotationAngles;
        currentInspectionDistance = savedInspectionDistance;
        isDetailInspecting = false;
        isInspectionTransitioning = true;
    }

    private void SampleAnimation()
    {
        animator.Play(openStateHash, 0, normalizedProgress);
        animator.Update(0f);
    }

    private void BeginReturnFromInspection()
    {
        ExitDetailInspection();
        isInspecting = false;
        isReturningFromInspection = true;
        isInspectionTransitioning = true;
    }

    private void MoveTowardsInspectionPose(Vector3 targetPosition, Quaternion targetRotation)
    {
        float blend = inspectionMoveSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-inspectionMoveSpeed * Time.deltaTime);

        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, targetPosition, blend),
            Quaternion.Slerp(transform.rotation, targetRotation, blend));
    }

    private void GetOriginalWorldPose(
        out Vector3 targetWorldPosition,
        out Quaternion targetWorldRotation)
    {
        if (originalParent == null)
        {
            targetWorldPosition = originalWorldPosition;
            targetWorldRotation = originalWorldRotation;
            return;
        }

        targetWorldPosition = originalParent.TransformPoint(originalLocalPosition);
        targetWorldRotation = originalParent.rotation * originalLocalRotation;
    }

    private bool isInteractionBlockingDetailView()
    {
        return isAnimating || isReturningFromInspection || isInspectionTransitioning;
    }

    private bool IsAtPose(Vector3 targetPosition, Quaternion targetRotation)
    {
        return Vector3.SqrMagnitude(transform.position - targetPosition) <= 0.000001f &&
            Quaternion.Angle(transform.rotation, targetRotation) <= 0.05f;
    }

    private void HandleNetworkStateInstanceChanged(BriefcaseNetworkState state)
    {
        BindNetworkState(state);
    }

    private void BindNetworkState(BriefcaseNetworkState state)
    {
        if (networkState == state)
        {
            return;
        }

        if (networkState != null)
        {
            networkState.StateChanged -= HandleNetworkStateChanged;
        }

        networkState = state;
        if (networkState == null || !networkState.IsSpawned)
        {
            return;
        }

        networkState.StateChanged += HandleNetworkStateChanged;
        BeginOpenTransition(networkState.IsOpen.Value, true);
    }

    private void HandleNetworkStateChanged()
    {
        if (networkState == null || !networkState.IsSpawned ||
            targetOpen == networkState.IsOpen.Value)
        {
            return;
        }

        BeginOpenTransition(networkState.IsOpen.Value, false);
    }

    private void EnsureInteractionCollider()
    {
        if (GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        Bounds localBounds = default;
        bool hasBounds = false;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Bounds worldBounds = renderers[rendererIndex].bounds;
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));
                        Vector3 localCorner = worldToLocal.MultiplyPoint3x4(worldCorner);

                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        BoxCollider interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.center = localBounds.center;
        interactionCollider.size = localBounds.size + Vector3.one * (colliderPadding * 2f);
        interactionCollider.isTrigger = true;
    }
}
