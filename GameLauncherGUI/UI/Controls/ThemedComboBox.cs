using System.Drawing.Drawing2D;

namespace GameLauncherGUI;

// Keeps native keyboard navigation, selection events and accessibility without the white system frame.
internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox(){DrawMode=DrawMode.OwnerDrawFixed;FlatStyle=FlatStyle.Flat;IntegralHeight=false;DropDownHeight=260;BackColor=Color.FromArgb(27,33,48);ForeColor=Color.FromArgb(233,236,247);UpdateHeight();}
    private void UpdateHeight(){ItemHeight=Math.Max(24,TextRenderer.MeasureText("Mg",Font).Height+4);}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);UpdateHeight();}
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected=(e.State&DrawItemState.Selected)!=0;using var fill=new SolidBrush(selected?VisualTheme.Blend(BackColor,VisualTheme.Accent,.22f):BackColor);e.Graphics.FillRectangle(fill,e.Bounds);
        string text=e.Index>=0&&e.Index<Items.Count?GetItemText(Items[e.Index]):Items.Count==0?"Нет доступных вариантов":"Выберите…";
        var bounds=e.Bounds;bounds.X+=10;bounds.Width=Math.Max(1,bounds.Width-16);
        SmoothText.Draw(e.Graphics,text,Font,bounds,Enabled?ForeColor:Color.FromArgb(127,137,159),TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);if(message.Msg is not (0x000F or 0x0317 or 0x0318)||Width<8||Height<8)return;
        using var g=message.Msg==0x000F?Graphics.FromHwnd(Handle):Graphics.FromHdc(message.WParam);using var cover=new Pen(BackColor,3);g.DrawRectangle(cover,1,1,Width-3,Height-3);
        using var fill=new SolidBrush(BackColor);g.FillRectangle(fill,Width-27,1,26,Height-2);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var arrow=new Pen(Enabled?ForeColor:Color.FromArgb(127,137,159),1.5f);int x=Width-15,y=Height/2;g.DrawLines(arrow,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});
        using var path=VisualTheme.Rounded(new(0,0,Width-1,Height-1),9);using var border=new Pen(Focused?VisualTheme.Accent:Color.FromArgb(47,54,76));g.DrawPath(border,path);
    }
}
