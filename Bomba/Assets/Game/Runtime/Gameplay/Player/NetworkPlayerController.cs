using System;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Interactions;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BriefcaseProtocol.Gameplay.Player
{
    [RequireComponent(typeof(NetworkObject), typeof(CharacterController))]
    public sealed class NetworkPlayerController : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 6.5f;
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float interactionDistance = 3f;
        [SerializeField] private LayerMask interactionMask = ~0;
        [SerializeField] private float poseSendRate = 20f;

        private readonly NetworkVariable<Vector3> networkPosition = new();
        private readonly NetworkVariable<Quaternion> networkRotation = new(Quaternion.identity);
        private CharacterController controller;
        private float cameraPitch;
        private float verticalVelocity;
        private float nextPoseSend;

        public static event Action<string> InteractionPromptChanged;
        public static event Action ManualToggleRequested;
        public static event Action<bool> PushToTalkChanged;

        public GameplayRole CurrentRole
        {
            get
            {
                var manager = NetworkMatchManager.Instance;
                if (manager == null)
                {
                    return GameplayRole.None;
                }

                foreach (var player in manager.Players)
                {
                    if (player.ClientId == OwnerClientId)
                    {
                        return player.RoleFor(manager.State.BuilderTeam);
                    }
                }

                return GameplayRole.None;
            }
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(IsOwner);
            }

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            IgnorePeerCollisions();
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsOwner)
            {
                UpdateLocalPlayer();
            }
            else
            {
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, networkPosition.Value, Time.deltaTime * 14f),
                    Quaternion.Slerp(transform.rotation, networkRotation.Value, Time.deltaTime * 14f));
            }
        }

        private void UpdateLocalPlayer()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
            {
                return;
            }

            var move = Vector2.zero;
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            move = Vector2.ClampMagnitude(move, 1f);

            var look = mouse.delta.ReadValue() * lookSensitivity;
            transform.Rotate(Vector3.up, look.x, Space.World);
            cameraPitch = Mathf.Clamp(cameraPitch - look.y, -85f, 85f);
            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            verticalVelocity += Physics.gravity.y * Time.deltaTime;

            var speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
            var direction = transform.forward * move.y + transform.right * move.x;
            controller.Move((direction * speed + Vector3.up * verticalVelocity) * Time.deltaTime);

            UpdateInteraction(keyboard);
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                ManualToggleRequested?.Invoke();
            }
            if (keyboard.vKey.wasPressedThisFrame) PushToTalkChanged?.Invoke(true);
            if (keyboard.vKey.wasReleasedThisFrame) PushToTalkChanged?.Invoke(false);

            if (Time.unscaledTime >= nextPoseSend)
            {
                nextPoseSend = Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);
                SubmitPoseServerRpc(transform.position, transform.rotation);
            }
        }

        private void UpdateInteraction(Keyboard keyboard)
        {
            if (playerCamera == null || !Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                    out var hit, interactionDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                InteractionPromptChanged?.Invoke(string.Empty);
                return;
            }

            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                InteractionPromptChanged?.Invoke(string.Empty);
                return;
            }

            var context = new InteractionContext(OwnerClientId, CurrentRole);
            var allowed = interactable.CanInteract(context);
            InteractionPromptChanged?.Invoke(allowed ? interactable.PromptKey : string.Empty);
            if (allowed && keyboard.eKey.wasPressedThisFrame)
            {
                interactable.Interact(context);
            }
        }

        [ServerRpc]
        private void SubmitPoseServerRpc(Vector3 position, Quaternion rotation)
        {
            var maximumStep = sprintSpeed * 0.25f + 1f;
            if (Vector3.Distance(networkPosition.Value, position) > maximumStep && networkPosition.Value != Vector3.zero)
            {
                position = Vector3.MoveTowards(networkPosition.Value, position, maximumStep);
            }

            networkPosition.Value = position;
            networkRotation.Value = rotation;
        }

        private void IgnorePeerCollisions()
        {
            var ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            foreach (var peer in FindObjectsByType<NetworkPlayerController>())
            {
                var peerCollider = peer.GetComponent<Collider>();
                if (peer != this && peerCollider != null)
                {
                    Physics.IgnoreCollision(ownCollider, peerCollider, true);
                }
            }
        }
    }
}
