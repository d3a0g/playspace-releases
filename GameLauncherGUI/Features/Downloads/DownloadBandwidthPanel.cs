namespace GameLauncherGUI;

internal sealed class DownloadBandwidthPanel : UserControl
{
    private sealed class SpeedInput : Panel
    {
        private readonly NumericUpDown editor;
        internal SpeedInput(NumericUpDown editor)
        {
            this.editor=editor;DoubleBuffered=true;ResizeRedraw=true;editor.Dock=DockStyle.None;editor.BorderStyle=BorderStyle.None;Controls.Add(editor);
            void Arrange(){editor.SetBounds(12,Math.Max(0,(Height-editor.PreferredSize.Height)/2),Math.Max(1,Width-24),editor.PreferredSize.Height);}
            Resize+=(s,e)=>Arrange();editor.FontChanged+=(s,e)=>Arrange();editor.GotFocus+=(s,e)=>Invalidate();editor.LostFocus+=(s,e)=>Invalidate();Click+=(s,e)=>editor.Focus();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Width<2||Height<2)return;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path=VisualTheme.Rounded(new(0,0,Width-1,Height-1),10);using var fill=new SolidBrush(editor.BackColor);using var pen=new Pen(editor.Focused?VisualTheme.Accent:Color.FromArgb(47,54,76));e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path);
        }
    }
    internal DownloadBandwidthPanel(TransferCenter center)
    {
        Height=185;Dock=DockStyle.Top;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,Padding=new Padding(4)};
        layout.ColumnStyles.Add(new(SizeType.Percent,42));layout.ColumnStyles.Add(new(SizeType.Percent,28));layout.ColumnStyles.Add(new(SizeType.Percent,30));
        layout.RowStyles.Add(new(SizeType.Absolute,34));layout.RowStyles.Add(new(SizeType.Absolute,48));layout.RowStyles.Add(new(SizeType.Percent,100));
        var caption=new SmoothLabel{Text="Скорость загрузок · 0 — без ограничения",Dock=DockStyle.Fill};layout.Controls.Add(caption,0,0);layout.SetColumnSpan(caption,3);
        var amount=new NumericUpDown{Dock=DockStyle.Fill,DecimalPlaces=6,Maximum=80000,AccessibleName="Ограничение скорости загрузок",Value=(decimal)DownloadSpeed.Value(center.RateLimit,center.SpeedUnit)};
        var unit=new ModernSelect{Dock=DockStyle.Fill,AccessibleName="Единица скорости"};unit.Items.AddRange(new object[]{"Мбит/с","Мбайт/с"});unit.SelectedIndex=(int)center.SpeedUnit;
        var save=new ModernButton("Применить",VisualTheme.Accent){Dock=DockStyle.Fill};layout.Controls.Add(new SpeedInput(amount){Dock=DockStyle.Fill},0,1);layout.Controls.Add(unit,1,1);layout.Controls.Add(save,2,1);
        var status=new SmoothLabel{Dock=DockStyle.Fill,Text="Лимит действует на текущие и новые загрузки модов, DLSS и WARP в PLAYSPACE. Steam и другие магазины ограничиваются в собственных настройках."};layout.Controls.Add(status,0,2);layout.SetColumnSpan(status,3);
        var previous=center.SpeedUnit;
        unit.SelectedIndexChanged+=(s,e)=>{long bytes=DownloadSpeed.Bytes(amount.Value,previous);previous=(DownloadSpeedUnit)unit.SelectedIndex;amount.Maximum=previous==DownloadSpeedUnit.Megabits?80000:10000;amount.Value=Math.Clamp((decimal)DownloadSpeed.Value(bytes,previous),0,amount.Maximum);};
        save.Click+=(s,e)=>{try{center.ConfigureBandwidth(DownloadSpeed.Bytes(amount.Value,(DownloadSpeedUnit)unit.SelectedIndex),(DownloadSpeedUnit)unit.SelectedIndex);status.Text=center.RateLimit==0?"Без ограничения · "+DownloadSpeed.Label(center.SpeedUnit):"Лимит: "+DownloadSpeed.Format(center.RateLimit,center.SpeedUnit)+". Применён к текущей загрузке.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){status.Text=ex.Message;}};
        Controls.Add(layout);
    }
}
