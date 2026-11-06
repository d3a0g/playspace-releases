using System.Runtime.CompilerServices;

namespace GameLauncherGUI;

internal static class PresentationOptions
{
    internal static float TextScale{get;private set;}=1;
    internal static int PosterWidth{get;private set;}=160;
    internal static bool ReduceMotion{get;private set;}
    private sealed record FontSpec(string Family,float Size,FontStyle Style);
    private static readonly ConditionalWeakTable<Control,FontSpec> fonts=new();
    internal static readonly IReadOnlyDictionary<string,Color> Accents=new Dictionary<string,Color>{{"Фиолетовый",Color.FromArgb(137,115,255)},{"Голубой",Color.FromArgb(77,174,232)},{"Мятный",Color.FromArgb(74,192,164)},{"Розовый",Color.FromArgb(218,112,172)}};
    internal static void Configure(LauncherPreferences preferences){TextScale=Math.Clamp(preferences.TextScalePercent,100,120)/100F;PosterWidth=Math.Clamp(preferences.PosterWidth,160,260);ReduceMotion=preferences.ReduceMotion;VisualTheme.Accent=Accents.TryGetValue(preferences.AccentName??"",out var color)?color:Accents["Фиолетовый"];}
    internal static void Apply(Control control)
    {
        if(control is Label or TextBox or CheckBox or ListBox or ComboBox or FormattedDescription){var font=fonts.GetValue(control,c=>new(c.Font.FontFamily.Name,c.Font.Size,c.Font.Style));float size=font.Size*TextScale;if(Math.Abs(control.Font.Size-size)>0.01)control.Font=new Font(font.Family,size,font.Style);}
        foreach(Control child in control.Controls)Apply(child);control.Invalidate();
    }
}
internal sealed class AppearanceForm : StudioDialog
{
    internal AppearanceForm(string folder,Action refresh)
    {
        var preferences=LauncherPreferences.Load(folder);Text="PLAYSPACE / Вид и доступность";Size=new Size(780,650);MinimumSize=new Size(680,580);
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(28)};ContentHost.Controls.Add(layout);
        void Label(string text)=>layout.Controls.Add(new Label{Text=text,Width=600,Height=34});Label("Цвет акцента");var accent=new ModernSelect{Width=580,Height=42};accent.Items.AddRange(PresentationOptions.Accents.Keys.Cast<object>());accent.SelectedIndex=Math.Max(0,accent.Items.FindIndex(i=>i.ToString()==preferences.AccentName));layout.Controls.Add(accent);
        Label("Размер текста");var scale=new ModernSelect{Width=580,Height=42};scale.Items.AddRange(new object[]{"100%","110%","120%"});scale.SelectedIndex=Math.Clamp((preferences.TextScalePercent-100)/10,0,2);layout.Controls.Add(scale);
        Label("Размер постеров в библиотеке");var cards=new ModernSelect{Width=580,Height=42};cards.Items.AddRange(new object[]{"Компактные","Средние","Крупные"});cards.SelectedIndex=preferences.PosterWidth>=240?2:preferences.PosterWidth>=200?1:0;layout.Controls.Add(cards);
        var motion=new ModernToggle{Text="Уменьшить анимации интерфейса",Checked=preferences.ReduceMotion,Width=600,Height=42};layout.Controls.Add(motion);var offline=new ModernToggle{Text="Офлайн-режим библиотеки",Checked=preferences.OfflineMode,Width=600,Height=42};layout.Controls.Add(offline);
        Label("В офлайн-режиме используются сохранённые обложки, описания и скриншоты. Новые данные не загружаются.");var apply=new ModernButton("Применить",VisualTheme.Accent){Width=280,Height=42};layout.Controls.Add(apply);var status=new Label{Width=600,Height=60};layout.Controls.Add(status);
        apply.Click+=(s,e)=>{try{var next=LauncherPreferences.Load(folder).Copy();next.AccentName=accent.SelectedItem?.ToString()??"Фиолетовый";next.TextScalePercent=100+scale.SelectedIndex*10;next.PosterWidth=new[]{160,200,240}[cards.SelectedIndex];next.ReduceMotion=motion.Checked;next.OfflineMode=offline.Checked;next.Save(folder);PresentationOptions.Configure(next);foreach(Form form in Application.OpenForms)PresentationOptions.Apply(form);refresh();status.Text="Настройки применены. Окна с фиксированными размерами можно увеличить или прокрутить.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text="Не удалось сохранить настройки.";}};
    }
}
