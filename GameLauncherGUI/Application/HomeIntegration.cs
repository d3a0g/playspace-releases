namespace GameLauncherGUI;

public partial class Form1
{
    private void ApplyHomePreferences()
    {
        pinnedOnly=preferences.HomeLibrary=="Закреплённые";recentOnly=preferences.HomeLibrary=="Недавние";
        listBoxGames.PosterView=preferences.HomePosters;sortGames.SelectedIndex=Math.Clamp(preferences.HomeSort,0,3);
        hero.Visible=preferences.HomeBanner;
        if(hero.Parent is TableLayoutPanel panel)panel.RowStyles[0].Height=preferences.HomeBanner?(hero.Compact?64:122):0;
    }
}
