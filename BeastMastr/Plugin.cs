using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using BeastMastr.UI;
using BeastMastr.UI.Tabs;

namespace BeastMastr;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/beastmastr";

    private readonly WindowSystem windowSystem = new("BeastMastr");
    private readonly MainWindow mainWindow;

    public Configuration Configuration { get; }

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        ECommonsMain.Init(pluginInterface, this);

        Configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var tabs = new List<ITab>();
        if (Configuration.ShowDataTab)
        {
            tabs.Add(new SheetsTab(Configuration));
            tabs.Add(new AddonsTab(Configuration));
        }

        tabs.Add(new SettingsTab(Configuration));

        mainWindow = new MainWindow(tabs);
        windowSystem.AddWindow(mainWindow);

        Services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open BeastMastr. Also: /beastmastr settings.",
        });

        Services.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "settings":
            case "config":
                OpenSettings();
                break;

            default:
                ToggleMainUi();
                break;
        }
    }

    private void ToggleMainUi() => mainWindow.Toggle();

    private void OpenSettings() => mainWindow.OpenAt("settings");

    public void Dispose()
    {
        Services.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;

        Services.Commands.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();

        ECommonsMain.Dispose();
    }
}
