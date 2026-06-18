using CounterStrikeSharp.API.Core;
using SpectatorList.Models;

namespace SpectatorList.Services
{
    public interface IStorageService
    {
        Task<bool> InitializeAsync();
        Task<PlayerDisplayPreferences> GetPlayerPreferencesAsync(CCSPlayerController player);
        Task<PlayerDisplayPreferences> TogglePlayerDisplayAsync(CCSPlayerController player);
        Task SetPlayerPreferencesAsync(CCSPlayerController player, PlayerDisplayPreferences preferences);
        void OnPlayerDisconnect(CCSPlayerController player);
        void ClearCache();
        string GetStorageType();
    }
}
