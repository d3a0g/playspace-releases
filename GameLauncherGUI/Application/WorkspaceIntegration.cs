namespace GameLauncherGUI;

public partial class Form1
{
    private readonly ModernSelect collectionFilter=new(){Width=180,Height=30};
    private readonly Dictionary<GameInfo,DateTimeOffset> sessionStarted=new();
    private IReadOnlyList<LauncherCommand> WorkspaceCommands()=>new LauncherCommand[]{
        new("Обновления игр","Библиотека",OpenGameUpdates),
        new("Помощь с запуском игры","Помощь",OpenLastLaunchHelp),
        new("Найти и объединить дубликаты","Библиотека",OpenDuplicates),
        new("Перенести выбранную игру на другой диск","Библиотека",OpenGameTransfer),
        new("Настроить главную страницу","Настройки",()=>{using var form=new HomeSettingsForm(appDataFolder,ApplyWorkspaceSettings);form.ShowDialog(this);}),
        new("Центр загрузок","Раздел",()=>{using var form=new DownloadsForm(Workspace.For(appDataFolder).Transfers);form.ShowDialog(this);}),
        new("Мои коллекции и теги","Библиотека",()=>{using var form=new CollectionsForm(myGames,Workspace.For(appDataFolder).Collections);form.ShowDialog(this);RefreshCollectionFilter();RefreshList();}),
        new("Сейчас в игре","Библиотека",()=>{using var form=new NowPlayingForm(()=>sessionStarted.Where(p=>sessions.TryGetValue(p.Key,out var state)&&state.Seen).Select(p=>new PlayingGame(p.Key,DateTimeOffset.UtcNow-p.Value,ModProfileLabel(p.Key))).ToArray(),OpenGameCare,OpenPerformance);form.ShowDialog(this);}),
        new("История изменений","Библиотека",()=>{using var form=new ActivityHistoryForm(myGames,Workspace.For(appDataFolder).History,SelectedGame);form.ShowDialog(this);}),
        new("Режим контроллера / большой экран","Библиотека",()=>{using var form=new ControllerLibraryForm(myGames,Workspace.For(appDataFolder).Collections,LoadWidePosterAsync);form.ShowDialog(this);if(form.LaunchRequested is GameInfo game)LaunchGame(game);}),
        new("Вид, доступность и офлайн-режим","Настройки",()=>{using var form=new AppearanceForm(appDataFolder,ApplyWorkspaceSettings);form.ShowDialog(this);}),
        new("Моды и наборы","Раздел",OpenMods),new("Рекомендации","Раздел",OpenRecommendations),new("Настройки Steam и приложения","Настройки",OpenSettings),
        new("Сеть / WARP и диагностика","Раздел",OpenNetwork),
        new("Первый запуск · мастер настройки","Помощь",()=>OpenHealth("first-run")),
        new("Копии и перенос настроек","Настройки",()=>OpenHealth("backup")),
        new("Место на диске и очистка","Обслуживание",()=>OpenHealth("storage")),
        new("Отчёт об ошибке","Поддержка",()=>OpenHealth("report")),
        new("Безопасный режим","Восстановление",()=>OpenHealth("safe-mode"))
    };
    private void OpenWorkspace(){using var form=new WorkspaceForm(WorkspaceCommands());form.ShowDialog(this);}
    private static string ModProfileLabel(GameInfo game)
    {
        try{if(StrategyGameAdapter.For(game) is StrategyGameAdapter strategy)return StrategyProfilesForm.ActiveName(new StrategyModsManager(strategy,game));string? root=NativeModAdapter.For(game)?.Root(game)??ModGameAdapter.For(game)?.FindRoot(game);return root==null?"Нет поддерживаемого адаптера":new ModProfileStore(root,()=>{}).ActiveName();}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){return "Не удалось определить набор";}
    }
    private void OpenPalette()
    {
        if(launchingGame)return;var commands=WorkspaceCommands().ToList();foreach(var game in myGames){var selected=game;commands.Add(new(selected.Name,"Играть",()=>LaunchGame(selected)));commands.Add(new(selected.Name+" · сохранения и запуск","Управление игрой",()=>OpenGameCare(selected)));commands.Add(new(selected.Name+" · графика","Производительность",()=>OpenPerformance(selected)));}
        foreach(var recommendation in RecommendedGame.All){var selected=recommendation;commands.Add(new(selected.Name,"Магазин / рекомендации",()=>{using var page=new RecommendationsForm(appDataFolder,LoadPosterAsync,null,selected.Id);page.ShowDialog(this);}));}
        using var form=new CommandPalette(commands);form.ShowDialog(this);form.Choice?.Invoke();
    }
    private void InitializeWorkspace()
    {
        foreach(var game in myGames)if(!Guid.TryParseExact(game.LibraryId,"N",out _))game.LibraryId=Guid.NewGuid().ToString("N");
        try{if(myGames.Count>0&&File.Exists(dataFile)&&Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(dataFile)).Any(item=>!Guid.TryParseExact(item.Value<string>("LibraryId"),"N",out _)))SaveData();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException){libraryNotice="Не удалось сохранить идентификаторы библиотеки для коллекций.";}
        collectionFilter.SetPlaceholder("Все коллекции");RefreshCollectionFilter();collectionFilter.SelectedIndexChanged+=(s,e)=>RefreshList();
        gameMenu!.Items.Add(new ToolStripMenuItem("История изменений этой игры…",null,(s,e)=>{using var form=new ActivityHistoryForm(myGames,Workspace.For(appDataFolder).History,SelectedGame);form.ShowDialog(this);}));
        ApplyWorkspaceSettings();
    }
    private void RefreshCollectionFilter(){string? current=collectionFilter.SelectedItem?.ToString();collectionFilter.SelectedIndex=-1;collectionFilter.Items.Clear();collectionFilter.Items.Add("Все коллекции");collectionFilter.Items.AddRange(Workspace.For(appDataFolder).Collections.State.Names.Cast<object>());collectionFilter.SelectedIndex=Math.Max(0,collectionFilter.Items.FindIndex(i=>i.ToString()==current));}
    private void ApplyWorkspaceSettings()
    {
        var next=LauncherPreferences.Load(appDataFolder);
        bool homeChanged=preferences.HomeLibrary!=next.HomeLibrary||preferences.HomeSort!=next.HomeSort||preferences.HomeBanner!=next.HomeBanner||preferences.HomePosters!=next.HomePosters;
        preferences=next;PresentationOptions.Configure(preferences);PresentationOptions.Apply(this);
        if(listBoxGames.LandscapePosters)listBoxGames.PosterMinimumWidth=Math.Clamp(preferences.PosterWidth+70,230,330);
        if(homeChanged)ApplyHomePreferences();RefreshList();lblStatus.Text=preferences.OfflineMode?"Офлайн-режим · сохранённая библиотека и кеш доступны":"Настройки пространства применены.";
    }
}
