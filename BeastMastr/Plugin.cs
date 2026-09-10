using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using KamiToolKit;
using BeastMastr.Automation;
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
    private readonly BoardOverlay boardOverlay;
    /// <summary>Built in the constructor body: it subscribes on construction. See the note above.</summary>
    private readonly EventRecorder recorder;
    private readonly FightSelector fightSelector;
    private readonly RankWatcher rankWatcher;
    private readonly EnemyCache enemies;
    private readonly BoardCache boardCache;
    private readonly TeamSelector teamSelector;
    private readonly ActionButtons actionButtons;
    private readonly CarryContextMenu carryMenu;
    private readonly NextRoomPanel nextRoom;
    private readonly RankPuller rankPuller;

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

    /// <summary>
    /// KamiToolKit has to be initialised before a single one of its nodes may be constructed, and
    /// it initialises asynchronously. Constructing a node early throws inside the constructor, which
    /// hands the runtime a half-built object it can only clean up in a finalizer — and that
    /// finalizer crashes the game. Nothing native is created until this has completed.
    /// </summary>
    private readonly Task kamiToolKitReady;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        ECommonsMain.Init(pluginInterface, this);

        Configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        delayedSweep = new DelayedSweep();
        Catalog = new BeastCatalog();

        recorder = new EventRecorder();
        fightSelector = new FightSelector(Configuration, Catalog);
        rankWatcher = new RankWatcher(Configuration);
        enemies = new EnemyCache();
        boardCache = new BoardCache(Configuration);
        teamSelector = new TeamSelector(Configuration, Catalog, rankWatcher);
        rankPuller = new RankPuller(rankWatcher);

        kamiToolKitReady = KamiToolKitLibrary.InitializeAsync(pluginInterface);
        notebook = new MonsterNotebookDecorator(Configuration, Catalog, Filter,
                                                () => kamiToolKitReady.IsCompletedSuccessfully);
        actionButtons = new ActionButtons(Configuration, teamSelector,
                                          () => kamiToolKitReady.IsCompletedSuccessfully);
        carryMenu = new CarryContextMenu(Configuration, Catalog, recorder);
        nextRoom = new NextRoomPanel(Configuration, boardCache, enemies,
                                     () => kamiToolKitReady.IsCompletedSuccessfully);

        var tabs = new List<ITab> { new BeastsTab(Catalog, Filter, Configuration, rankWatcher, rankPuller) };
        if (Configuration.ShowDataTab)
        {
            tabs.Add(new SheetsTab(Configuration));
            tabs.Add(new AddonsTab(Configuration, delayedSweep));
            tabs.Add(new BoardTab(Catalog, recorder, rankWatcher, enemies, boardCache));
        }

        tabs.Add(new SettingsTab(Configuration, fightSelector, teamSelector));

        mainWindow = new MainWindow(tabs);
        windowSystem.AddWindow(mainWindow);

        boardOverlay = new BoardOverlay(Configuration, enemies);
        windowSystem.AddWindow(boardOverlay);

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
        recorder.Dispose();
        fightSelector.Dispose();
        rankWatcher.Dispose();
        enemies.Dispose();
        boardCache.Dispose();
        teamSelector.Dispose();
        actionButtons.Dispose();
        carryMenu.Dispose();
        nextRoom.Dispose();
        rankPuller.Dispose();
        KamiToolKitLibrary.Dispose();

        ECommonsMain.Dispose();
    }
}
