namespace GameLauncherGUI;

internal sealed class HomeSettingsPanel : UserControl
{
    internal HomeSettingsPanel(string folder, Action refresh)
    {
        Dock=DockStyle.Fill; var page=SettingsPage.Create("Главная страница", "Выберите начальный раздел и вид библиотеки. Все игры останутся доступны через меню."); Controls.Add(page);
        var value=LauncherPreferences.Load(folder);
        var section=new ModernSelect{Width=540,Height=44,AccessibleName="Начальный раздел"};section.Items.AddRange(new object[]{"Все игры","Закреплённые","Недавние"});section.SelectedIndex=Math.Max(0,section.Items.FindIndex(x=>x.ToString()==value.HomeLibrary));
        var sort=new ModernSelect{Width=540,Height=44,AccessibleName="Начальная сортировка"};sort.Items.AddRange(new object[]{"Порядок добавления","Название","Последние запуски","Время игры"});sort.SelectedIndex=Math.Clamp(value.HomeSort,0,3);
        var banner=new ModernToggle{Width=540,Height=44,Text="Показывать большой баннер игры",Checked=value.HomeBanner};
        var posters=new ModernToggle{Width=540,Height=44,Text="Открывать библиотеку сеткой постеров",Checked=value.HomePosters};
        var personal=new ModernToggle{Width=540,Height=44,Text="Персональная подборка «Для вас»",Checked=value.PersonalRecommendations};
        page.Controls.Add(SettingsPage.Label("При открытии лаунчера"));page.Controls.Add(section);page.Controls.Add(SettingsPage.Label("Сортировка"));page.Controls.Add(sort);page.Controls.Add(banner);page.Controls.Add(posters);page.Controls.Add(personal);
        page.Controls.Add(SettingsPage.Label("Подборка использует закрепления, жанры и время игры только на этом ПК. Она не прогнозирует FPS и не отправляет историю сторонним сервисам.",100));
        var status=SettingsPage.Label("",60);
        page.Controls.Add(SettingsPage.Action("Сохранить главную страницу",()=>{try{var next=LauncherPreferences.Load(folder);next.HomeLibrary=section.SelectedItem!.ToString()!;next.HomeSort=sort.SelectedIndex;next.HomeBanner=banner.Checked;next.HomePosters=posters.Checked;next.PersonalRecommendations=personal.Checked;next.Save(folder);refresh();status.Text="Сохранено. Оформление главной страницы применено.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text=ex.Message;}},true));page.Controls.Add(status);
    }
}

internal sealed class HomeSettingsForm : StudioDialog
{
    internal HomeSettingsForm(string folder,Action refresh){Text="PLAYSPACE / Главная страница";Size=new(850,760);MinimumSize=new(740,640);ContentHost.Controls.Add(new HomeSettingsPanel(folder,refresh));}
}
