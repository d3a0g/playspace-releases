using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
namespace GameLauncherGUI;
internal static class GameDescription
{
    public static string Format(JObject data)
    {
        string html=data.Value<string>("detailed_description")??data.Value<string>("about_the_game")??data.Value<string>("short_description")??"Описание пока недоступно.";
        html=Regex.Replace(html,@"<(script|style)[^>]*>.*?</\1>","",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        html=Regex.Replace(html,@"<h[1-6][^>]*>(.*?)</h[1-6]>",m=>"\n\n"+Regex.Replace(m.Groups[1].Value,"<[^>]+>","").ToUpperInvariant()+"\n",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        html=Regex.Replace(html,@"<li[^>]*>","\n• ",RegexOptions.IgnoreCase);
        html=Regex.Replace(html,@"<\s*(?:br\s*/?|/p|/div|/li)\s*>","\n",RegexOptions.IgnoreCase);
        string plain=WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]+>",""));plain=Regex.Replace(plain,@"[\t ]+"," ");plain=Regex.Replace(plain,@"\n\s*\n\s*\n","\n\n").Trim();
        var meta=new List<string>();string developers=string.Join(", ",data["developers"]?.Values<string>()??Array.Empty<string>());if(developers.Length>0)meta.Add("Разработчик: "+developers);
        string date=data["release_date"]?.Value<string>("date")??"";if(date.Length>0)meta.Add("Дата выхода: "+date);
        string genres=string.Join(" · ",data["genres"]?.Select(g=>g.Value<string>("description")).Where(x=>!string.IsNullOrWhiteSpace(x))??Array.Empty<string>());if(genres.Length>0)meta.Add("Жанры: "+genres);
        return (meta.Count>0?string.Join("\n",meta)+"\n\nОБ ИГРЕ\n\n":"")+plain;
    }
}
