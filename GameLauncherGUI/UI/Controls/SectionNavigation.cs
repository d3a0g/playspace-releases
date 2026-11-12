namespace GameLauncherGUI;

internal sealed class SectionNavigationState
{
    internal string? Section;
    internal Dictionary<string,int> Scroll=new();
}
internal sealed class SectionNavigation : UserControl
{
    private readonly FlowLayoutPanel navigation=new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8,14,12,8)};
    private readonly Panel content=new(){Dock=DockStyle.Fill,Padding=new Padding(12,0,0,0)};
    private readonly List<(ModernButton Button,Control Page)> pages=new();
    private readonly SectionNavigationState state;
    private string? wanted;
    private int current=-1;
    internal SectionNavigation(SectionNavigationState? state=null)
    {
        this.state=state??new();wanted=this.state.Section;
        Dock=DockStyle.Fill;BackColor=Color.FromArgb(14,17,27);
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(12,0,18,16)};shell.ColumnStyles.Add(new(SizeType.Absolute,230));shell.ColumnStyles.Add(new(SizeType.Percent,100));shell.Controls.Add(navigation,0,0);shell.Controls.Add(content,1,0);Controls.Add(shell);
        navigation.SizeChanged+=(s,e)=>{foreach(var pair in pages)pair.Button.Width=Math.Max(130,navigation.ClientSize.Width-24);};
    }
    internal void Add(string title,Control page)
    {
        int index=pages.Count;var button=new ModernButton(title,Color.FromArgb(31,36,53)){Width=180,Height=46,Margin=new Padding(0,4,0,4),IsNavigation=true,IsSectionNavigation=true,AccessibleName=title,Font=new Font("Segoe UI",10,FontStyle.Bold)};page.Dock=DockStyle.Fill;pages.Add((button,page));navigation.Controls.Add(button);button.Click+=(s,e)=>Select(index);if(index==0||title==wanted)Select(index);
    }
    internal void Select(int index)
    {
        if(index<0||index>=pages.Count)return;SaveCurrent();current=index;state.Section=pages[index].Button.Text;content.Controls.Clear();content.Controls.Add(pages[index].Page);foreach(var pair in pages)pair.Button.IsActive=ReferenceEquals(pair.Page,pages[index].Page);UiTypography.Apply(pages[index].Page);PresentationOptions.Apply(pages[index].Page);UiLayout.Apply(pages[index].Page);if(pages[index].Page is ScrollableControl scroll)scroll.AutoScrollPosition=new(0,state.Scroll.GetValueOrDefault(pages[index].Button.Text));
    }
    private void SaveCurrent(){if(current>=0&&pages[current].Page is ScrollableControl scroll)state.Scroll[pages[current].Button.Text]=-scroll.AutoScrollPosition.Y;}
    protected override void Dispose(bool disposing){if(disposing){SaveCurrent();foreach(var pair in pages)if(!pair.Page.IsDisposed)pair.Page.Dispose();}base.Dispose(disposing);}
}
internal static class SettingsPage
{
    internal static FlowLayoutPanel Create(string title,string subtitle)
    {
        var page=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(12,18,8,16),BackColor=Color.FromArgb(14,17,27)};
        page.Controls.Add(new SmoothLabel{Text=title,Width=660,Height=44,Font=new Font("Segoe UI",22,FontStyle.Bold)});page.Controls.Add(new SmoothLabel{Text=subtitle,Width=640,Height=60,ForeColor=Color.FromArgb(151,165,190)});return page;
    }
    internal static SmoothLabel Label(string text,int height=36)=>new(){Text=text,Width=620,Height=height,ForeColor=Color.FromArgb(176,189,211)};
    internal static ModernButton Action(string text,Action action,bool primary=false){var button=new ModernButton(text,primary?VisualTheme.Accent:Color.FromArgb(31,36,53)){Width=540,Height=42,Margin=new Padding(0,6,0,4)};button.Click+=(s,e)=>action();return button;}
}
