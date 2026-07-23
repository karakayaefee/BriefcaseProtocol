using UnityEngine;

public interface IPlayerInteractable
{
    void Interact();
}

[DisallowMultipleComponent]
public sealed class BriefcaseInteractable : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private Animator animator;
    [SerializeField] private string openStateName =
        "Base Layer.Briefcase_LidRoot|Briefcase_Open";
    [SerializeField, Min(0f)] private float colliderPadding = 0.02f;

    private int openStateHash;
    private bool targetOpen;
    private bool isAnimating;
    private float animationDuration = 1f;
    private float normalizedProgress;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
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

    public void Interact()
    {
        if (animator == null || isAnimating)
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

        targetOpen = !targetOpen;
        isAnimating = true;
        animator.speed = 0f;
        SampleAnimation();
    }

    private void SampleAnimation()
    {
        animator.Play(openStateHash, 0, normalizedProgress);
        animator.Update(0f);
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
