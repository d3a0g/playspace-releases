namespace GameLauncherGUI;

internal sealed partial class PerformanceForm : StudioDialog
{
    private sealed record ResolutionChoice(int Width, int Height) {
        public override string ToString() => Width == 0 ? "Текущее разрешение экрана" : $"{Width} × {Height}";
    }
    private readonly GameInfo[] games;
    private readonly Func<GameInfo, PerformanceProfile, bool> saveProfile;
    private readonly ThemedComboBox gamePicker = Picker(), quality = Picker(), resolution = Picker(), windowMode = Picker();
    private readonly NumericUpDown target = Number(30,360,60), measured = Number(0,1000,0), scale = Number(50,100,100);
    private readonly CheckBox vsync = new ModernToggle { Text="VSync", ForeColor=Color.FromArgb(211,211,230), Dock=DockStyle.Fill };
    private readonly CheckBox autoApply = new ModernToggle { Text="Автоприменение", ForeColor=Color.FromArgb(211,211,230), Dock=DockStyle.Fill };
    private readonly FormattedDescription hardware = ReadOnlyText(), plan = ReadOnlyText();
    private readonly SmoothLabel status = new() { Dock=DockStyle.Fill, ForeColor=Color.FromArgb(151,163,192), TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true };
    private readonly SmoothLabel support = new() { Dock=DockStyle.Fill, ForeColor=Color.FromArgb(151,163,192), Font=new Font("Segoe UI",9) };
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
        BuildStudio();
        quality.Items.AddRange(new object[]{"Низкие","Средние","Высокие"}); windowMode.Items.AddRange(new object[]{"Полноэкранный","Безрамочный","Оконный"});
        foreach(var size in new[]{new ResolutionChoice(0,0),new(1280,720),new(1600,900),new(1920,1080),new(2560,1440),new(3840,2160)}) resolution.Items.Add(size);
        foreach(var game in games) gamePicker.Items.Add(game.Name);
        gamePicker.SelectedIndexChanged+=(s,e)=>LoadProfile();
        target.ValueChanged+=(s,e)=>Edited(); measured.ValueChanged+=(s,e)=>Edited(); scale.ValueChanged+=(s,e)=>Edited();
        quality.SelectedIndexChanged+=(s,e)=>Edited(); resolution.SelectedIndexChanged+=(s,e)=>Edited(); windowMode.SelectedIndexChanged+=(s,e)=>Edited();
        vsync.CheckedChanged+=(s,e)=>Edited();autoApply.CheckedChanged+=(s,e)=>Edited();unlimited.CheckedChanged+=(s,e)=>{if(!loading){UpdateCapabilities();Edited();}};
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
        suggest.Enabled=Game!=null;UpdatePlan();UpdateBenchmark();
    }
    private PerformanceProfile CurrentProfile()
    {
        var size=resolution.SelectedItem as ResolutionChoice ?? new(0,0);
        return new(){TargetFps=(int)target.Value,MeasuredFps=(int)measured.Value,RenderScale=(int)scale.Value,
            Quality=quality.SelectedItem?.ToString()??"Средние",ResolutionWidth=size.Width,ResolutionHeight=size.Height,
            WindowMode=windowMode.SelectedItem?.ToString()??"Безрамочный",VSync=vsync.Checked,UnlimitedFps=unlimited.Checked,ApplyOnLaunch=autoApply.Checked,ConfigFile=configPath,DetailLevels=new(detailLevels),NativeOptions=new(nativeOptions)};
    }
    private void LoadProfile()
    {
        if(Game is not GameInfo game)return; loading=true;
        var p=(game.Performance??new PerformanceProfile()).Copy();
        if(game.Performance==null&&GamePerformanceController.Backend(game)==PerformanceBackend.Unreal&&GamePerformanceController.Config(game,p) is string existing){try{p=GamePerformanceController.ReadConfig(existing);}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException){status.Text="Не удалось прочитать текущие настройки: "+ex.Message;}}
        target.Value=p.TargetFps;measured.Value=p.MeasuredFps;scale.Value=p.RenderScale;quality.SelectedItem=p.Quality;windowMode.SelectedItem=p.WindowMode;
        var size=new ResolutionChoice(p.ResolutionWidth,p.ResolutionHeight);
        if(!resolution.Items.Contains(size))resolution.Items.Add(size);resolution.SelectedItem=size;
        configPath=GamePerformanceController.Config(game,p);detailLevels=GraphicsParameters.Sanitize(p.DetailLevels);nativeOptions=new(p.NativeOptions);vsync.Checked=p.VSync;unlimited.Checked=p.UnlimitedFps;autoApply.Checked=p.ApplyOnLaunch;BuildAdvanced();
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
        resolution.Enabled=windowMode.Enabled=kind is PerformanceBackend.Unity or PerformanceBackend.Unreal or PerformanceBackend.Source2;autoApply.Enabled=kind!=PerformanceBackend.None;
        quality.Enabled=scale.Enabled=vsync.Enabled=hasConfig&&kind==PerformanceBackend.Unreal;target.Enabled=kind==PerformanceBackend.Unreal&&hasConfig||kind==PerformanceBackend.Source2;
        launch.Enabled=game!=null&&kind!=PerformanceBackend.None&&(kind!=PerformanceBackend.Cyberpunk||hasConfig);config.Enabled=kind is PerformanceBackend.Unreal or PerformanceBackend.Cyberpunk;
        unlimited.Enabled=kind==PerformanceBackend.Unreal&&hasConfig||kind==PerformanceBackend.Source2;target.Enabled=target.Enabled&&!unlimited.Checked;
        foreach(var button in presetButtons)button.Enabled=kind!=PerformanceBackend.None;
        restore.Enabled=hasConfig&&File.Exists(configPath+".playspace-backup");
        support.Text=game==null?"Добавь игру, чтобы настроить профиль.":GamePerformanceController.Support(game,CurrentProfile());
    }
    private void Edited(){if(loading)return;foreach(var button in presetButtons)button.IsActive=false;status.Text="Есть несохранённые изменения";UpdatePlan();}
    private void UpdatePlan(){UpdateBenchmark();plan.Text=Game!=null&&GamePerformanceController.Backend(Game)==PerformanceBackend.Cyberpunk?$"Cyberpunk 2077\n\nИзменено отдельных параметров: {nativeOptions.Count}.\n\nПрименение через UserSettings.json при закрытой игре. Текущие значения показаны на вкладке «Графика».\n\nМой замер: "+(measured.Value>0?measured.Value+" FPS":"не введён")+". Цель не является прогнозом FPS.":snapshot==null?"Выбери профиль и параметры. Оборудование определяется автоматически.":PerformanceAdvisor.Plan(snapshot,CurrentProfile());}
    private void Preset(int index)
    {
        if(Game!=null&&GamePerformanceController.Backend(Game)==PerformanceBackend.Cyberpunk){
            if(configPath==null){status.Text="Сначала подключи конфигурацию игры.";return;}
            try{foreach(var setting in CyberpunkGraphics.Describe(CyberpunkGraphics.Read(configPath))){if(setting.Choices.Length>1&&setting.Choices.All(v=>new[]{"Low","Medium","High","Ultra"}.Contains(v)))nativeOptions[setting.Key]=setting.Choices[Math.Min(index,setting.Choices.Length-1)];if(index<2&&setting.Name.StartsWith("RayTrac",StringComparison.Ordinal)&&setting.Type=="bool")nativeOptions[setting.Key]="false";}BuildAdvanced();Edited();for(int i=0;i<presetButtons.Count;i++)presetButtons[i].IsActive=i==index;}
            catch(Exception ex)when(ex is IOException or InvalidDataException or Newtonsoft.Json.JsonException){status.Text=ex.Message;}return;
        }
        loading=true;
        detailLevels.Clear();unlimited.Checked=false;UpdateCapabilities();
        if(quality.Enabled)quality.SelectedIndex=index;
        if(scale.Enabled)scale.Value=index==0?67:index==1?85:100;
        if(target.Enabled)target.Value=index==0?120:60;
        if(vsync.Enabled)vsync.Checked=index==2;
        resolution.SelectedItem=quality.Enabled?resolution.Items[0]:resolution.Items[index==0?1:index==1?2:0];
        loading=false;BuildAdvanced();Edited();for(int i=0;i<presetButtons.Count;i++)presetButtons[i].IsActive=i==index;
    }
    private void Save(bool play)
    {
        if(Game is not GameInfo game)return;
        var p=CurrentProfile(); if(play)p.ApplyOnLaunch=true;
        if(play&&(GamePerformanceController.Backend(game)==PerformanceBackend.None||GamePerformanceController.Backend(game)==PerformanceBackend.Cyberpunk&&configPath==null))return;
        if(!saveProfile(game,p))return;autoApply.Checked=p.ApplyOnLaunch;status.Text="Профиль сохранён";
        if(play){LaunchRequested=game;DialogResult=DialogResult.OK;Close();}
    }
    private void ChooseConfig()
    {
        bool native=Game!=null&&GamePerformanceController.Backend(Game)==PerformanceBackend.Cyberpunk;using var picker=new OpenFileDialog {Title="Конфигурация выбранной игры",Filter=native?"Cyberpunk UserSettings.json|UserSettings.json":"Unreal GameUserSettings.ini|GameUserSettings.ini",CheckFileExists=true};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{if(native)CyberpunkGraphics.Describe(CyberpunkGraphics.Read(picker.FileName));else GamePerformanceController.EditIni(File.ReadAllText(picker.FileName),CurrentProfile(),new Size(1920,1080));configPath=picker.FileName;BuildAdvanced();UpdateCapabilities();Edited();}
        catch(Exception ex){status.Text="Конфиг не подключён: "+ex.Message;}
    }
    private void Restore()
    {
        if(Game is not GameInfo game || configPath==null)return;
        try{DlssManager.EnsureClosed(game);var profile=CurrentProfile();profile.ApplyOnLaunch=false;if(!saveProfile(game,profile))return;GamePerformanceController.RestoreConfig(configPath);autoApply.Checked=false;status.Text="Исходный конфиг восстановлен";UpdateCapabilities();}
        catch(Exception ex){status.Text="Не удалось восстановить: "+ex.Message;}
    }
    private static SmoothLabel Section(string text)=>new(){Text=text,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8,FontStyle.Bold),ForeColor=Color.FromArgb(170,148,247)};
    private static Control Setting(string label,Control input)
    {
        var row=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,1),BackColor=Color.FromArgb(23,28,42)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        row.Controls.Add(new SmoothLabel {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(182,190,213),AutoEllipsis=true,Font=new Font("Segoe UI",9)},0,0);
        Control editor=input is NumericUpDown number?new StudioNumberField(number):input;editor.Dock=DockStyle.Fill;editor.Margin=new Padding(4,7,0,7);row.Controls.Add(editor,1,0);return row;
    }
    private static ThemedComboBox Picker()=>new(){DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,37,55),ForeColor=Color.FromArgb(232,230,249),IntegralHeight=false,DropDownHeight=240,Font=new Font("Segoe UI",9)};
    private static NumericUpDown Number(int min,int max,int value)=>new(){Minimum=min,Maximum=max,Value=value,BackColor=Color.FromArgb(32,37,55),ForeColor=Color.FromArgb(232,230,249),BorderStyle=BorderStyle.FixedSingle};
    private static FormattedDescription ReadOnlyText()=>new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.FromArgb(174,185,210),Padding=new Padding(0,0,10,0),Margin=new Padding(0,4,0,4),Font=new Font("Segoe UI",9)};
}
