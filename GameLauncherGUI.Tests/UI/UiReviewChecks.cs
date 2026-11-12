using System.Reflection;
using GameLauncherGUI;
using Newtonsoft.Json.Linq;

internal static class UiReviewChecks
{
    private sealed class FakeWarp : IWarpClient
    {
        public Task<WarpStatus> Status(CancellationToken ct)=>Task.FromResult(new WarpStatus(WarpState.Disconnected,"Компонент установлен. Подключение отключено."));
        public Task Connect(bool consent,CancellationToken ct)=>Task.CompletedTask;
        public Task Disconnect(CancellationToken ct)=>Task.CompletedTask;
    }
    private sealed record View(string Name,Func<Form> Create,Action<Form>? Prepare=null);
    private static IEnumerable<Control> Children(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c)));
    private static void Click(Form form,string text)=>Children(form).OfType<ModernButton>().First(b=>b.Text==text).PerformClick();
    internal static void Run()
    {
        string output=Path.GetFullPath("artifacts/ui-review");Directory.CreateDirectory(output);string folder=Path.Combine(output,"fixtures");Directory.CreateDirectory(folder);
        var preferences=new LauncherPreferences{OfflineMode=true,OnboardingCompleted=true,ReduceMotion=true};preferences.Save(folder);Workspace.Use(folder);
        string gameRoot=Path.Combine(folder,"DemoGame");Directory.CreateDirectory(Path.Combine(gameRoot,"REPO_Data"));File.WriteAllText(Path.Combine(gameRoot,"REPO.exe"),"fixture");
        var game=new GameInfo{Name="R.E.P.O.",SteamAppId="3241660",ExePath=Path.Combine(gameRoot,"REPO.exe"),InstallDirectory=gameRoot,Care=new()};
        var second=new GameInfo{Name="Portal 2",SteamAppId="620",ExePath=Path.Combine(gameRoot,"Portal2.exe"),InstallDirectory=gameRoot,SteamPlaytimeMinutes=1430};File.WriteAllText(second.ExePath,"fixture");
        var games=new[]{game,second};GameLibraryStore.Save(Path.Combine(folder,"games.json"),games.ToList());
        string graphicsIni=Path.Combine(folder,"GameUserSettings.ini");File.WriteAllText(graphicsIni,"[/Script/Engine.GameUserSettings]\nResolutionSizeX=1920\nResolutionSizeY=1080\n[ScalabilityGroups]\nsg.ShadowQuality=2\nsg.TextureQuality=3\n");
        var unrealGame=new GameInfo{Name="MECCHA CHAMELEON",SteamAppId="4704690",ExePath=Path.Combine(gameRoot,"PenguinHotel.exe"),Performance=new(){ConfigFile=graphicsIni}};
        string graphicsJson=Path.Combine(folder,"UserSettings.json");File.WriteAllText(graphicsJson,RedesignChecks.NativeFixture);var nativeGame=new GameInfo{Name="Cyberpunk 2077",SteamAppId="1091500",ExePath=Path.Combine(gameRoot,"Cyberpunk2077.exe"),Performance=new(){ConfigFile=graphicsJson}};
        LocalJson.Write(Path.Combine(folder,"mod-catalog","repo.json"),new[]{new ModPackage{Name="ExampleMod",Owner="Demo",Versions=new(){new(){Version="1.0.0",Description="Демонстрационная карточка мода. Реальная установка не выполняется."}}}});
        LocalJson.Write(Path.Combine(folder,"store-details","620.json"),JObject.Parse("{\"name\":\"Portal 2\",\"short_description\":\"Головоломка с порталами и совместным режимом. Демонстрационные данные для проверки интерфейса.\",\"about_the_game\":\"<h2>Об игре</h2><p>Решайте головоломки, исследуйте лаборатории и играйте вместе с другом.</p>\",\"screenshots\":[]}"));
        var commands=new[]{new LauncherCommand("Центр загрузок","Раздел",()=>{}),new LauncherCommand("Мои коллекции и теги","Библиотека",()=>{}),new LauncherCommand("Копии и перенос настроек","Настройки",()=>{}),new LauncherCommand("Место на диске и очистка","Обслуживание",()=>{}),new LauncherCommand("Отчёт об ошибке","Поддержка",()=>{}),new LauncherCommand("Безопасный режим","Восстановление",()=>{})};
        StudioDialog Panel(Control control){var form=new StudioDialog{Text="PLAYSPACE / Моды",Size=new(1080,760)};var host=Children(form).OfType<Panel>().First(p=>p.Dock==DockStyle.Fill);host.Padding=new Padding(24);host.Controls.Add(control);return form;}
        var strategy=StrategyGameAdapter.All[0];StrategyModsManager Manager()=>new(strategy,new(){Name="Hearts of Iron IV",SteamAppId=strategy.SteamId,InstallDirectory=gameRoot,ExePath=Path.Combine(gameRoot,"hoi4.exe")},Path.Combine(folder,"paradox"),()=>{});
        Form Main(){var form=new Form1(folder,false);form.ClosePrompt=()=> (CloseBehavior.Exit,false);return form;}
        var views=new List<View>{
            new("library-list",Main),new("library-selected",Main,f=>{var list=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f)!;list.SelectItem(0);}),new("library-posters",Main,f=>{var list=(CustomCheckListBox)typeof(Form1).GetField("listBoxGames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f)!;list.PosterView=true;list.ResetView();}),
            new("settings-steam",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{},healthAction:_=>{})),
            new("settings-application",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{},healthAction:_=>{}),f=>Click(f,"Запуск и трей")),
            new("settings-whats-new",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{}),f=>Click(f,"Что нового")),
            new("settings-notifications",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{}),f=>Click(f,"Уведомления")),
            new("settings-appearance",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{}),f=>Click(f,"Оформление")),
            new("settings-updates",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{}),f=>Click(f,"Обновления")),
            new("settings-data",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{},healthAction:_=>{}),f=>Click(f,"Данные и помощь")),
            new("settings-shortcuts",()=>new SettingsForm("","",(_,_,_,_)=>Task.CompletedTask,()=>{}),f=>Click(f,"Ярлыки")),
            new("dlss-compatibility",()=>new DlssCompatibilityForm(game,"NVIDIA GeForce RTX 3060 Ti",folder)),
            new("dlss-libraries",()=>new DlssCompatibilityForm(game,"NVIDIA GeForce RTX 3060 Ti",folder),f=>Click(f,"Библиотеки")),
            new("dlss-user-checks",()=>new DlssCompatibilityForm(game,"NVIDIA GeForce RTX 3060 Ti",folder),f=>Click(f,"Мои проверки")),
            new("recommendations",()=>new RecommendationsForm(folder,_=>Task.FromResult<Bitmap?>(null))),
            new("recommendation-page",()=>new RecommendationsForm(folder,_=>Task.FromResult<Bitmap?>(null),initialGameId:"620")),
            new("mods-catalogue",()=>new ModsForm(()=>Task.FromResult(games.ToList()))),new("mods-installed",()=>new ModsForm(()=>Task.FromResult(games.ToList())),f=>{var button=Children(f).OfType<ModernButton>().First(b=>b.Text.StartsWith("Установленные"));button.PerformClick();}),
            new("mods-native",()=>Panel(new NativeModsPanel(NativeModAdapter.All.First(a=>a.SteamId=="294100"),game))),
            new("mods-strategy",()=>Panel(new StrategyModsPanel(strategy,game,Manager()))),
            new("mod-profiles",()=>new ModProfilesForm(game,gameRoot)),new("strategy-profiles",()=>new StrategyProfilesForm(game,Manager())),
            new("performance",()=>new PerformanceForm(games,game,(_,_)=>true)),
            new("performance-unreal",()=>new PerformanceForm(new[]{unrealGame},unrealGame,(_,_)=>true),f=>Click(f,"Графика")),
            new("performance-native",()=>new PerformanceForm(new[]{nativeGame},nativeGame,(_,_)=>true),f=>Click(f,"Графика")),
            new("performance-preview",()=>new PerformanceForm(new[]{unrealGame},unrealGame,(_,_)=>true),f=>Click(f,"Изменения")),
            new("performance-benchmarks",()=>new PerformanceForm(new[]{nativeGame},nativeGame,(_,_)=>true),f=>{((PerformanceForm)f).SetSnapshot(new("Ryzen 9 5900X","AMD Radeon RX 5600 XT",16UL*1024*1024*1024,8UL*1024*1024*1024,1920,1080,144,false));Click(f,"Тесты");}),
            new("benchmark-sources",()=>new BenchmarkSourcesForm()),new("benchmark-fps",()=>new BenchmarkSourcesForm(),f=>Click(f,"Замеры FPS")),new("benchmark-steam",()=>new BenchmarkSourcesForm(),f=>Click(f,"Статистика Steam")),new("benchmark-videos",()=>new BenchmarkSourcesForm(),f=>Click(f,"Обзоры и видео")),new("benchmark-method",()=>new BenchmarkSourcesForm(),f=>Click(f,"Методика")),
            new("dlss",()=>new DlssForm(game)),new("dlss-rtx30",()=>new RtxOptionsForm(game,"NVIDIA GeForce RTX 3060 Ti")),new("dlss-experimental",()=>new UniversalDlss5Form(game,folder)),
            new("game-backups",()=>new GameCareForm(game,folder,(_,_,_)=>{},()=>false)),
            new("game-launch",()=>new GameCareForm(game,folder,(_,_,_)=>{},()=>false),f=>Click(f,"Запуск и заметки")),
            new("restore-plan",()=>new RestorePlanForm(new[]{new RestoreEntry("save.dat","Заменить")},gameRoot)),
            new("network",()=>new NetworkForm(folder,new FakeWarp(),(_,_)=>Task.FromResult(false),_=>Task.FromResult(false))),
            new("diagnostics",()=>new NetworkDiagnosticsForm()),new("diagnostic-report",()=>new DiagnosticReportForm("Демонстрационный отчёт. Запросы не выполнялись.")),
            new("workspace",()=>new WorkspaceForm(commands)),new("palette",()=>new CommandPalette(commands)),
            new("collections",()=>new CollectionsForm(games,Workspace.For(folder).Collections)),new("history",()=>new ActivityHistoryForm(games,Workspace.For(folder).History)),
            new("now-playing",()=>new NowPlayingForm(()=>Array.Empty<PlayingGame>(),_=>{},_=>{})),
            new("controller",()=>new ControllerLibraryForm(games,Workspace.For(folder).Collections,_=>Task.FromResult<Bitmap?>(null),()=>null,false)),
            new("appearance",()=>new AppearanceForm(folder,()=>{})),
            new("welcome-1",()=>new FirstRunForm(folder,2,0)),new("welcome-2",()=>new FirstRunForm(folder,2,1)),new("welcome-3",()=>new FirstRunForm(folder,2,2)),
            new("support-report",()=>new SupportReportForm(folder,2)),new("settings-backups",()=>new SettingsTransferForm(folder,()=>false)),new("storage",()=>new StorageManagerForm(folder,games,()=>false)),
            new("quick-actions",()=>new StudioActionsForm(game.Name,commands)),new("readiness",()=>new ReadinessForm(game,new[]{new ReadinessItem("Файл запуска",false,"Файл найден."),new ReadinessItem("Границы проверки",false,"Демонстрационные данные.")})),
            new("ubisoft",()=>new UbisoftLaunchForm("Rainbow Six Siege",()=>{})),new("close-choice",()=>new CloseChoiceDialog()),
            new("update",()=>new UpdateAvailableForm(new("1.0.6","https://example.test","## Интерфейс\n- Выровнены кнопки и поля.\n- Улучшена навигация.\n\n## Исправления\n- Исправлено обрезание элементов."))),
            new("downloads",()=>new DownloadsForm(Workspace.For(folder).Transfers))
        };
        var warnings=new List<string>();int total=0;
        foreach(int scale in new[]{100,120})foreach(var view in views){
            preferences.TextScalePercent=scale;preferences.Save(folder);PresentationOptions.Configure(preferences);
            using var form=view.Create();form.StartPosition=FormStartPosition.Manual;form.Location=new(20,20);form.Show();Application.DoEvents();view.Prepare?.Invoke(form);Application.DoEvents();
            foreach(bool compact in new[]{false,true}){
                if(compact){form.Size=form.MinimumSize.Width>=640?form.MinimumSize:new Size(Math.Min(form.Width,800),Math.Min(form.Height,640));Application.DoEvents();}
                string name=view.Name+"-"+scale+(compact?"-compact":"");
                foreach(Control c in Children(form).Where(c=>c.Visible&&c.Width>0&&c.Height>0&&c.Parent!=null)){
                    if(c is not (ModernButton or ModernSelect or ModernSearchField or InputSurface or ComboBox or NumericUpDown))continue;
                    if(c.Parent is ScrollableControl scroll&&scroll.AutoScroll||c.Parent is ModernSearchField or CategoryRail)continue;
                    if(c.Left<-1||c.Top<-1||c.Right>c.Parent!.ClientSize.Width+2||c.Bottom>c.Parent.ClientSize.Height+2)warnings.Add(name+" · "+c.GetType().Name+" · "+c.Text+" · "+c.Bounds+" parent="+c.Parent.ClientSize);
                }
                using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(output,name+".png"));total++;
            }
            form.Close();Application.DoEvents();
        }
        preferences.TextScalePercent=100;preferences.Save(folder);PresentationOptions.Configure(preferences);
        File.WriteAllLines(Path.Combine(output,"layout-warnings.txt"),warnings);File.WriteAllText(Path.Combine(output,"summary.txt"),$"{views.Count} views, {total} captures, 100/120% text, normal/compact sizes. Mock data and offline mode.\nBounds warnings: {warnings.Count}");
        Console.WriteLine($"UI REVIEW: {views.Count} views, {total} captures. Bounds warnings: {warnings.Count}. Offline fixtures; no actual games, installs or support messages. {output}");
    }
}
