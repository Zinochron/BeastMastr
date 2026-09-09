using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <summary>
/// The bestiary query: free text plus any combination of status, trait, classification and damage
/// type. Pure, so the whole of it can be asserted on without a client.
///
/// Every selected condition must hold — "which of my beasts sleeps *and* is a Wavekin" is the
/// question worth asking, and an any-of filter cannot express it. Leaving a group empty means that
/// group does not narrow anything.
/// </summary>
public sealed class BeastFilter
{
    public string Search { get; set; } = string.Empty;
    public HashSet<BeastStatus> Statuses { get; } = [];
    public HashSet<BeastTrait> Traits { get; } = [];
    public HashSet<int> Classifications { get; } = [];
    public HashSet<DamageType> DamageTypes { get; } = [];

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Search)
        && Statuses.Count == 0
        && Traits.Count == 0
        && Classifications.Count == 0
        && DamageTypes.Count == 0;

    /// <summary>
    /// Cheap stand-in for "has this changed". The sets are public and mutated in place, so there is
    /// no event to subscribe to; a watcher compares this instead of diffing four collections.
    /// </summary>
    public int Signature()
    {
        var hash = new HashCode();
        hash.Add(Search);
        hash.Add(Statuses.Count);
        hash.Add(Traits.Count);
        hash.Add(Classifications.Count);
        hash.Add(DamageTypes.Count);

        foreach (var status in Statuses.Order()) hash.Add(status);
        foreach (var trait in Traits.Order()) hash.Add(trait);
        foreach (var classification in Classifications.Order()) hash.Add(classification);
        foreach (var damage in DamageTypes.Order()) hash.Add(damage);

        return hash.ToHashCode();
    }

    public void Clear()
    {
        Search = string.Empty;
        Statuses.Clear();
        Traits.Clear();
        Classifications.Clear();
        DamageTypes.Clear();
    }

    public bool Matches(Beast beast)
    {
        if (!MatchesSearch(beast))
            return false;

        if (Statuses.Any(status => !beast.Inflicts(status)))
            return false;

        if (Traits.Any(trait => !beast.Has(trait)))
            return false;

        if (Classifications.Count > 0 && !Classifications.Contains(beast.Classification))
            return false;

        if (DamageTypes.Count > 0 && !DamageTypes.Any(beast.DamageTypes.Contains))
            return false;

        return true;
    }

    /// <summary>
    /// Free text covers the name, the classification and every action name and description, so
    /// typing "sleep" finds the beast whose action says so even when the status flag is what you
    /// would have filtered on. Case and surrounding space are ignored.
    /// </summary>
    private bool MatchesSearch(Beast beast)
    {
        var needle = Search.Trim();
        if (needle.Length == 0)
            return true;

        if (Contains(beast.Name, needle)
            || Contains(beast.ClassificationName, needle)
            || Contains(beast.Habitat, needle))
            return true;

        return beast.Actions.Any(action => Contains(action.Name, needle)
                                           || Contains(action.Description, needle));
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<Beast> Apply(IEnumerable<Beast> beasts) => beasts.Where(Matches);
}
