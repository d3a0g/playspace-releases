namespace GameLauncherGUI;

internal sealed class GameTransferForm : StudioDialog
{
    private bool busy;
    private sealed class TransferProgress(IProgress<string> target):IProgress<string>{private long next;public void Report(string value){long now=Environment.TickCount64,previous=Interlocked.Read(ref next);if(now>=previous&&Interlocked.CompareExchange(ref next,now+150,previous)==previous)target.Report(value);}}
    private readonly CancellationTokenSource cancellation=new();
    internal GameTransferForm(GameInfo game,string folder,Action<GameTransferJournal,bool>? applyPaths=null)
    {
        Text="PLAYSPACE / Перенос игры · "+game.Name;Size=new(900,740);MinimumSize=new(760,620);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,Padding=new Padding(24)};
        layout.RowStyles.Add(new(SizeType.Absolute,92));layout.RowStyles.Add(new(SizeType.Absolute,52));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,100));ContentHost.Controls.Add(layout);
        string source=GameDiskUsage.ResolveRoot(game.InstallDirectory,game.ExePath).Root??"";
        var sourceBox=new TextBox{Dock=DockStyle.Fill,Text=source,ReadOnly=true,AccessibleName="Исходная папка игры"};
        var sourcePanel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2};sourcePanel.Controls.Add(new SmoothLabel{Text="Исходная папка — проверьте, что это одна игра",AutoSize=true});sourcePanel.Controls.Add(sourceBox);layout.Controls.Add(sourcePanel,0,0);
        var choose=new ModernButton("Выбрать диск и папку назначения…",VisualTheme.Accent){Dock=DockStyle.Fill};layout.Controls.Add(choose,0,1);
        var detail=new FormattedDescription{Dock=DockStyle.Fill,Text="Закройте игру и игровые клиенты. Лаунчер скопирует файлы и сверит SHA-256. Старый путь сохранится через ссылку NTFS, поэтому ярлыки и записи магазинов останутся прежними.\n\nПеренос не меняет настройки магазина и не является его официальной функцией. Не отключайте целевой диск. Не удаляйте старую ссылку.\n\nПосле проверки запуска можно отдельно удалить страховочную копию. До этого место на исходном диске не освобождается. При сбое копирования исходная игра остаётся на месте; неполную целевую папку можно проверить вручную."};layout.Controls.Add(detail,0,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true};layout.Controls.Add(actions,0,3);
        var run=new ModernButton("Перенести",VisualTheme.Accent){Width=165,Height=42,Enabled=false};var cleanup=new ModernButton("Освободить место",Color.FromArgb(35,40,63)){Width=190,Height=42};var restore=new ModernButton("Вернуть исходную папку",Color.FromArgb(35,40,63)){Width=240,Height=42};var cancel=new ModernButton("Отменить операцию",Color.FromArgb(35,40,63)){Width=200,Height=42};actions.Controls.AddRange(new Control[]{run,cleanup,restore,cancel});
        string journalFile=Path.Combine(folder,"game-transfers",game.LibraryId+".json");GameTransferPlan? plan=null;
        var progress=new TransferProgress(new Progress<string>(message=>{if(!IsDisposed&&busy)detail.Text=message;}));
        async Task Work(Func<Task> work){if(busy)return;busy=true;choose.Enabled=run.Enabled=cleanup.Enabled=restore.Enabled=false;try{await work();}catch(OperationCanceledException){if(!IsDisposed)detail.Text="Операция отменена. Исходные данные сохранены. Если копирование уже началось, целевая папка может содержать неполную копию.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or Newtonsoft.Json.JsonException or System.ComponentModel.Win32Exception){if(!IsDisposed)detail.Text=ex.Message+"\n\nЖурнал восстановления: "+journalFile;}finally{busy=false;if(!IsDisposed){UpdateActions();}}}
        choose.Click+=async(s,e)=>{using var picker=new FolderBrowserDialog{Description="Выберите родительскую папку на целевом диске"};if(picker.ShowDialog(this)!=DialogResult.OK)return;string target=Path.Combine(picker.SelectedPath,Path.GetFileName(source));plan=null;await Work(async()=>{GameTransfer.EnsureClosed(game);plan=await Task.Run(()=>GameTransfer.Preview(source,target,cancellation.Token,progress));detail.Text=$"Из: {plan.Source}\n\nВ: {plan.Target}\n\nФайлов: {plan.Files.Length:N0}\nОбъём: {GameDiskUsage.Format(plan.Bytes)}\n\nПосле переноса исходная копия будет сохранена до отдельного подтверждения удаления. Старый путь останется ссылкой на новую папку.";});};
        run.Click+=async(s,e)=>{if(plan==null)return;if(MessageBox.Show(this,"Начать проверенный перенос с сохранением страховочной копии?","Перенос игры",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;await Work(async()=>{var journal=await Task.Run(()=>GameTransfer.Execute(plan,journalFile,()=>GameTransfer.EnsureClosed(game),cancellation.Token,progress));applyPaths?.Invoke(journal,true);plan=null;detail.Text="Перенесено в: "+journal.Target+"\n\nИсходная копия: "+journal.Backup+"\n\nПроверьте запуск игры. Затем закройте игру и клиент, вернитесь сюда и нажмите «Освободить место». Или верните исходную папку: новая копия при этом сохранится.";});};
        GameTransferJournal Read(){if(!File.Exists(journalFile))throw new IOException("Для этой игры нет журнала переноса.");return LocalJson.Read<GameTransferJournal?>(journalFile,()=>null)??throw new IOException("Журнал повреждён.");}
        void UpdateActions()
        {
            choose.Enabled=!cancellation.IsCancellationRequested;run.Enabled=plan!=null&&!cancellation.IsCancellationRequested;
            cleanup.Enabled=restore.Enabled=false;
            if(!File.Exists(journalFile))return;
            try{var journal=Read();cleanup.Enabled=journal.Phase is "Перенесено; исходная копия сохранена" or "Освобождение места";restore.Enabled=journal.Phase is not ("Завершено" or "Освобождение места");}
            catch(Exception ex)when(ex is IOException or Newtonsoft.Json.JsonException){detail.Text=ex.Message+"\nЖурнал: "+journalFile;}
        }
        Shown+=(s,e)=>{UpdateActions();if(File.Exists(journalFile))try{var journal=Read();detail.Text="Состояние: "+journal.Phase+"\n\nИсходный путь: "+journal.Source+"\nЦелевая папка: "+journal.Target+"\nСтраховочная копия: "+journal.Backup+"\n\nПроверьте запуск игры перед удалением страховочной копии. При возврате новые изменения останутся в целевой копии; обратно они не сливаются.";}catch(Exception ex)when(ex is IOException or Newtonsoft.Json.JsonException){detail.Text=ex.Message;}};
        cleanup.Click+=async(s,e)=>{if(MessageBox.Show(this,"Удалить страховочную копию после проверки новой папки? Игра и магазин должны быть закрыты. Автоматический возврат после этого недоступен.","Освободить место",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;await Work(async()=>{await Task.Run(()=>GameTransfer.ReleaseBackup(Read(),journalFile,()=>GameTransfer.EnsureClosed(game),cancellation.Token));detail.Text="Страховочная копия удалена. Игра хранится на целевом диске, старый путь работает через ссылку.";});};
        restore.Click+=async(s,e)=>{if(MessageBox.Show(this,"Вернуть исходную папку? Новая копия на целевом диске останется нетронутой.","Возврат",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;await Work(async()=>{var journal=Read();await Task.Run(()=>GameTransfer.Restore(journal,journalFile,()=>GameTransfer.EnsureClosed(game)));applyPaths?.Invoke(journal,false);detail.Text="Исходная папка восстановлена. Целевая копия сохранена; при необходимости удалите её вручную после проверки.";});};
        cancel.Click+=(s,e)=>{if(busy)cancellation.Cancel();else Close();};FormClosing+=(s,e)=>{if(busy){e.Cancel=true;cancellation.Cancel();}};Disposed+=(s,e)=>cancellation.Dispose();
    }
}
