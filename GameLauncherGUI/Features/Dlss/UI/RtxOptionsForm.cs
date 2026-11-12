using System.Diagnostics;

namespace GameLauncherGUI;

internal sealed class RtxOptionsForm : StudioDialog
{
    private readonly GameInfo game;
    private readonly RtxCompatibility gpu;
    private string? executable;
    private bool busy,disposed;
    private readonly CancellationTokenSource cancellation=new();
    private readonly SmoothLabel status=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(176,189,214)};
    private readonly SmoothLabel selectedExe=new(){Dock=DockStyle.Fill,AutoEllipsis=true};
    private readonly ModernToggle consent=new(){Text="Это одиночная игра без античита",Dock=DockStyle.Fill};
    private readonly ModernToggle dx12=new(){Text="Игра запускается в DirectX 12 и имеет DLSS / FSR 2+ / XeSS",Dock=DockStyle.Fill};
    private readonly ModernButton install=new("Установить выбранный режим",VisualTheme.Accent){Dock=DockStyle.Fill};
    private readonly ModernButton remove=new("Удалить OptiScaler",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};
    private readonly ThemedComboBox modes=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ThemedComboBox presets=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly SmoothLabel hint=new(){Dock=DockStyle.Fill};
    internal RtxOptionsForm(GameInfo game,string? adapter=null)
    {
        this.game=game;gpu=RtxCompatibility.Detect(adapter??PcSnapshot.Read().Gpu);executable=File.Exists(game.ExePath)?game.ExePath:null;
        Text="PLAYSPACE / RTX 30 и генерация кадров";Size=new(960,850);MinimumSize=new(800,740);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};ContentHost.Controls.Add(scroll);
        var layout=new TableLayoutPanel{Dock=DockStyle.Top,Height=920,ColumnCount=1,RowCount=12,Padding=new Padding(24)};
        foreach(float height in new[]{92f,82,46,32,48,48,100,42,42,52,48})layout.RowStyles.Add(new(SizeType.Absolute,height));layout.RowStyles.Add(new(SizeType.Percent,100));scroll.Controls.Add(layout);
        layout.Controls.Add(new StudioHeading("Больше возможностей для RTX 30","Масштабирование и генерация кадров настраиваются отдельно.","RTX / FSR FRAME GENERATION"){Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(new SmoothLabel{Text=gpu.Summary,Dock=DockStyle.Fill},0,1);
        var choose=new ModernButton("Выбрать основной EXE игры…",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};layout.Controls.Add(choose,0,2);layout.Controls.Add(selectedExe,0,3);
        modes.Items.AddRange(new object[]{"Только масштабирование · без генерации кадров","DLSSG / Streamline 2 → FSR Frame Generation","FSR 3.1 → FSR Frame Generation","FSR 3.0 → FSR Frame Generation","DLSSG → FSR через Nukem · в том числе старый Streamline","OptiFG → FSR · если в игре нет генерации кадров"});modes.SelectedIndex=0;
        presets.Items.AddRange(new object[]{"Модель DLSS: выбор игры · рекомендуется начать с этого","Модель K · предыдущий Transformer","DLSS 4.5 · модель L · выше нагрузка на RTX 30","DLSS 4.5 · модель M · выше нагрузка на RTX 30"});presets.SelectedIndex=0;
        layout.Controls.Add(modes,0,4);layout.Controls.Add(presets,0,5);layout.Controls.Add(hint,0,6);layout.Controls.Add(consent,0,7);layout.Controls.Add(dx12,0,8);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};actions.ColumnStyles.Add(new(SizeType.Percent,65));actions.ColumnStyles.Add(new(SizeType.Percent,35));actions.Controls.Add(install,0,0);actions.Controls.Add(remove,1,0);layout.Controls.Add(actions,0,9);
        var help=new ModernButton("Совместимость и инструкции OptiScaler ↗",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};layout.Controls.Add(help,0,10);layout.Controls.Add(status,0,11);
        choose.Click+=(s,e)=>{using var picker=new OpenFileDialog{Title="Выбери основной x64 EXE: для Unreal — Binaries/Win64/*Shipping.exe",Filter="Игра Windows x64|*.exe",InitialDirectory=Directory.Exists(game.InstallDirectory)?game.InstallDirectory:""};if(picker.ShowDialog(this)==DialogResult.OK){try{UniversalDlss5.ValidateGame(game,picker.FileName,true);executable=picker.FileName;RefreshState();}catch(Exception ex){status.Text=ex.Message;}}};
        modes.SelectedIndexChanged+=(s,e)=>RefreshState();presets.SelectedIndexChanged+=(s,e)=>RefreshState();consent.CheckedChanged+=(s,e)=>RefreshState();dx12.CheckedChanged+=(s,e)=>RefreshState();
        install.Click+=async(s,e)=>await Run(async()=>{
            string exe=executable??throw new IOException("Выбери EXE игры.");
            await new OptiScalerManager().Install(game,exe,gpu.Adapter,consent.Checked,dx12.Checked,(RtxFrameMode)modes.SelectedIndex,(RtxPreset)presets.SelectedIndex,new Progress<string>(message=>{if(!IsDisposed)status.Text=message;}),cancellation.Token);
            return "Установлено. Включи DLSS в игре. Insert — меню OptiScaler, End — генерация кадров. Для смены профиля здесь сначала удали мод. Настройки можно менять через игровое меню OptiScaler.";
        });
        remove.Click+=async(s,e)=>await Run(async()=>{string exe=executable??throw new IOException("Выбери EXE игры.");await Task.Run(()=>OptiScalerManager.Remove(game,exe));return "OptiScaler удалён. Изменённые INI сохранены в .playspace-mods/preserved. Обновлённая отдельно DLSS SR остаётся: её откат доступен в окне DLSS.";});
        help.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(OptiScalerManager.ProjectUrl+"/wiki/General-Compatibility"){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception ex){status.Text=ex.Message;}};
        Shown+=(s,e)=>RefreshState();FormClosing+=(s,e)=>{if(busy){cancellation.Cancel();e.Cancel=true;status.Text="Завершаем текущую операцию…";}};
    }
    private void RefreshState()
    {
        selectedExe.Text=executable??"Основной EXE не выбран. Для Unreal выбери *Shipping.exe в Binaries/Win64.";
        hint.Text=(RtxFrameMode)modes.SelectedIndex switch{
            RtxFrameMode.Streamline=>"Нужна игра с DLSS Frame Generation и Streamline 2. Включи DLSS FG в игре; OptiScaler выводит кадры через FSR. Для старого Streamline выбери Nukem.",
            RtxFrameMode.Nukem=>"Нужна встроенная DLSS Frame Generation. Включи её в игре. Генерация кадров заменяется на FSR через Nukem.",
            RtxFrameMode.Fsr31 or RtxFrameMode.Fsr30=>"Выбери версию, встроенную в игру, и включи FSR Frame Generation в её настройках. Масштабирование через DLSS сохраняется, если игра допускает такое сочетание.",
            RtxFrameMode.Upscaler=>"Экспериментальный вариант для DX12-игр без встроенной генерации кадров. Включи масштабирование в игре; возможны артефакты интерфейса, настрой HUDfix через Insert.",
            _=>"Генерация кадров отключена. DLSS 4.5 загружается отдельно с проверкой подписи NVIDIA. Библиотека игры сохраняется; OptiScaler использует свою копию."};
        bool installed=false,allowed=false;string message="";
        try{if(executable!=null){installed=OptiScalerManager.Installed(executable);OptiScalerManager.ValidateGame(game,executable,consent.Checked,gpu.Adapter,dx12.Checked);allowed=true;}}
        catch(Exception ex)when(ex is IOException or InvalidOperationException or ArgumentException or BadImageFormatException){message=ex.Message;}
        install.Enabled=!busy&&allowed&&!installed&&!Workspace.Current.Offline;remove.Enabled=!busy&&installed;modes.Enabled=presets.Enabled=!busy&&!installed;
        status.Text=installed?"OptiScaler установлен для выбранного EXE. Удаление доступно ниже; исходные файлы игры не перезаписывались.":message.Length>0?message:"Выбери режим. Совместимость и прирост FPS зависят от конкретной игры; автоматического теста её рендера нет.";
        if(Workspace.Current.Offline&&!installed)status.Text+="\nДля загрузки отключи офлайн-режим.";
    }
    private async Task Run(Func<Task<string>> action)
    {
        if(busy)return;busy=true;install.Enabled=remove.Enabled=modes.Enabled=presets.Enabled=false;string message;
        try{message=await action();}catch(Exception ex){message="Операция не выполнена: "+ex.Message;}
        finally{busy=false;}
        if(!IsDisposed){RefreshState();status.Text=message;}
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
