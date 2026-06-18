using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Menu;
using Clientprefs.API;

using SpectatorList.Configs;
using SpectatorList.Managers;
using SpectatorList.Models;
using SpectatorList.Services;

namespace SpectatorList;

[MinimumApiVersion(369)]
public class SpectatorList : BasePlugin, IPluginConfig<SpectatorConfig>
{
    public override string ModuleName => "SpectatorList";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "luca.uy, Marchand";

    public SpectatorConfig Config { get; set; } = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _updateTimer;
    private Dictionary<int, List<string>> _lastSpectatorLists = new();
    private DisplayManager? _displayManager;
    private IClientprefsApi? _clientPrefsApi;
    private readonly PluginCapability<IClientprefsApi> _clientPrefsCapability = new("Clientprefs");

    public void OnConfigParsed(SpectatorConfig config)
    {
        Config = config;
        if (_clientPrefsApi != null)
        {
            InitializeStorage();
        }
    }

    private void InitializeStorage()
    {
        _displayManager?.Dispose();
        _displayManager = null;

        if (_clientPrefsApi == null)
        {
            Server.PrintToConsole("[SpectatorList] Clientprefs API not available; preferences will use configuration defaults for this session.");
            return;
        }

        var storage = StorageFactory.CreateStorageService(Config, _clientPrefsApi);
        if (storage == null)
        {
            return;
        }

        _displayManager = new DisplayManager(Config, this, storage);
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _displayManager?.CleanupAllDisplays();
        _lastSpectatorLists.Clear();
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _displayManager?.CleanupAllDisplays();
        _lastSpectatorLists.Clear();
        return HookResult.Continue;
    }

    public override void Load(bool hotReload)
    {
        foreach (var command in Config.Commands)
        {
            AddCommand(command, "Toggle spectator list display", OnSpectatorListCommand);
        }

        StartUpdateTimer();

        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _clientPrefsApi = _clientPrefsCapability.Get();
        if (_clientPrefsApi == null)
        {
            Server.PrintToConsole("[SpectatorList] Clientprefs plugin not found. Player preferences will not be persisted.");
        }

        InitializeStorage();
    }

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _updateTimer = null;

        _displayManager?.Dispose();
        _displayManager = null;
    }

    private void StartUpdateTimer()
    {
        _updateTimer?.Kill();
        _updateTimer = AddTimer(Config.Update.CheckInterval, CheckAndUpdateSpectatorLists, TimerFlags.REPEAT);
    }

    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    private void OnSpectatorListCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid)
            return;

        if (!string.IsNullOrEmpty(Config.CommandPermissions) && !AdminManager.PlayerHasPermissions(player, Config.CommandPermissions))
        {
            commandInfo.ReplyToCommand($"{Localizer["prefix"]} {Localizer["no_permissions"]}");
            return;
        }

        if (!string.IsNullOrEmpty(Config.CanViewList) && !AdminManager.PlayerHasPermissions(player, Config.CanViewList))
        {
            commandInfo.ReplyToCommand($"{Localizer["prefix"]} {Localizer["no_permissions"]}");
            return;
        }

        if (_displayManager == null)
        {
            commandInfo.ReplyToCommand($"{Localizer["prefix"]} Display manager not initialized");
            return;
        }

        if (commandInfo.ArgCount > 1)
        {
            _ = HandlePreferenceCommand(player, commandInfo);
            return;
        }

        _ = ShowSpectatorMenuAsync(player);
    }

    private async Task ShowSpectatorMenuAsync(CCSPlayerController player)
    {
        if (_displayManager == null || !player.IsValid)
        {
            return;
        }

        var preferences = await _displayManager.GetPlayerPreferencesAsync(player);

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
            {
                return;
            }

            var menu = CreateMenu("Spectator List");

            AddToggleMenuItem(menu, preferences);
            AddDisplayTypeMenuItem(menu);

            menu.Display(player, 0);
        });
    }

    private BaseMenu CreateMenu(string title, BaseMenu? prevMenu = null)
    {
        var menuType = string.IsNullOrWhiteSpace(Config.Menu.MenuType) ? "WasdMenu" : Config.Menu.MenuType;
        BaseMenu menu;
        try
        {
            menu = MenuManager.MenuByType(menuType, title, this);
        }
        catch
        {
            menu = new WasdMenu(title, this);
        }

        if (menu is WasdMenu wasdMenu)
        {
            wasdMenu.WasdMenu_FreezePlayer = Config.Menu.FreezePlayer;
        }

        menu.PrevMenu = prevMenu;
        return menu;
    }

    private void AddToggleMenuItem(BaseMenu menu, PlayerDisplayPreferences preferences)
    {
        var toggleOption = menu.AddItem(BuildToggleOptionText(preferences.Enabled), async (p, option) =>
        {
            await HandleToggleMenuSelectionAsync(p, option, menu);
        });

        toggleOption.PostSelectAction = PostSelectAction.Nothing;
    }

    private void AddDisplayTypeMenuItem(BaseMenu menu)
    {
        var displayTypeOption = menu.AddItem(ColorizeText("Display Type", "White"), async (p, _) =>
        {
            await ShowDisplayTypeMenuAsync(p, menu);
        });

        displayTypeOption.PostSelectAction = PostSelectAction.Nothing;
    }

    private async Task HandleToggleMenuSelectionAsync(CCSPlayerController player, ItemOption option, BaseMenu menu)
    {
        if (_displayManager == null || player == null || !player.IsValid)
        {
            return;
        }

        var updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
        {
            prefs.Enabled = !prefs.Enabled;
        });

        if (!player.IsValid)
        {
            return;
        }

        option.Text = BuildToggleOptionText(updatedPreferences.Enabled);

        var message = updatedPreferences.Enabled
            ? Localizer["spectator_display_enabled"]
            : Localizer["spectator_display_disabled"];

        SendPreferenceFeedback(player, message);

        if (!updatedPreferences.Enabled)
        {
            _displayManager.CleanupPlayerDisplay(player);
        }
        else
        {
            Server.NextFrame(() =>
            {
                try
                {
                    if (player.IsValid && _displayManager != null)
                    {
                        var spectators = GetPlayersSpectating(player);
                        if (spectators.Count > 0)
                        {
                            _ = _displayManager.DisplaySpectatorListAsync(player, spectators);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[SpectatorList] Error displaying spectators after enabling via menu: {ex.Message}");
                }
            });
        }

        RefreshMenu(menu, player);
    }

    private async Task ShowDisplayTypeMenuAsync(CCSPlayerController player, BaseMenu parentMenu)
    {
        if (_displayManager == null || player == null || !player.IsValid)
        {
            return;
        }

        var preferences = await _displayManager.GetPlayerPreferencesAsync(player);

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
            {
                return;
            }

            var menu = CreateMenu("Spectator List - Display Type", parentMenu);

            ItemOption? chatOption = null;
            ItemOption? hudOption = null;
            ItemOption? bothOption = null;

            chatOption = menu.AddItem(BuildDisplayTypeOptionText("CHAT", preferences.SendToChat), async (p, _) =>
            {
                await HandleDisplayTypeSelectionAsync(p, DisplaySelection.Chat, menu, chatOption!, hudOption!, bothOption!);
            });
            chatOption.PostSelectAction = PostSelectAction.Nothing;

            hudOption = menu.AddItem(BuildDisplayTypeOptionText("HUD", preferences.UseCenterMessage), async (p, _) =>
            {
                await HandleDisplayTypeSelectionAsync(p, DisplaySelection.Hud, menu, chatOption!, hudOption!, bothOption!);
            });
            hudOption.PostSelectAction = PostSelectAction.Nothing;

            var bothEnabled = preferences.SendToChat && preferences.UseCenterMessage;
            bothOption = menu.AddItem(BuildDisplayTypeOptionText("BOTH", bothEnabled), async (p, _) =>
            {
                await HandleDisplayTypeSelectionAsync(p, DisplaySelection.Both, menu, chatOption!, hudOption!, bothOption!);
            });
            bothOption.PostSelectAction = PostSelectAction.Nothing;

            menu.Display(player, 0);
        });
    }

    private async Task HandleDisplayTypeSelectionAsync(CCSPlayerController player, DisplaySelection selection, BaseMenu menu, ItemOption chatOption, ItemOption hudOption, ItemOption bothOption)
    {
        if (_displayManager == null || player == null || !player.IsValid)
        {
            return;
        }

        bool appliedFallbackToBoth = false;

        var updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
        {
            switch (selection)
            {
                case DisplaySelection.Chat:
                    prefs.SendToChat = !prefs.SendToChat;
                    break;
                case DisplaySelection.Hud:
                    prefs.UseCenterMessage = !prefs.UseCenterMessage;
                    break;
                case DisplaySelection.Both:
                    var enableBoth = !(prefs.SendToChat && prefs.UseCenterMessage);
                    prefs.SendToChat = enableBoth;
                    prefs.UseCenterMessage = enableBoth;
                    break;
            }

            if (!prefs.SendToChat && !prefs.UseCenterMessage)
            {
                prefs.SendToChat = true;
                prefs.UseCenterMessage = true;
                appliedFallbackToBoth = true;
            }
        });

        if (!player.IsValid)
        {
            return;
        }

        UpdateDisplayTypeOptionColors(chatOption, hudOption, bothOption, updatedPreferences);
        AnnounceDisplayChoice(player, selection, updatedPreferences);
        if (appliedFallbackToBoth)
        {
            SendPreferenceFeedback(player, "No display outputs selected; enabling BOTH as fallback");
        }
        if (updatedPreferences.Enabled)
        {
            Server.NextFrame(() =>
            {
                try
                {
                    if (player.IsValid && _displayManager != null)
                    {
                        var spectators = GetPlayersSpectating(player);
                        if (spectators.Count > 0)
                        {
                            _ = _displayManager.DisplaySpectatorListAsync(player, spectators);
                        }
                        else
                        {
                            _displayManager.CleanupPlayerDisplay(player);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[SpectatorList] Error displaying spectators after display type change: {ex.Message}");
                }
            });
        }
        RefreshMenu(menu, player);
    }

    private void AnnounceDisplayChoice(CCSPlayerController player, DisplaySelection selection, PlayerDisplayPreferences preferences)
    {
        var choiceLabel = selection switch
        {
            DisplaySelection.Chat => "CHAT",
            DisplaySelection.Hud => "HUD",
            DisplaySelection.Both => "BOTH",
            _ => "DISPLAY"
        };

        var enabled = selection switch
        {
            DisplaySelection.Chat => preferences.SendToChat,
            DisplaySelection.Hud => preferences.UseCenterMessage,
            DisplaySelection.Both => preferences.SendToChat && preferences.UseCenterMessage,
            _ => false
        };

        var status = enabled ? "ENABLED" : "DISABLED";
        SendPreferenceFeedback(player, $"{choiceLabel} {status}");
    }

    private static void UpdateDisplayTypeOptionColors(ItemOption chatOption, ItemOption hudOption, ItemOption bothOption, PlayerDisplayPreferences preferences)
    {
        chatOption.Text = BuildDisplayTypeOptionText("CHAT", preferences.SendToChat);
        hudOption.Text = BuildDisplayTypeOptionText("HUD", preferences.UseCenterMessage);
        bothOption.Text = BuildDisplayTypeOptionText("BOTH", preferences.SendToChat && preferences.UseCenterMessage);
    }

    private static string BuildDisplayTypeOptionText(string label, bool enabled)
    {
        return ColorizeText(label, enabled ? "Lime" : "LightRed");
    }

    private static string BuildToggleOptionText(bool enabled)
    {
        return ColorizeText(enabled ? "Toggle OFF" : "Toggle ON", enabled ? "LightRed" : "Lime");
    }

    private static string ColorizeText(string text, string color)
    {
        return $"<font color='{ResolveColor(color)}'>{text}</font>";
    }

    private static string ResolveColor(string color)
    {
        return color.Equals("LightRed", StringComparison.OrdinalIgnoreCase) ? "#ff4d4d" : color;
    }

    private enum DisplaySelection
    {
        Chat,
        Hud,
        Both
    }

    private static void RefreshMenu(BaseMenu menu, CCSPlayerController player)
    {
        Server.NextFrame(() =>
        {
            if (!player.IsValid)
            {
                return;
            }

            var activeMenu = MenuManager.GetActiveMenu(player);
            if (activeMenu != null && ReferenceEquals(activeMenu.Menu, menu))
            {
                activeMenu.Display();
            }
        });
    }

    private async Task HandleToggleCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        try
        {
            if (_displayManager == null)
            {
                Server.NextFrame(() =>
                {
                    if (player.IsValid)
                    {
                        player.PrintToChat($"{Localizer["prefix"]} Display manager not initialized");
                    }
                });
                return;
            }

            var preferences = await _displayManager.TogglePlayerDisplayAsync(player);
            string message = preferences.Enabled ? Localizer["spectator_display_enabled"] : Localizer["spectator_display_disabled"];

            Server.NextFrame(() =>
            {
                try
                {
                    if (player.IsValid)
                    {
                        player.PrintToChat($"{Localizer["prefix"]} {message}");
                    }
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[SpectatorList] Error sending response: {ex.Message}");
                }
            });

            if (preferences.Enabled)
            {
                Server.NextFrame(() =>
                {
                    try
                    {
                        if (player.IsValid && _displayManager != null)
                        {
                            var spectators = GetPlayersSpectating(player);
                            if (spectators.Count > 0)
                            {
                                _ = _displayManager.DisplaySpectatorListAsync(player, spectators);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.PrintToConsole($"[SpectatorList] Error displaying spectators: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
            {
                Server.PrintToConsole($"[SpectatorList] Error in HandleToggleCommand: {ex.Message}");
                if (player.IsValid)
                {
                    player.PrintToChat($"{Localizer["prefix"]} An error occurred while processing your request");
                }
            });
        }
    }

    private async Task HandlePreferenceCommand(CCSPlayerController player, CommandInfo commandInfo)
    {
        try
        {
            if (_displayManager == null)
            {
                commandInfo.ReplyToCommand($"{Localizer["prefix"]} Display manager not initialized");
                return;
            }

            if (commandInfo.ArgCount <= 1)
            {
                await HandleToggleCommand(player, commandInfo);
                return;
            }

            var action = commandInfo.GetArg(1).ToLowerInvariant();
            bool? explicitValue = null;

            if (commandInfo.ArgCount > 2 && TryParsePreferenceValue(commandInfo.GetArg(2), out var parsedValue))
            {
                explicitValue = parsedValue;
            }

            PlayerDisplayPreferences? updatedPreferences = null;

            switch (action)
            {
                case "chat":
                case "sendtochat":
                    updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
                    {
                        prefs.SendToChat = explicitValue ?? !prefs.SendToChat;
                    });
                    SendPreferenceFeedback(player, $"Chat output {(updatedPreferences.SendToChat ? "enabled" : "disabled")}");
                    break;

                case "center":
                case "centermessage":
                    updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
                    {
                        prefs.UseCenterMessage = explicitValue ?? !prefs.UseCenterMessage;
                    });
                    SendPreferenceFeedback(player, $"Center message {(updatedPreferences.UseCenterMessage ? "enabled" : "disabled")}");
                    break;

                case "screen":
                case "screenview":
                    updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
                    {
                        prefs.UseScreenView = explicitValue ?? !prefs.UseScreenView;
                    });
                    SendPreferenceFeedback(player, $"Screen view {(updatedPreferences.UseScreenView ? "enabled" : "disabled")}");
                    break;

                case "reset":
                    var defaults = PlayerDisplayPreferences.FromDefaults(Config.Display);
                    updatedPreferences = await _displayManager.UpdatePreferencesAsync(player, prefs =>
                    {
                        prefs.Enabled = defaults.Enabled;
                        prefs.SendToChat = defaults.SendToChat;
                        prefs.UseCenterMessage = defaults.UseCenterMessage;
                        prefs.UseScreenView = defaults.UseScreenView;
                    });
                    SendPreferenceFeedback(player, "Spectator settings reset to defaults");
                    break;

                default:
                    await HandleToggleCommand(player, commandInfo);
                    return;
            }

            if (updatedPreferences != null && updatedPreferences.Enabled)
            {
                Server.NextFrame(() =>
                {
                    try
                    {
                        if (player.IsValid && _displayManager != null)
                        {
                            var spectators = GetPlayersSpectating(player);
                            if (spectators.Count > 0)
                            {
                                _ = _displayManager.DisplaySpectatorListAsync(player, spectators);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.PrintToConsole($"[SpectatorList] Error displaying spectators after preference update: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
            {
                Server.PrintToConsole($"[SpectatorList] Error updating preferences: {ex.Message}");
                if (player.IsValid)
                {
                    player.PrintToChat($"{Localizer["prefix"]} An error occurred while updating your preferences");
                }
            });
        }
    }

    private void SendPreferenceFeedback(CCSPlayerController player, string message)
    {
        Server.NextFrame(() =>
        {
            if (player.IsValid)
            {
                player.PrintToChat($"{Localizer["prefix"]} {message}");
            }
        });
    }

    private static bool TryParsePreferenceValue(string value, out bool result)
    {
        result = false;

        switch (value.ToLowerInvariant())
        {
            case "1":
            case "on":
            case "true":
            case "yes":
            case "enable":
            case "enabled":
                result = true;
                return true;
            case "0":
            case "off":
            case "false":
            case "no":
            case "disable":
            case "disabled":
                result = false;
                return true;
            default:
                return false;
        }
    }

    private void CheckAndUpdateSpectatorLists()
    {
        if (_displayManager == null) return;

        var alivePlayers = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive).ToList();
        var spectatorMap = BuildSpectatorMap();

        foreach (var player in alivePlayers)
        {
            if (!spectatorMap.TryGetValue(player.Slot, out var currentSpectators))
                currentSpectators = new List<CCSPlayerController>();

            var currentSpectatorNames = currentSpectators.Select(s => s.PlayerName).ToList();

            bool hasChanged = false;
            if (_lastSpectatorLists.ContainsKey(player.Slot))
            {
                var lastList = _lastSpectatorLists[player.Slot];
                hasChanged = !currentSpectatorNames.SequenceEqual(lastList);
            }
            else
            {
                hasChanged = currentSpectatorNames.Count > 0;
            }

            _lastSpectatorLists[player.Slot] = currentSpectatorNames;

            if (hasChanged && Config.Update.ShowOnChange)
            {
                if (currentSpectators.Count > 0)
                {
                    _ = _displayManager.DisplaySpectatorListAsync(player, currentSpectators);
                }
                else
                {
                    _displayManager.CleanupPlayerDisplay(player);
                }
            }
        }

        var alivePlayerSlots = alivePlayers.Select(p => p.Slot).ToHashSet();
        var slotsToRemove = _lastSpectatorLists.Keys.Where(slot => !alivePlayerSlots.Contains(slot)).ToList();
        foreach (var slot in slotsToRemove)
        {
            _lastSpectatorLists.Remove(slot);
        }

        if (Config.Update.ShowPeriodic)
        {
            _ = ShowPeriodicSpectatorLists();
        }
    }

    private async Task ShowPeriodicSpectatorLists()
    {
        if (_displayManager == null) return;

        bool ShouldShowPeriodic()
        {
            return Server.CurrentTime % Config.Update.PeriodicInterval < Config.Update.CheckInterval;
        }

        if (!ShouldShowPeriodic())
            return;

        var alivePlayers = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive).ToList();
        var spectatorMap = BuildSpectatorMap();
        var tasks = new List<Task>();

        foreach (var player in alivePlayers)
        {
            if (spectatorMap.TryGetValue(player.Slot, out var spectators) && spectators.Count > 0)
            {
                tasks.Add(_displayManager.DisplaySpectatorListAsync(player, spectators));
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            Server.NextFrame(() =>
            {
                Server.PrintToConsole($"[SpectatorList] Error in ShowPeriodicSpectatorLists: {errorMessage}");
            });
        }
    }

    private List<CCSPlayerController> GetPlayersSpectating(CCSPlayerController targetPlayer)
    {
        var spectators = new List<CCSPlayerController>();

        if (targetPlayer?.PlayerPawn?.Value == null)
            return spectators;

        var allPlayers = Utilities.GetPlayers();
        foreach (var player in allPlayers)
        {
            if (!player.IsValid || player.Slot == targetPlayer.Slot)
                continue;

            // Ignore players still on the team selection screen (unassigned team).
            if (player.TeamNum == 0)
                continue;

            if (player.PawnIsAlive)
                continue;

            bool isSpectating = false;
            if (player.PlayerPawn?.Value != null)
            {
                var observerServices = player.PlayerPawn.Value.ObserverServices;
                if (observerServices != null)
                {
                    var observerTarget = observerServices.ObserverTarget;
                    if (observerTarget?.Value?.Handle == targetPlayer.PlayerPawn.Value.Handle)
                    {
                        isSpectating = true;
                    }
                }
            }

            if (!isSpectating && player.ObserverPawn?.Value != null)
            {
                var observerServices = player.ObserverPawn.Value.ObserverServices;
                if (observerServices != null)
                {
                    var observerTarget = observerServices.ObserverTarget;
                    if (observerTarget?.Value?.Handle == targetPlayer.PlayerPawn.Value.Handle)
                    {
                        isSpectating = true;
                    }
                }
            }

            if (isSpectating)
            {
                spectators.Add(player);
            }
        }

        return spectators;
    }

    private static nint? GetObserverTargetHandle(CCSPlayerController player)
    {
        var playerPawnTarget = player.PlayerPawn?.Value?.ObserverServices?.ObserverTarget;
        if (playerPawnTarget?.Value != null)
            return playerPawnTarget.Value.Handle;

        var observerPawnTarget = player.ObserverPawn?.Value?.ObserverServices?.ObserverTarget;
        if (observerPawnTarget?.Value != null)
            return observerPawnTarget.Value.Handle;

        return null;
    }

    private Dictionary<int, List<CCSPlayerController>> BuildSpectatorMap()
    {
        var map = new Dictionary<int, List<CCSPlayerController>>();
        var allPlayers = Utilities.GetPlayers();

        var pawnHandleToTarget = new Dictionary<nint, CCSPlayerController>();
        foreach (var player in allPlayers)
        {
            if (!player.IsValid || !player.PawnIsAlive)
                continue;

            var pawn = player.PlayerPawn?.Value;
            if (pawn != null)
                pawnHandleToTarget[pawn.Handle] = player;
        }

        foreach (var player in allPlayers)
        {
            if (!player.IsValid || player.TeamNum == 0 || player.PawnIsAlive)
                continue;

            var observerTargetHandle = GetObserverTargetHandle(player);
            if (observerTargetHandle == null)
                continue;

            if (pawnHandleToTarget.TryGetValue(observerTargetHandle.Value, out var target) && target.Slot != player.Slot)
            {
                if (!map.TryGetValue(target.Slot, out var list))
                {
                    list = new List<CCSPlayerController>();
                    map[target.Slot] = list;
                }

                list.Add(player);
            }
        }

        return map;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            _displayManager?.OnPlayerDisconnect(player);
            _lastSpectatorLists.Remove(player.Slot);
            Server.NextFrame(UpdateAllSpectatorLists);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            _displayManager?.CleanupPlayerDisplay(player);
            Server.NextFrame(UpdateAllSpectatorLists);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            _displayManager?.CleanupPlayerDisplay(player);
            Server.NextFrame(UpdateAllSpectatorLists);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            Server.NextFrame(UpdateAllSpectatorLists);
        }
        return HookResult.Continue;
    }

    private void UpdateAllSpectatorLists()
    {
        if (_displayManager == null) return;

        var alivePlayers = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive).ToList();
        var allPlayers = Utilities.GetPlayers().Where(p => p.IsValid).ToList();

        foreach (var player in allPlayers)
        {
            if (!player.PawnIsAlive)
            {
                _displayManager.CleanupPlayerDisplay(player);
            }
        }

        var spectatorMap = BuildSpectatorMap();
        var tasks = new List<Task>();

        foreach (var player in alivePlayers)
        {
            if (!spectatorMap.TryGetValue(player.Slot, out var currentSpectators))
                currentSpectators = new List<CCSPlayerController>();

            var currentSpectatorNames = currentSpectators.Select(s => s.PlayerName).ToList();
            var hasChanged = !_lastSpectatorLists.TryGetValue(player.Slot, out var lastList) ||
                             !currentSpectatorNames.SequenceEqual(lastList);

            if (currentSpectators.Count > 0 && hasChanged)
            {
                tasks.Add(_displayManager.DisplaySpectatorListAsync(player, currentSpectators));
            }
            else
            {
                _displayManager.CleanupPlayerDisplay(player);
            }

            _lastSpectatorLists[player.Slot] = currentSpectatorNames;
        }

        var alivePlayerSlots = alivePlayers.Select(p => p.Slot).ToHashSet();
        var slotsToRemove = _lastSpectatorLists.Keys.Where(slot => !alivePlayerSlots.Contains(slot)).ToList();
        foreach (var slot in slotsToRemove)
        {
            _lastSpectatorLists.Remove(slot);
        }

        if (tasks.Count > 0)
        {
            _ = Task.WhenAll(tasks).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Server.NextFrame(() =>
                    {
                        Server.PrintToConsole($"[SpectatorList] Error in UpdateAllSpectatorLists: {t.Exception.Message}");
                    });
                }
            });
        }
    }
}
