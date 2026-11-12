namespace GameLauncherGUI;

public partial class Form1
{
    private readonly BrowseState libraryBrowse=new();
    private string? lastLibraryView;
    private string LibraryViewKey=>string.Join("|",searchGames.Text,sortGames.SelectedIndex,pinnedOnly,recentOnly,collectionFilter.SelectedItem,listBoxGames.PosterView,librarySource.SelectedIndex);
    private void CompactGameMenu()
    {
        if(gameMenu==null)return;
        var previous=gameMenu.Items.Cast<ToolStripItem>().ToArray();
        var launch=previous.OfType<ToolStripMenuItem>().First(i=>i.Text?.Contains("Играть")==true);
        var profile=previous.OfType<ToolStripMenuItem>().First(i=>i.Text?.Contains("Профиль производительности")==true);
        var folder=previous.OfType<ToolStripMenuItem>().First(i=>i.Text=="Открыть папку игры");
        gameMenu.Items.Clear();launch.Text="Играть";profile.Text="Профиль";folder.Text="Открыть папку";
        var more=new ToolStripMenuItem("⋯ Ещё");more.DropDown.BackColor=panelColor;more.DropDown.ForeColor=textColor;
        foreach(var item in previous){if(item==launch||item==profile||item==folder)continue;more.DropDownItems.Add(item);}
        gameMenu.Items.AddRange(new ToolStripItem[]{launch,profile,folder,more});
    }
}
