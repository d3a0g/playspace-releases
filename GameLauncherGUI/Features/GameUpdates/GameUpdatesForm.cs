namespace GameLauncherGUI;

internal sealed class GameUpdatesForm : StudioDialog
{
    private readonly UiTaskScope tasks;
    internal GameUpdatesForm(GameInfo[] library,string folder,string? selectedId=null,Func<CancellationToken,Task<IReadOnlyList<GameUpdateEntry>>>? reader=null)
    {
        Text="PLAYSPACE / Обновления игр";Size=new(1050,780);MinimumSize=new(860,660);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,Padding=new Padding(24)};
        layout.RowStyles.Add(new(SizeType.Absolute,82));layout.RowStyles.Add(new(SizeType.Percent,55));layout.RowStyles.Add(new(SizeType.Percent,45));layout.RowStyles.Add(new(SizeType.Absolute,52));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text="Обновления вашей библиотеки\nИзвестное состояние установок. Проверка последней версии и загрузка выполняются магазинами.",Font=new Font("Segoe UI",13)},0,0);
        var grid=new CustomCheckListBox{Dock=DockStyle.Fill,ShowCheckBoxes=false,AccessibleName="Состояние обновлений игр"};var detail=new FormattedDescription{Dock=DockStyle.Fill,AccessibleName="Сведения об обновлении игры"};layout.Controls.Add(grid,0,1);layout.Controls.Add(detail,0,2);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new(SizeType.Percent,100F/3));var refresh=new ModernButton("Проверить состояние",VisualTheme.Accent){Dock=DockStyle.Fill};var open=new ModernButton("Открыть клиент",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};var page=new ModernButton("Открыть игру в Steam",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};actions.Controls.Add(refresh,0,0);actions.Controls.Add(open,1,0);actions.Controls.Add(page,2,0);layout.Controls.Add(actions,0,3);
        IReadOnlyList<GameUpdateEntry> entries=Array.Empty<GameUpdateEntry>();
        tasks=new(this,(operation,error)=>{if(!IsDisposed)detail.Text="Не удалось прочитать состояние игр. Повторите проверку.";LauncherSession.Record(folder,operation,error);});
        void Selection(){var selected=entries.ElementAtOrDefault(grid.SelectedIndex);open.Enabled=selected!=null;page.Enabled=selected!=null&&GameUpdates.PageUri(selected)!=null;open.Text=selected?.Client=="Локальная игра"?"Открыть папку":"Открыть "+(selected?.Client??"клиент");detail.Text=selected==null?"Игры не найдены. Сначала добавьте их в библиотеку.":selected.Game.Name+" · "+selected.Label+"\n\n"+selected.Detail;}
        async Task Refresh(CancellationToken token){refresh.Enabled=false;try{entries=await (reader?.Invoke(token)??Task.Run(()=>GameUpdates.Read(library,token:token),token));if(IsDisposed||token.IsCancellationRequested)return;grid.Items.Clear();foreach(var entry in entries)grid.Items.Add(new(entry.Game.Name,false,entry.Client+" · "+entry.Label));grid.ResetView();grid.SelectItem(Math.Max(0,entries.ToList().FindIndex(e=>e.Game.LibraryId==selectedId)));Selection();}finally{if(!IsDisposed)refresh.Enabled=true;}}
        void Open(bool gamePage){var selected=entries.ElementAtOrDefault(grid.SelectedIndex);if(selected==null)return;try{GameUpdates.Open(selected,gamePage);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or Newtonsoft.Json.JsonException or System.ComponentModel.Win32Exception){detail.Text="Не удалось открыть клиент: "+ex.Message;}}
        grid.SelectionChanged+=(s,e)=>Selection();open.Click+=(s,e)=>Open(false);page.Click+=(s,e)=>Open(true);refresh.Click+=async(s,e)=>await tasks.RunAsync("Состояние обновлений игр",Refresh);Shown+=async(s,e)=>await tasks.RunAsync("Состояние обновлений игр",Refresh);
    }
    protected override void Dispose(bool disposing){if(disposing)tasks.Dispose();base.Dispose(disposing);}
}
