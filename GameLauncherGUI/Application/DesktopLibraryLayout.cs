namespace GameLauncherGUI;

public partial class Form1
{
    private readonly ModernSelect librarySource=new(){Width=176,Height=36};
    private ModernButton? desktopGridButton,desktopListButton;
    private readonly ToolTip libraryRailTips=new(){InitialDelay=250,ReshowDelay=100};
    private void ConfigureDesktopLibrary()
    {
        if(listBoxGames.Parent is not TableLayoutPanel content||content.Parent is not TableLayoutPanel shell)return;
        var sidebar=(TableLayoutPanel)shell.GetControlFromPosition(0,0)!;
        shell.Padding=new Padding(8,4,8,10);shell.ColumnStyles[0].Width=60;sidebar.Padding=new Padding(2,4,6,4);
        foreach(int row in new[]{0,5,13})sidebar.RowStyles[row].Height=0;
        foreach(var button in sidebar.Controls.OfType<ModernButton>()){button.IconOnly=true;button.AccessibleName??=button.Text.Replace("\n"," ");libraryRailTips.SetToolTip(button,button.Text.Replace("\n"," · "));button.Margin=new Padding(0,2,0,2);}
        Disposed+=(s,e)=>libraryRailTips.Dispose();
        var oldHeader=(TableLayoutPanel)content.GetControlFromPosition(0,1)!;
        var actions=(TableLayoutPanel)content.GetControlFromPosition(0,3)!;
        var searchHost=searchGames.Parent!;
        var toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};
        toolbar.ColumnStyles.Add(new(SizeType.Percent,100));toolbar.ColumnStyles.Add(new(SizeType.Absolute,62));toolbar.ColumnStyles.Add(new(SizeType.Absolute,62));toolbar.ColumnStyles.Add(new(SizeType.Absolute,94));toolbar.ColumnStyles.Add(new(SizeType.Absolute,126));
        toolbar.Controls.Add(searchHost,0,0);searchHost.Dock=DockStyle.Fill;searchHost.Margin=new Padding(0,4,8,4);
        desktopGridButton=new ModernButton("▦",panelColor){Dock=DockStyle.Fill,AccessibleName="Сетка обложек",Font=new Font("Segoe UI",16)};
        desktopListButton=new ModernButton("☰",panelColor){Dock=DockStyle.Fill,AccessibleName="Компактный список",Font=new Font("Segoe UI",14)};
        desktopGridButton.Click+=(s,e)=>SetDesktopView(true);desktopListButton.Click+=(s,e)=>SetDesktopView(false);toolbar.Controls.Add(desktopGridButton,1,0);toolbar.Controls.Add(desktopListButton,2,0);
        var filterToggle=new ModernButton("Фильтры",panelColor){Dock=DockStyle.Fill,AccessibleName="Показать или скрыть фильтры"};toolbar.Controls.Add(filterToggle,3,0);toolbar.Controls.Add(btnAddManual,4,0);
        btnScan.Visible=false;Controls.Add(btnScan);
        var heading=new Panel{Dock=DockStyle.Fill};heading.Controls.Add(libraryCount);libraryCount.Dock=DockStyle.Fill;libraryCount.Font=new Font("Segoe UI",10);libraryCount.Margin=Padding.Empty;
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new(SizeType.Percent,100));body.ColumnStyles.Add(new(SizeType.Absolute,202));
        body.Controls.Add(listBoxGames,0,0);listBoxGames.LandscapePosters=true;listBoxGames.CompactRows=true;listBoxGames.PosterMinimumWidth=Math.Clamp(preferences.PosterWidth+70,230,330);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(14,6,4,8),BackColor=Color.FromArgb(16,24,42)};body.Controls.Add(filters,1,0);
        void Caption(string text)=>filters.Controls.Add(new SmoothLabel{Text=text,Width=176,Height=30,ForeColor=Color.FromArgb(151,175,201),TextAlign=ContentAlignment.BottomLeft});
        filters.Controls.Add(new SmoothLabel{Text="Фильтры",Width=176,Height=38,Font=new Font("Segoe UI",13,FontStyle.Bold)});
        Caption("Коллекция");collectionFilter.Dock=DockStyle.None;collectionFilter.Width=176;collectionFilter.Height=36;filters.Controls.Add(collectionFilter);
        Caption("Источник");librarySource.Items.AddRange(new object[]{"Все источники","Steam","Epic Games","Ubisoft Connect","Riot Client","Локальная игра"});librarySource.SelectedIndex=0;filters.Controls.Add(librarySource);librarySource.SelectedIndexChanged+=(s,e)=>RefreshList();
        Caption("Сортировка");sortGames.Dock=DockStyle.None;sortGames.Width=176;sortGames.Height=36;filters.Controls.Add(sortGames);
        var reset=new ModernButton("Сбросить фильтры",panelColor){Width=176,Height=36,Margin=new Padding(0,14,0,0)};reset.Click+=(s,e)=>{searchGames.Clear();librarySource.SelectedIndex=0;collectionFilter.SelectedIndex=0;};filters.Controls.Add(reset);
        var controller=new ModernButton("Большой экран",panelColor){Width=176,Height=36};controller.Click+=(s,e)=>{using var form=new ControllerLibraryForm(myGames,Workspace.For(appDataFolder).Collections,LoadWidePosterAsync);form.ShowDialog(this);if(form.LaunchRequested is GameInfo game)LaunchGame(game);};filters.Controls.Add(controller);
        filterToggle.Click+=(s,e)=>{bool show=!filters.Visible;filters.Visible=show;body.ColumnStyles[1].Width=show?202:0;};
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,BackColor=Color.FromArgb(20,25,37)};bottom.ColumnStyles.Add(new(SizeType.Absolute,440));bottom.ColumnStyles.Add(new(SizeType.Percent,100));bottom.Controls.Add(actions,0,0);
        var bannerHost=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=1,ColumnCount=1,Padding=new Padding(12,0,0,0)};bannerHost.RowStyles.Add(new(SizeType.Absolute,64));hero.Compact=true;hero.Dock=DockStyle.Fill;bannerHost.Controls.Add(hero,0,0);bottom.Controls.Add(bannerHost,1,0);
        content.Controls.Remove(oldHeader);oldHeader.Dispose();content.RowStyles[0].Height=52;content.RowStyles[1].Height=30;content.RowStyles[3].Height=64;
        content.Controls.Add(toolbar,0,0);content.Controls.Add(heading,0,1);content.Controls.Add(body,0,2);content.Controls.Add(bottom,0,3);
        if(Controls.OfType<Panel>().FirstOrDefault(p=>p.Name=="WindowChrome") is Panel chrome)
        {
            chrome.Height=44;toolbar.Padding=Padding.Empty;content.RowStyles[0].Height=0;
            var icons=chrome.Controls.OfType<PictureBox>().ToArray();var windowGroups=chrome.Controls.OfType<FlowLayoutPanel>().ToArray();
            foreach(var label in chrome.Controls.OfType<Label>())label.Visible=false;
            var titleLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};titleLayout.ColumnStyles.Add(new(SizeType.Absolute,54));titleLayout.ColumnStyles.Add(new(SizeType.Percent,100));titleLayout.ColumnStyles.Add(new(SizeType.Absolute,150));
            titleLayout.Controls.Add(toolbar,1,0);
            foreach(var icon in icons){icon.Anchor=AnchorStyles.None;titleLayout.Controls.Add(icon,0,0);libraryRailTips.SetToolTip(icon,"LauncherPlaySpace");}
            foreach(var windowActions in windowGroups){windowActions.Dock=DockStyle.Fill;windowActions.Padding=new Padding(6,3,10,3);titleLayout.Controls.Add(windowActions,2,0);}
            chrome.Controls.Add(titleLayout);titleLayout.BringToFront();
            toolbar.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}};
        }
        if(preferences.DesktopLayoutVersion<1){var next=LauncherPreferences.Load(appDataFolder);next.DesktopLayoutVersion=1;next.HomePosters=true;try{next.Save(appDataFolder);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){LauncherSession.Record(appDataFolder,"Сохранение вида библиотеки",ex);}preferences=next;}
        ApplyHomePreferences();RefreshList();
    }
    private void SetDesktopView(bool posters)
    {
        listBoxGames.PosterView=posters;
        try{var next=LauncherPreferences.Load(appDataFolder);next.HomePosters=posters;next.Save(appDataFolder);preferences=next;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){lblStatus.Text="Вид изменён, но его не удалось сохранить.";}
        RefreshList();
    }
    private Task<Bitmap?> LoadWidePosterAsync(GameInfo game)
    {
        posters.VerifyAccess();string id=game.SteamAppId??"";if(LauncherSession.SafeMode||resourcesDisposed||!GameUpdates.SteamId(id))return Task.FromResult<Bitmap?>(null);
        return posters.GetAsync("wide:"+id,token=>PosterLoader.LoadAsync(appDataFolder,id,Workspace.For(appDataFolder).Offline,token,true));
    }
}
