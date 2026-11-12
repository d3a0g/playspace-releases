using System.Runtime.CompilerServices;

namespace GameLauncherGUI;

internal static class PresentationOptions
{
    internal static float TextScale{get;private set;}=1;
    internal static int PosterWidth{get;private set;}=160;
    internal static bool ReduceMotion{get;private set;}
    internal static bool HighContrast{get;private set;}
    private sealed record FontSpec(string Family,float Size,FontStyle Style);
    private static readonly ConditionalWeakTable<Control,FontSpec> fonts=new();
    private static readonly ConditionalWeakTable<Font,object> scaledFonts=new();
    internal static float DrawingScale(Font font)=>scaledFonts.TryGetValue(font,out _)?1:TextScale;
    internal static readonly IReadOnlyDictionary<string,Color> Accents=new Dictionary<string,Color>{{"Фиолетовый",Color.FromArgb(139,135,190)},{"Голубой",Color.FromArgb(109,157,203)},{"Мятный",Color.FromArgb(74,192,164)},{"Розовый",Color.FromArgb(218,112,172)}};
    internal static void Configure(LauncherPreferences preferences){TextScale=Math.Clamp(preferences.TextScalePercent,100,120)/100F;PosterWidth=Math.Clamp(preferences.PosterWidth,160,260);ReduceMotion=preferences.ReduceMotion;HighContrast=preferences.HighContrast;VisualTheme.Accent=Accents.TryGetValue(preferences.AccentName??"",out var color)?color:Accents["Фиолетовый"];}
    internal static void Apply(Control control)
    {
        if(control is Label or TextBox or CheckBox or ListBox or ComboBox or FormattedDescription){var font=fonts.GetValue(control,c=>new(c.Font.FontFamily.Name,c.Font.Size,c.Font.Style));float size=font.Size*TextScale;if(Math.Abs(control.Font.Size-size)>0.01)control.Font=new Font(font.Family,size,font.Style);scaledFonts.GetValue(control.Font,_=>new());}
        foreach(Control child in control.Controls)Apply(child);control.Invalidate();
    }
}
