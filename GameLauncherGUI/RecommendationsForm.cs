using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal sealed record RecommendedGame(string Id,string Name,params string[] Categories)
{
    public string StoreUrl => "https://store.steampowered.com/app/"+Id+"/";
    public static readonly RecommendedGame[] All = new RecommendedGame[] {
        new("730","Counter-Strike 2","Шутеры","Кооп"),new("1172470","Apex Legends","Шутеры","Кооп"),new("548430","Deep Rock Galactic","Шутеры","Кооп"),
        new("892970","Valheim","Выживание","Кооп"),new("648800","Raft","Выживание","Кооп"),new("264710","Subnautica","Выживание","Приключения"),
        new("620","Portal 2","Головоломки","Кооп"),new("105600","Terraria","Выживание","Кооп","Песочницы"),new("427520","Factorio","Стратегии","Песочницы","Кооп"),
        new("1966720","Lethal Company","Хорроры","Кооп"),new("739630","Phasmophobia","Хорроры","Кооп"),new("883710","Resident Evil 2","Хорроры"),
        new("394360","Hearts of Iron IV","Стратегии"),new("281990","Stellaris","Стратегии"),new("1158310","Crusader Kings III","Стратегии"),
        new("1091500","Cyberpunk 2077","Ролевые","Приключения"),new("1086940","Baldur's Gate 3","Ролевые","Кооп"),new("1245620","ELDEN RING","Ролевые","Приключения"),
        new("413150","Stardew Valley","Уютные","Кооп","Песочницы"),new("1332010","Stray","Уютные","Приключения"),new("220","Half-Life 2","Шутеры","Приключения"),
        new("367520","Hollow Knight","Приключения"),new("477160","Human Fall Flat","Головоломки","Кооп"),new("391540","Undertale","Ролевые","Уютные")
    }.Concat(ExpandedRecommendations.Games).Concat(SteamRecommendationSnapshot.Load()).DistinctBy(game=>game.Id).Take(500).ToArray();
}
internal sealed class RecommendationFavorites
{
    private readonly string file;
    public HashSet<string> Ids { get; private set; } = new();
    public RecommendationFavorites(string folder)
    {
        file=Path.Combine(folder,"recommendation-favorites.json");
        try {if(File.Exists(file))Ids=JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(file))??new();}
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    public void Toggle(string id)
    {
        var updated=new HashSet<string>(Ids);if(!updated.Add(id))updated.Remove(id);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonConvert.SerializeObject(updated));File.Move(temp,file,true);Ids=updated;}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
internal sealed class RecommendationsForm : StudioDialog
{
    private readonly CustomCheckListBox grid=new() {Dock=DockStyle.Fill,PosterView=true,ShowPinButtons=true,ShowCheckBoxes=false};
    private readonly ComboBox category=new() {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White,Dock=DockStyle.Fill};
    private readonly TextBox search=new() {Dock=DockStyle.Fill,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White,PlaceholderText="Поиск игры"};
    private readonly Label status=new() {Dock=DockStyle.Fill,ForeColor=Color.FromArgb(170,180,205)};
    private readonly Label title=new() {Dock=DockStyle.Fill,Font=new Font("Segoe UI",17,FontStyle.Bold)};
    private readonly FormattedDescription description=new() {Dock=DockStyle.Fill};
        private readonly Panel cataloguePage=new(){Dock=DockStyle.Fill};
    private readonly Panel gamePage=new(){Dock=DockStyle.Fill,Visible=false};
    private readonly CategoryRail categoryRail=new(){Dock=DockStyle.Fill};
    private readonly PictureBox banner=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(18,22,34)};
    private readonly List<Bitmap> screenshots=new();
    private readonly Label shotCount=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.FromArgb(171,180,205)};
    private readonly ModernButton previousShot=new("←",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private readonly ModernButton nextShot=new("→",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private int shotIndex;
    private readonly ModernButton store=new("Перейти к игре в Steam ↗",Color.FromArgb(115,94,244)) {Dock=DockStyle.Fill,Enabled=false};
    private readonly ModernButton pin=new("☆ Закрепить",Color.FromArgb(40,45,65)) {Dock=DockStyle.Fill,Enabled=false};
    private readonly RecommendationFavorites favorites;
    private readonly string cache;
    private readonly Func<GameInfo,Task<Bitmap?>> loadPoster;
    private RecommendedGame[] visible=Array.Empty<RecommendedGame>();
    private RecommendedGame? selected;
    private CancellationTokenSource? detailCancellation;
    private int viewVersion;
    private static readonly HttpClient sharedHttp=new() {Timeout=TimeSpan.FromSeconds(12)};
    private readonly HttpClient http;
    public RecommendationsForm(string folder,Func<GameInfo,Task<Bitmap?>> loadPoster,HttpClient? storeClient=null)
    {
        http=storeClient??sharedHttp;this.loadPoster=loadPoster;favorites=new(folder);cache=Path.Combine(folder,"store-details");
        Text="PLAYSPACE / Рекомендации";MinimumSize=new Size(960,720);Size=new Size(1160,830);
        ContentHost.Controls.Add(cataloguePage);ContentHost.Controls.Add(gamePage);
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));cataloguePage.Controls.Add(layout);
        var filters=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};filters.RowStyles.Add(new RowStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        filters.Controls.Add(new Label{Text="Найди свой следующий мир",Dock=DockStyle.Fill,Font=new Font("Segoe UI",18,FontStyle.Bold),AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft},0,0);
        var searchBox=new ModernSearchField(search){Dock=DockStyle.Fill,Margin=new Padding(0,10,0,12)};filters.Controls.Add(searchBox,1,0);layout.Controls.Add(filters,0,0);
        category.Items.AddRange(new object[]{"Все игры","★ Закреплённые"});category.Items.AddRange(RecommendedGame.All.SelectMany(g=>g.Categories).Distinct().Cast<object>().ToArray());category.SelectedIndex=0;
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,192));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.Controls.Add(categoryRail,0,0);body.Controls.Add(grid,1,0);layout.Controls.Add(body,0,1);layout.Controls.Add(status,0,2);
        for(int i=0;i<category.Items.Count;i++){int index=i;var button=new ModernButton(category.Items[i].ToString()!,Color.FromArgb(25,30,46)){Width=166,Height=40,Margin=new Padding(0,0,8,6),Font=new Font("Segoe UI",9),IsActive=i==0};button.Click+=(s,e)=>category.SelectedIndex=index;categoryRail.Controls.Add(button);}
        var detailLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(28,8,28,20)};
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent,58));detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent,42));gamePage.Controls.Add(detailLayout);
        var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var back=new ModernButton("←  К подборке",Color.FromArgb(31,35,51)){Dock=DockStyle.Fill,Margin=new Padding(0,0,16,8)};back.Click+=(s,e)=>BackToCatalogue();heading.Controls.Add(back,0,0);heading.Controls.Add(title,1,0);detailLayout.Controls.Add(heading,0,0);
        detailLayout.Controls.Add(banner,0,1);
        var carousel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};carousel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));carousel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));carousel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));carousel.Controls.Add(previousShot,0,0);carousel.Controls.Add(shotCount,1,0);carousel.Controls.Add(nextShot,2,0);detailLayout.Controls.Add(carousel,0,2);
        previousShot.Click+=(s,e)=>ShowShot(shotIndex-1);nextShot.Click+=(s,e)=>ShowShot(shotIndex+1);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(0,8,0,8)};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));actions.Controls.Add(store,0,0);actions.Controls.Add(pin,1,0);pin.Margin=new Padding(12,0,0,0);detailLayout.Controls.Add(actions,0,3);
        var about=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(18)};description.Font=new Font("Segoe UI",11);about.Controls.Add(description);detailLayout.Controls.Add(about,0,4);
        category.SelectedIndexChanged+=(s,e)=>{for(int i=0;i<categoryRail.Controls.Count;i++)((ModernButton)categoryRail.Controls[i]).IsActive=i==category.SelectedIndex;RefreshGames();};search.TextChanged+=(s,e)=>RefreshGames();
        grid.ItemClick+=async(s,e)=>await SelectGame();grid.ItemDoubleClick+=async(s,e)=>await SelectGame();
        grid.PinToggleRequested+=i=>Toggle(visible.ElementAtOrDefault(i));pin.Click+=(s,e)=>Toggle(selected);
        store.Click+=(s,e)=>{if(selected!=null)try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(selected.StoreUrl){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){status.Text="Не удалось открыть браузер.";}};
        Shown+=(s,e)=>RefreshGames();description.Text="Выбери игру: здесь появятся описание и скриншоты из Steam. Подборка по жанрам, без учёта характеристик ПК.";
    }
    private void Toggle(RecommendedGame? game)
    {
        if(game==null)return;
        try {favorites.Toggle(game.Id);if(category.SelectedIndex==1 && !gamePage.Visible)RefreshGames();else {for(int i=0;i<visible.Length;i++)grid.Items[i].IsPinned=favorites.Ids.Contains(visible[i].Id);grid.Invalidate();UpdatePin();}}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){status.Text="Не удалось сохранить закреплённые игры.";}
    }
    private void UpdatePin()=>pin.Text=selected!=null&&favorites.Ids.Contains(selected.Id)?"★ Открепить":"☆ Закрепить";
    private async void RefreshGames()
    {
        int version=++viewVersion;string filter=category.SelectedItem?.ToString()??"Все игры";
        visible=RecommendedGame.All.Where(g=>(category.SelectedIndex==0 || category.SelectedIndex==1&&favorites.Ids.Contains(g.Id)||g.Categories.Contains(filter))&&g.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).ToArray();
        detailCancellation?.Cancel();selected=null;store.Enabled=pin.Enabled=false;title.Text="Рекомендации";description.Text="Выбери игру, чтобы прочитать описание магазина Steam.";ClearShots();grid.Items.Clear();
        foreach(var game in visible)grid.Items.Add(new CustomListItem(game.Name,false,string.Join(" · ",game.Categories)){IsPinned=favorites.Ids.Contains(game.Id)});
        grid.ResetView();status.Text=visible.Length==0?"Здесь пока нет игр. Закрепляй понравившиеся карточки звёздочкой.":$"Игр в подборке: {visible.Length}";
        var games=visible.ToArray();var items=grid.Items.ToArray();
        int cursor=-1;
        async Task LoadNext()
        {
            while(!IsDisposed && version==viewVersion)
            {
                int i=Interlocked.Increment(ref cursor);if(i>=games.Length)return;
                var image=await loadPoster(new GameInfo{Name=games[i].Name,SteamAppId=games[i].Id,ExePath=""});
                if(IsDisposed||version!=viewVersion)return;items[i].PosterImage=image;grid.Invalidate();
            }
        }
        await Task.WhenAll(Enumerable.Range(0,4).Select(_=>LoadNext()));
    }
    private async Task SelectGame()
    {
        selected=visible.ElementAtOrDefault(grid.SelectedIndex);detailCancellation?.Cancel();detailCancellation?.Dispose();detailCancellation=new();var token=detailCancellation.Token;
        var game=selected;if(game!=null){cataloguePage.Hide();gamePage.Show();gamePage.BringToFront();}ClearShots();store.Enabled=pin.Enabled=game!=null;UpdatePin();if(game==null)return;title.Text=game.Name;description.Text="Загрузка описания Steam…";
        try {
            JObject? data=null;string file=Path.Combine(cache,game.Id+".json");
            try {var response=JObject.Parse(await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={game.Id}&l=russian",token));data=response[game.Id]?["success"]?.Value<bool>()==true?response[game.Id]?["data"] as JObject:null;
                if(data!=null){Directory.CreateDirectory(cache);await File.WriteAllTextAsync(file,data.ToString(),token);}}
            catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) {if(!token.IsCancellationRequested&&File.Exists(file))data=JObject.Parse(await File.ReadAllTextAsync(file,token));}
            token.ThrowIfCancellationRequested();if(IsDisposed)return;
            if(data==null){description.Text="Steam не предоставил описание. Можно открыть официальную страницу игры.";return;}
            description.Text=GameDescription.Format(data);
            foreach(var shot in (data["screenshots"] as JArray??new JArray()).Take(12)) {
                string? url=shot.Value<string>("path_full")??shot.Value<string>("path_thumbnail");if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.Host.EndsWith(".steamstatic.com",StringComparison.OrdinalIgnoreCase))continue;
                try {
                    using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
                    using var input=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();byte[] buffer=new byte[81920];int count;
                    while((count=await input.ReadAsync(buffer,token))>0){if(bytes.Length+count>8*1024*1024)throw new InvalidDataException();bytes.Write(buffer,0,count);}
                    bytes.Position=0;using var original=Image.FromStream(bytes);var image=new Bitmap(original,1280,Math.Max(1,1280*original.Height/original.Width));
                    if(IsDisposed||token.IsCancellationRequested){image.Dispose();return;}
                    screenshots.Add(image);if(screenshots.Count==1)ShowShot(0);else UpdateShotControls();
                } catch(Exception ex) when(ex is HttpRequestException or IOException or ArgumentException or OutOfMemoryException) { }
            }
            if(screenshots.Count==0 && !token.IsCancellationRequested)
                shotCount.Text="Скриншоты недоступны. Можно открыть страницу Steam.";
        } catch(OperationCanceledException) { }
        catch(Exception ex) when(ex is IOException or JsonException or ArgumentException) {if(!IsDisposed&&!token.IsCancellationRequested)description.Text="Не удалось загрузить данные Steam. Переход в магазин доступен.";}
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(gamePage.Visible) {
            if(keyData==Keys.Escape){BackToCatalogue();return true;}
            if(!description.Focused && keyData is Keys.Left or Keys.Right){ShowShot(shotIndex+(keyData==Keys.Left?-1:1));return true;}
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void BackToCatalogue(){detailCancellation?.Cancel();ClearShots();gamePage.Hide();cataloguePage.Show();cataloguePage.BringToFront();if(category.SelectedIndex==1)RefreshGames();}
    private void ShowShot(int index){if(screenshots.Count==0)return;shotIndex=(index%screenshots.Count+screenshots.Count)%screenshots.Count;banner.Image=screenshots[shotIndex];UpdateShotControls();}
    private void UpdateShotControls(){previousShot.Enabled=nextShot.Enabled=screenshots.Count>1;shotCount.Text=screenshots.Count==0?"Скриншоты появятся после загрузки из Steam":$"{shotIndex+1} / {screenshots.Count}  ·  Скриншоты Steam";}
    private void ClearShots(){banner.Image=null;foreach(var image in screenshots)image.Dispose();screenshots.Clear();shotIndex=0;UpdateShotControls();}
    protected override void Dispose(bool disposing){if(disposing){viewVersion++;detailCancellation?.Cancel();detailCancellation?.Dispose();ClearShots();}base.Dispose(disposing);}
}
public partial class Form1
{
    private void OpenRecommendations(){using var form=new RecommendationsForm(appDataFolder,LoadPosterAsync);form.ShowDialog(this);}
}
