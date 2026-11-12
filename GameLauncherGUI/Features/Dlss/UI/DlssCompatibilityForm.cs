using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed class DlssCompatibilityCard : UserControl
{
    private readonly SmoothLabel summary=new(){Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Color.FromArgb(193,204,226)};
    internal DlssCompatibilityCard(GameInfo game,string adapter)
    {
        Dock=DockStyle.Fill;BackColor=Color.FromArgb(14,17,27);
        var card=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(12)};Controls.Add(card);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.FromArgb(23,28,42)};layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.ColumnStyles.Add(new(SizeType.Absolute,220));card.Controls.Add(layout);
        summary.Text=adapter+"\nВерсии DLSS определяются при сканировании.";layout.Controls.Add(summary,0,0);
        var open=new ModernButton("Совместимость DLSS",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill,Margin=new Padding(12,12,0,12),AccessibleName="Открыть совместимость DLSS и мои проверки"};layout.Controls.Add(open,1,0);
        open.Click+=(s,e)=>{using var form=new DlssCompatibilityForm(game);form.ShowDialog(FindForm());};
    }
    internal void UpdateSummary(DlssSnapshot snapshot)=>summary.Text=snapshot.Adapter+"\n"+(snapshot.Files.Length==0?"DLSS: библиотеки не найдены":string.Join(" · ",snapshot.Files.Select(f=>f.Kind+" "+f.Version).Distinct()))+(snapshot.Incomplete?"\nСканирование неполное":"")+"\nРезультаты проверок — только с этого ПК.";
}
internal sealed class DlssCompatibilityForm : StudioDialog
{
    private readonly GameInfo game;
    private readonly string adapter;
    private readonly DlssCheckStore store;
    private readonly CancellationTokenSource cancellation=new();
    private DlssSnapshot? snapshot;
    private bool busy,disposed,currentCheck,checkStoreHealthy=true;
    private readonly FormattedDescription overview=new(){Dock=DockStyle.Fill},libraries=new(){Dock=DockStyle.Fill},history=new(){Dock=DockStyle.Fill,Font=new Font("Segoe UI",10)};
    private readonly SmoothLabel status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Color.FromArgb(178,189,213)};
    private readonly ThemedComboBox outcome=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox note=new(){Dock=DockStyle.Fill,Multiline=true,MaxLength=600,PlaceholderText="Режим, качество изображения, FPS или описание проблемы. До 600 символов."};
    private readonly ModernToggle tested=new(){Text="Я проверил выбранный режим в самой игре",Dock=DockStyle.Fill};
    private readonly ModernButton save=new("Сохранить мою проверку",VisualTheme.Accent){Dock=DockStyle.Fill,Enabled=false};
    private readonly ModernButton delete=new("Удалить мою отметку",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill,Enabled=false};
    private readonly ModernButton refresh=new("Обновить",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill,AccessibleName="Обновить данные совместимости"};
    internal DlssCompatibilityForm(GameInfo game,string? adapter=null,string? folder=null)
    {
        this.game=game;this.adapter=adapter??PcSnapshot.Read().Gpu;store=new(folder??Workspace.Current.Folder,game);
        Text="PLAYSPACE / Совместимость DLSS · "+game.Name;Size=new(980,820);MinimumSize=new(780,660);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=4};layout.RowStyles.Add(new(SizeType.Absolute,92));layout.RowStyles.Add(new(SizeType.Absolute,48));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,42));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new StudioHeading("Совместимость DLSS",game.Name,"ОБНАРУЖЕНИЕ И ЛИЧНЫЕ ПРОВЕРКИ"){Dock=DockStyle.Fill},0,0);
        var navigation=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4};foreach(float width in new[]{25f,25,30,20})navigation.ColumnStyles.Add(new(SizeType.Percent,width));layout.Controls.Add(navigation,0,1);
        var buttons=new[]{new ModernButton("Обзор",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill},new ModernButton("Библиотеки",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill},new ModernButton("Мои проверки",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill}};
        var host=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(host,0,2);layout.Controls.Add(status,0,3);
        Control Card(Control content){var card=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(10)};card.Controls.Add(content);return card;}
        var pages=new Control[]{Card(overview),Card(libraries),CreateChecksPage()};host.Controls.Add(pages[0]);
        void SelectPage(int index){host.Controls.Clear();host.Controls.Add(pages[index]);for(int i=0;i<buttons.Length;i++)buttons[i].IsActive=i==index;PresentationOptions.Apply(pages[index]);UiLayout.Apply(pages[index]);}
        for(int i=0;i<buttons.Length;i++){int index=i;navigation.Controls.Add(buttons[i],i,0);buttons[i].Click+=(s,e)=>SelectPage(index);}navigation.Controls.Add(refresh,3,0);buttons[0].IsActive=true;
        refresh.Click+=async(s,e)=>await Reload();Shown+=async(s,e)=>await Reload();tested.CheckedChanged+=(s,e)=>save.Enabled=!busy&&snapshot!=null&&tested.Checked;
        save.Click+=async(s,e)=>{
            if(snapshot==null||busy||!tested.Checked)return;string context=snapshot.Context;busy=true;UpdateButtons();
            try{var current=await Task.Run(()=>DlssSnapshot.Read(game,this.adapter,cancellation.Token),cancellation.Token);if(IsDisposed)return;if(current.Context!=context){SetSnapshot(current);status.Text="Конфигурация изменилась. Проверь игру ещё раз перед сохранением.";return;}
                store.Save(current,(DlssTestOutcome)outcome.SelectedIndex,note.Text);ShowChecks();status.Text="Проверка сохранена только на этом ПК. Данные не отправлялись.";
            }catch(Exception ex)when(ex is IOException or JsonException or ArgumentException or OperationCanceledException){if(!IsDisposed)status.Text="Не удалось сохранить: "+ex.Message;}
            finally{busy=false;if(!IsDisposed)UpdateButtons();}
        };
        delete.Click+=(s,e)=>{if(snapshot==null||busy)return;try{store.Remove(snapshot.Context);ShowChecks();status.Text="Отметка для текущей конфигурации удалена.";}catch(Exception ex)when(ex is IOException or JsonException){status.Text=ex.Message;}};
        Disposed+=(s,e)=>{foreach(var page in pages)if(!page.IsDisposed)page.Dispose();};
    }
    private Control CreateChecksPage()
    {
        var page=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};foreach(float h in new[]{66f,44,84,42,46})page.RowStyles.Add(new(SizeType.Absolute,h));page.RowStyles.Add(new(SizeType.Percent,100));
        page.Controls.Add(new SmoothLabel{Text="Здесь только твои проверки на этом ПК; общая база сообщества не подключена. В заметке укажи режим, настройки и результат — до 600 символов.",Dock=DockStyle.Fill},0,0);
        outcome.Items.AddRange(new object[]{"Работает","Есть проблемы","Не работает"});outcome.SelectedIndex=0;page.Controls.Add(outcome,0,1);page.Controls.Add(note,0,2);page.Controls.Add(tested,0,3);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};actions.ColumnStyles.Add(new(SizeType.Percent,60));actions.ColumnStyles.Add(new(SizeType.Percent,40));actions.Controls.Add(save,0,0);actions.Controls.Add(delete,1,0);page.Controls.Add(actions,0,4);page.Controls.Add(history,0,5);return page;
    }
    private async Task Reload()
    {
        if(busy)return;busy=true;UpdateButtons();status.Text="Проверяем локальные файлы…";
        try{var current=await Task.Run(()=>DlssSnapshot.Read(game,adapter,cancellation.Token),cancellation.Token);if(IsDisposed)return;SetSnapshot(current);status.Text="Данные обновлены. Проверка не запускает игру и не обращается в интернет.";}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException){if(!IsDisposed){snapshot=null;overview.Text="Не удалось проверить игру. "+ex.Message;libraries.Text="Нет актуальных данных.";status.Text=ex.Message;}}
        finally{busy=false;if(!IsDisposed)UpdateButtons();}
    }
    private void SetSnapshot(DlssSnapshot current)
    {
        snapshot=current;overview.Text="ВИДЕОКАРТА\n"+current.Adapter+"\n\nВЕРСИЯ ИГРЫ\n"+current.GameVersion+"\n\n"+current.Features;libraries.Text=current.Libraries+"\n\nВерсия DLL не равна названию всех технологий DLSS. Найденные файлы могли остаться от мода и не использоваться игрой.";ShowChecks();
    }
    private void ShowChecks()
    {
        if(snapshot==null)return;DlssUserCheck[] checks;
        try{checks=store.Read();checkStoreHealthy=true;}catch(Exception ex)when(ex is IOException or JsonException){checkStoreHealthy=false;currentCheck=false;tested.Checked=false;history.Text="Не удалось прочитать сохранённые проверки. Файл не перезаписан. "+ex.Message;return;}
        var current=checks.FirstOrDefault(c=>c.Context==snapshot.Context);currentCheck=current!=null;outcome.SelectedIndex=(int)(current?.Outcome??DlssTestOutcome.Works);note.Text=current?.Note??"";tested.Checked=false;delete.Enabled=currentCheck&&!busy;
        history.Text=checks.Length==0?"Проверок пока нет. Запусти игру, проверь нужные функции и сохрани результат здесь.":string.Join("\n\n",checks.Select(c=>(c.Context==snapshot.Context?"ТЕКУЩАЯ КОНФИГУРАЦИЯ":"ДРУГАЯ / ПРЕЖНЯЯ КОНФИГУРАЦИЯ")+$"\n{c.Result} · {c.CheckedUtc.ToLocalTime():dd.MM.yyyy HH:mm}\n{c.Adapter}\nВерсия игры: {c.GameVersion}\n{c.Note}"));
    }
    private void UpdateButtons(){refresh.Enabled=!busy;note.Enabled=outcome.Enabled=tested.Enabled=!busy&&checkStoreHealthy;save.Enabled=!busy&&checkStoreHealthy&&snapshot!=null&&tested.Checked;delete.Enabled=!busy&&snapshot!=null&&currentCheck;}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;cancellation.Cancel();cancellation.Dispose();}base.Dispose(disposing);}
}
