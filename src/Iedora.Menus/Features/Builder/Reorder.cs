namespace Iedora.Menus;

/// <summary>An entity whose sibling order is editable — reordered as a group by <see cref="Reorder.Apply"/>.</summary>
internal interface IOrderable
{
    Guid Id { get; }
    int Position { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Shared reorder for a builder's children (categories under a menu, items under a category):
/// validate the ids are exactly the current set, then assign positions 0..n by the requested order.</summary>
internal static class Reorder
{
    /// <summary>Apply <paramref name="orderedIds"/> as the new 0-based positions (stamping UpdatedAt).
    /// Returns false — no change — when they aren't a permutation of the current entities' ids.</summary>
    public static bool Apply<T>(IReadOnlyList<T> entities, IReadOnlyList<Guid> orderedIds, DateTimeOffset now)
        where T : IOrderable
    {
        if (!IsPermutation(orderedIds, entities.Select(e => e.Id))) return false;

        var byId = entities.ToDictionary(e => e.Id);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            byId[orderedIds[i]].Position = i;
            byId[orderedIds[i]].UpdatedAt = now;
        }
        return true;
    }

    /// <summary>True iff <paramref name="ordered"/> names every id in <paramref name="existing"/> exactly
    /// once (same set, same count, no duplicates).</summary>
    public static bool IsPermutation(IReadOnlyList<Guid> ordered, IEnumerable<Guid> existing)
    {
        var existingSet = existing.ToHashSet();
        return ordered.Count == existingSet.Count && ordered.ToHashSet().SetEquals(existingSet);
    }
}
