namespace GameLauncherGUI;

internal sealed record LauncherCommand(string Name,string Category,Action Invoke){public override string ToString()=>Category+" · "+Name;}
internal sealed class CommandPalette : StudioDialog
{
    private readonly TextBox query=new(){Dock=DockStyle.Fill,PlaceholderText="Название игры, настройки, моды, копии…"};private readonly ListBox results=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,BorderStyle=BorderStyle.None,IntegralHeight=false,ItemHeight=30};
    internal LauncherCommand? Choice{get;private set;}
    internal static LauncherCommand[] Find(IEnumerable<LauncherCommand> commands,string query)=>commands.Where(c=>(c.Category+" "+c.Name).Contains(query.Trim(),StringComparison.CurrentCultureIgnoreCase)).Take(80).ToArray();
    internal CommandPalette(IReadOnlyList<LauncherCommand> commands)
    {
        Text="PLAYSPACE / Быстрый поиск · Ctrl+K";Size=new Size(820,620);MinimumSize=new Size(700,500);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));ContentHost.Controls.Add(layout);layout.Controls.Add(new ModernSearchField(query){Dock=DockStyle.Fill},0,0);layout.Controls.Add(results,0,1);var open=new ModernButton("Открыть · Enter",VisualTheme.Accent){Dock=DockStyle.Fill};layout.Controls.Add(open,0,2);
        void Filter(){results.Items.Clear();results.Items.AddRange(Find(commands,query.Text));if(results.Items.Count>0)results.SelectedIndex=0;open.Enabled=results.Items.Count>0;}
        void Select(){if(results.SelectedItem is LauncherCommand command){Choice=command;DialogResult=DialogResult.OK;}}
        query.TextChanged+=(s,e)=>Filter();query.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Down){results.Focus();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Enter){Select();e.SuppressKeyPress=true;}};results.DoubleClick+=(s,e)=>Select();results.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){Select();e.SuppressKeyPress=true;}};open.Click+=(s,e)=>Select();Shown+=(s,e)=>{Filter();query.Focus();};
    }
}
internal sealed class CollectionsForm : StudioDialog
{
    internal CollectionsForm(IReadOnlyList<GameInfo> games,LibraryCollections collections)
    {
        Text="PLAYSPACE / Коллекции и теги";Size=new Size(900,720);MinimumSize=new Size(800,620);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));ContentHost.Controls.Add(layout);
        var picker=new ModernSelect{Dock=DockStyle.Fill};picker.SetPlaceholder("Создайте свою коллекцию");var name=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Название: Пройти, С друзьями, На выходные…"};var list=new CheckedListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,BorderStyle=BorderStyle.None,CheckOnClick=true};var status=new SmoothLabel{Dock=DockStyle.Fill};layout.Controls.Add(picker,0,0);layout.Controls.Add(name,0,1);layout.Controls.Add(list,0,2);layout.Controls.Add(status,0,4);bool loading=false;
        void Games(){loading=true;list.Items.Clear();if(picker.SelectedItem is string collection)foreach(var game in games.OrderBy(g=>g.Name))list.Items.Add(game,collections.Contains(game,collection));list.DisplayMember="Name";loading=false;}
        void Refresh(string? selected=null){picker.SelectedIndex=-1;picker.Items.Clear();picker.Items.AddRange(collections.State.Names.Cast<object>());picker.SelectedIndex=selected==null?(picker.Items.Count>0?0:-1):picker.Items.FindIndex(i=>i.ToString()==selected);Games();}
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};foreach(var title in new[]{"Создать","Переименовать","Удалить коллекцию"}){
            var button=new ModernButton(title,title=="Создать"?VisualTheme.Accent:Color.FromArgb(35,40,63)){Width=220,Height=40};actions.Controls.Add(button);button.Click+=(s,e)=>{try{string? current=picker.SelectedItem?.ToString();if(title=="Создать"){collections.Create(name.Text);Refresh(name.Text.Trim());}else if(current!=null&&title=="Переименовать"){collections.Rename(current,name.Text);Refresh(name.Text.Trim());}else if(current!=null&&MessageBox.Show(this,"Удалить коллекцию? Игры и их файлы останутся на месте.","Коллекции",MessageBoxButtons.OKCancel)==DialogResult.OK){collections.Remove(current);Refresh();}status.Text="Коллекции сохранены.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){status.Text=ex.Message;}};
        }layout.Controls.Add(actions,0,3);picker.SelectedIndexChanged+=(s,e)=>Games();list.ItemCheck+=(s,e)=>{if(loading||picker.SelectedItem is not string collection||list.Items[e.Index] is not GameInfo game)return;try{collections.Set(game.LibraryId,collection,e.NewValue==CheckState.Checked);status.Text="Состав коллекции сохранён.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){e.NewValue=e.CurrentValue;status.Text="Не удалось сохранить коллекцию.";}};Shown+=(s,e)=>Refresh();
    }
}
internal sealed class ActivityHistoryForm : StudioDialog
{
    internal ActivityHistoryForm(IReadOnlyList<GameInfo> games,ActivityHistory history,GameInfo? selected=null)
    {
        Text="PLAYSPACE / История изменений";Size=new Size(920,720);MinimumSize=new Size(780,600);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));ContentHost.Controls.Add(layout);var picker=new ModernSelect{Dock=DockStyle.Fill};picker.Items.Add("Все игры");picker.Items.AddRange(games.Cast<object>());picker.SelectedIndex=selected==null?0:Math.Max(0,picker.Items.FindIndex(item=>ReferenceEquals(item,selected)));var text=new FormattedDescription{Dock=DockStyle.Fill};layout.Controls.Add(picker,0,0);layout.Controls.Add(text,0,1);layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text="Последние 1000 событий PlaySpace. Внешние изменения другими программами не отслеживаются."},0,2);
        void Refresh(){var items=history.Entries().Where(entry=>picker.SelectedItem is not GameInfo game||entry.GameId==game.LibraryId).ToArray();text.Text=items.Length==0?"История пока пуста. Здесь появятся запуски, копии и изменения модов или настроек.":string.Join("\n\n",items.Select(entry=>$"{entry.Time.ToLocalTime():dd.MM.yyyy HH:mm} · {entry.Kind}\n{entry.GameName}\n{entry.Text}"));}picker.SelectedIndexChanged+=(s,e)=>Refresh();Shown+=(s,e)=>Refresh();
    }
}
internal sealed record PlayingGame(GameInfo Game,TimeSpan Session,string ModProfile="Набор не определён");
internal sealed class NowPlayingForm : StudioDialog
{
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    internal NowPlayingForm(Func<IReadOnlyList<PlayingGame>> current,Action<GameInfo> care,Action<GameInfo> performance)
    {
        Text="PLAYSPACE / Сейчас в игре";Size=new Size(900,660);MinimumSize=new Size(760,560);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));ContentHost.Controls.Add(layout);var picker=new ModernSelect{Dock=DockStyle.Fill};picker.SetPlaceholder("Запущенных игр пока нет");var description=new FormattedDescription{Dock=DockStyle.Fill};layout.Controls.Add(picker,0,0);layout.Controls.Add(description,0,1);var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var saves=new ModernButton("Сохранения и копии",VisualTheme.Accent){Width=260,Height=40};var settings=new ModernButton("Профиль производительности",Color.FromArgb(35,40,63)){Width=310,Height=40};actions.Controls.Add(saves);actions.Controls.Add(settings);layout.Controls.Add(actions,0,2);
        void Refresh(){var previous=picker.SelectedItem as GameInfo;var active=current();picker.SelectedIndex=-1;picker.Items.Clear();picker.Items.AddRange(active.Select(item=>item.Game).Cast<object>());picker.SelectedIndex=active.Count==0?-1:Math.Max(0,picker.Items.FindIndex(item=>ReferenceEquals(item,previous)));var item=active.FirstOrDefault(i=>ReferenceEquals(i.Game,picker.SelectedItem));saves.Enabled=settings.Enabled=item!=null;description.Text=item==null?"Запустите игру через PlaySpace. Карточка появится, когда будет обнаружен её процесс.\n\nДля точного отслеживания выберите основной EXE через контекстное меню игры.":$"{item.Game.Name}\n\nТекущая отслеживаемая сессия: {item.Session:hh\\:mm\\:ss}\nПрофиль производительности: "+(item.Game.Performance?.ApplyOnLaunch==true?"цель "+item.Game.Performance.TargetFps+" FPS · "+item.Game.Performance.WindowMode+" (параметры запуска)":"не применяется автоматически")+"\nНабор модов: "+item.ModProfile+"\n\nПараметры модов и сохранений доступны в управлении игрой. Менять файлы во время игры нельзя.";}
        saves.Click+=(s,e)=>{if(picker.SelectedItem is GameInfo game)care(game);};settings.Click+=(s,e)=>{if(picker.SelectedItem is GameInfo game)performance(game);};timer.Tick+=(s,e)=>Refresh();Shown+=(s,e)=>{Refresh();timer.Start();};
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
internal sealed class WorkspaceForm : StudioDialog
{
    internal WorkspaceForm(IReadOnlyList<LauncherCommand> commands)
    {
        Text="PLAYSPACE / Моё пространство";Size=new Size(820,720);MinimumSize=new Size(720,600);var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(28)};ContentHost.Controls.Add(panel);panel.Controls.Add(new SmoothLabel{Text="Всё для твоей библиотеки",Width=680,Height=52,Font=new Font("Segoe UI",22,FontStyle.Bold)});
        foreach(var command in commands){var button=new ModernButton(command.Name,Color.FromArgb(31,35,51)){Width=620,Height=46,Margin=new Padding(0,0,0,8)};button.Click+=(s,e)=>command.Invoke();panel.Controls.Add(button);}panel.Controls.Add(new SmoothLabel{Text="Ctrl+K в главном окне — поиск игр и действий. Настройки сохраняются локально.",Width=620,Height=56});
    }
}
