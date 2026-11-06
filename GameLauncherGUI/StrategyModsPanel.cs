using System.Diagnostics;

namespace GameLauncherGUI;

internal sealed class StrategyModsPanel : UserControl
{
    private readonly StrategyModsManager manager;
    private readonly StrategyGameAdapter adapter;
    private readonly ListBox list = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(23,28,42), ForeColor = Color.FromArgb(232,230,247), BorderStyle = BorderStyle.None, IntegralHeight = false, ItemHeight = 28 };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(23,28,42), ForeColor = Color.FromArgb(182,193,217), BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Поиск установленных модов…", BackColor = Color.FromArgb(30,35,51), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(160,173,202) };
    private readonly ModernButton toggle = new("Включить", Color.FromArgb(115,94,244));
    private readonly ModernButton remove = new("Удалить", Color.FromArgb(46,35,55));
    private readonly ModernButton up = new("↑ Раньше", Color.FromArgb(35,40,63)), down = new("↓ Позже", Color.FromArgb(35,40,63));
    private readonly ModernButton import = new("+ Импорт ZIP", Color.FromArgb(115,94,244)), workshop = new("Steam Workshop ↗", Color.FromArgb(35,40,63));
    private readonly ModernButton refresh = new("Обновить", Color.FromArgb(35,40,63)), restore = new("Вернуть Paradox", Color.FromArgb(35,40,63));
    private List<StrategyModEntry> entries = new();
    private CancellationTokenSource cancellation = new();
    private bool busy;
    public event Action<bool>? BusyChanged;

    public StrategyModsPanel(StrategyGameAdapter adapter, GameInfo game)
    {
        this.adapter = adapter; manager = new StrategyModsManager(adapter, game);
        BackColor = Color.FromArgb(14,17,27); ForeColor = Color.FromArgb(235,232,249); Font = new Font("Segoe UI",10); Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130)); tools.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var button in new[]{import,workshop,refresh,restore,toggle,remove,up,down}){button.Dock=DockStyle.Fill;button.Margin=new Padding(0,4,8,6);button.Font=new Font("Segoe UI",9,FontStyle.Bold);}
        search.Margin=new Padding(0,10,12,6);tools.Controls.Add(import,0,0);tools.Controls.Add(workshop,1,0);tools.Controls.Add(search,2,0);tools.Controls.Add(refresh,3,0);
        layout.Controls.Add(tools,0,0);
        layout.Controls.Add(new Label {Text="Workshop скачивает Steam; здесь видны уже загруженные моды. ZIP устанавливается прямо в PLAYSPACE.\nВключение и порядок действуют при прямом запуске игры из PLAYSPACE. Наборы Paradox Launcher не изменяются.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(153,167,195)},0,1);
        var body=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        list.Margin=new Padding(0,4,14,4);body.Controls.Add(list,0,0);
        var card=new RoundedPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
        var side=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,BackColor=Color.FromArgb(23,28,42)};
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        side.RowStyles.Add(new RowStyle(SizeType.Percent,100));side.RowStyles.Add(new RowStyle(SizeType.Absolute,46));side.RowStyles.Add(new RowStyle(SizeType.Absolute,46));side.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        side.Controls.Add(details,0,0);side.SetColumnSpan(details,2);side.Controls.Add(toggle,0,1);side.SetColumnSpan(toggle,2);
        side.Controls.Add(up,0,2);side.Controls.Add(down,1,2);side.Controls.Add(remove,0,3);side.Controls.Add(restore,1,3);
        card.Controls.Add(side);body.Controls.Add(card,1,0);layout.Controls.Add(body,0,2);layout.Controls.Add(status,0,3);Controls.Add(layout);
        list.SelectedIndexChanged+=(s,e)=>ShowDetails();search.TextChanged+=(s,e)=>FillList();
        refresh.Click+=async(s,e)=>{if(busy)cancellation.Cancel();else await ReloadAsync();};
        import.Click+=async(s,e)=>{
            using var picker=new OpenFileDialog {Title="ZIP одного мода с descriptor.mod",Filter="ZIP моды|*.zip",CheckFileExists=true};
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            var progress=new Progress<string>(text=>{if(!IsDisposed)status.Text=text;});
            await RunAsync(()=>manager.ImportZip(picker.FileName,progress,cancellation.Token),"Мод импортирован. Выбери его и нажми «Включить».");
        };
        workshop.Click+=(s,e)=>{
            try{Process.Start(new ProcessStartInfo {FileName=adapter.WorkshopUrl,UseShellExecute=true});status.Text="Подпишись на мод в Steam, дождись загрузки и нажми «Обновить».";}
            catch(Exception ex){status.Text="Не удалось открыть Steam Workshop: "+ex.Message;}
        };
        toggle.Click+=async(s,e)=>{if(list.SelectedItem is StrategyModEntry entry)await RunAsync(()=>{manager.SetEnabled(entry,!entry.Enabled);return "";},"Набор модов сохранён для запуска из PLAYSPACE.");};
        up.Click+=async(s,e)=>{if(list.SelectedItem is StrategyModEntry entry)await RunAsync(()=>{manager.Move(entry,-1);return "";},"Порядок загрузки сохранён.");};
        down.Click+=async(s,e)=>{if(list.SelectedItem is StrategyModEntry entry)await RunAsync(()=>{manager.Move(entry,1);return "";},"Порядок загрузки сохранён.");};
        remove.Click+=async(s,e)=>{
            if(list.SelectedItem is not StrategyModEntry entry || entry.ManagedId==null)return;
            if(MessageBox.Show(this,"Удалить установленный PLAYSPACE мод/регистрацию «"+entry.Name+"»? Файлы Steam Workshop не удаляются.","Удаление мода",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            await RunAsync(()=>{manager.Remove(entry);return "";},"Мод удалён из PLAYSPACE.");
        };
        restore.Click+=async(s,e)=>{
            if(MessageBox.Show(this,"Восстановить список включённых модов, который был до первой настройки в PLAYSPACE? Остальные параметры DLC сохранятся.","Управление Paradox",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            await RunAsync(()=>{manager.RestoreControl();return "";},"Исходный список модов восстановлен. Управление возвращено Paradox Launcher.");
        };
        ShowDetails();
    }
    public async Task ReloadAsync()
    {
        if(busy)return;SetBusy(true);status.Text="Проверяем локальные и загруженные Workshop-моды…";
        try{var result=await Task.Run(manager.List);if(!IsDisposed){entries=result;FillList();status.Text=$"{adapter.Name}: {entries.Count} модов. Папка данных: {manager.UserRoot}";}}
        catch(Exception ex){if(!IsDisposed)status.Text="Не удалось прочитать моды: "+ex.Message;}
        finally{if(!IsDisposed)SetBusy(false);}
    }
    private async Task RunAsync(Func<string> work,string success)
    {
        if(busy)return;cancellation.Dispose();cancellation=new();SetBusy(true);
        try{await Task.Run(work);entries=await Task.Run(manager.List);if(!IsDisposed){FillList();status.Text=success;}}
        catch(OperationCanceledException){if(!IsDisposed)status.Text="Импорт отменён до применения файлов.";}
        catch(Exception ex){if(!IsDisposed)status.Text="Операция не выполнена: "+ex.Message;}
        finally{if(!IsDisposed)SetBusy(false);}
    }
    private void SetBusy(bool value){busy=value;import.Enabled=workshop.Enabled=restore.Enabled=search.Enabled=!value;refresh.Text=value?"Отменить":"Обновить";BusyChanged?.Invoke(value);ShowDetails();}
    private void FillList(){var previous=list.SelectedItem as StrategyModEntry;list.BeginUpdate();list.Items.Clear();foreach(var entry in entries.Where(mod=>mod.Name.Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)))list.Items.Add(entry);list.EndUpdate();int index=previous==null?-1:list.Items.Cast<StrategyModEntry>().ToList().FindIndex(mod=>mod.Descriptor==previous.Descriptor&&mod.Content==previous.Content);if(list.Items.Count>0)list.SelectedIndex=index>=0?index:0;ShowDetails();}
    private void ShowDetails()
    {
        var entry=list.SelectedItem as StrategyModEntry;toggle.Enabled=!busy&&entry!=null;up.Enabled=down.Enabled=!busy&&entry?.Enabled==true;remove.Enabled=!busy&&entry?.ManagedId!=null;
        toggle.Text=entry?.Enabled==true?"Отключить":"Включить";remove.Text=entry?.Workshop==true?"Убрать регистрацию":"Удалить мод";
        details.Text=entry==null?"Установи ZIP-мод или загрузи его через Steam Workshop.\r\n\r\nЗдесь появятся переключатели и порядок загрузки. Совместимость версий и DLC зависит от самого мода.":
            entry.Name+"\r\n\r\n"+(entry.Enabled?"● Включён в профиле PLAYSPACE":"○ Отключён")+"\r\nЗаявленная версия игры: "+entry.SupportedVersion+"\r\nИсточник: "+(entry.Workshop?"Steam Workshop":"Локальный мод")+
            "\r\n\r\nЗависимости:\r\n"+string.Join("\r\n",StrategyModsManager.Dependencies(entry.Metadata))+"\r\n\r\nПапка:\r\n"+entry.Content;
    }
    protected override void Dispose(bool disposing){if(disposing){cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
