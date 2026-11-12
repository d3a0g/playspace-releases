using System.Reflection;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record ChangeEntry(string Version,string Stage,string[] Changes,string[] KnownIssues)
{
    public override string ToString()=>Version;
    internal string Body=>"ЧТО ИЗМЕНИЛОСЬ\n\n"+string.Join("\n\n",Changes.Select(x=>"• "+x))+"\n\nИЗВЕСТНЫЕ ПРОБЛЕМЫ И ОГРАНИЧЕНИЯ\n\n"+string.Join("\n\n",KnownIssues.Select(x=>"• "+x));
}
internal static class ChangeHistory
{
    internal static string BuildVersion=>typeof(Form1).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??UpdateService.DisplayVersion;
    internal static ChangeEntry[] Load()
    {
        using var stream=typeof(ChangeHistory).Assembly.GetManifestResourceStream("LauncherPlaySpace.Changelog")??throw new InvalidDataException("История изменений не найдена.");using var reader=new StreamReader(stream);
        var entries=JsonConvert.DeserializeObject<ChangeEntry[]>(reader.ReadToEnd())??throw new InvalidDataException("История изменений пуста.");
        if(entries.Length==0||entries.Any(e=>string.IsNullOrWhiteSpace(e.Version)||string.IsNullOrWhiteSpace(e.Stage)||e.Changes==null||e.KnownIssues==null)||entries.Select(e=>e.Version).Distinct().Count()!=entries.Length)throw new InvalidDataException("Некорректная история версий.");
        return entries;
    }
}
internal sealed class WhatsNewPanel : UserControl
{
    internal WhatsNewPanel()
    {
        Dock=DockStyle.Fill;BackColor=Color.FromArgb(14,17,27);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};layout.RowStyles.Add(new(SizeType.Absolute,92));layout.RowStyles.Add(new(SizeType.Absolute,44));layout.RowStyles.Add(new(SizeType.Absolute,40));layout.RowStyles.Add(new(SizeType.Percent,100));Controls.Add(layout);
        layout.Controls.Add(new StudioHeading("Что нового","Изменения по версиям и известные ограничения.","УСТАНОВЛЕНА "+ChangeHistory.BuildVersion){Dock=DockStyle.Fill},0,0);
        var versions=new ThemedComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Версия приложения"};layout.Controls.Add(versions,0,1);
        var stage=new SmoothLabel{Dock=DockStyle.Fill,ForeColor=Color.FromArgb(175,164,229),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};layout.Controls.Add(stage,0,2);
        var card=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(12)};var body=new FormattedDescription{Dock=DockStyle.Fill,AccessibleName="Изменения и известные проблемы"};card.Controls.Add(body);layout.Controls.Add(card,0,3);
        versions.SelectedIndexChanged+=(s,e)=>{if(versions.SelectedItem is ChangeEntry entry){stage.Text=entry.Stage;body.Text=entry.Body;}};
        try{var entries=ChangeHistory.Load();versions.Items.AddRange(entries);int current=Array.FindIndex(entries,e=>e.Version==ChangeHistory.BuildVersion);versions.SelectedIndex=current>=0?current:0;}
        catch(Exception ex)when(ex is IOException or JsonException){body.Text="Не удалось прочитать историю изменений. "+ex.Message;}
    }
}
