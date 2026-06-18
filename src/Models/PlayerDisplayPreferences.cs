using System.Text.Json;
using SpectatorList.Configs;

namespace SpectatorList.Models
{
    public class PlayerDisplayPreferences
    {
        public bool Enabled { get; set; } = true;
        public bool SendToChat { get; set; }
        public bool UseCenterMessage { get; set; }
        public bool UseScreenView { get; set; }

        public static PlayerDisplayPreferences FromDefaults(DisplaySettings defaults)
        {
            return new PlayerDisplayPreferences
            {
                Enabled = defaults.EnabledByDefault,
                SendToChat = defaults.SendToChat,
                UseCenterMessage = defaults.UseCenterMessage,
                UseScreenView = defaults.UseScreenView
            };
        }

        public static PlayerDisplayPreferences FromJson(string? json, DisplaySettings defaults)
        {
            var preferences = FromDefaults(defaults);
            if (string.IsNullOrWhiteSpace(json))
                return preferences;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty(nameof(Enabled), out var enabled) && (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
                {
                    preferences.Enabled = enabled.GetBoolean();
                }

                if (root.TryGetProperty(nameof(SendToChat), out var sendToChat) && (sendToChat.ValueKind == JsonValueKind.True || sendToChat.ValueKind == JsonValueKind.False))
                {
                    preferences.SendToChat = sendToChat.GetBoolean();
                }

                if (root.TryGetProperty(nameof(UseCenterMessage), out var centerMessage) && (centerMessage.ValueKind == JsonValueKind.True || centerMessage.ValueKind == JsonValueKind.False))
                {
                    preferences.UseCenterMessage = centerMessage.GetBoolean();
                }

                if (root.TryGetProperty(nameof(UseScreenView), out var screenView) && (screenView.ValueKind == JsonValueKind.True || screenView.ValueKind == JsonValueKind.False))
                {
                    preferences.UseScreenView = screenView.GetBoolean();
                }
            }
            catch
            {
                // If parsing fails, fallback to defaults without overwriting them.
            }

            return preferences;
        }

        public PlayerDisplayPreferences Clone()
        {
            return new PlayerDisplayPreferences
            {
                Enabled = Enabled,
                SendToChat = SendToChat,
                UseCenterMessage = UseCenterMessage,
                UseScreenView = UseScreenView
            };
        }

        public bool MatchesDefaults(DisplaySettings defaults)
        {
            return Enabled &&
                   SendToChat == defaults.SendToChat &&
                   UseCenterMessage == defaults.UseCenterMessage &&
                   UseScreenView == defaults.UseScreenView;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
