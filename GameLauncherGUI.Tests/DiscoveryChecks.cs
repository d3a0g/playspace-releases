using System.Net;
using System.Net.Http;
using System.Reflection;
using GameLauncherGUI;

internal static class DiscoveryChecks
{
    private sealed class StoreFixture : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if(request.RequestUri!.Host.EndsWith(".steamstatic.com")) {
                using var image=new Bitmap(640,360);using(var graphics=Graphics.FromImage(image)) {
                    graphics.Clear(request.RequestUri.AbsolutePath.Contains("first")?Color.MediumPurple:Color.DarkCyan);
                    using var font=new Font("Segoe UI",28);graphics.DrawString("STEAM SCREENSHOT FIXTURE",font,Brushes.White,25,120);
                }
                using var stream=new MemoryStream();image.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(stream.ToArray())});
            }
            if(request.RequestUri!.AbsolutePath.Contains("GetOwnedGames"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"response\":{\"games\":[{\"appid\":620,\"playtime_forever\":120000},{\"appid\":730,\"playtime_forever\":0}]}}")});
            Calls++;if(request.RequestUri!.Host=="api.steampowered.com")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"playerstats\":{\"success\":true,\"achievements\":[{\"achieved\":1},{\"achieved\":0}]}}")});
            string id=System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["appids"]??"730";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\""+id+"\":{\"success\":true,\"data\":{\"short_description\":\"Short\",\"detailed_description\":\"Fixture &amp; description\",\"screenshots\":[{\"path_full\":\"https://shared.fastly.steamstatic.com/first.png\"},{\"path_full\":\"https://shared.fastly.steamstatic.com/second.png\"}]}}}")});
        }
    }
    public static void Run(bool ui)
    {
        string root=Path.Combine(Path.GetTempPath(),"PlayspaceDiscovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(string category in new[]{"Шутеры","Выживание","Кооп","Головоломки","Хорроры","Стратегии","Ролевые","Уютные"})
            if(!RecommendedGame.All.Any(g=>g.Categories.Contains(category)))throw new Exception("Empty recommendation category: "+category);
        if(RecommendedGame.All.Select(g=>g.Id).Distinct().Count()!=RecommendedGame.All.Length || RecommendedGame.All.Any(g=>new Uri(g.StoreUrl).Host!="store.steampowered.com"))throw new Exception("Invalid store catalogue");
        if(RecommendedGame.All.Length<120)throw new Exception("Catalogue was not expanded enough");
        var favorites=new RecommendationFavorites(root);favorites.Toggle("620");if(!new RecommendationFavorites(root).Ids.Contains("620"))throw new Exception("Favorite didn't persist");favorites.Toggle("620");if(new RecommendationFavorites(root).Ids.Count!=0)throw new Exception("Favorite didn't unpin");
        if(ui) {
            using var handler=new StoreFixture();using var client=new HttpClient(handler);
            using var form=new RecommendationsForm(root,_=>Task.FromResult<Bitmap?>(null),client);form.Show();Application.DoEvents();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var grid=(CustomCheckListBox)typeof(RecommendationsForm).GetField("grid",flags)!.GetValue(form)!;
            var category=(ComboBox)typeof(RecommendationsForm).GetField("category",flags)!.GetValue(form)!;
            category.SelectedItem="Хорроры";
            if(grid.Items.Count<3)throw new Exception("Horror category empty");
            grid.SelectItem(0);
            var detailsTask=(Task)typeof(RecommendationsForm).GetMethod("SelectGame",flags)!.Invoke(form,null)!;
            var loadUntil=DateTime.UtcNow.AddSeconds(3);while(!detailsTask.IsCompleted&&DateTime.UtcNow<loadUntil){Application.DoEvents();Thread.Sleep(10);}detailsTask.GetAwaiter().GetResult();
            var description=(Control)typeof(RecommendationsForm).GetField("description",flags)!.GetValue(form)!;
            var deadline=DateTime.UtcNow.AddSeconds(3);while(description.Text=="Загрузка описания Steam…" && DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(10);}
            if(description.Text!="Fixture & description" || handler.Calls!=1)throw new Exception("Steam details: "+description.Text+" calls="+handler.Calls);
            var gamePage=(Panel)typeof(RecommendationsForm).GetField("gamePage",flags)!.GetValue(form)!;
            var count=(Label)typeof(RecommendationsForm).GetField("shotCount",flags)!.GetValue(form)!;
            var next=(ModernButton)typeof(RecommendationsForm).GetField("nextShot",flags)!.GetValue(form)!;
            if(!gamePage.Visible||!count.Text.StartsWith("1 / 2"))throw new Exception("Game page or screenshot carousel missing");
            next.PerformClick();if(!count.Text.StartsWith("2 / 2"))throw new Exception("Screenshot next failed");
            next.PerformClick();if(!count.Text.StartsWith("1 / 2"))throw new Exception("Screenshot wrap failed");
            Directory.CreateDirectory("artifacts");using(var pageImage=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(pageImage,form.ClientRectangle);pageImage.Save("artifacts/recommendation-game.png");}
            typeof(RecommendationsForm).GetMethod("BackToCatalogue",flags)!.Invoke(form,null);
            if(gamePage.Visible)throw new Exception("Back navigation failed");
            detailsTask=(Task)typeof(RecommendationsForm).GetMethod("SelectGame",flags)!.Invoke(form,null)!;
            while(!detailsTask.IsCompleted){Application.DoEvents();Thread.Sleep(10);}detailsTask.GetAwaiter().GetResult();
            var pin=(ModernButton)typeof(RecommendationsForm).GetField("pin",flags)!.GetValue(form)!;pin.PerformClick();typeof(RecommendationsForm).GetMethod("BackToCatalogue",flags)!.Invoke(form,null);category.SelectedIndex=1;
            if(grid.Items.Count!=1)throw new Exception("Favorites filter failed");
                        using(var launcher=new Form1(Path.Combine(root,"library"))) {
                var games=new List<GameInfo>{new(){Name="A",ExePath="steam://rungameid/620",SteamAppId="620"},new(){Name="B",ExePath="steam://rungameid/730",SteamAppId="730"},new(){Name="Local",ExePath="fixture.exe",SteamAppId=""}};
                typeof(Form1).GetField("myGames",flags)!.SetValue(launcher,games);
                typeof(Form1).GetField("steamUser",flags)!.SetValue(launcher,"76561198000000000");typeof(Form1).GetField("steamKey",flags)!.SetValue(launcher,new string('a',32));launcher.AchievementClient=client;
                var task=(Task)typeof(Form1).GetMethod("SyncLibraryAchievements",flags)!.Invoke(launcher,new object?[]{null})!;
                var until=DateTime.UtcNow.AddSeconds(3);while(!task.IsCompleted&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}task.GetAwaiter().GetResult();
                if(games[0].DisplayPlayedSeconds!=2000*3600||games[1].SteamPlaytimeMinutes!=0||games[2].SteamPlaytimeMinutes!=null)throw new Exception("Steam playtime totals were not synced");
                games[0].PlayedSeconds=10;games[1].PlayedSeconds=100;
                var sort=(ModernSelect)typeof(Form1).GetField("sortGames",flags)!.GetValue(launcher)!;sort.SelectedIndex=3;
                if(((List<GameInfo>)typeof(Form1).GetField("visibleGames",flags)!.GetValue(launcher)!)[0]!=games[0])throw new Exception("Steam total playtime sort failed");
                if(games[0].AchievementSummary?.Contains("50%")!=true||games[1].AchievementSummary?.Contains("50%")!=true||games[2].AchievementSummary!=null)throw new Exception("Library-wide achievement sync failed");
            }
            Directory.CreateDirectory("artifacts");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save("artifacts/recommendations.png");}
            using var settings=new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{});settings.Show();using(var image=new Bitmap(settings.Width,settings.Height)){settings.DrawToBitmap(image,settings.ClientRectangle);image.Save("artifacts/steam-settings.png");}
        }
        Console.WriteLine("PASS: "+RecommendedGame.All.Length+" recommendations, full game page, screenshot carousel/back navigation, modern sorting, genres, official store links, favorite persistence/filter and Steam description fixture; settings UI. No Steam credentials used.");
    }
}
