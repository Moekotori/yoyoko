namespace Chat.Core.Messaging;

public static class JumpRank
{
    public const int RecentCap = 8;

    public static IReadOnlyList<T> Channels<T>(IReadOnlyList<T> all, Func<T, Guid> id, Func<T, string> name,
        IReadOnlyDictionary<Guid, long> visited, string query)
    {
        var recents = all
            .Where(item => visited.ContainsKey(id(item)))
            .OrderByDescending(item => visited[id(item)])
            .Take(RecentCap)
            .ToList();
        bool Match(T item) => query.Length == 0
            || name(item).Contains(query, StringComparison.OrdinalIgnoreCase);
        if (query.Length == 0)
            return recents.Count > 0 ? recents : all;
        var recentHits = recents.Where(Match).ToList();
        var seen = recentHits.Select(id).ToHashSet();
        return recentHits.Concat(all.Where(item => Match(item) && seen.Add(id(item)))).ToList();
    }
}
