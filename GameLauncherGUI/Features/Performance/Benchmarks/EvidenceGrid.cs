using System.Drawing.Text;

namespace GameLauncherGUI;

internal sealed class EvidenceGrid : DataGridView
{
    internal EvidenceGrid(params(string Key,string Caption,int Weight)[] columns)
    {
        DoubleBuffered=true;Dock=DockStyle.Fill;ReadOnly=true;AllowUserToAddRows=AllowUserToDeleteRows=AllowUserToResizeRows=false;RowHeadersVisible=false;MultiSelect=false;SelectionMode=DataGridViewSelectionMode.FullRowSelect;BorderStyle=BorderStyle.None;BackgroundColor=Color.FromArgb(23,28,42);GridColor=Color.FromArgb(41,48,67);EnableHeadersVisualStyles=false;Font=new Font("Segoe UI",10);
        ColumnHeadersDefaultCellStyle=new(){BackColor=Color.FromArgb(31,38,55),ForeColor=Color.FromArgb(192,203,226),Font=new Font("Segoe UI",9,FontStyle.Bold)};DefaultCellStyle=new(){BackColor=BackgroundColor,ForeColor=Color.FromArgb(223,229,242),SelectionBackColor=Color.FromArgb(54,49,86),SelectionForeColor=Color.White,Padding=new Padding(8,0,8,0)};AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(26,32,46);RowTemplate.Height=38;ColumnHeadersHeight=38;AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;ScrollBars=ScrollBars.Vertical;TabStop=true;
        foreach(var column in columns)Columns.Add(new DataGridViewTextBoxColumn{Name=column.Key,HeaderText=column.Caption,FillWeight=column.Weight,MinimumWidth=40,SortMode=DataGridViewColumnSortMode.Automatic});
    }
    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if(e.ColumnIndex<0){base.OnCellPainting(e);return;}
        e.Paint(e.ClipBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground|DataGridViewPaintParts.Border);var bounds=e.CellBounds;bounds.Inflate(-8,-3);e.Graphics!.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        SmoothText.Draw(e.Graphics,e.FormattedValue?.ToString(),e.CellStyle!.Font??Font,bounds,e.RowIndex<0?ColumnHeadersDefaultCellStyle.ForeColor:(e.State&DataGridViewElementStates.Selected)!=0?Color.White:DefaultCellStyle.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        e.Handled=true;
    }
}
