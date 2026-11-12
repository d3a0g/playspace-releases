namespace GameLauncherGUI;

internal sealed record RecommendationScore(RecommendedGame Game, double Score, string Reason);
internal static class PersonalRecommendations
{
    internal static RecommendationScore[] Rank(IEnumerable<GameInfo> library, IEnumerable<string> favorites)
    {
        var installed = library.ToArray();
        var owned = installed.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).Select(g => g.SteamAppId).ToHashSet();
        var favoriteIds = favorites.ToHashSet();
        var weights = new Dictionary<string, double>();
        foreach (var known in RecommendedGame.All)
        {
            var matching = installed.Where(g => g.SteamAppId == known.Id).ToArray();
            double weight = (favoriteIds.Contains(known.Id) ? 4 : 0) + matching.Sum(g => (g.IsPinned ? 4 : 0) + Math.Log10(1 + Math.Max(0, g.DisplayPlayedSeconds) / 3600));
            foreach (string category in known.Categories) weights[category] = weights.GetValueOrDefault(category) + weight;
        }
        return RecommendedGame.All.Where(g => !owned.Contains(g.Id)).Select(g =>
        {
            string? category = g.Categories.OrderByDescending(c => weights.GetValueOrDefault(c)).FirstOrDefault(c => weights.GetValueOrDefault(c) > 0);
            return new RecommendationScore(g, g.Categories.Sum(c => weights.GetValueOrDefault(c)), category == null
                ? "Попробуйте новый жанр — данных о предпочтениях пока мало"
                : "Вам нравится жанр «" + category + "»: закрепления и время игры");
        }).OrderByDescending(r => r.Score).ThenBy(r => r.Game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
