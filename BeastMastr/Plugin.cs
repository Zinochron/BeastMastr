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
    /// <summary>Built in the constructor body: it subscribes on construction. See the note above.</summary>
    private readonly EventRecorder recorder;
    private readonly ActionWatcher actionWatcher;
    private readonly RunRecorder runRecorder;
    private readonly FightSelector fightSelector;
    private readonly RankWatcher rankWatcher;
    private readonly EnemyCache enemies;
    private readonly BoardCache boardCache;
    private readonly BoardModel boardModel;
    private readonly BoardTerrain boardTerrain;
    private readonly RouteKeeper routeKeeper;
    private readonly RouteOverlay routeOverlay;
    private readonly TeamSelector teamSelector;
    private readonly HealthSelector healthSelector;
    private readonly DifficultySelector difficultySelector;
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
        actionWatcher = new ActionWatcher();
        runRecorder = new RunRecorder(recorder, actionWatcher);
        fightSelector = new FightSelector(Configuration, Catalog);
        rankWatcher = new RankWatcher(Configuration);
        enemies = new EnemyCache();
        boardCache = new BoardCache(Configuration);
        boardModel = new BoardModel(Configuration);
        boardTerrain = new BoardTerrain(Configuration, boardModel);
        routeKeeper = new RouteKeeper(Configuration, Catalog, boardModel, boardTerrain);
        teamSelector = new TeamSelector(Configuration, Catalog, rankWatcher);
        healthSelector = new HealthSelector(Catalog);
        difficultySelector = new DifficultySelector(Configuration);
        rankPuller = new RankPuller(rankWatcher);

        kamiToolKitReady = KamiToolKitLibrary.InitializeAsync(pluginInterface);
        notebook = new MonsterNotebookDecorator(Configuration, Catalog, Filter,
                                                () => kamiToolKitReady.IsCompletedSuccessfully);
        actionButtons = new ActionButtons(Configuration, teamSelector, fightSelector, healthSelector,
                                          () => kamiToolKitReady.IsCompletedSuccessfully);
        carryMenu = new CarryContextMenu(Configuration, Catalog, recorder);
        nextRoom = new NextRoomPanel(Configuration, boardCache, enemies, boardModel, routeKeeper,
                                     () => kamiToolKitReady.IsCompletedSuccessfully);
        routeOverlay = new RouteOverlay(Configuration, boardModel, routeKeeper,
                                        () => kamiToolKitReady.IsCompletedSuccessfully);

        var tabs = new List<ITab>
        {
            new BeastsTab(Catalog, Filter, Configuration, rankWatcher, rankPuller),
            new RunTab(Configuration, boardModel, boardTerrain, routeKeeper, routeOverlay, runRecorder),
        };
        if (Configuration.ShowDataTab)
        {
            tabs.Add(new SheetsTab(Configuration));
            tabs.Add(new AddonsTab(Configuration, delayedSweep));
            tabs.Add(new BoardTab(Catalog, recorder, runRecorder, rankWatcher, enemies, boardCache));
        }

        tabs.Add(new SettingsTab(Configuration, fightSelector, teamSelector, difficultySelector, nextRoom));

        mainWindow = new MainWindow(tabs);
        windowSystem.AddWindow(mainWindow);

        Services.Commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open BeastMastr. Also: /beastmastr beasts, /beastmastr settings, " +
                          "/beastmastr room (open or close the next-room window), " +
                          "/beastmastr record (start or stop recording a run to a file), " +
                          "/beastmastr scan (scan the board's ground with vnavmesh), /beastmastr run (the Run tab).",
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

            case "room":
                nextRoom.Toggle();
                break;

            case "record":
                runRecorder.Toggle();
                break;

            case "scan":
                boardTerrain.RequestScan();
                Services.Chat.Print($"[BeastMastr] {boardTerrain.Status}");
                break;

            case "run":
                mainWindow.OpenAt("run");
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
        runRecorder.Dispose();
        actionWatcher.Dispose();
        recorder.Dispose();
        fightSelector.Dispose();
        rankWatcher.Dispose();
        enemies.Dispose();
        boardCache.Dispose();
        routeKeeper.Dispose();
        boardTerrain.Dispose();
        boardModel.Dispose();
        teamSelector.Dispose();
        healthSelector.Dispose();
        difficultySelector.Dispose();
        actionButtons.Dispose();
        carryMenu.Dispose();
        nextRoom.Dispose();
        routeOverlay.Dispose();
        rankPuller.Dispose();
        KamiToolKitLibrary.Dispose();

        ECommonsMain.Dispose();
    }
}
