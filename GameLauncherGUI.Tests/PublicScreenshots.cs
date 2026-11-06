using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;
using Newtonsoft.Json.Linq;

internal static class PublicScreenshots
{
    private sealed class StorePreview : DelegatingHandler
    {
        public StorePreview():base(new HttpClientHandler()){}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            if(request.RequestUri!.Host=="store.steampowered.com"&&File.Exists("artifacts/steam-public-preview.json"))return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(await File.ReadAllTextAsync("artifacts/steam-public-preview.json",cancellation))};
            var response=await base.SendAsync(request,cancellation);
            if(request.RequestUri!.Host=="store.steampowered.com"&&response.IsSuccessStatusCode){
                var body=JObject.Parse(await response.Content.ReadAsStringAsync(cancellation));
                foreach(var entry in body.Properties())if(entry.Value["data"] is JObject data&&data["screenshots"] is JArray screenshots)data["screenshots"]=new JArray(screenshots.Take(2));
                response.Content=new StringContent(body.ToString());
            }
            return response;
        }
    }
    public static void Run()
    {
        string output=Path.GetFullPath("docs/screenshots");Directory.CreateDirectory(output);
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};var covers=new Dictionary<string,Bitmap>();
        foreach(var game in RecommendedGame.All.Take(12).Concat(RecommendedGame.All.Where(g=>new[]{"620","413150","892970","548430","1091500","394360","105600","264710"}.Contains(g.Id))).DistinctBy(g=>g.Id)){
            try{using var stream=http.GetStreamAsync($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{game.Id}/library_600x900_2x.jpg").GetAwaiter().GetResult();using var image=Image.FromStream(stream);covers[game.Id]=new Bitmap(image,300,450);}catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or ArgumentException){}
        }
        Task<Bitmap?> Cover(GameInfo game)=>Task.FromResult(covers.GetValueOrDefault(game.SteamAppId));
        void Save(Form form,string name){Application.DoEvents();using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(output,name));}
        var demo=RecommendedGame.All.Where(g=>new[]{"620","413150","892970","548430","1091500","394360","105600","264710"}.Contains(g.Id)).Select(g=>new GameInfo{Name=g.Name,SteamAppId=g.Id,ExePath="steam://rungameid/"+g.Id,IsPinned=g.Id is "620" or "892970"}).ToList();
        string storage=Path.Combine(Path.GetTempPath(),"PlaySpacePublicScreenshots-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(storage);GameLibraryStore.Save(Path.Combine(storage,"games.json"),demo);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(var main=new Form1(storage)){
            main.Show();Application.DoEvents();var list=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",flags)!.GetValue(main)!;
            var visible=(List<GameInfo>)typeof(Form1).GetField("visibleGames",flags)!.GetValue(main)!;
            for(int i=0;i<visible.Count;i++){list.Items[i].PosterImage=covers.GetValueOrDefault(visible[i].SteamAppId);list.Items[i].IconImage=covers.GetValueOrDefault(visible[i].SteamAppId);}
            list.PosterView=true;list.Invalidate();Save(main,"library.png");
        }
        using(var handler=new StorePreview())using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(20)})using(var recommendations=new RecommendationsForm(storage,Cover,client)){
            recommendations.Show();Application.DoEvents();Save(recommendations,"recommendations.png");
            var grid=(CustomCheckListBox)typeof(RecommendationsForm).GetField("grid",flags)!.GetValue(recommendations)!;grid.SelectItem(Array.FindIndex(RecommendedGame.All,g=>g.Id=="620"));
            var details=(Task)typeof(RecommendationsForm).GetMethod("SelectGame",flags)!.Invoke(recommendations,null)!;
            var deadline=DateTime.UtcNow.AddSeconds(70);while(!details.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(15);}if(!details.IsCompleted)throw new Exception("Store preview timed out");details.GetAwaiter().GetResult();
            var banner=(PictureBox)typeof(RecommendationsForm).GetField("banner",flags)!.GetValue(recommendations)!;
            if(banner.Image==null)throw new Exception("Steam screenshots not available; refusing placeholder screenshot");Save(recommendations,"game-details.png");
        }
        using(var settings=new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{})){settings.Show();Save(settings,"settings.png");}
        using(var dlss=new UniversalDlss5Form(demo.Single(g=>g.SteamAppId=="1091500"),Path.Combine(storage,"no-module"))){dlss.Show();Application.DoEvents();Save(dlss,"dlss5.png");}
        using(var splash=new StartupSplash()){splash.Report("Загружаем библиотеку…");splash.Show();Save(splash,"startup.png");}
        foreach(var cover in covers.Values)cover.Dispose();Console.WriteLine("Saved 6 real interface screenshots with demo game data, public Steam artwork and no account credentials.");
    }
}
