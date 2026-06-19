using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

using SpectatorList.Configs;

namespace SpectatorList.Display
{
    public class CenterMessageDisplay : IDisposable
    {
        private readonly CCSPlayerController _player;
        private readonly SpectatorConfig _config;
        private readonly BasePlugin _plugin;
        private bool _isDisplaying = false;
        private string? _currentMessage;

        public CenterMessageDisplay(CCSPlayerController player, SpectatorConfig config, BasePlugin plugin)
        {
            _player = player;
            _config = config;
            _plugin = plugin;
        }

        public void ShowSpectatorList(List<CCSPlayerController> spectators)
        {
            if (!_config.Display.UseCenterMessage || !_player.IsValid || spectators.Count == 0)
                return;

            try
            {
                _currentMessage = BuildMessage(spectators);
                _isDisplaying = true;
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[SpectatorList] Error showing center message: {ex.Message}");
            }
        }

        public void Render()
        {
            if (!_isDisplaying || !_player.IsValid || string.IsNullOrEmpty(_currentMessage))
                return;

            _player.PrintToCenter(_currentMessage);
        }

        private string BuildMessage(List<CCSPlayerController> spectators)
        {
            string titleText = _plugin.Localizer["spectators_title", spectators.Count];
            titleText = titleText.Replace("[SpectatorList]", "").Trim();

            int maxToShow = Math.Min(spectators.Count, _config.Display.MaxNamesInMessage);
            var spectatorNames = new List<string>();

            for (int i = 0; i < maxToShow; i++)
            {
                var spectator = spectators[i];
                if (spectator.IsValid && !string.IsNullOrEmpty(spectator.PlayerName))
                {
                    spectatorNames.Add(spectator.PlayerName);
                }
            }

            string spectatorsList = string.Join(", ", spectatorNames);

            if (spectators.Count > maxToShow)
            {
                int remaining = spectators.Count - maxToShow;
                string andMoreText = _plugin.Localizer["and_more", remaining];
                andMoreText = andMoreText.Replace("[SpectatorList]", "").Trim();
                spectatorsList += $", {andMoreText}";
            }

            string message = _config.Display.CenterMessage;
            message = message.Replace("{TITLE}", titleText);
            message = message.Replace("{SPECTATORS}", spectatorsList);
            message = message.Replace("{COUNT}", spectators.Count.ToString());

            return message;
        }

        public void HideDisplay()
        {
            _currentMessage = null;
            _isDisplaying = false;
        }

        public void Dispose()
        {
            HideDisplay();
        }
    }
}
