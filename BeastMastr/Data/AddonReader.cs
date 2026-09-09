using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.NativeWrapper;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// Reading a game window: its AtkValues and its node tree.
///
/// Both are indexed by position with no names attached, so every index this plugin ends up relying
/// on is written down in <c>README-DEV.md</c> against the capture it came from. This class is how
/// those captures are taken.
/// </summary>
public static unsafe class AddonReader
{
    public static AtkUnitBasePtr Find(string name) => Services.GameGui.GetAddonByName(name);

    public static bool IsOpen(string name)
    {
        var addon = Find(name);
        return !addon.IsNull && addon.IsVisible;
    }

    public static bool TryGet(string name, out AtkUnitBase* addon)
    {
        addon = null;
        return GenericHelpers.TryGetAddonByName(name, out addon)
               && addon != null
               && addon->UldManager.LoadedState == AtkLoadState.Loaded;
    }

    // ---- AtkValues --------------------------------------------------------

    public sealed record Value(int Index, string Type, string Text);

    public static List<Value> Values(string name)
    {
        var addon = Find(name);
        if (addon.IsNull)
            return [];

        return addon.AtkValues
                    .Select((value, index) => new Value(index,
                                                        value.ValueType.ToString(),
                                                        value.GetValue()?.ToString() ?? string.Empty))
                    .ToList();
    }

    // ---- Node tree --------------------------------------------------------

    /// <summary>
    /// One node, flattened. <paramref name="Depth"/> keeps the tree shape without needing a tree of
    /// objects, because the only consumer draws it as indented lines.
    /// </summary>
    public sealed record Node(
        int Depth,
        uint NodeId,
        string Type,
        bool Visible,
        float ScreenX,
        float ScreenY,
        float Width,
        float Height,
        string Text);

    public static List<Node> Nodes(string name)
    {
        var nodes = new List<Node>();

        if (!TryGet(name, out var addon) || addon->RootNode == null)
            return nodes;

        Walk(addon->RootNode, 0, nodes);
        return nodes;
    }

    /// <summary>
    /// Depth first over siblings and children. Component nodes hold their children behind their own
    /// UldManager rather than in ChildNode, so they are followed separately — without that, the
    /// interesting parts of a window (list rows, the board's room entries) stay invisible.
    /// </summary>
    private static void Walk(AtkResNode* node, int depth, List<Node> into)
    {
        // A window with a deep tree is normal; a cycle is not. The cap is a seatbelt, not a limit.
        if (node == null || depth > 24 || into.Count > 4000)
            return;

        for (var current = node; current != null; current = current->PrevSiblingNode)
        {
            into.Add(Describe(current, depth));

            if ((uint)current->Type >= 1000)
            {
                var component = ((AtkComponentNode*)current)->Component;
                if (component != null && component->UldManager.RootNode != null)
                    Walk(component->UldManager.RootNode, depth + 1, into);
            }
            else if (current->ChildNode != null)
            {
                Walk(current->ChildNode, depth + 1, into);
            }
        }
    }

    private static Node Describe(AtkResNode* node, int depth)
    {
        var text = string.Empty;
        if (node->Type == NodeType.Text)
            text = ((AtkTextNode*)node)->NodeText.ToString();

        return new Node(depth,
                        node->NodeId,
                        DescribeType(node),
                        node->IsVisible(),
                        node->ScreenX,
                        node->ScreenY,
                        node->Width,
                        node->Height,
                        text);
    }

    /// <summary>
    /// Component nodes carry their component's kind in the type id above 1000, and that kind is what
    /// identifies a room entry or a list row — so it is spelled out rather than printed as a number.
    /// </summary>
    private static string DescribeType(AtkResNode* node)
    {
        if ((uint)node->Type < 1000)
            return node->Type.ToString();

        var component = ((AtkComponentNode*)node)->Component;
        if (component == null)
            return $"Component({(uint)node->Type})";

        return $"Component/{component->GetComponentType()}";
    }

    public static string ToText(string addon, IEnumerable<Node> nodes)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {addon}");

        foreach (var node in nodes)
        {
            text.Append(new string(' ', node.Depth * 2));
            text.Append($"[{node.NodeId}] {node.Type}");
            text.Append(node.Visible ? " " : " (hidden) ");
            text.Append($"@{node.ScreenX:0}/{node.ScreenY:0} {node.Width:0}x{node.Height:0}");

            if (!string.IsNullOrEmpty(node.Text))
                text.Append($"  \"{node.Text}\"");

            text.AppendLine();
        }

        return text.ToString();
    }

    public static string ToText(string addon, IEnumerable<Value> values)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {addon} AtkValues");

        foreach (var value in values)
            text.AppendLine($"{value.Index}\t{value.Type}\t{value.Text}");

        return text.ToString();
    }
}
