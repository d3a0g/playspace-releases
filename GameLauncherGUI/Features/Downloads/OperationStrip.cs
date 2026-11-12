namespace GameLauncherGUI;

internal sealed class OperationStrip : UserControl
{
    private readonly System.Windows.Forms.Timer timer=new(){Interval=500};
    private readonly SmoothLabel label=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill,Maximum=1000,Height=3,Style=ProgressBarStyle.Continuous};
    private readonly TransferCenter center;
    private bool active;
    internal event Action<bool>? ActivityChanged;
    internal event Action? OpenRequested;
    internal OperationStrip(TransferCenter center)
    {
        this.center=center;Dock=DockStyle.Fill;Height=46;Visible=false;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.ColumnStyles.Add(new(SizeType.Absolute,112));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,4));
        var open=new ModernButton("Загрузки",Color.FromArgb(31,36,53)){Dock=DockStyle.Fill,Margin=new Padding(8,0,0,4)};open.Click+=(s,e)=>OpenRequested?.Invoke();layout.Controls.Add(label,0,0);layout.Controls.Add(open,1,0);layout.Controls.Add(progress,0,1);layout.SetColumnSpan(progress,2);Controls.Add(layout);
        timer.Tick+=(s,e)=>RefreshOperation();HandleCreated+=(s,e)=>RefreshOperation();timer.Start();
    }
    internal void RefreshOperation()
    {
        var entries=center.Items();var running=entries.FirstOrDefault(j=>j.State==TransferState.Running);int queued=entries.Count(j=>j.State==TransferState.Queued);
        bool visible=running!=null||queued>0;
        if(active!=visible){active=visible;Visible=visible;ActivityChanged?.Invoke(visible);}
        if(!visible)return;
        if(running==null){label.Text=$"В очереди: {queued} · ожидаем загрузку";progress.Value=0;return;}
        int amount=running.Total>0?(int)Math.Clamp(running.Received/(double)running.Total.Value*1000,0,1000):0;progress.Value=amount;
        label.Text=running.Title+" · "+(running.Total>0?$"{amount/10.0:N0}% · ":"")+DownloadSpeed.Format(running.BytesPerSecond,center.SpeedUnit)+(queued>0?$" · в очереди {queued}":"");
        label.AccessibleName=label.Text;
    }
    protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();}base.Dispose(disposing);}
}
