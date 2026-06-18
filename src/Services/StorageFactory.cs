using CounterStrikeSharp.API;
using SpectatorList.Configs;
using Clientprefs.API;

namespace SpectatorList.Services
{
    public static class StorageFactory
    {
        public static IStorageService? CreateStorageService(SpectatorConfig config, IClientprefsApi? clientprefsApi)
        {
            if (clientprefsApi == null)
            {
                Server.PrintToConsole("[SpectatorList] Clientprefs API not available. Preferences will fallback to defaults.");
                return null;
            }

            return new ClientPrefsStorage(config, clientprefsApi);
        }
    }
}
