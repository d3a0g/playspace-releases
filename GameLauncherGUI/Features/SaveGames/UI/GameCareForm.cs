namespace GameLauncherGUI;

internal sealed class GameCareForm : StudioDialog
{
    private readonly GameInfo game;private readonly GameCareOptions options;private readonly GameBackupStore backups;
    private readonly Action<GameCareOptions,string,string> persist;private readonly Func<bool> running;
    private readonly TextBox savePath=new(){ReadOnly=true,Dock=DockStyle.Fill,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};
    private readonly TextBox arguments=new(){Width=680,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};
    private readonly TextBox notes=new(){Width=680,Height=128,Multiline=true,MaxLength=8000,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(30,35,51),ForeColor=Color.White};
    private readonly ModernToggle autoBackup=new(){Text="Делать копию перед запуском игры",Width=680,Height=38};
    private readonly ModernSelect snapshots=new(){Width=680,Height=42};
    private readonly FormattedDescription preview=new(){Width=680,Height=220};
    private readonly SmoothLabel status=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(179,170,223)};
    private readonly ModernButton create=new("Создать копию",VisualTheme.Accent){Width=158,Height=40};
    private readonly ModernButton verify=new("Проверить SHA-256",Color.FromArgb(35,40,63)){Width=164,Height=40};
    private readonly ModernButton restore=new("Восстановить…",Color.FromArgb(35,40,63)){Width=158,Height=40};
    private readonly ModernButton inspect=new("Список файлов",Color.FromArgb(35,40,63)){Width=158,Height=40};
    private readonly CancellationTokenSource cancellation=new();private bool busy,disposed;private string launchPath;
    private readonly Panel deck=new(){Dock=DockStyle.Fill};
    internal GameCareForm(GameInfo game,string dataFolder,Action<GameCareOptions,string,string> persist,Func<bool>? running=null)
    {
        this.game=game;this.persist=persist;this.running=running??(()=>false);var original=game.Care??throw new ArgumentException("Не заданы параметры игры.");
        options=new(){Id=original.Id,SaveDirectory=original.SaveDirectory,Notes=original.Notes,BackupBeforeLaunch=original.BackupBeforeLaunch};backups=new(options,dataFolder,this.running);launchPath=game.ExePath;
        Text="PLAYSPACE / Управление игрой";Size=new Size(940,820);MinimumSize=new Size(820,700);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new StudioHeading(game.Name,"Копии, запуск и личные заметки","МОЯ ИГРА"){Dock=DockStyle.Fill},0,0);
        var tabs=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var saveTab=new ModernButton("Сохранения и копии",Color.FromArgb(49,38,77)){Width=220,Height=38,IsActive=true};var launchTab=new ModernButton("Запуск и заметки",Color.FromArgb(35,40,63)){Width=220,Height=38};tabs.Controls.Add(saveTab);tabs.Controls.Add(launchTab);layout.Controls.Add(tabs,0,1);
        layout.Controls.Add(deck,0,2);
        FlowLayoutPanel Page()=>new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};
        var saves=Page();var launches=Page();launches.Visible=false;deck.Controls.Add(saves);deck.Controls.Add(launches);
        void Show(bool launch){saves.Visible=!launch;launches.Visible=launch;(launch?launches:saves).BringToFront();saveTab.IsActive=!launch;launchTab.IsActive=launch;}
        saveTab.Click+=(s,e)=>Show(false);launchTab.Click+=(s,e)=>Show(true);
        saves.Controls.Add(new SmoothLabel{Text="Выберите папку сохранений или конфигурации именно этой игры. Перед копированием и восстановлением закройте игру.",Width=680,Height=50});
        var paths=new TableLayoutPanel{Width=700,Height=44,ColumnCount=2};paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));paths.Controls.Add(savePath,0,0);var choose=new ModernButton("Выбрать папку…",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};paths.Controls.Add(choose,1,0);saves.Controls.Add(paths);savePath.Text=options.SaveDirectory;
        saves.Controls.Add(autoBackup);autoBackup.Checked=options.BackupBeforeLaunch;saves.Controls.Add(new SmoothLabel{Text="Одна копия: до 2 ГБ и 10 000 файлов. Для каждой игры — до 50 копий; старые автоматически не удаляются.",Width=680,Height=42});snapshots.SetPlaceholder("Копий пока нет");saves.Controls.Add(snapshots);
        var actions=new FlowLayoutPanel{Width=700,Height=50,WrapContents=false};foreach(var button in new[]{create,verify,inspect,restore})actions.Controls.Add(button);saves.Controls.Add(actions);saves.Controls.Add(preview);
        var open=new ModernButton("Открыть папку резервных копий",Color.FromArgb(35,40,63)){Width=340,Height=38};saves.Controls.Add(open);
        choose.Click+=(s,e)=>{using var dialog=new FolderBrowserDialog{Description="Папка сохранений или настроек выбранной игры",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(options.SaveDirectory)?options.SaveDirectory:""};if(dialog.ShowDialog(this)!=DialogResult.OK)return;string old=options.SaveDirectory;try{options.SaveDirectory=dialog.SelectedPath;backups.Source();savePath.Text=options.SaveDirectory;status.Text="Папка выбрана. Нажмите «Сохранить настройки игры».";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){options.SaveDirectory=old;status.Text=ex.Message;}};
        create.Click+=async(s,e)=>await Work(async()=>{var point=await Task.Run(()=>backups.Create("Вручную",cancellation.Token));RefreshPoints(point.Id);Workspace.For(dataFolder).History.Add(game,"Копии","Создана ручная резервная копия");status.Text="Резервная копия создана. SHA-256 файлов сохранены.";});
        verify.Click+=async(s,e)=>await Work(async()=>{if(snapshots.SelectedItem is not GameSnapshot point)return;await Task.Run(()=>backups.Verify(point.Id,cancellation.Token));status.Text="Целостность копии подтверждена для всех файлов.";});
        inspect.Click+=async(s,e)=>await Work(async()=>{if(snapshots.SelectedItem is not GameSnapshot point)return;var plan=await Task.Run(()=>backups.Preview(point.Id,cancellation.Token));preview.Text=string.Join("\n",plan.Select(p=>p.Action+" · "+p.Path));status.Text=$"Проверено файлов: {plan.Count}. Показан план для выбранной папки сохранений.";});
        restore.Click+=async(s,e)=>await Work(async()=>{if(snapshots.SelectedItem is not GameSnapshot point)return;var plan=await Task.Run(()=>backups.Preview(point.Id,cancellation.Token));using var confirm=new RestorePlanForm(plan,backups.Source(true));if(confirm.ShowDialog(this)!=DialogResult.OK)return;int restored=await Task.Run(()=>backups.Restore(point.Id,cancellation.Token));RefreshPoints(point.Id);Workspace.For(dataFolder).History.Add(game,"Копии",$"Восстановлены файлы из копии: {restored}");status.Text=$"Восстановлено файлов: {restored}. Исходное состояние сохранено отдельной страховочной копией, если были изменения.";});
        open.Click+=(s,e)=>{try{Directory.CreateDirectory(backups.Folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(backups.Folder){UseShellExecute=true});}catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){status.Text="Не удалось открыть папку копий.";}};
        snapshots.SelectedIndexChanged+=(s,e)=>{preview.Text=snapshots.SelectedItem is GameSnapshot point?$"{point}\n\nВыберите «Список файлов» для проверки и предпросмотра или «Проверить SHA-256». Восстановление потребует подтверждения. При восстановлении новые файлы, которых нет в копии, сохраняются.":"Копий пока нет.";UpdateActions();};
        launches.Controls.Add(new SmoothLabel{Text="Параметры запуска",Width=680,Height=30,Font=new Font("Segoe UI",12,FontStyle.Bold)});arguments.Text=game.LaunchArguments;launches.Controls.Add(arguments);
        launches.Controls.Add(new SmoothLabel{Text="Применяются при запуске из PlaySpace. Используйте только параметры, которые поддерживает сама игра. Для Steam параметры передаются через установленный Steam.exe.",Width=680,Height=64});
        var repair=new ModernButton("Исправить путь к EXE…",Color.FromArgb(35,40,63)){Width=300,Height=40};launches.Controls.Add(repair);var pathLabel=new SmoothLabel{Text=launchPath,Width=680,Height=52,AutoEllipsis=true};launches.Controls.Add(pathLabel);
        repair.Click+=(s,e)=>{using var dialog=new OpenFileDialog{Title="Основной EXE этой игры",Filter="Приложение игры (*.exe)|*.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==DialogResult.OK){launchPath=dialog.FileName;pathLabel.Text=launchPath;status.Text="Новый путь выбран. Нажмите «Сохранить настройки игры».";}};
        var readiness=new ModernButton("Проверить готовность к запуску",Color.FromArgb(35,40,63)){Width=340,Height=40};launches.Controls.Add(readiness);var readinessText=new FormattedDescription{Width=680,Height=150,Text="Проверка не запускает игру и не устанавливает компоненты."};launches.Controls.Add(readinessText);
        readiness.Click+=async(s,e)=>await Work(async()=>{var candidate=Candidate();var checks=await Task.Run(()=>GameReadiness.Inspect(candidate,this.running,dataFolder));readinessText.Text=string.Join("\n\n",checks.Select(c=>(c.Blocking?"НУЖНО ИСПРАВИТЬ · ":"ИНФОРМАЦИЯ · ")+c.Name+"\n"+c.Detail));});
        launches.Controls.Add(new SmoothLabel{Text="Личные заметки · до 8000 символов",Width=680,Height=30});notes.Text=options.Notes;launches.Controls.Add(notes);
        var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,260));bottom.Controls.Add(status,0,0);var save=new ModernButton("Сохранить настройки игры",VisualTheme.Accent){Dock=DockStyle.Fill};bottom.Controls.Add(save,1,0);layout.Controls.Add(bottom,0,3);
        save.Click+=(s,e)=>{if(busy)return;try{_ = GameReadiness.Arguments(arguments.Text);options.BackupBeforeLaunch=autoBackup.Checked;options.Notes=notes.Text;if(options.BackupBeforeLaunch||options.SaveDirectory.Length>0)backups.Source();persist(options,arguments.Text,launchPath);status.Text="Настройки игры сохранены.";}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException){status.Text="Не удалось сохранить: "+ex.Message;}};
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Дождитесь завершения операции с файлами.";}};
        Shown+=async(s,e)=>await Work(()=>{RefreshPoints();return Task.CompletedTask;});UpdateActions();
    }
    private GameInfo Candidate()=>new(){Name=game.Name,ExePath=launchPath,SteamAppId=game.SteamAppId,TrackingExecutable=game.TrackingExecutable,InstallDirectory=game.InstallDirectory,Performance=game.Performance,Care=options,LaunchArguments=arguments.Text};
    private void RefreshPoints(string? selected=null){snapshots.SelectedIndex=-1;snapshots.Items.Clear();foreach(var point in backups.List())snapshots.Items.Add(point);snapshots.SelectedIndex=selected==null?(snapshots.Items.Count>0?0:-1):snapshots.Items.Cast<GameSnapshot>().ToList().FindIndex(p=>p.Id==selected);UpdateActions();}
    private void UpdateActions(){deck.Enabled=!busy;create.Enabled=!busy;verify.Enabled=inspect.Enabled=restore.Enabled=!busy&&snapshots.SelectedItem is GameSnapshot;snapshots.Enabled=!busy;}
    private async Task Work(Func<Task> action){if(busy||disposed)return;busy=true;UpdateActions();try{await action();}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or Newtonsoft.Json.JsonException){if(!disposed)status.Text=ex.Message;}finally{busy=false;if(!disposed)UpdateActions();}}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
internal sealed class RestorePlanForm : StudioDialog
{
    internal RestorePlanForm(IReadOnlyList<RestoreEntry> plan,string destination)
    {
        Text="PLAYSPACE / Подтверждение восстановления";Size=new Size(800,650);MinimumSize=new Size(700,520);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,106));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new SmoothLabel{Text=$"Папка назначения: {destination}\n\nБудет создана страховочная копия текущего состояния. Файлов в плане: {plan.Count}. Новые файлы вне копии не удаляются. Закройте игру и дождитесь синхронизации Steam Cloud.",Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new FormattedDescription{Dock=DockStyle.Fill,Text=string.Join("\n",plan.Select(p=>p.Action+" · "+p.Path))},0,1);var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var apply=new ModernButton("Подтвердить восстановление",VisualTheme.Accent){Width=310,Height=42};var cancel=new ModernButton("Отмена",Color.FromArgb(35,40,63)){Width=200,Height=42};apply.Click+=(s,e)=>DialogResult=DialogResult.OK;cancel.Click+=(s,e)=>Close();buttons.Controls.Add(apply);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,2);
    }
}
