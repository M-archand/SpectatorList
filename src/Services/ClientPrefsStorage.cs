using Clientprefs.API;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using SpectatorList.Configs;
using SpectatorList.Models;

namespace SpectatorList.Services
{
    public class ClientPrefsStorage : IStorageService
    {
        private const string CookieName = "spectatorlist_config";
        private const string CookieDescription = "JSON SpectatorList settings";

        private readonly IClientprefsApi? _clientprefsApi;
        private readonly DisplaySettings _defaults;
        private readonly Dictionary<string, PlayerDisplayPreferences> _preferenceCache;
        private readonly HashSet<string> _playersWithStoredPrefs;

        private int _cookieId = -1;
        private bool _databaseReady;
        private bool _eventsBound;
        private bool _registerScheduled;
        private bool _registrationInProgress;

        public ClientPrefsStorage(SpectatorConfig config, IClientprefsApi? clientprefsApi)
        {
            _clientprefsApi = clientprefsApi;
            _defaults = config.Display;
            _preferenceCache = new Dictionary<string, PlayerDisplayPreferences>();
            _playersWithStoredPrefs = new HashSet<string>();

            if (_clientprefsApi != null)
            {
                _clientprefsApi.OnDatabaseLoaded += OnDatabaseLoaded;
                _clientprefsApi.OnPlayerCookiesCached += OnPlayerCookiesCached;
                _eventsBound = true;
            }
        }

        public async Task<bool> InitializeAsync()
        {
            var success = await EnsureCookieRegisteredAsync();

            var cookieId = _cookieId;

            Server.NextFrame(() =>
            {
                if (!success)
                {
                    Server.PrintToConsole("[SpectatorList] Clientprefs cookie registration failed; preferences will use defaults only.");
                }
                else
                {
                    Server.PrintToConsole($"[SpectatorList] Using Clientprefs storage (cookie id: {cookieId})");
                }
            });

            return success;
        }

        public async Task<PlayerDisplayPreferences> GetPlayerPreferencesAsync(CCSPlayerController player)
        {
            await EnsureCookieRegisteredAsync();
            return GetPlayerPreferences(player);
        }

        private PlayerDisplayPreferences GetPlayerPreferences(CCSPlayerController player)
        {
            var preferences = PlayerDisplayPreferences.FromDefaults(_defaults);

            if (player == null || !player.IsValid || player.SteamID == 0)
            {
                return preferences;
            }

            var steamId = player.SteamID.ToString();

            if (_preferenceCache.TryGetValue(steamId, out var cached))
            {
                return cached;
            }

            if (_clientprefsApi == null || _cookieId == -1)
            {
                _preferenceCache[steamId] = preferences;
                return preferences;
            }

            try
            {
                var rawValue = _clientprefsApi.GetPlayerCookie(player, _cookieId);
                preferences = PlayerDisplayPreferences.FromJson(rawValue, _defaults);

                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    _playersWithStoredPrefs.Add(steamId);
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[SpectatorList] Failed to read clientprefs for {steamId}: {ex.Message}");
            }

            _preferenceCache[steamId] = preferences;
            return preferences;
        }

        public async Task<PlayerDisplayPreferences> TogglePlayerDisplayAsync(CCSPlayerController player)
        {
            await EnsureCookieRegisteredAsync();
            return TogglePlayerDisplay(player);
        }

        private PlayerDisplayPreferences TogglePlayerDisplay(CCSPlayerController player)
        {
            var preferences = GetPlayerPreferences(player);
            preferences.Enabled = !preferences.Enabled;
            _ = PersistPreferencesAsync(player, preferences);
            return preferences;
        }

        public async Task SetPlayerPreferencesAsync(CCSPlayerController player, PlayerDisplayPreferences preferences)
        {
            await EnsureCookieRegisteredAsync();
            await PersistPreferencesAsync(player, preferences);
        }

        public void OnPlayerDisconnect(CCSPlayerController player)
        {
            if (player == null || player.SteamID == 0)
                return;

            var steamId = player.SteamID.ToString();
            _preferenceCache.Remove(steamId);
            _playersWithStoredPrefs.Remove(steamId);
        }

        public void ClearCache()
        {
            _preferenceCache.Clear();
            _playersWithStoredPrefs.Clear();

            if (_clientprefsApi != null && _eventsBound)
            {
                _clientprefsApi.OnDatabaseLoaded -= OnDatabaseLoaded;
                _clientprefsApi.OnPlayerCookiesCached -= OnPlayerCookiesCached;
                _eventsBound = false;
            }
        }

        public string GetStorageType()
        {
            return "Clientprefs";
        }

        private Task<bool> PersistPreferencesAsync(CCSPlayerController player, PlayerDisplayPreferences preferences)
        {
            if (player == null || !player.IsValid || player.SteamID == 0 || _clientprefsApi == null || _cookieId == -1)
            {
                return Task.FromResult(false);
            }

            var steamId = player.SteamID.ToString();

            _preferenceCache[steamId] = preferences;

            var hasStoredPrefs = _playersWithStoredPrefs.Contains(steamId);
            var usesDefaults = preferences.MatchesDefaults(_defaults);

            if (!hasStoredPrefs && usesDefaults)
            {
                return Task.FromResult(true);
            }

            try
            {
                var serialized = preferences.ToJson();
                _clientprefsApi.SetPlayerCookie(player, _cookieId, serialized);
                _playersWithStoredPrefs.Add(steamId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[SpectatorList] Failed to persist clientprefs for {steamId}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private void OnDatabaseLoaded()
        {
            _databaseReady = true;
            Server.NextFrame(() => _ = TryRegisterCookieAsync());
        }

        private void OnPlayerCookiesCached(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.SteamID == 0)
            {
                return;
            }

            var steamId = player.SteamID.ToString();

            try
            {
                var rawValue = _cookieId == -1 || _clientprefsApi == null ? string.Empty : _clientprefsApi.GetPlayerCookie(player, _cookieId);
                var preferences = PlayerDisplayPreferences.FromJson(rawValue, _defaults);

                _preferenceCache[steamId] = preferences;

                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    _playersWithStoredPrefs.Add(steamId);
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[SpectatorList] Error caching preferences for {steamId}: {ex.Message}");
            }
        }

        private async Task<bool> EnsureCookieRegisteredAsync()
        {
            if (_cookieId != -1)
            {
                return true;
            }

            if (_clientprefsApi == null)
            {
                return false;
            }

            const int maxAttempts = 50;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                ScheduleRegisterCookie();

                if (_cookieId != -1)
                {
                    return true;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }
                catch
                {
                    break;
                }
            }

            return _cookieId != -1;
        }

        private void ScheduleRegisterCookie()
        {
            if (_registerScheduled || _cookieId != -1)
            {
                return;
            }

            _registerScheduled = true;
            Server.NextFrame(() =>
            {
                _registerScheduled = false;
                _ = TryRegisterCookieAsync();
            });
        }

        private async Task TryRegisterCookieAsync()
        {
            if (_clientprefsApi == null || _cookieId != -1)
            {
                return;
            }

            if (!_databaseReady || _registrationInProgress)
            {
                return;
            }

            _registrationInProgress = true;

            try
            {
                var existingId = _clientprefsApi.FindPlayerCookie(CookieName);
                if (existingId != -1)
                {
                    _cookieId = existingId;
                    return;
                }

                var cookieId = await _clientprefsApi.RegPlayerCookieAsync(CookieName, CookieDescription, CookieAccess.CookieAccess_Public);
                if (cookieId != -1)
                {
                    _cookieId = cookieId;
                }
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[SpectatorList] Failed to register clientprefs cookie: {ex.Message}");
            }
            finally
            {
                _registrationInProgress = false;
            }
        }
    }
}
