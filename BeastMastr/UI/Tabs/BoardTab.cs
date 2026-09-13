using System;
using System.Linq;
using System.Numerics;
using System.Text;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BeastMastr.UI.Tabs;

/// <summary>
/// What the board readers currently see, and what is standing around in the world.
///
/// The first half says whether the overlay has anything to draw and whether tiles pair with rooms.
/// The second exists because the room markers in the Crucible overworld are not a window at all —
/// nothing in the HUD describes them — so identifying them means looking at the objects in the
/// world instead, and that has to be captured while a run is actually under way.
/// </summary>
public sealed class BoardTab : ITab
{
    private readonly BeastCatalog catalog;
    private readonly EventRecorder recorder;
    private readonly RankWatcher ranks;
    private readonly EnemyCache enemies;
    private readonly BoardCache board;

    private string lastPath = string.Empty;
    private float radius = 60f;

    public BoardTab(BeastCatalog catalog, EventRecorder recorder, RankWatcher ranks, EnemyCache enemies,
                    BoardCache board)
    {
        this.catalog = catalog;
        this.recorder = recorder;
        this.ranks = ranks;
        this.enemies = enemies;
        this.board = board;
    }

    public string Title => "Board";
    public string Id => "board";

    public void Draw()
    {
        if (ImGui.Button("Save all of this to a file"))
            lastPath = CaptureStore.Save("board", Report()) ?? "could not be written — see the log";

        if (lastPath.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(lastPath);
        }

        ImGuiHelpers.ScaledDummy(4f);
        DrawBoardCache();
        ImGuiHelpers.ScaledDummy(4f);
        DrawOpenAddons();
        ImGuiHelpers.ScaledDummy(4f);
        DrawReaders();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRecorder();
        ImGuiHelpers.ScaledDummy(4f);
        DrawEnemies();
        ImGuiHelpers.ScaledDummy(4f);
        DrawRoster();
        ImGuiHelpers.ScaledDummy(4f);
        DrawMarkers();
        ImGuiHelpers.ScaledDummy(4f);
        DrawObjects();
    }

    /// <summary>
    /// What the next-room panel is working from, spelled out.
    ///
    /// The move it thinks the run is on is derived — the board marks a current room and the rows are
    /// counted off against the room list — so it is shown rather than left implicit. A panel briefing
    /// the wrong room and a panel briefing the right one look identical until you can see which move
    /// it settled on.
    /// </summary>
    private void DrawBoardCache()
    {
        if (!ImGui.CollapsingHeader("The board being played", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled(board.Source);

        if (board.Rooms.Count == 0)
        {
            ImGui.TextDisabled("Open the board once and it will be kept, here and between sessions.");
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Forget this board"))
            board.Forget();

        var rows = StageMapReader.Rows();
        ImGui.TextUnformatted(
            $"Moves: {string.Join(", ", board.Moves)}   " +
            $"Run located on: {(board.CurrentMove < 0 ? "not yet" : $"move {board.CurrentMove}")}   " +
            $"Briefing: {(board.NextMove < 0 ? "nothing" : $"move {board.NextMove}")}");

        // The row check is the whole basis for reading a move off the board, so what it saw is
        // printed next to what it wanted: one row per move, plus the row the run starts on.
        ImGui.TextDisabled(
            $"Board rows now: {rows.Count} holding {string.Join("/", rows.Select(row => row.Count))} — " +
            $"expected {board.Moves.Count + 1} holding 1/" +
            string.Join("/", board.Moves.Select(move => board.OnMove(move).Count)));

        foreach (var move in board.Moves)
        {
            var here = move == board.NextMove ? " <- next" : string.Empty;
            foreach (var room in board.OnMove(move))
                ImGui.TextUnformatted($"  move {room.Move,2}  {room.Kind,-22} {room.Label}{here}");
        }
    }

    /// <summary>
    /// Every window the game has loaded, not only the XBM ones. The Crucible's overworld markers
    /// are not objects — the object table holds nothing but the player out there — and they are not
    /// in the HUD, so if any window draws them it is one nobody has named yet, and it will be in
    /// here.
    /// </summary>
    private static void DrawOpenAddons()
    {
        if (!ImGui.CollapsingHeader("Every window open right now", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var open = AddonReader.OpenAddonNames(string.Empty);
        ImGui.TextDisabled($"{open.Count} loaded");

        foreach (var chunk in open.Chunk(6))
            ImGui.TextUnformatted(string.Join("   ", chunk));
    }

    private void DrawReaders()
    {
        if (!ImGui.CollapsingHeader("Board readers", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var mapOpen = StageMapReader.IsOpen;
        var detailOpen = StageDetailReader.IsOpen;

        ImGui.TextUnformatted($"XBMStageMap open: {mapOpen}");
        ImGui.TextUnformatted($"XBMStageDetailList open: {detailOpen}");

        ImGui.TextUnformatted(CrucibleModeReader.Read() is { } mode
                                  ? $"Crucible mode: {mode.Label} ({mode.Index + 1} of {mode.Options.Count}) — " +
                                    string.Join(", ", mode.Options)
                                  : "Crucible mode: the board window does not offer the choice.");

        var rooms = StageMapReader.Read();
        var details = StageDetailReader.Read();

        ImGui.TextUnformatted($"Tiles read: {rooms.Count}   Rooms described: {details.Count}");

        if (rooms.Count > 0 && details.Count > 0 && rooms.Any(r => r.DetailIndex >= details.Count))
        {
            ImGui.TextColored(new Vector4(0.95f, 0.6f, 0.3f, 1f),
                              "Some tiles point past the end of the room list — the pairing is wrong.");
        }

        foreach (var room in rooms)
        {
            var info = details.ElementAtOrDefault(room.DetailIndex);
            ImGui.TextUnformatted(
                $"  tile idx {room.DetailIndex,2}{(room.IsCurrent ? " (here)" : "")} " +
                $"@{room.ScreenPosition.X:0}/{room.ScreenPosition.Y:0} {room.Size.X:0}x{room.Size.Y:0}" +
                $"  ->  {(info == null ? "(no room at that index)" : $"move {info.Move} {info.Kind} {info.Label}")}");
        }

        foreach (var room in details)
            ImGui.TextDisabled($"  [{room.Index,2}] move {room.Move,2} {room.Kind,-22} {room.Label} — {room.Detail}");
    }

    /// <summary>
    /// Everything nearby, with the screen position the game would put it at. If the Crucible's room
    /// markers are world objects, they are in here — and whatever identifies them is what a card
    /// would have to anchor to.
    /// </summary>
    private void DrawObjects()
    {
        if (!ImGui.CollapsingHeader("Objects in the world", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("within", ref radius, 5f, 200f, "%.0f yalms");

        var player = Services.Objects.LocalPlayer;
        if (player == null)
        {
            ImGui.TextDisabled("No local player.");
            return;
        }

        using var table = ImRaii.Table("##objects", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Kind");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("DataId");
        ImGui.TableSetupColumn("Dist");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Screen");
        ImGui.TableHeadersRow();

        foreach (var obj in Services.Objects)
        {
            var distance = Vector3.Distance(obj.Position, player.Position);
            if (distance > radius)
                continue;

            var onScreen = Services.GameGui.WorldToScreen(obj.Position, out var screen);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.ObjectKind.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.Name.TextValue.Length > 0 ? obj.Name.TextValue : "(unnamed)");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(obj.BaseId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{distance:0.0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled($"{obj.Position.X:0}/{obj.Position.Y:0}/{obj.Position.Z:0}");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off screen");
        }
    }

    /// <summary>
    /// What the game sends a window when you click in it. Selecting a beast means sending the same
    /// thing, and the only safe way to know what that is, is to watch a real click first.
    /// </summary>
    private void DrawRecorder()
    {
        if (!ImGui.CollapsingHeader("Clicks", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var recording = recorder.Recording;
        if (ImGui.Checkbox("Record clicks", ref recording))
            recorder.Recording = recording;

        Widgets.HelpMarker(
            "Watches only, sends nothing. Turn it on, click one beast in the selection window, and " +
            "the entry at the top is what selecting that beast looks like.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
            recorder.Clear();

        ImGui.TextDisabled($"{recorder.Entries.Count} recorded; cursor movement is dropped.");

        foreach (var entry in recorder.WithoutHover().Take(14))
            ImGui.TextUnformatted($"  {entry.At:HH:mm:ss.fff}  {entry.Addon}  {entry.EventType}  {entry.Detail}");
    }

    /// <summary>
    /// What the board has said about each room's enemies so far. Fills in as rooms are selected.
    /// </summary>
    private void DrawEnemies()
    {
        if (!ImGui.CollapsingHeader($"Rooms with enemies known ({enemies.RoomsKnown})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var live = RoomEnemyReader.Read();
        ImGui.TextDisabled(live == null
                               ? "Board window closed."
                               : $"Selected room: {live.SelectedRoom}, listing {live.Enemies.Count} enemies.");

        if (enemies.RoomsKnown == 0)
        {
            ImGui.TextDisabled("Open the board and click through the rooms.");
            return;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Forget all"))
            enemies.Clear();

        foreach (var room in StageDetailReader.Read())
        {
            var known = enemies.InRoom(room.Label);
            if (known.Count == 0)
                continue;

            using var node = ImRaii.TreeNode($"move {room.Move} {room.Label}###room{room.Index}");
            if (!node.Success)
                continue;

            foreach (var enemy in known)
            {
                ImGui.TextUnformatted("  " + enemies.Describe(enemy));
                ImGui.TextDisabled("    " + string.Join("   ", enemy.Stats.Select(stat => $"{stat.Label} {stat.Stars}")));

                foreach (var action in enemies.Details(enemy.Name)?.Actions ?? [])
                {
                    ImGui.TextDisabled(
                        $"    {action.Name}: {action.DamageType} at {action.Target}, {action.AreaOfEffect}" +
                        $"{(action.Status.Length > 0 ? $", inflicts {action.Status}" : string.Empty)}" +
                        $" — interruption {action.Interruption}{(action.Nullified ? " (covered)" : string.Empty)}");
                }
            }
        }
    }

    /// <summary>
    /// The roster, with the progression rank that leveling mode has to sort on.
    /// </summary>
    private void DrawRoster()
    {
        if (!ImGui.CollapsingHeader("Roster", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var slots = PetPartyReader.Read(catalog);
        if (slots.Count == 0)
        {
            ImGui.TextDisabled("XBMPetParty is not open.");
            return;
        }

        ImGui.TextDisabled(PetPartyReader.Prompt());
        ImGui.TextDisabled(PetPartyReader.Count() is { } count
                               ? $"Team {count.Members}/{count.Capacity}, as the window counts it"
                               : "The window shows no team count.");
        ImGui.TextDisabled($"Ranks learned so far: {ranks.KnownCount} of 50");

        foreach (var slot in slots)
        {
            ImGui.TextUnformatted(
                $"  {slot.Index,2}  {slot.Name,-16} rank {slot.Rank,-3}" +
                $"{(slot.IsCalled ? $" called into slot {slot.CallSlot}" : string.Empty)}" +
                $"  {(slot.Beast == null ? "(not matched)" : $"No. {slot.Beast.Number} {slot.Beast.ClassificationName}")}");
        }
    }

    /// <summary>
    /// The map's markers, raw. The room icons float over the platforms and appear on the minimap,
    /// and they are neither objects nor a window — so if they are anywhere, they are here.
    /// </summary>
    private static void DrawMarkers()
    {
        if (!ImGui.CollapsingHeader("Map markers", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var player = Services.Objects.LocalPlayer;
        if (player != null)
            ImGui.TextDisabled($"player world {player.Position.X:0.0}/{player.Position.Y:0.0}/{player.Position.Z:0.0}");

        ImGui.TextWrapped(MapMarkerReader.DescribeTransform());

        var markers = MapMarkerReader.Read();
        if (markers.Count == 0)
        {
            ImGui.TextDisabled("None. If the room icons are visible right now, they are not map markers either.");
            return;
        }

        using var table = ImRaii.Table("##markers", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("From");
        ImGui.TableSetupColumn("Icon");
        ImGui.TableSetupColumn("Map X / Y");
        ImGui.TableSetupColumn("World / screen");
        ImGui.TableHeadersRow();

        foreach (var marker in markers)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled(marker.Kind?.ToString() ?? marker.Source);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(marker.IconId.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{marker.MapX} / {marker.MapY}");
            ImGui.TableNextColumn();

            if (marker.World is not { } world)
            {
                ImGui.TextDisabled(marker.Subtext);
                continue;
            }

            var onScreen = Services.GameGui.WorldToScreen(world, out var screen);
            ImGui.TextUnformatted($"{world.X:0.0}/{world.Z:0.0}  ->  " +
                                  (onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off screen"));
        }
    }

    private string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"# Board report {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"XBMStageMap open: {StageMapReader.IsOpen}");
        text.AppendLine($"XBMStageDetailList open: {StageDetailReader.IsOpen}");
        text.AppendLine();

        foreach (var room in StageMapReader.Read())
            text.AppendLine($"tile idx={room.DetailIndex} current={room.IsCurrent} " +
                            $"pos={room.ScreenPosition.X:0}/{room.ScreenPosition.Y:0} size={room.Size.X:0}x{room.Size.Y:0}");

        // The rows are what a move is read off, so they go in whole: every second row from the
        // bottom should be a room row, and its tile count should match what the room list says that
        // move offers. A capture where those disagree is the one that disproves the mapping.
        text.AppendLine();
        foreach (var (row, index) in StageMapReader.Rows().Select((row, index) => (row, index)))
        {
            text.AppendLine($"row {index} ({row.Count} tiles) " +
                            string.Join("  ", row.Select(tile => $"idx={tile.DetailIndex}" +
                                                                 $"{(tile.IsCurrent ? "*" : string.Empty)}" +
                                                                 $"@{tile.ScreenPosition.X:0}/{tile.ScreenPosition.Y:0}")));
        }

        text.AppendLine();
        text.AppendLine(CrucibleModeReader.Read() is { } mode
                            ? $"# Crucible mode: {mode.Label} ({mode.Index + 1} of {mode.Options.Count}) — " +
                              string.Join(", ", mode.Options)
                            : "# Crucible mode: not offered");

        text.AppendLine();
        text.AppendLine($"# Board cache: {board.Source}");
        text.AppendLine($"standing on move {board.CurrentMove}, briefing move {board.NextMove}");
        foreach (var room in board.Rooms)
            text.AppendLine($"cached [{room.Index}] move={room.Move} kind={room.Kind} \"{room.Label}\"");

        text.AppendLine();
        foreach (var room in StageDetailReader.Read())
            text.AppendLine($"room [{room.Index}] move={room.Move} kind={room.Kind} \"{room.Label}\" \"{room.Detail}\"");

        text.AppendLine();
        text.AppendLine("# Every window open right now");
        foreach (var name in AddonReader.OpenAddonNames(string.Empty))
            text.AppendLine(name);

        text.AppendLine();
        text.AppendLine("# Clicks");
        text.AppendLine(recorder.Report());

        text.AppendLine();
        text.AppendLine("# Enemies per room");
        foreach (var room in StageDetailReader.Read())
        {
            foreach (var enemy in enemies.InRoom(room.Label))
                text.AppendLine($"move {room.Move}	{room.Label}	{enemy.Name}	weak={enemy.Weakness}	" +
                                string.Join(" ", enemy.Stats.Select(stat => $"{stat.Label}={stat.Stars}")));
        }

        text.AppendLine();
        text.AppendLine("# Roster");
        text.AppendLine(PetPartyReader.Prompt());
        foreach (var slot in PetPartyReader.Read(catalog))
            text.AppendLine($"{slot.Index}	{slot.Name}	rank={slot.Rank}	called={slot.CallSlot}	" +
                            $"icon={slot.IconId}	beast={(slot.Beast == null ? "?" : slot.Beast.Number.ToString())}");

        // The roster window's raw values go in whole. The rank sits at a different offset
        // depending on what the window is showing, and reading it back from a capture beats
        // another round trip guessing which.
        text.AppendLine();
        text.AppendLine(AddonReader.ToText(XbmColumns.PetParty.Addon,
                                           AddonReader.Values(XbmColumns.PetParty.Addon)));

        // Its node tree too: the roster's button is placed against the list inside it, and a capture
        // of where that list sits is what turns a wrong placement into a one-line fix.
        text.AppendLine();
        text.AppendLine(AddonReader.ToText(XbmColumns.PetParty.Addon,
                                           AddonReader.Nodes(XbmColumns.PetParty.Addon)));

        text.AppendLine();
        text.AppendLine(AddonReader.ToText(XbmColumns.StageList.Addon,
                                           AddonReader.Values(XbmColumns.StageList.Addon)));

        // The board window whole, values and nodes. The difficulty stepper lives here rather than in
        // the board selection — its right-hand button sends this window [5] — and which value holds
        // the number it is set to is what a capture of this screen has to answer.
        text.AppendLine();
        text.AppendLine(AddonReader.ToText(XbmColumns.StageDetailList.Addon,
                                           AddonReader.Values(XbmColumns.StageDetailList.Addon)));
        text.AppendLine();
        text.AppendLine(AddonReader.ToText(XbmColumns.StageDetailList.Addon,
                                           AddonReader.Nodes(XbmColumns.StageDetailList.Addon)));

        text.AppendLine();
        text.AppendLine("# Map markers");
        text.AppendLine(MapMarkerReader.DescribeTransform());
        var self = Services.Objects.LocalPlayer;
        if (self != null)
            text.AppendLine($"player world {self.Position.X:0.0}/{self.Position.Y:0.0}/{self.Position.Z:0.0}");

        foreach (var marker in MapMarkerReader.Read())
            text.AppendLine($"{marker.Source}	icon={marker.IconId}	kind={marker.Kind}	" +
                            $"map={marker.MapX}/{marker.MapY}	world={marker.World?.X:0.0}/{marker.World?.Z:0.0}	\"{marker.Subtext}\"");

        text.AppendLine();
        text.AppendLine("# Objects in the world");

        var player = Services.Objects.LocalPlayer;
        foreach (var obj in Services.Objects)
        {
            var distance = player == null ? 0f : Vector3.Distance(obj.Position, player.Position);
            if (player != null && distance > radius)
                continue;

            var onScreen = Services.GameGui.WorldToScreen(obj.Position, out var screen);
            text.AppendLine($"{obj.ObjectKind}\t{obj.Name.TextValue}\tdataId={obj.BaseId}\tdist={distance:0.0}\t" +
                            $"world={obj.Position.X:0}/{obj.Position.Y:0}/{obj.Position.Z:0}\t" +
                            $"screen={(onScreen ? $"{screen.X:0}/{screen.Y:0}" : "off")}");
        }

        return text.ToString();
    }

    public void Dispose() { }
}
