using System.Diagnostics;

namespace GameLauncherGUI;

internal enum EvidenceView { Fps, Rigs, Steam, Sources }
internal sealed record EvidenceGame(string Id,string Name){public override string ToString()=>Name;}
internal sealed class BenchmarkBrowser : UserControl
{
    private readonly EvidenceView view;
    private readonly EvidenceGrid grid;
    private readonly TextBox search=new(){PlaceholderText="Поиск: видеокарта, процессор или игра"};
    private readonly ThemedComboBox gpu=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ThemedComboBox game=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly SmoothLabel detail=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(175,189,214),AutoEllipsis=true};
    private readonly SmoothLabel count=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private bool updating;
    private string context="";
    internal BenchmarkBrowser(EvidenceView view=EvidenceView.Fps)
    {
        this.view=view;Dock=DockStyle.Fill;BackColor=Color.FromArgb(23,28,42);
        grid=view switch{
            EvidenceView.Fps=>new(("gpu","GPU",17),("cpu","CPU стенда",21),("game","Игра",21),("settings","Условия",29),("fps","FPS",12)),
            EvidenceView.Rigs=>new(("gpu","GPU",23),("cpu","CPU",35),("ram","RAM",15),("results","Замеры",12),("share","Steam GPU",15)),
            EvidenceView.Steam=>new(("name","Видеокарта в опросе",75),("share","Доля",25)),
            _=>new(("name","Публикация",40),("info","Что содержит",60))};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(4)};layout.RowStyles.Add(new(SizeType.Absolute,44));layout.RowStyles.Add(new(SizeType.Absolute,44));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,86));layout.RowStyles.Add(new(SizeType.Absolute,42));Controls.Add(layout);
        var filters=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};filters.ColumnStyles.Add(new(SizeType.Percent,50));filters.ColumnStyles.Add(new(SizeType.Percent,50));filters.Controls.Add(gpu,0,0);filters.Controls.Add(game,1,0);layout.Controls.Add(filters,0,0);
        gpu.Items.Add("Все GPU");gpu.Items.AddRange(BenchmarkCatalog.All.Select(r=>r.Gpu).Distinct().OrderBy(s=>s).Cast<object>().ToArray());gpu.SelectedIndex=0;game.Items.Add(new EvidenceGame("","Все игры"));game.Items.AddRange(BenchmarkCatalog.All.GroupBy(r=>r.GameId).OrderBy(g=>g.First().Game).Select(g=>new EvidenceGame(g.Key,g.First().Game)).ToArray());game.SelectedIndex=0;
        var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};searchRow.ColumnStyles.Add(new(SizeType.Percent,100));searchRow.ColumnStyles.Add(new(SizeType.Absolute,150));searchRow.Controls.Add(new ModernSearchField(search){Dock=DockStyle.Fill},0,0);var reset=new ModernButton("Показать всё",Color.FromArgb(31,36,53)){Dock=DockStyle.Fill};searchRow.Controls.Add(reset,1,0);layout.Controls.Add(searchRow,0,1);layout.Controls.Add(grid,0,2);layout.Controls.Add(detail,0,3);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.Absolute,180));footer.Controls.Add(count,0,0);var open=new ModernButton("Источник ↗",Color.FromArgb(31,36,53)){Dock=DockStyle.Fill};footer.Controls.Add(open,1,0);layout.Controls.Add(footer,0,4);
        gpu.SelectedIndexChanged+=(s,e)=>RefreshRows();game.SelectedIndexChanged+=(s,e)=>RefreshRows();search.TextChanged+=(s,e)=>RefreshRows();reset.Click+=(s,e)=>{updating=true;gpu.SelectedIndex=game.SelectedIndex=0;search.Clear();updating=false;RefreshRows();};grid.SelectionChanged+=(s,e)=>DescribeSelection();open.Click+=(s,e)=>OpenSelection();grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)OpenSelection();};
        if(view!=EvidenceView.Fps){game.Visible=false;filters.SetColumnSpan(gpu,2);}if(view is EvidenceView.Steam or EvidenceView.Sources){gpu.Visible=false;game.Visible=false;filters.Controls.Remove(gpu);filters.Controls.Remove(game);filters.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text=view==EvidenceView.Steam?"Steam · октябрь 2026 · анонимная сводная статистика":"Публикации и видеоматериалы · без автоматической отправки данных",AutoEllipsis=true},0,0);filters.SetColumnSpan(filters.Controls[filters.Controls.Count-1],2);}
        SizeChanged+=(s,e)=>{
            bool compact=Height<420;
            layout.RowStyles[0].Height=44;
            layout.RowStyles[1].Height=44;
            layout.RowStyles[3].Height=Height<320?0:compact?56:86;
            detail.Visible=Height>=320;
            layout.RowStyles[4].Height=42;
        };
        RefreshRows();
    }
    internal void SetContext(GameInfo selected,PcSnapshot pc)
    {
        string key=selected.Name+"|"+selected.SteamAppId+"|"+BenchmarkCatalog.GpuKey(pc.Gpu);if(context==key)return;context=key;updating=true;
        string gpuKey=BenchmarkCatalog.GpuKey(pc.Gpu);if(!gpu.Items.Contains(gpuKey))gpu.Items.Add(gpuKey);gpu.SelectedItem=gpuKey;
        string gameId=string.IsNullOrWhiteSpace(selected.SteamAppId)?"local:"+selected.Name:selected.SteamAppId;var choice=game.Items.Cast<EvidenceGame>().FirstOrDefault(g=>g.Id==gameId);if(choice==null){choice=new(gameId,selected.Name);game.Items.Add(choice);}game.SelectedItem=choice;updating=false;RefreshRows();
    }
    private void RefreshRows()
    {
        if(updating||grid.IsDisposed)return;grid.Rows.Clear();string query=search.Text.Trim(),gpuFilter=gpu.SelectedIndex>0?gpu.SelectedItem?.ToString()??"":"",gameFilter=(game.SelectedItem as EvidenceGame)?.Id??"";bool Match(string text)=>query.Length==0||text.Contains(query,StringComparison.OrdinalIgnoreCase);
        void Add(object tag,params object[] values){int row=grid.Rows.Add(values);grid.Rows[row].Tag=tag;foreach(DataGridViewCell cell in grid.Rows[row].Cells)cell.ToolTipText=string.Join(" / ",values);}
        if(view==EvidenceView.Fps){foreach(var r in BenchmarkCatalog.All.Where(r=>(gpuFilter.Length==0||BenchmarkCatalog.GpuKey(r.Gpu)==BenchmarkCatalog.GpuKey(gpuFilter))&&(gameFilter.Length==0||r.GameId==gameFilter)&&Match(r.Gpu+" "+r.Cpu+" "+r.Game+" "+r.Preset)))Add(r,r.Gpu,r.Cpu,r.Game,$"{r.Width}×{r.Height} · {r.Preset}",r.Fps.ToString("0.0"));}
        else if(view==EvidenceView.Rigs){foreach(var r in HardwareEvidence.Rigs.Where(r=>(gpuFilter.Length==0||BenchmarkCatalog.GpuKey(r.Gpu)==BenchmarkCatalog.GpuKey(gpuFilter))&&Match(r.Id)))Add(r,r.Gpu,r.Cpu,"—",r.Results,(HardwareEvidence.SteamShare(r.Gpu) is double share?share.ToString("0.00")+"%":"Нет данных"));}
        else if(view==EvidenceView.Steam){foreach(var r in HardwareEvidence.Steam.Gpus.Where(r=>Match(r.Name)))Add(r,r.Name,r.Share.ToString("0.00")+"%");}
        else{
            foreach(var group in BenchmarkCatalog.All.GroupBy(r=>r.Source).Where(g=>Match(g.First().Gpu+" "+g.First().Publisher+" "+g.Key)))Add(group.Key,group.First().Publisher+" · "+group.First().Gpu,$"{group.Count()} замеров; CPU и условия указаны в результатах");
            foreach(var video in HardwareEvidence.Videos.Where(v=>Match(v.Title+" "+v.Config)))Add(video.Url,"YouTube · "+video.Title,video.Config+"; численные FPS не оцифрованы");
            if(Match("Steam"))Add(HardwareEvidence.Steam.Source,"Steam Hardware Survey","Октябрь 2026 · компоненты, а не связки пользователей");
        }
        count.Text=$"Показано: {grid.Rows.Count}";DescribeSelection();
    }
    private void DescribeSelection()
    {
        object? tag=grid.CurrentRow?.Tag??(grid.Rows.Count>0?grid.Rows[0].Tag:null);detail.Text=tag switch{
            BenchmarkObservation r=>$"{r.Game} · {r.Version} · {r.Gpu} / {r.Cpu}\n{r.Width}×{r.Height} · {r.Preset} · RT: {r.RayTracing} · масштабирование: {r.Upscaling} · FG: {r.FrameGeneration}\n{r.Publisher}. Отдельный опубликованный замер, не прогноз для твоего ПК.",
            ObservedRig r=>$"Реальный тестовый стенд из публикаций: {r.Id}. RAM в использованных игровых строках не указана.\nДоля Steam относится только к GPU; частота использования этой связки CPU/GPU неизвестна.",
            HardwareShare=>$"Steam {HardwareEvidence.Steam.Month}: доля одного компонента. Опрос добровольный и анонимный.\nRAM: 16 ГБ — 41,42%; 32 ГБ — 37,06%. Это отдельная статистика, её нельзя приписывать выбранному GPU.",
            string url=>url,
            _=>"Нет результатов для выбранных фильтров. Нажми «Показать всё». Отсутствие измерений не заменяется расчётным FPS."};
    }
    private void OpenSelection()
    {
        string? source=grid.CurrentRow?.Tag switch{BenchmarkObservation r=>r.Source,ObservedRig r=>r.Source,HardwareShare=>HardwareEvidence.Steam.Source,string url=>url,_=>null};if(source==null)return;
        if(!Uri.TryCreate(source,UriKind.Absolute,out var uri)||uri.Scheme!="https")return;try{Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){MessageBox.Show(FindForm(),"Не удалось открыть браузер.");}
    }
}
