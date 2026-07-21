using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BriefcaseProtocol.Gameplay.Player;
using Unity.Services.Vivox;
using UnityEngine;

namespace BriefcaseProtocol.Services
{
    public sealed class VivoxVoiceService : MonoBehaviour
    {
        private readonly Dictionary<string, VivoxParticipant> participants = new();
        private string activeChannel = string.Empty;
        private bool initialized;

        public bool IsReady { get; private set; }
        public event Action<string, bool> SpeakingChanged;
        public event Action<string> VoiceError;

        private void OnEnable()
        {
            NetworkPlayerController.PushToTalkChanged += SetPushToTalk;
        }

        private void OnDisable()
        {
            NetworkPlayerController.PushToTalkChanged -= SetPushToTalk;
            UnsubscribeParticipants();
        }

        public async Task<bool> InitializeAsync(string displayName)
        {
            if (IsReady) return true;
            if (!await ProjectBootstrap.ServicesReadyTask) return false;
            try
            {
                if (!initialized)
                {
                    await VivoxService.Instance.InitializeAsync();
                    initialized = true;
                }

                await VivoxService.Instance.LoginAsync(new LoginOptions
                {
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName
                });
                VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
                VivoxService.Instance.MuteInputDevice();
                IsReady = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Vivox initialization failed: {exception.Message}");
                VoiceError?.Invoke(exception.Message);
                return false;
            }
        }

        public async Task<bool> JoinMatchChannelAsync(string sessionCode, string displayName)
        {
            if (!await InitializeAsync(displayName)) return false;
            try
            {
                activeChannel = SanitizeChannel($"briefcase-{sessionCode}");
                await VivoxService.Instance.JoinGroupChannelAsync(activeChannel, ChatCapability.AudioOnly,
                    new ChannelOptions { MakeActiveChannelUponJoining = false });
                await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);
                return true;
            }
            catch (Exception exception)
            {
                VoiceError?.Invoke(exception.Message);
                return false;
            }
        }

        public async Task LeaveAsync()
        {
            if (!IsReady) return;
            try
            {
                await VivoxService.Instance.LeaveAllChannelsAsync();
                activeChannel = string.Empty;
            }
            catch (Exception exception)
            {
                VoiceError?.Invoke(exception.Message);
            }
        }

        public void SetPushToTalk(bool pressed)
        {
            if (!IsReady) return;
            if (pressed)
            {
                VivoxService.Instance.UnmuteInputDevice();
                _ = VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.All);
            }
            else
            {
                VivoxService.Instance.MuteInputDevice();
                _ = VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);
            }
        }

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            participants[participant.PlayerId] = participant;
            participant.ParticipantSpeechDetected += () =>
                SpeakingChanged?.Invoke(participant.PlayerId, participant.SpeechDetected);
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            participants.Remove(participant.PlayerId);
            SpeakingChanged?.Invoke(participant.PlayerId, false);
        }

        private void UnsubscribeParticipants()
        {
            if (!initialized) return;
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            participants.Clear();
        }

        private static string SanitizeChannel(string value)
        {
            var builder = new StringBuilder();
            foreach (var character in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character) || character == '-') builder.Append(character);
            }
            return builder.Length == 0 ? "briefcase-local" : builder.ToString();
        }
    }
}
