namespace GameLauncherGUI;

internal sealed class CategoryRail : Panel
{
    private int offset;
    private int Maximum=>Math.Max(0,Controls.Count*46-ClientSize.Height);
    public CategoryRail(){DoubleBuffered=true;ResizeRedraw=true;AutoScroll=false;}
    protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);e.Control!.GotFocus+=(s,args)=>{if(e.Control.Top<0)offset=Math.Max(0,offset+e.Control.Top);else if(e.Control.Bottom>Height)offset=Math.Min(Maximum,offset+e.Control.Bottom-Height);Arrange();};Arrange();}
    protected override void OnResize(EventArgs e){base.OnResize(e);Arrange();}
    private void Arrange(){offset=Math.Clamp(offset,0,Maximum);for(int i=0;i<Controls.Count;i++)Controls[i].SetBounds(0,i*46-offset,Math.Max(1,ClientSize.Width-18),40);Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);offset=Math.Clamp(offset-e.Delta,0,Maximum);Arrange();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Maximum<=0)return;using var pen=new Pen(Color.FromArgb(72,65,105),3){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};float height=Math.Max(30,Height*Height/(float)(Controls.Count*46));float top=offset/(float)Maximum*(Height-height);e.Graphics.DrawLine(pen,Width-6,top+2,Width-6,top+height-2);}
}
