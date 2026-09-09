using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using BeastMastr.Data;
using BeastMastr.Native;
using BeastMastr.Rules;
using BeastMastr.UI;
using BeastMastr.UI.Tabs;

namespace BeastMastr;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/beastmastr";

    private readonly WindowSystem windowSystem = new("BeastMastr");
    private readonly MainWindow mainWindow;

    /// <summary>
    /// Assigned in the constructor body, never as a field initializer. Field initializers run
    /// before the constructor runs, which is before <c>Create&lt;Services&gt;()</c> has filled
    /// anything in — so anything reaching for a Dalamud service on construction has to be built
    /// after that call, or it dies on a null service and the plugin fails to load.
    /// </summary>
    private readonly DelayedSweep delayedSweep;

    public Configuration Configuration { get; }
    public BeastCatalog Catalog { get; }

    /// <summary>One filter, shared: what is typed in the Beasts tab dims the game's own bestiary.</summary>
    public BeastFilter Filter { get; } = new();

    private readonly MonsterNotebookDecorator notebook;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        ECommonsMain.Init(pluginInterface, this);

        Configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        delayedSweep = new DelayedSweep();
        Catalog = new BeastCatalog();
        notebook = new MonsterNotebookDecorator(Configuration, Catalog, Filter);

        var tabs = new List<ITab> { new BeastsTab(Catalog, Filter) };
        if (Configuration.ShowDataTab)
        {
            tabs.Add(new SheetsTab(Configuration));
            tabs.Add(new AddonsTab(Configuration, delayedSweep));
        }

        tabs.Add(new SettingsTab(Configuration));

        mainWindow = new MainWindow(tabs);
        windowSystem.AddWindow(mainWindow);

        Services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open BeastMastr. Also: /beastmastr beasts, /beastmastr settings.",
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

            case "beasts":
                mainWindow.OpenAt("beasts");
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
        delayedSweep.Dispose();
        notebook.Dispose();

        ECommonsMain.Dispose();
    }
}
