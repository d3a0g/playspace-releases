namespace GameLauncherGUI;

internal sealed class AppearanceForm : StudioDialog
{
    internal AppearanceForm(string folder,Action refresh){Text="PLAYSPACE / Вид и доступность";Size=new(850,760);MinimumSize=new(740,640);ContentHost.Controls.Add(new AppearanceSettingsPanel(folder,refresh));}
}
internal sealed class AppearanceSettingsPanel : UserControl
{
    internal AppearanceSettingsPanel(string folder,Action refresh)
    {
        Dock=DockStyle.Fill;BackColor=Color.FromArgb(14,17,27);var page=SettingsPage.Create("Оформление","Цвет, читаемость и поведение библиотеки.");Controls.Add(page);var preferences=LauncherPreferences.Load(folder);
        void Caption(string text)=>page.Controls.Add(SettingsPage.Label(text,30));
        Caption("Цвет акцента");var accent=new ModernSelect{Width=540,Height=40};accent.Items.AddRange(PresentationOptions.Accents.Keys.Cast<object>());accent.SelectedIndex=Math.Max(0,accent.Items.FindIndex(i=>i.ToString()==preferences.AccentName));page.Controls.Add(accent);
        Caption("Размер текста");var scale=new ModernSelect{Width=540,Height=40};scale.Items.AddRange(new object[]{"100%","110%","120%"});scale.SelectedIndex=Math.Clamp((preferences.TextScalePercent-100)/10,0,2);page.Controls.Add(scale);
        Caption("Размер постеров");var cards=new ModernSelect{Width=540,Height=40};cards.Items.AddRange(new object[]{"Компактные","Средние","Крупные"});cards.SelectedIndex=preferences.PosterWidth>=240?2:preferences.PosterWidth>=200?1:0;page.Controls.Add(cards);
        var motion=new ModernToggle{Text="Уменьшить анимации",Checked=preferences.ReduceMotion,Width=540,Height=42};var offline=new ModernToggle{Text="Офлайн-режим библиотеки",Checked=preferences.OfflineMode,Width=540,Height=42};var contrast=new ModernToggle{Text="Яркий текст повышенной контрастности",Checked=preferences.HighContrast,Width=540,Height=42};page.Controls.Add(motion);page.Controls.Add(contrast);page.Controls.Add(offline);page.Controls.Add(SettingsPage.Label("Клавиатура: Tab — переход, Enter/Пробел — действие, Home/End и PageUp/PageDown — список, Ctrl+D — закрепить игру, Alt+Enter — профиль игры, Shift+F10 — меню, Escape — назад.",100));page.Controls.Add(SettingsPage.Label("В офлайн-режиме доступны сохранённые обложки и описания. Новые данные не загружаются.",64));var status=SettingsPage.Label("",55);
        page.Controls.Add(SettingsPage.Action("Применить оформление",()=>{try{var next=LauncherPreferences.Load(folder).Copy();next.AccentName=accent.SelectedItem?.ToString()??"Фиолетовый";next.TextScalePercent=100+scale.SelectedIndex*10;next.PosterWidth=new[]{160,200,240}[cards.SelectedIndex];next.ReduceMotion=motion.Checked;next.HighContrast=contrast.Checked;next.OfflineMode=offline.Checked;next.Save(folder);PresentationOptions.Configure(next);foreach(Form form in Application.OpenForms){PresentationOptions.Apply(form);UiLayout.Apply(form);}refresh();status.Text="Настройки применены.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text=ex.Message;}},true));page.Controls.Add(status);
    }
}
