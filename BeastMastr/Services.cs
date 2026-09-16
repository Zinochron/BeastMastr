using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;

namespace BeastMastr;

/// <summary>
/// Dalamud services used across the plugin. Populated once via
/// <see cref="IDalamudPluginInterface.Create{T}"/> in <see cref="Plugin"/>'s constructor.
/// </summary>
public sealed class Services
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static INotificationManager Notifications { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static IKeyState Keys { get; private set; } = null!;
    [PluginService] public static IGamepadState Gamepad { get; private set; } = null!;
}
