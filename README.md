# SpectatorList CS2
Shows real-time spectators both in chat messages and on-screen display with customizable permissions and exclusion flags.

> [!CAUTION]
> The current solution for `"UseScreenView"` does not work as well as it did before the update on 07/28/2025. It is recommended not to use this feature for now, pending a more stable method.

![Counter-strike 2 Screenshot 2025 07 06 - 19 34 39 42](https://github.com/user-attachments/assets/d8a908ea-7baa-4609-bdee-29545edd693e)

---

## 🚀 Installation

### Basic Installation
1. Install [CounterStrike Sharp](https://github.com/roflmuffin/CounterStrikeSharp) and [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
2. Download [SpectatorList.zip](https://github.com/wiruwiru/SpectatorList-CS2/releases/latest) from releases
3. Extract and upload to your game server
4. Start server and configure the generated config file

### Dependency: Clientprefs
- Install the [Clientprefs plugin](https://github.com/hey-cruze/Clientprefs) so player settings can persist.
- SpectatorList registers the cookie `spectatorlist_config` (id: `6`, description: `JSON SpectatorList settings`, access: public).
- Each player stores a single JSON blob in `clientprefs_playerdata.value`, for example `{"Enabled":false,"SendToChat":true,"UseCenterMessage":false,"UseScreenView":true}`.

---

## ⚙️ Storage (Clientprefs)

- Preferences are stored via the Clientprefs cookie `spectatorlist_config` (id `6`) with description `JSON SpectatorList settings` and public access.
- One row per steamid lives in `clientprefs_playerdata`, storing a JSON blob of all toggleable options.
- No data is created for a player until they change a setting away from the defaults provided in `DisplaySettings`.

---

## 📋 Main Configuration Parameters
| Parameter            | Description                                                                                       | Required |
|----------------------|---------------------------------------------------------------------------------------------------|----------|
| `Commands`           | List of chat commands players can use to toggle spectator list display. (**Default**: `["css_speclist", "css_specs", "css_spectators"]`) | **YES**  |
| `CommandPermissions` | Permission flag required to use the toggle commands. Leave empty for all players. (**Default**: `"@css/vip"`) | **YES**  |
| `CanViewList`        | Permission flag required to view spectator lists (both chat and screen). Leave empty for all players. (**Default**: `"@css/vip"`) | **YES**  |
| `UpdateSettings`     | Configuration for automatic updates and periodic displays. | **YES**  |
| `DisplaySettings`    | Configuration for how spectator lists are displayed and the defaults applied to new players. | **YES**  |

### Update Settings Parameters
| Parameter         | Description                                                                                         | Required |
|-------------------|-----------------------------------------------------------------------------------------------------|----------|
| `CheckInterval`   | How often (in seconds) to check for spectator changes. (**Default**: `2.0`) | **YES**  |
| `ShowOnChange`    | Show spectator list automatically when spectators change. (**Default**: `true`) | **YES**  |
| `ShowPeriodic`    | Show spectator list at regular intervals even without changes. (**Default**: `false`) | **YES**  |
| `PeriodicInterval` | Interval (in seconds) for periodic displays when `ShowPeriodic` is enabled. (**Default**: `5.0`) | **YES**  |

### Display Settings Parameters
| Parameter         | Description                                                                                         | Required |
|-------------------|-----------------------------------------------------------------------------------------------------|----------|
| `ExclusionFlag`   | Players with this flag will be hidden from spectator lists. (**Default**: `"@css/generic"`) | **YES**  |
| `MaxNamesInMessage` | Maximum number of spectator names to show before showing "and X more...". (**Default**: `5`) | **YES**  |
| `SendToChat`      | Enable/disable chat messages for spectator lists. (**Default**: `false`) | **YES**  |
| `UseCenterMessage` | Enable/disable center screen HTML message display for spectator lists. (**Default**: `false`) | **YES**  |
| `CenterMessageDuration` | Duration (in seconds) to show center message before auto-hiding. Set to 0 for permanent display. (**Default**: `5.0`) | **YES**  |
| `CenterMessage` | Custom message template for center messages. Use placeholders: `{TITLE}`, `{SPECTATORS}`, `{COUNT}`. (**Default**: `"⚠ Spectators: {SPECTATORS}"`) | **YES**  |
| `CenterMessageDuration` | Duration (seconds) to auto-hide the center message. Set to `0` to keep it visible while spectators are present. (**Default**: `0.0`) | **YES**  |
| `UseScreenView`   | Enable/disable on-screen floating text display. (**Default**: `true`) | **YES**  |
| `ScreenViewSettings` | Configuration for on-screen display positioning and appearance. | **YES**  |

### Screen View Settings Parameters
| Parameter         | Description                                                                                         | Required |
|-------------------|-----------------------------------------------------------------------------------------------------|----------|
| `PositionX`       | Horizontal position offset for on-screen display. (**Default**: `-8.0`) | **YES**  |
| `PositionY`       | Vertical position offset for on-screen display. (**Default**: `1.0`) | **YES**  |
| `TitleColor`      | Hex color code for the spectator list title. (**Default**: `"#FFD700"`) | **YES**  |
| `PlayerNameColor` | Hex color code for spectator names. (**Default**: `"#FFFFFF"`) | **YES**  |
| `CountColor`      | Hex color code for spectator count. (**Default**: `"#87CEEB"`) | **YES**  |

### Player Preference Commands
- `css_speclist`: Toggle the spectator list on/off for yourself.
- `css_speclist chat [on|off]`: Enable or disable chat output for your spectator list.
- `css_speclist center [on|off]`: Enable or disable the center message display for your spectator list.
- `css_speclist screen [on|off]`: Enable or disable the on-screen floating text display for your spectator list.
- `css_speclist reset`: Return to the defaults defined in `DisplaySettings`.

---

## 📊 Support

For issues, questions, or feature requests, please visit our [GitHub Issues](https://github.com/wiruwiru/SpectatorList-CS2/issues) page.
