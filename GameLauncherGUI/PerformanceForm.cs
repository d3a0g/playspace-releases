namespace GameLauncherGUI;

internal sealed class PerformanceForm : StudioDialog
{
    private sealed record ResolutionChoice(int Width, int Height) {
        public override string ToString() => Width == 0 ? "Текущее разрешение экрана" : $"{Width} × {Height}";
    }
    private readonly GameInfo[] games;
    private readonly Func<GameInfo, PerformanceProfile, bool> saveProfile;
    private readonly ComboBox gamePicker = Picker(), quality = Picker(), resolution = Picker(), windowMode = Picker();
    private readonly NumericUpDown target = Number(30,360,60), measured = Number(0,1000,0), scale = Number(50,100,100);
    private readonly CheckBox vsync = new ModernToggle { Text="VSync", ForeColor=Color.FromArgb(211,211,230), Dock=DockStyle.Fill };
    private readonly CheckBox autoApply = new ModernToggle { Text="Автоприменение", ForeColor=Color.FromArgb(211,211,230), Dock=DockStyle.Fill };
    private readonly TextBox hardware = ReadOnlyText(), plan = ReadOnlyText();
    private readonly Label status = new() { Dock=DockStyle.Fill, ForeColor=Color.FromArgb(151,163,192), TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true };
    private readonly Label support = new() { Dock=DockStyle.Fill, ForeColor=Color.FromArgb(151,163,192), Font=new Font("Segoe UI",9) };
    private readonly ModernButton save = new("Сохранить", Color.FromArgb(40,36,65));
    private readonly ModernButton launch = new("▶  Применить и играть", Color.FromArgb(115,94,244));
    private readonly ModernButton suggest = new("Подобрать FPS", Color.FromArgb(35,40,63));
    private readonly ModernButton config = new("Выбрать конфиг", Color.FromArgb(35,40,63));
    private readonly ModernButton restore = new("Восстановить", Color.FromArgb(42,35,51));
    private PcSnapshot? snapshot;
    private bool loading;
    private string? configPath;
    private readonly List<ModernButton> presetButtons = new();
    internal GameInfo? LaunchRequested { get; private set; }
    private GameInfo? Game => gamePicker.SelectedIndex>=0 && gamePicker.SelectedIndex<games.Length ? games[gamePicker.SelectedIndex] : null;

    public PerformanceForm(GameInfo[] games, GameInfo? selected, Func<GameInfo,PerformanceProfile,bool> saveProfile)
    {
        this.games=games; this.saveProfile=saveProfile;
        Text="PLAYSPACE · Производительность"; Size=new Size(1120,840); MinimumSize=new Size(960,720);
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,6,24,18),ColumnCount=1,RowCount=4};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,94)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        layout.Controls.Add(new StudioHeading("Твоя игра. Твой баланс.","Профили графики, поддерживаемые параметры и запуск в один клик.","PLAYSPACE / ПРОИЗВОДИТЕЛЬНОСТЬ") {Dock=DockStyle.Fill},0,0);
        gamePicker.Dock=DockStyle.Fill; gamePicker.Margin=new Padding(0,4,0,8); layout.Controls.Add(gamePicker,0,1);
        var columns=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var left=new RoundedPanel {Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0)};
        var info=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,BackColor=Color.FromArgb(23,28,42),Margin=Padding.Empty};
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(float h in new[]{26f,122f,58f,25f}) info.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        info.RowStyles.Add(new RowStyle(SizeType.Percent,100)); info.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        info.Controls.Add(Section("ТВОЙ ПК"),0,0); hardware.Text="Определяем оборудование…"; info.Controls.Add(hardware,0,1);
        info.Controls.Add(Setting("Текущий замер FPS\n0 = нет замера",measured),0,2);
        info.Controls.Add(Section("ОЦЕНКА И ПЛАН"),0,3); plan.ScrollBars=ScrollBars.Vertical; info.Controls.Add(plan,0,4);
        var fileActions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        fileActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); fileActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); fileActions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var button in new[]{config,restore}) {button.Dock=DockStyle.Fill; button.Font=new Font("Segoe UI",8,FontStyle.Bold);button.Margin=new Padding(0,6,6,0);}
        fileActions.Controls.Add(config,0,0); fileActions.Controls.Add(restore,1,0); info.Controls.Add(fileActions,0,5); left.Controls.Add(info); columns.Controls.Add(left,0,0);
        var right=new RoundedPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
        var settingsLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Color.FromArgb(23,28,42),Margin=Padding.Empty};
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));
        var presets=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        presets.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(string name in new[]{"Скорость","Баланс","Качество"}) {
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f)); int index=presets.Controls.Count;
            var button=new ModernButton(name,Color.FromArgb(40,35,64)) {Dock=DockStyle.Fill,Margin=new Padding(0,0,6,8),Font=new Font("Segoe UI",9,FontStyle.Bold)};
            button.Click+=(s,e)=>Preset(index); presetButtons.Add(button); presets.Controls.Add(button,index,0);
        }
        settingsLayout.Controls.Add(presets,0,0);
        var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(23,28,42),Margin=Padding.Empty};
        var settings=new TableLayoutPanel {Dock=DockStyle.Top,ColumnCount=1,RowCount=7,AutoSize=true,Margin=Padding.Empty};
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var rows=new[]{Setting("Разрешение",resolution),Setting("Режим экрана",windowMode),Setting("Качество графики",quality),Setting("Масштаб рендера, %",scale),Setting("Лимит FPS",target),Setting("Синхронизация",vsync),Setting("Профиль запуска",autoApply)};
        for(int i=0;i<rows.Length;i++){settings.RowStyles.Add(new RowStyle(SizeType.Absolute,50));settings.Controls.Add(rows[i],0,i);}
        scroll.Controls.Add(settings); settingsLayout.Controls.Add(scroll,0,1); support.Margin=new Padding(0,10,0,0); settingsLayout.Controls.Add(support,0,2);
        right.Controls.Add(settingsLayout); columns.Controls.Add(right,1,0); layout.Controls.Add(columns,0,2);
        var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        footer.RowStyles.Add(new RowStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));
        foreach(var button in new[]{suggest,save,launch}){button.Dock=DockStyle.Fill;button.Margin=new Padding(0,12,8,0);}
        footer.Controls.Add(suggest,0,0);footer.Controls.Add(status,1,0);footer.Controls.Add(save,2,0);footer.Controls.Add(launch,3,0);layout.Controls.Add(footer,0,3);
        ContentHost.Controls.Add(layout);
        quality.Items.AddRange(new object[]{"Низкие","Средние","Высокие"}); windowMode.Items.AddRange(new object[]{"Полноэкранный","Безрамочный","Оконный"});
        foreach(var size in new[]{new ResolutionChoice(0,0),new(1280,720),new(1600,900),new(1920,1080),new(2560,1440),new(3840,2160)}) resolution.Items.Add(size);
        foreach(var game in games) gamePicker.Items.Add(game.Name);
        gamePicker.SelectedIndexChanged+=(s,e)=>LoadProfile();
        target.ValueChanged+=(s,e)=>Edited(); measured.ValueChanged+=(s,e)=>Edited(); scale.ValueChanged+=(s,e)=>Edited();
        quality.SelectedIndexChanged+=(s,e)=>Edited(); resolution.SelectedIndexChanged+=(s,e)=>Edited(); windowMode.SelectedIndexChanged+=(s,e)=>Edited();
        vsync.CheckedChanged+=(s,e)=>Edited();autoApply.CheckedChanged+=(s,e)=>Edited();
        save.Click+=(s,e)=>Save(false);launch.Click+=(s,e)=>Save(true);
        suggest.Click+=(s,e)=>{if(snapshot!=null){target.Value=PerformanceAdvisor.SuggestTarget(snapshot,(int)measured.Value);status.Text="Цель по замеру и экрану";}};
        config.Click+=(s,e)=>ChooseConfig(); restore.Click+=(s,e)=>Restore();
        if(games.Length>0)gamePicker.SelectedIndex=Math.Max(0,Array.IndexOf(games,selected));
        else {quality.SelectedIndex=1;resolution.SelectedIndex=0;windowMode.SelectedIndex=1;UpdateCapabilities();status.Text="Сначала добавь игру";}
        gamePicker.Enabled=save.Enabled=games.Length>0;suggest.Enabled=false;
        Shown+=async(s,e)=>{try{var pc=await Task.Run(PcSnapshot.Read);if(!IsDisposed)SetSnapshot(pc);}catch(Exception ex){if(!IsDisposed)hardware.Text="Нет данных оборудования: "+ex.Message;}};
    }

    internal void SetSnapshot(PcSnapshot pc)
    {
        snapshot=pc; static string Memory(ulong b)=>b==0?"нет данных":$"{b/(1024d*1024*1024):0.0} ГБ";
        hardware.Text=$"{pc.Cpu}\r\n\r\n{pc.Gpu}\r\nRAM {Memory(pc.TotalRam)} · свободно {Memory(pc.AvailableRam)}\r\n{pc.Width} × {pc.Height} · {pc.RefreshRate} Гц";
        suggest.Enabled=Game!=null;UpdatePlan();
    }
    private PerformanceProfile CurrentProfile()
    {
        var size=resolution.SelectedItem as ResolutionChoice ?? new(0,0);
        return new(){TargetFps=(int)target.Value,MeasuredFps=(int)measured.Value,RenderScale=(int)scale.Value,
            Quality=quality.SelectedItem?.ToString()??"Средние",ResolutionWidth=size.Width,ResolutionHeight=size.Height,
            WindowMode=windowMode.SelectedItem?.ToString()??"Безрамочный",VSync=vsync.Checked,ApplyOnLaunch=autoApply.Checked,ConfigFile=configPath};
    }
    private void LoadProfile()
    {
        if(Game is not GameInfo game)return; loading=true;
        var p=(game.Performance??new PerformanceProfile()).Copy();
        target.Value=p.TargetFps;measured.Value=p.MeasuredFps;scale.Value=p.RenderScale;quality.SelectedItem=p.Quality;windowMode.SelectedItem=p.WindowMode;
        var size=new ResolutionChoice(p.ResolutionWidth,p.ResolutionHeight);
        if(!resolution.Items.Contains(size))resolution.Items.Add(size);resolution.SelectedItem=size;
        configPath=GamePerformanceController.Config(game,p);vsync.Checked=p.VSync;autoApply.Checked=p.ApplyOnLaunch;
        loading=false;status.Text=game.Performance==null?"Новый профиль":"Профиль загружен";UpdateCapabilities();UpdatePlan();
    }
    private void UpdateCapabilities()
    {
        var game=Game;var kind=game==null?PerformanceBackend.None:GamePerformanceController.Backend(game);
        bool hasConfig=game!=null&&GamePerformanceController.Config(game,CurrentProfile())!=null;
        string desired=windowMode.SelectedItem?.ToString()??"Безрамочный";
        string[] modes=kind==PerformanceBackend.Unreal&&!hasConfig?new[]{"Полноэкранный","Оконный"}:new[]{"Полноэкранный","Безрамочный","Оконный"};
        if(!windowMode.Items.Cast<string>().SequenceEqual(modes)){
            bool wasLoading=loading;loading=true;windowMode.Items.Clear();windowMode.Items.AddRange(modes);
            windowMode.SelectedItem=modes.Contains(desired)?desired:modes[0];loading=wasLoading;
        }
        resolution.Enabled=windowMode.Enabled=autoApply.Enabled=kind!=PerformanceBackend.None;
        quality.Enabled=scale.Enabled=vsync.Enabled=hasConfig;target.Enabled=hasConfig||kind==PerformanceBackend.Source2;
        launch.Enabled=game!=null&&kind!=PerformanceBackend.None;config.Enabled=kind==PerformanceBackend.Unreal;
        foreach(var button in presetButtons)button.Enabled=kind!=PerformanceBackend.None;
        restore.Enabled=hasConfig&&File.Exists(configPath+".playspace-backup");
        support.Text=game==null?"Добавь игру, чтобы настроить профиль.":GamePerformanceController.Support(game,CurrentProfile());
    }
    private void Edited(){if(loading)return;foreach(var button in presetButtons)button.IsActive=false;status.Text="Есть несохранённые изменения";UpdatePlan();}
    private void UpdatePlan(){plan.Text=snapshot==null?"Выбери профиль и параметры. Оборудование определяется автоматически.":PerformanceAdvisor.Plan(snapshot,CurrentProfile());}
    private void Preset(int index)
    {
        loading=true;
        if(quality.Enabled)quality.SelectedIndex=index;
        if(scale.Enabled)scale.Value=index==0?67:index==1?85:100;
        if(target.Enabled)target.Value=index==0?120:60;
        if(vsync.Enabled)vsync.Checked=index==2;
        resolution.SelectedItem=quality.Enabled?resolution.Items[0]:resolution.Items[index==0?1:index==1?2:0];
        loading=false;Edited();for(int i=0;i<presetButtons.Count;i++)presetButtons[i].IsActive=i==index;
    }
    private void Save(bool play)
    {
        if(Game is not GameInfo game)return;
        var p=CurrentProfile(); if(play)p.ApplyOnLaunch=true;
        if(play&&GamePerformanceController.Backend(game)==PerformanceBackend.None)return;
        if(!saveProfile(game,p))return;autoApply.Checked=p.ApplyOnLaunch;status.Text="Профиль сохранён";
        if(play){LaunchRequested=game;DialogResult=DialogResult.OK;Close();}
    }
    private void ChooseConfig()
    {
        using var picker=new OpenFileDialog {Title="GameUserSettings.ini выбранной игры",Filter="GameUserSettings.ini|GameUserSettings.ini",CheckFileExists=true};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{GamePerformanceController.EditIni(File.ReadAllText(picker.FileName),CurrentProfile(),new Size(1920,1080));configPath=picker.FileName;UpdateCapabilities();Edited();}
        catch(Exception ex){status.Text="Конфиг не подключён: "+ex.Message;}
    }
    private void Restore()
    {
        if(Game is not GameInfo game || configPath==null)return;
        try{var profile=CurrentProfile();profile.ApplyOnLaunch=false;if(!saveProfile(game,profile))return;GamePerformanceController.RestoreConfig(configPath);autoApply.Checked=false;status.Text="Исходный конфиг восстановлен";UpdateCapabilities();}
        catch(Exception ex){status.Text="Не удалось восстановить: "+ex.Message;}
    }
    private static Label Section(string text)=>new(){Text=text,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8,FontStyle.Bold),ForeColor=Color.FromArgb(170,148,247)};
    private static Control Setting(string label,Control input)
    {
        var row=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,1),BackColor=Color.FromArgb(23,28,42)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        row.Controls.Add(new Label {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(182,190,213),AutoEllipsis=true,Font=new Font("Segoe UI",9)},0,0);
        input.Dock=DockStyle.Fill;input.Margin=new Padding(4,12,0,8);row.Controls.Add(input,1,0);return row;
    }
    private static ComboBox Picker()=>new(){DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,37,55),ForeColor=Color.FromArgb(232,230,249),IntegralHeight=false,DropDownHeight=240,Font=new Font("Segoe UI",9)};
    private static NumericUpDown Number(int min,int max,int value)=>new(){Minimum=min,Maximum=max,Value=value,BackColor=Color.FromArgb(32,37,55),ForeColor=Color.FromArgb(232,230,249),BorderStyle=BorderStyle.FixedSingle};
    private static TextBox ReadOnlyText()=>new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.FromArgb(174,185,210),BorderStyle=BorderStyle.None,Margin=new Padding(0,4,0,4),TabStop=false,Font=new Font("Segoe UI",9)};
}
