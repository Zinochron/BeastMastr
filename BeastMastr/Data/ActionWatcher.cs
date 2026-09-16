using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace BeastMastr.Data;

/// <summary>
/// Watches every action the client is asked to use, and tells a hotbar press apart from everything
/// else. Watching only: both hooks pass straight through and change nothing.
///
/// Two questions depend on it. The run recorder wants to know what the job actually presses in a
/// fight. The manual-input guard wants to know whether *the player* pressed something — and
/// <c>UseAction</c> alone cannot say, because BossMod and this plugin call it too. A press from the
/// hotbar goes through <c>RaptureHotbarModule.ExecuteSlot</c> first, and nothing automated does,
/// so a use that arrives inside one is the player's.
/// </summary>
public sealed unsafe class ActionWatcher : IDisposable
{
    /// <param name="FromHotbar">Pressed on a hotbar, which is to say by the player.</param>
    public sealed record Use(DateTime At, ActionType Type, uint ActionId, ulong TargetId,
                             ActionManager.UseActionMode Mode, bool Accepted, bool FromHotbar);

    private delegate bool UseActionDelegate(ActionManager* manager, ActionType type, uint actionId, ulong targetId,
                                            uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId,
                                            bool* outOptAreaTargeted);

    private delegate byte ExecuteSlotDelegate(RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot);

    private delegate byte ExecuteSlotByIdDelegate(RaptureHotbarModule* module, uint hotbarId, uint slotId);

    private readonly Hook<UseActionDelegate>? useAction;
    private readonly Hook<ExecuteSlotDelegate>? executeSlot;
    private readonly Hook<ExecuteSlotByIdDelegate>? executeSlotById;

    /// <summary>How deep inside a hotbar press the game currently is. One press can nest the other.</summary>
    private int hotbarDepth;

    public ActionWatcher()
    {
        useAction = TryHook<UseActionDelegate>("UseAction", ActionManager.Addresses.UseAction.Value, OnUseAction);
        executeSlot = TryHook<ExecuteSlotDelegate>("ExecuteSlot", RaptureHotbarModule.Addresses.ExecuteSlot.Value,
                                                   OnExecuteSlot);
        executeSlotById = TryHook<ExecuteSlotByIdDelegate>("ExecuteSlotById",
                                                           RaptureHotbarModule.Addresses.ExecuteSlotById.Value,
                                                           OnExecuteSlotById);
    }

    /// <summary>Every use, as it happens.</summary>
    public event Action<Use>? ActionUsed;

    /// <summary>When the player last pressed anything on a hotbar. <see cref="DateTime.MinValue"/> before the first.</summary>
    public DateTime LastHotbarPressAt { get; private set; } = DateTime.MinValue;

    /// <summary>Whether the hooks are in. Without them nothing manual can be told apart, which the guard has to know.</summary>
    public bool IsWatching => useAction != null && executeSlot != null;

    private static Hook<T>? TryHook<T>(string name, nint address, T detour) where T : Delegate
    {
        try
        {
            var hook = Services.Interop.HookFromAddress(address, detour);
            hook.Enable();
            return hook;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Could not watch {name}; actions pressed by hand cannot be told apart.");
            return null;
        }
    }

    private bool OnUseAction(ActionManager* manager, ActionType type, uint actionId, ulong targetId, uint extraParam,
                             ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        var accepted = useAction!.Original(manager, type, actionId, targetId, extraParam, mode, comboRouteId,
                                           outOptAreaTargeted);

        try
        {
            ActionUsed?.Invoke(new Use(DateTime.Now, type, actionId, targetId, mode, accepted, hotbarDepth > 0));
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Reporting an action use threw.");
        }

        return accepted;
    }

    private byte OnExecuteSlot(RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot)
    {
        LastHotbarPressAt = DateTime.Now;
        hotbarDepth++;

        try
        {
            return executeSlot!.Original(module, slot);
        }
        finally
        {
            hotbarDepth--;
        }
    }

    private byte OnExecuteSlotById(RaptureHotbarModule* module, uint hotbarId, uint slotId)
    {
        LastHotbarPressAt = DateTime.Now;
        hotbarDepth++;

        try
        {
            return executeSlotById!.Original(module, hotbarId, slotId);
        }
        finally
        {
            hotbarDepth--;
        }
    }

    public void Dispose()
    {
        useAction?.Dispose();
        executeSlot?.Dispose();
        executeSlotById?.Dispose();
    }
}
