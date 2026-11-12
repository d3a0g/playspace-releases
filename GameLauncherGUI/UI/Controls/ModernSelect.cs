namespace GameLauncherGUI;

// Uses the same rounded buttons and menu palette as the library.
internal sealed class ModernSelect : UserControl
{
    public List<object> Items { get; } = new();
    private int selectedIndex=-1;
    private string placeholder="Сортировка";
    internal void SetPlaceholder(string text){placeholder=text;if(selectedIndex<0)button.Text=placeholder+"  ▾";}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get=>selectedIndex;set {if(selectedIndex==value)return;selectedIndex=value;button.Text=(SelectedItem?.ToString()??placeholder)+"  ▾";SelectedIndexChanged?.Invoke(this,EventArgs.Empty);} }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem=>Items.ElementAtOrDefault(selectedIndex);
    public event EventHandler? SelectedIndexChanged;
    private readonly ModernButton button=new("Сортировка  ▾",Color.FromArgb(31,35,51)){Dock=DockStyle.Fill,Font=new Font("Segoe UI",9)};
    private readonly ContextMenuStrip menu=new(){BackColor=Color.FromArgb(23,28,42),ForeColor=Color.White,Renderer=new ToolStripProfessionalRenderer(new GameMenuColors()),ShowImageMargin=false};
    public ModernSelect(){Height=32;Controls.Add(button);button.Click+=(s,e)=>{menu.Items.Clear();for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem((i==SelectedIndex?"✓  ":"    ")+Items[i]){Padding=new Padding(12,9,12,9)};item.Click+=(s,e)=>SelectedIndex=index;menu.Items.Add(item);}StudioMenus.Show(menu,button);};}
    protected override void Dispose(bool disposing){if(disposing)menu.Dispose();base.Dispose(disposing);}
}
