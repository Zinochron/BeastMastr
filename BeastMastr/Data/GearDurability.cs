using FFXIVClientStructs.FFXIV.Client.Game;

namespace BeastMastr.Data;

/// <summary>What the gear worn has left, so a run can be sent to repair between boards.</summary>
public static unsafe class GearDurability
{
    /// <summary>A whole item's condition, as the game counts it.</summary>
    private const float Full = 30000f;

    /// <summary>The worst piece worn, as a share of full; 1 when nothing is worn or nothing is worn down.</summary>
    public static float Lowest()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 1f;

        var worn = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (worn == null || !worn->IsLoaded)
            return 1f;

        var lowest = 1f;
        for (var slot = 0; slot < worn->Size; slot++)
        {
            var item = worn->GetInventorySlot(slot);
            if (item == null || item->ItemId == 0)
                continue;

            var share = item->Condition / Full;
            if (share < lowest)
                lowest = share;
        }

        return lowest;
    }
}
