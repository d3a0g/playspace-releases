namespace GameLauncherGUI;

internal static class LaunchPreparation
{
    internal static string[] Arguments(GameInfo game,string[] savedArguments,bool withoutPreparation,bool safeMode)
    {
        if(safeMode)return Array.Empty<string>();
        // Saved arguments can identify the product for Riot/other clients. Keep them on a diagnostic retry.
        if(withoutPreparation)return savedArguments.ToArray();
        return UnrealModLayout.LaunchArguments(game).Concat(GamePerformanceController.Prepare(game)).Concat(savedArguments).ToArray();
    }
}
