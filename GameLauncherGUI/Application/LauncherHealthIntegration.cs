using System.Diagnostics;

namespace GameLauncherGUI;

public partial class Form1
{
    private bool recoveryApplied;
    private bool HealthBusy()=>launchingGame||sessions.Count>0||Workspace.For(appDataFolder).Transfers.Items().Any(t=>t.State is TransferState.Running or TransferState.Queued)||myGames.Any(IsGameRunning);
    private void FirstRun()=>Tasks.StartUnique("Мастер первого запуска",_=>FirstRunAsync());
    private async Task FirstRunAsync()
    {
        int step=0;
        while(!IsDisposed){
            using var wizard=new FirstRunForm(appDataFolder,myGames.Count,step);wizard.ShowDialog(this);
            preferences=LauncherPreferences.Load(appDataFolder);
            if(IsDisposed||resourcesDisposed)return;
            if(wizard.ScanRequested){await ScanGamesAsync();step=1;continue;}
            if(wizard.SteamRequested){OpenSettings();step=2;continue;}
            return;
        }
    }
    private void OpenHealth(string section)
    {
        if(section=="appearance"){using var form=new AppearanceForm(appDataFolder,ApplyWorkspaceSettings);form.ShowDialog(this);return;}
        if(section=="first-run"){FirstRun();return;}
        if(section=="report"){using var report=new SupportReportForm(appDataFolder,myGames.Count);report.ShowDialog(this);return;}
        if(section=="storage"){using var storage=new StorageManagerForm(appDataFolder,myGames.ToArray(),HealthBusy);storage.ShowDialog(this);return;}
        if(section=="backup"){
            if(recoveryApplied)return;using var transfer=new SettingsTransferForm(appDataFolder,HealthBusy);transfer.ShowDialog(this);
            if(transfer.RestartRequired){recoveryApplied=true;sessionTimer.Stop();updateTimer.Stop();exiting=true;BeginInvoke(new Action(Close));}return;
        }
        if(section=="safe-mode"){
            if(HealthBusy()){MessageBox.Show(this,"Сначала завершите игры и загрузки.");return;}
            if(MessageBox.Show(this,"Полностью закрыть лаунчер? После закрытия запустите его через ярлык «PlaySpace · безопасный режим» из меню «Пуск» или с параметром --safe-mode. Установленные моды в файлах игр этим не отключаются.","Безопасный режим",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            LauncherSession.Record(appDataFolder,"Запрошен безопасный режим");exiting=true;Close();
        }
    }
    private void OpenNetwork()
    {
        if(LauncherSession.SafeMode){MessageBox.Show(this,"Сетевые компоненты недоступны в безопасном режиме. Перезапустите лаунчер в обычном режиме.");return;}
        using var form=new NetworkForm(appDataFolder);form.ShowDialog(this);
    }
    private void CheckGameReady(GameInfo game)=>Tasks.Start("Проверка готовности игры",_=>CheckGameReadyAsync(game));
    private async Task CheckGameReadyAsync(GameInfo game)
    {
        try{var checks=await Task.Run(()=>GameReadiness.Inspect(game,()=>IsGameRunning(game),appDataFolder));if(IsDisposed)return;
            using var form=new ReadinessForm(game,checks);form.ShowDialog(this);
        }catch(Exception ex)when(HealthUi.Failure(ex)){if(IsDisposed||resourcesDisposed)return;lblStatus.Text="Проверка не выполнена. Повторите или создайте отчёт.";LauncherSession.Record(appDataFolder,"Ошибка проверки готовности",ex);}
    }
    private void OpenGameActions(GameInfo game)
    {
        using var form=new StudioActionsForm(game.Name,new LauncherCommand[]{new("▶ Играть","",()=>LaunchGame(game)),new("Моды и наборы","",()=>OpenModsFor(game)),new("Сохранения и запуск","",()=>OpenGameCare(game)),new("Проверить готовность","",()=>CheckGameReady(game)),new("Профиль производительности","",()=>OpenPerformance(game)),new("Открыть папку","",()=>{if(ResolveGameFolder(game) is string folder)Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});})});
        form.ShowDialog(this);form.Choice?.Invoke();
    }
    private void OpenModsFor(GameInfo game)
    {
        if(LauncherSession.SafeMode){lblStatus.Text="Моды недоступны в безопасном режиме.";return;}
        using var form=new ModsForm(()=>Task.FromResult(myGames.ToList()),game);form.ShowDialog(this);
    }
}

internal sealed class StudioActionsForm : StudioDialog
{
    internal Action? Choice { get; private set; }
    internal StudioActionsForm(string title,IEnumerable<LauncherCommand> commands)
    {
        Text="PLAYSPACE / "+title;Size=new(740,520);var page=HealthUi.Page(ContentHost);ContentHost.Controls.Add(page);
        bool first=true;foreach(var command in commands){page.Controls.Add(HealthUi.Button(command.Name,(s,e)=>{Choice=command.Invoke;Close();},first));first=false;}
    }
}
internal sealed class ReadinessForm : StudioDialog
{
    internal ReadinessForm(GameInfo game,IReadOnlyList<ReadinessItem> checks)
    {
        Text="PLAYSPACE / Готовность · "+game.Name;Size=new(780,640);
        var view=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,Text=string.Join("\r\n\r\n",checks.Select(c=>(c.Blocking?"ТРЕБУЕТ ВНИМАНИЯ · ":"ИНФОРМАЦИЯ · ")+c.Name+"\r\n"+c.Detail))};ContentHost.Padding=new Padding(24);ContentHost.Controls.Add(view);
    }
}
