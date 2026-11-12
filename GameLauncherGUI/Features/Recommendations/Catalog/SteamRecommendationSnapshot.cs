using Newtonsoft.Json;
namespace GameLauncherGUI;
internal static class SteamRecommendationSnapshot
{
    public static RecommendedGame[] Load(){using var stream=typeof(SteamRecommendationSnapshot).Assembly.GetManifestResourceStream("LauncherPlaySpace.Recommendations");if(stream==null)return Array.Empty<RecommendedGame>();using var reader=new StreamReader(stream);return JsonConvert.DeserializeObject<RecommendedGame[]>(reader.ReadToEnd())??Array.Empty<RecommendedGame>();}
}
