using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Buys Beast Gear in a shop, one piece at a time: the dearest piece that is not held, not bought and
/// affordable, until none is left — and then, if asked, the strongest healing items the tokens left allow.
///
/// A purchase is <c>[2, n]</c> on the shop, as recorded, and the game asks "Purchase the ice shield?".
/// The question is checked before it is answered: yes only when it names the item meant, otherwise no,
/// and buying stops.
/// </summary>
public sealed class ShopBuyer
{
    private static readonly TimeSpan SendDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(3);

    private readonly HashSet<int> tried = [];
    private readonly bool buyPotions;
    private readonly float potionsFirstBelow;
    private readonly List<string> bought = [];
    private RoomActions.ShopOffer? pending;
    private DateTime stageSince = DateTime.Now;
    private int stage;
    private int tokensBefore;

    /// <param name="buyPotions">With no gear left to buy, spend what is left on healing items, strongest first.</param>
    /// <param name="potionsFirstBelow">
    /// At or below this share of HP, healing items come before gear: the run met Borgny at 45% after the
    /// last shop's tokens went on a Mystic Veil.
    /// </param>
    public ShopBuyer(bool buyPotions = false, float potionsFirstBelow = 0f)
    {
        this.buyPotions = buyPotions;
        this.potionsFirstBelow = potionsFirstBelow;
    }

    public bool Done { get; private set; }

    /// <summary>What was bought, for the log.</summary>
    public string Summary => bought.Count == 0 ? "nothing" : string.Join(", ", bought);

    public void Tick()
    {
        if (Done)
            return;

        var now = DateTime.Now;
        switch (stage)
        {
            // Choosing, once the shop has had a moment to fill in or refresh.
            case 0:
                if (now - stageSince < SendDelay)
                    return;

                var tokens = RoomActions.ShopTokens();
                var offers = RoomActions.ShopOffers();
                var gear = offers.Where(offer => offer.IsGear && !offer.Bought && !offer.Held &&
                                                 offer.Price > 0 && offer.Price <= tokens &&
                                                 !tried.Contains(offer.Index))
                                 .OrderByDescending(offer => offer.Price)
                                 .FirstOrDefault();

                // HP does not come back on its own, and the Strix alone took three quarters of it.
                var healing = buyPotions
                                  ? offers.Where(offer => ItemUser.Heals.ContainsKey(offer.Row) && !offer.Bought &&
                                                          offer.Price > 0 && offer.Price <= tokens &&
                                                          !tried.Contains(offer.Index))
                                          .OrderByDescending(offer => ItemUser.Heals[offer.Row])
                                          .ThenBy(offer => offer.Price)
                                          .FirstOrDefault()
                                  : null;

                var low = Services.Objects.LocalPlayer is { MaxHp: > 0 } player &&
                          (float)player.CurrentHp / player.MaxHp <= potionsFirstBelow;
                pending = low ? healing ?? gear : gear ?? healing;

                if (pending == null)
                {
                    Done = true;
                    return;
                }

                tried.Add(pending.Index);
                tokensBefore = tokens;
                if (!RoomActions.Send(RoomActions.BuyItem(pending.Index)))
                {
                    Done = true;
                    return;
                }

                Next(1);
                return;

            // The question, which has to name the piece meant.
            case 1:
                if (AddonReader.IsOpen(XbmColumns.RunWindows.SelectOk))
                {
                    if (RoomActions.Send(RoomActions.Ok))
                        Next(0);

                    return;
                }

                if (AddonReader.IsOpen(RoomActions.YesnoAddon))
                {
                    var question = RoomActions.YesnoText();
                    if (question.Length == 0)
                        return;

                    if (question.Contains(pending!.Singular, StringComparison.OrdinalIgnoreCase))
                    {
                        if (RoomActions.Send(RoomActions.Yes))
                            Next(2);

                        return;
                    }

                    Services.Log.Warning($"Shop: asked \"{question}\" when buying {pending.Name}; answering no and buying nothing more.");
                    RoomActions.Send(RoomActions.No);
                    Done = true;
                    return;
                }

                if (now - stageSince > AskTimeout)
                    Next(0);

                return;

            // The purchase going through: the tokens drop, or the offer shows as bought.
            case 2:
                var went = RoomActions.ShopTokens() < tokensBefore ||
                           RoomActions.ShopOffers().Any(offer => offer.Index == pending!.Index && offer.Bought);
                if (went && !AddonReader.IsOpen(RoomActions.YesnoAddon))
                {
                    bought.Add(pending!.Name);
                    Services.Log.Information($"Shop: bought {pending.Name} for {pending.Price} tokens.");
                    Next(0);
                    return;
                }

                if (now - stageSince > SettleTimeout)
                    Next(0);

                return;
        }
    }

    private void Next(int to)
    {
        stage = to;
        stageSince = DateTime.Now;
    }
}
