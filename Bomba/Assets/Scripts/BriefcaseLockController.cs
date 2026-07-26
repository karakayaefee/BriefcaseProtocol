using UnityEngine;

public interface IInspectionPointerInteractable
{
    bool IsInspectionInteractionActive { get; }
    bool TryBeginInspectionInteraction(Collider hitCollider, Transform viewTransform);
    bool TryUseInspectionInteraction(Collider hitCollider);
    void ExitInspectionInteraction();
}

[DisallowMultipleComponent]
public sealed class BriefcaseLockController : MonoBehaviour, IInspectionPointerInteractable
{
    [Header("Briefcase References")]
    [SerializeField] private BriefcaseInteractable briefcaseInteractable;
    [SerializeField] private Transform lockBody;
    [SerializeField] private Transform releaseButton;
    [SerializeField] private LockDialController dial1;
    [SerializeField] private LockDialController dial2;
    [SerializeField] private LockDialController dial3;

    [Header("Combination")]
    [SerializeField, Range(0, 9)] private int correctDigit1 = 1;
    [SerializeField, Range(0, 9)] private int correctDigit2 = 2;
    [SerializeField, Range(0, 9)] private int correctDigit3 = 3;

    [Header("Lock Close-Up")]
    [SerializeField, Min(0.1f)] private float lockCloseUpDistance = 0.15f;
    [SerializeField] private Vector3 lockCloseUpOffset = Vector3.zero;
    [SerializeField, Min(20f)] private float arrowButtonSize = 42f;
    [SerializeField, Min(20f)] private float arrowVerticalOffset = 125f;

    [Header("Optional Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip unlockSound;
    [SerializeField] private bool enableDebugLogs = true;

    private Camera inspectionCamera;
    private float lockedFeedbackUntil;
    private float incorrectCombinationFeedbackUntil;
    private int inspectionEnteredFrame;

    public bool IsUnlocked { get; private set; }
    public bool IsInspectionInteractionActive { get; private set; }
    public LockDialController Dial1 => dial1;
    public LockDialController Dial2 => dial2;
    public LockDialController Dial3 => dial3;
    public int VisibleArrowControlCount => CanAdjustDials ? 6 : 0;
    public bool CanAdjustDials =>
        IsInspectionInteractionActive &&
        !IsUnlocked &&
        Time.frameCount > inspectionEnteredFrame &&
        briefcaseInteractable != null &&
        !briefcaseInteractable.IsInteractionAnimating;

    private void Awake()
    {
        ResolveReferences();
        EnsureLockCollider();
        EnsureReleaseButtonCollider();

        dial1?.SetLockController(this);
        dial2?.SetLockController(this);
        dial3?.SetLockController(this);
    }

    private void OnDisable()
    {
        if (IsInspectionInteractionActive)
        {
            ExitInspectionInteraction();
        }
    }

    private void OnValidate()
    {
        correctDigit1 = Mathf.Clamp(correctDigit1, 0, 9);
        correctDigit2 = Mathf.Clamp(correctDigit2, 0, 9);
        correctDigit3 = Mathf.Clamp(correctDigit3, 0, 9);
        lockCloseUpDistance = Mathf.Max(0.1f, lockCloseUpDistance);
        arrowButtonSize = Mathf.Max(20f, arrowButtonSize);
        arrowVerticalOffset = Mathf.Max(20f, arrowVerticalOffset);
    }

    public bool TryBeginInspectionInteraction(
        Collider hitCollider,
        Transform viewTransform)
    {
        if (IsInspectionInteractionActive)
        {
            return true;
        }

        if (IsUnlocked ||
            hitCollider == null ||
            viewTransform == null ||
            briefcaseInteractable == null ||
            lockBody == null ||
            !briefcaseInteractable.IsInspectionActive ||
            briefcaseInteractable.IsInteractionAnimating)
        {
            return false;
        }

        Transform hitTransform = hitCollider.transform;
        if (hitTransform != lockBody && !hitTransform.IsChildOf(lockBody))
        {
            return false;
        }

        if (!briefcaseInteractable.TryEnterDetailInspection(
                lockBody,
                lockCloseUpDistance,
                lockCloseUpOffset))
        {
            return false;
        }

        inspectionCamera = viewTransform.GetComponent<Camera>();
        if (inspectionCamera == null)
        {
            inspectionCamera = viewTransform.GetComponentInParent<Camera>();
        }

        IsInspectionInteractionActive = true;
        inspectionEnteredFrame = Time.frameCount;
        Log("Entered lock inspection mode.");
        return true;
    }

    public bool TryUseInspectionInteraction(Collider hitCollider)
    {
        if (!IsInspectionInteractionActive || hitCollider == null || releaseButton == null)
        {
            return false;
        }

        Transform hitTransform = hitCollider.transform;
        if (hitTransform != releaseButton && !hitTransform.IsChildOf(releaseButton))
        {
            return false;
        }

        TryReleaseAndOpenBriefcase();
        return true;
    }

    public void ExitInspectionInteraction()
    {
        if (!IsInspectionInteractionActive)
        {
            return;
        }

        IsInspectionInteractionActive = false;
        briefcaseInteractable?.ExitDetailInspection();
        inspectionCamera = null;
        Log("Lock inspection exited.");
    }

    public void NotifyDialMovementComplete(LockDialController changedDial)
    {
        if (changedDial == null)
        {
            return;
        }

        int dialIndex = changedDial == dial1 ? 1 : changedDial == dial2 ? 2 : 3;
        Log($"Dial {dialIndex} changed to {changedDial.CurrentValue}.");
        Log($"Current combination: {GetCombinationText()}.");
    }

    public void NotifyLockedOpenAttempt()
    {
        lockedFeedbackUntil = Time.unscaledTime + 1.25f;
        Log("Open attempt blocked because the lock is locked.");
    }

    private void TryReleaseAndOpenBriefcase()
    {
        if (IsUnlocked || briefcaseInteractable == null ||
            briefcaseInteractable.IsInteractionAnimating)
        {
            return;
        }

        if (dial1 == null || dial2 == null || dial3 == null ||
            dial1.IsAnimating || dial2.IsAnimating || dial3.IsAnimating)
        {
            Log("Release button ignored while a dial is moving.");
            return;
        }

        Log($"Release button pressed with combination {GetCombinationText()}.");
        if (!HasCorrectCombination())
        {
            incorrectCombinationFeedbackUntil = Time.unscaledTime + 1.25f;
            Log("Incorrect combination. Briefcase remains locked.");
            return;
        }

        IsUnlocked = true;
        if (audioSource != null && unlockSound != null)
        {
            audioSource.PlayOneShot(unlockSound);
        }

        Log("Correct combination confirmed with ReleaseButton_L.");
        ExitInspectionInteraction();

        if (!briefcaseInteractable.IsOpen)
        {
            briefcaseInteractable.Interact();
        }
    }

    private bool HasCorrectCombination()
    {
        return dial1 != null &&
            dial2 != null &&
            dial3 != null &&
            dial1.CurrentValue == correctDigit1 &&
            dial2.CurrentValue == correctDigit2 &&
            dial3.CurrentValue == correctDigit3;
    }

    private void OnGUI()
    {
        if (IsInspectionInteractionActive)
        {
            if (IsUnlocked)
            {
                DrawCenteredStatus("UNLOCKED", new Color(0.2f, 1f, 0.35f, 1f));
                return;
            }

            DrawDialControls(dial1);
            DrawDialControls(dial2);
            DrawDialControls(dial3);

            if (Time.unscaledTime < incorrectCombinationFeedbackUntil)
            {
                DrawCenteredStatus("WRONG CODE", new Color(1f, 0.35f, 0.25f, 1f));
            }

            return;
        }

        if (Time.unscaledTime < lockedFeedbackUntil)
        {
            DrawCenteredStatus("LOCKED", new Color(1f, 0.35f, 0.25f, 1f));
        }
    }

    private void DrawDialControls(LockDialController dial)
    {
        if (dial == null || inspectionCamera == null)
        {
            return;
        }

        Vector3 screenPosition = inspectionCamera.WorldToScreenPoint(dial.VisualCenterWorld);
        if (screenPosition.z <= 0f)
        {
            return;
        }

        float guiX = screenPosition.x;
        float guiY = Screen.height - screenPosition.y;
        float halfSize = arrowButtonSize * 0.5f;
        Rect upRect = new Rect(
            guiX - halfSize,
            guiY - arrowVerticalOffset - arrowButtonSize,
            arrowButtonSize,
            arrowButtonSize);
        Rect downRect = new Rect(
            guiX - halfSize,
            guiY + arrowVerticalOffset,
            arrowButtonSize,
            arrowButtonSize);

        GUIStyle arrowStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(arrowButtonSize * 0.55f),
            fontStyle = FontStyle.Bold
        };

        bool previousEnabled = GUI.enabled;
        GUI.enabled = dial.CanAcceptInput;
        if (GUI.Button(upRect, "\u25B2", arrowStyle))
        {
            dial.TryIncrease();
        }

        if (GUI.Button(downRect, "\u25BC", arrowStyle))
        {
            dial.TryDecrease();
        }

        GUI.enabled = previousEnabled;
    }

    private void DrawCenteredStatus(string message, Color color)
    {
        Rect statusRect = new Rect(
            (Screen.width - 260f) * 0.5f,
            Screen.height * 0.12f,
            260f,
            48f);
        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            fontStyle = FontStyle.Bold
        };
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(statusRect, message, style);
        GUI.color = previousColor;
    }

    private string GetCombinationText()
    {
        int value1 = dial1 != null ? dial1.CurrentValue : -1;
        int value2 = dial2 != null ? dial2.CurrentValue : -1;
        int value3 = dial3 != null ? dial3.CurrentValue : -1;
        return $"{value1}-{value2}-{value3}";
    }

    private void ResolveReferences()
    {
        if (briefcaseInteractable == null)
        {
            briefcaseInteractable = GetComponent<BriefcaseInteractable>();
        }

        lockBody ??= FindDescendantByName(transform, "LockBody_L");
        releaseButton ??= FindDescendantByName(transform, "ReleaseButton_L");
        dial1 ??= FindDial("Dial_01_L");
        dial2 ??= FindDial("Dial_02_L");
        dial3 ??= FindDial("Dial_03_L");
    }

    private LockDialController FindDial(string dialName)
    {
        Transform dialTransform = FindDescendantByName(transform, dialName);
        return dialTransform != null
            ? dialTransform.GetComponent<LockDialController>()
            : null;
    }

    private void EnsureLockCollider()
    {
        EnsureColliderForRenderer(lockBody, 0.0001f);
    }

    private void EnsureReleaseButtonCollider()
    {
        EnsureColliderForRenderer(releaseButton, 0.0001f);
    }

    private static void EnsureColliderForRenderer(Transform target, float padding)
    {
        if (target == null || target.GetComponent<Collider>() != null)
        {
            return;
        }

        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (targetRenderer == null)
        {
            return;
        }

        Bounds worldBounds = targetRenderer.bounds;
        Matrix4x4 worldToLocal = target.worldToLocalMatrix;
        Bounds localBounds = new Bounds(
            worldToLocal.MultiplyPoint3x4(worldBounds.center),
            Vector3.zero);
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = worldBounds.center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    localBounds.Encapsulate(worldToLocal.MultiplyPoint3x4(worldCorner));
                }
            }
        }

        BoxCollider targetCollider = target.gameObject.AddComponent<BoxCollider>();
        targetCollider.center = localBounds.center;
        targetCollider.size = localBounds.size + Vector3.one * Mathf.Max(0f, padding);
        targetCollider.isTrigger = true;
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (descendants[i].name == objectName)
            {
                return descendants[i];
            }
        }

        return null;
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[BriefcaseLock] {message}", this);
        }
    }
}
