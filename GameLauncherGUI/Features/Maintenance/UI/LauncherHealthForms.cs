using System.Diagnostics;

namespace GameLauncherGUI;

internal static class HealthUi
{
    internal static FlowLayoutPanel Page(Panel parent)=>new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(24)};
    internal static SmoothLabel Label(string text,int height=60)=>new(){Text=text,Width=650,Height=height,ForeColor=Color.FromArgb(184,192,215)};
    internal static ModernButton Button(string text,EventHandler click,bool primary=false){var b=new ModernButton(text,primary?VisualTheme.Accent:Color.FromArgb(31,36,53)){Width=640,Height=42,Margin=new Padding(0,5,0,7)};b.Click+=click;return b;}
    internal static bool Failure(Exception ex)=>ex is IOException or UnauthorizedAccessException or ArgumentException or Newtonsoft.Json.JsonException or InvalidDataException or System.ComponentModel.Win32Exception;
}

internal sealed class FirstRunForm : StudioDialog
{
    internal bool ScanRequested { get; private set; }
    internal bool SteamRequested { get; private set; }
    private int step;
    private readonly SmoothLabel title=new(){Width=640,Height=50,Font=new Font("Segoe UI",19,FontStyle.Bold)};
    private readonly Label body=HealthUi.Label("",150);
    internal FirstRunForm(string folder,int games,int initialStep=0)
    {
        step=Math.Clamp(initialStep,0,2);
        Text="PLAYSPACE / Добро пожаловать";Size=new(740,480);
        var page=HealthUi.Page(ContentHost);ContentHost.Controls.Add(page);page.Controls.Add(title);page.Controls.Add(body);
        var action=HealthUi.Button("",(s,e)=>{if(step==0){ScanRequested=true;Close();}else if(step==1){SteamRequested=true;Close();}else Finish();},true);page.Controls.Add(action);
        ModernButton? next=null;next=HealthUi.Button("Далее / пропустить",(s,e)=>{if(step<2){step++;RefreshStep();}else Finish();});page.Controls.Add(next);
        void RefreshStep(){next.Visible=step<2;title.Text=$"{step+1} / 3 · "+new[]{"Соберём вашу библиотеку","Статистика Steam — по желанию","Всё готово для игры"}[step];body.Text=new[]{
            $"В библиотеке сейчас игр: {games}. Нажмите «Найти игры», чтобы запустить поиск установленных игр на этом ПК. Можно пропустить и добавить EXE вручную позже. Поиск не устанавливает игры.",
            "Для часов и достижений откройте профиль Steam и введите свой SteamID и API-ключ. Подключение необязательно. Ключ вводится только в настройках лаунчера и не включается в экспорт настроек.",
            "ПКМ по игре: запуск, моды, сохранения, папка и проверка готовности. Ctrl+K — быстрые команды. Копии настроек, диагностика и место на диске доступны в «Моё пространство» и настройках. Безопасный режим отключает сетевые загрузки и применение дополнительных настроек лаунчером."}[step];action.Text=new[]{"Найти игры","Открыть профиль Steam","Завершить настройку"}[step];}
        void Finish(){try{var p=LauncherPreferences.Load(folder);p.OnboardingCompleted=true;p.Save(folder);DialogResult=DialogResult.OK;Close();}catch(Exception ex)when(HealthUi.Failure(ex)){body.Text="Не удалось сохранить завершение настройки. Можно повторить позже.";}}
        RefreshStep();
    }
}

internal sealed class SupportReportForm : StudioDialog
{
    internal SupportReportForm(string folder,int games)
    {
        Text="PLAYSPACE / Отчёт для поддержки";Size=new(820,740);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};
        foreach(float h in new[]{60f,85,48,100,58})layout.RowStyles.Add(new RowStyle(h==100?SizeType.Percent:SizeType.Absolute,h));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new SmoothLabel{Text="Опишите проблему. Отчёт сначала показывается вам, затем его можно сохранить и приложить к обращению. Автоматической отправки нет.",Dock=DockStyle.Fill});
        var comment=new TextBox{Dock=DockStyle.Fill,Multiline=true,PlaceholderText="Что нажали, что ожидали и что произошло? Не вводите пароли и ключи.",BackColor=Color.FromArgb(29,34,50),ForeColor=Color.White};layout.Controls.Add(comment);
        var preview=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White};
        var generate=new ModernButton("Сформировать / обновить предпросмотр",VisualTheme.Accent){Dock=DockStyle.Fill};layout.Controls.Add(generate);layout.Controls.Add(preview);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(actions);var save=new ModernButton("Сохранить TXT",VisualTheme.Accent){Width=240,Height=40,Enabled=false};
        var bot=new ModernButton("Открыть поддержку ↗",Color.FromArgb(34,49,65)){Width=300,Height=40};actions.Controls.Add(save);actions.Controls.Add(bot);
        generate.Click+=(s,e)=>{preview.Text=LauncherSession.Report(folder,games,comment.Text);save.Enabled=true;};
        comment.TextChanged+=(s,e)=>save.Enabled=false;
        save.Click+=(s,e)=>{using var dialog=new SaveFileDialog{Filter="Текстовый отчёт|*.txt",FileName="PlaySpace-report.txt"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{File.WriteAllText(dialog.FileName,LauncherSession.Redact(preview.Text));}catch(Exception ex)when(HealthUi.Failure(ex)){MessageBox.Show(this,"Не удалось сохранить отчёт.");}};
        bot.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo("https://t.me/PlaySpaceSupport_bot"){UseShellExecute=true});}catch(Exception ex)when(HealthUi.Failure(ex)){MessageBox.Show(this,"Откройте @PlaySpaceSupport_bot в Telegram.");}};
    }
}

internal sealed class SettingsTransferForm : StudioDialog
{
    internal bool RestartRequired { get; private set; }
    internal SettingsTransferForm(string folder,Func<bool> busy)
    {
        Text="PLAYSPACE / Перенос настроек";Size=new(740,640);var page=HealthUi.Page(ContentHost);ContentHost.Controls.Add(page);
        page.Controls.Add(HealthUi.Label("Библиотека, закрепления, коллекции, рекомендации и профили — в одном файле. Ключи Steam, учётные данные, тексты заметок, параметры запуска и изображения не экспортируются. Пути к играм включаются: храните копию приватно.",100));
        var status=HealthUi.Label("Перед восстановлением завершите игры и загрузки. Восстановление заменяет библиотеку и настройки; сами игры и их сохранения не меняются.",100);
        var export=HealthUi.Button("Экспортировать настройки…",(s,e)=>{using var save=new SaveFileDialog{Filter="Копия настроек PlaySpace|*.json",FileName="PlaySpace-settings.json"};if(save.ShowDialog(this)!=DialogResult.OK)return;try{SettingsTransfer.Export(folder,save.FileName);status.Text="Копия сохранена. Собственные иконки и сохранения игр переносятся отдельно.";}catch(Exception ex)when(HealthUi.Failure(ex)){status.Text="Экспорт не выполнен: "+ex.Message;}});page.Controls.Add(export);
        page.Controls.Add(HealthUi.Button("Создать локальную копию настроек",(s,e)=>{try{SettingsTransfer.Export(folder,ManagedPaths.Child(folder,"settings-backups/settings-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json"));status.Text="Копия создана в хранилище настроек лаунчера.";}catch(Exception ex)when(HealthUi.Failure(ex)){status.Text=ex.Message;}}));
        var restore=HealthUi.Button("Выбрать копию и посмотреть изменения…",(s,e)=>{
            if(busy()){status.Text="Сначала закройте игры и дождитесь завершения загрузок.";return;}
            using var open=new OpenFileDialog{Filter="Копия настроек PlaySpace|*.json"};if(open.ShowDialog(this)!=DialogResult.OK)return;
            try{var b=SettingsTransfer.Read(open.FileName);string preview=$"Игр в копии: {b.Games.Count}\nКоллекций: {b.Collections.Names.Count}\nЗакреплённых рекомендаций: {b.Favorites.Count}\n\n"+string.Join("\n",b.Games.Take(8).Select(g=>g.Name+" · "+g.ExePath))+(b.Games.Count>8?"\n…полный список доступен в файле копии":"")+"\n\nБиблиотека будет заменена. Перед заменой создадим страховочную копию. Импортированные профили и автокопии не будут применяться автоматически.\n\nПродолжить?";
                if(MessageBox.Show(this,preview,"Предпросмотр восстановления",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                if(busy())throw new IOException("Началась игра или загрузка. Повторите после завершения.");
                SettingsTransfer.Restore(folder,open.FileName);RestartRequired=true;status.Text="Настройки восстановлены. Полностью закройте лаунчер и запустите заново. До перезапуска редактирование библиотеки недоступно.";export.Enabled=false;
                foreach(Control control in page.Controls)if(control is ModernButton)control.Enabled=false;
            }catch(Exception ex)when(HealthUi.Failure(ex)){status.Text="Восстановление не выполнено: "+ex.Message;}
        });page.Controls.Add(restore);page.Controls.Add(status);
    }
}

internal sealed class StorageManagerForm : StudioDialog
{
    private readonly CancellationTokenSource cancellation=new();
    private bool disposed;
    private bool deleting;
    internal StorageManagerForm(string folder,IReadOnlyList<GameInfo> games,Func<bool> busy)
    {
        Text="PLAYSPACE / Место на диске";Size=new(840,720);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),RowCount=5,ColumnCount=1};foreach(float h in new[]{52f,55,100,48,68})layout.RowStyles.Add(new(h==100?SizeType.Percent:SizeType.Absolute,h));ContentHost.Controls.Add(layout);
        var choice=new ModernSelect{Dock=DockStyle.Fill};foreach(var group in LauncherStorage.Groups)choice.Items.Add(group.Name);foreach(var game in games)choice.Items.Add("Игра (только размер): "+game.Name);layout.Controls.Add(choice);
        var status=new SmoothLabel{Dock=DockStyle.Fill,Text="Выберите раздел. Игры доступны только для просмотра размера; очистка не удаляет файлы игр."};layout.Controls.Add(status);
        var files=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White};layout.Controls.Add(files);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(buttons);var scan=new ModernButton("Посчитать / обновить",VisualTheme.Accent){Width=310,Height=40};var remove=new ModernButton("Удалить показанные файлы…",Color.FromArgb(87,44,60)){Width=390,Height=40,Enabled=false};buttons.Controls.Add(scan);buttons.Controls.Add(remove);
        layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text="Кеш можно загрузить заново при подключении. Удаление резервных копий необратимо. Список показывает точные файлы; новые файлы, появившиеся после подсчёта, не удаляются."});
        IReadOnlyList<StorageFile> preview=Array.Empty<StorageFile>();int scannedIndex=-1;
        choice.SelectedIndexChanged+=(s,e)=>{remove.Enabled=false;files.Clear();scannedIndex=-1;};
        scan.Click+=async(s,e)=>{int index=choice.SelectedIndex;if(index<0)return;scan.Enabled=false;choice.Enabled=false;remove.Enabled=false;status.Text="Считаем файлы…";try{
            bool managed=index<LauncherStorage.Groups.Length;string root=folder,relative=managed?LauncherStorage.Groups[index].Relative:"";
            if(!managed){var game=games[index-LauncherStorage.Groups.Length];root=Directory.Exists(game.InstallDirectory)?game.InstallDirectory!:Path.GetDirectoryName(game.ExePath)??"";if(!Directory.Exists(root))throw new IOException("Папка игры не найдена. Уточните путь в управлении игрой.");string parent=Path.GetDirectoryName(Path.GetFullPath(root))??throw new IOException("Корень диска не сканируется.");relative=Path.GetFileName(root);root=parent;}
            var token=cancellation.Token;preview=await Task.Run(()=>LauncherStorage.Scan(root,relative,token));if(IsDisposed)return;scannedIndex=index;
            files.Text=string.Join(Environment.NewLine,preview.Select(f=>$"{f.Bytes/1048576.0:N2} МБ  {f.Relative}"));status.Text=$"Файлов: {preview.Count} · Всего: {preview.Sum(f=>f.Bytes)/1048576.0:N1} МБ"+(managed?"":" · Только просмотр");remove.Enabled=managed&&preview.Count>0;
        }catch(OperationCanceledException){}catch(Exception ex)when(HealthUi.Failure(ex)){if(!IsDisposed)status.Text=ex.Message;}finally{if(!IsDisposed){scan.Enabled=true;choice.Enabled=true;}}};
        remove.Click+=async(s,e)=>{if(deleting||scannedIndex!=choice.SelectedIndex||scannedIndex<0||scannedIndex>=LauncherStorage.Groups.Length)return;if(busy()){status.Text="Очистка недоступна во время игр, загрузок или установки.";return;}if(MessageBox.Show(this,$"Безвозвратно удалить {preview.Count} показанных файлов ({preview.Sum(f=>f.Bytes)/1048576.0:N1} МБ)?\n\nПроверьте список выше. Если это копии — убедитесь, что они больше не нужны.","Подтверждение очистки",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;deleting=true;remove.Enabled=scan.Enabled=choice.Enabled=false;status.Text="Удаляем подтверждённые файлы…";try{var group=LauncherStorage.Groups[scannedIndex];int removed=await Task.Run(()=>LauncherStorage.Delete(folder,group,preview));if(IsDisposed)return;status.Text=$"Удалено файлов: {removed}";files.Clear();}catch(Exception ex)when(HealthUi.Failure(ex)){if(!IsDisposed)status.Text=ex.Message;}finally{deleting=false;if(!IsDisposed)scan.Enabled=choice.Enabled=true;}};
        choice.SelectedIndex=0;FormClosing+=(s,e)=>{if(deleting){e.Cancel=true;status.Text="Дождитесь завершения очистки.";}else cancellation.Cancel();};
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
