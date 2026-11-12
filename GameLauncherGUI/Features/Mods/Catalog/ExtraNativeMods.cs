using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal static class ExtraNativeMods
{
    internal static readonly NativeModAdapter[] All={
        new("550","Left 4 Dead 2 / VPK","vpk","left4dead2/addons"),new("500","Left 4 Dead / VPK","vpk","left4dead/addons"),
        new("440","Team Fortress 2 / VPK","vpk","tf/custom"),new("240","Counter-Strike: Source / VPK","vpk","cstrike/custom"),new("220","Half-Life 2 / VPK","vpk","hl2/custom"),
        new("4000","Garry's Mod / ZIP addons","gmod","garrysmod/addons"),
        new("322330","Don't Starve Together","dontstarve","mods"),new("219740","Don't Starve","dontstarve","mods"),
        new("251570","7 Days to Die","7days","7DaysToDie/Mods","appdata"),
        new("261550","Mount & Blade II: Bannerlord","bannerlord","Modules"),
        new("8930","Civilization V","civ","My Games/Sid Meier's Civilization 5/MODS","documents"),
        new("289070","Civilization VI","civ","My Games/Sid Meier's Civilization VI/Mods","documents"),
        new("1142710","Total War: WARHAMMER III / PACK","pack","data"),
        new("262060","Darkest Dungeon","darkest","mods")
    };
    internal static string? Hint(string layout)=>layout switch{
        "vpk"=>"ZIP с VPK-аддонами. Активация — в игре. Серверы с sv_pure могут запрещать изменённый контент; скрипты SourceMod и DLL-плагины этим адаптером не устанавливаются.",
        "gmod"=>"ZIP одного распакованного аддона с addon.json. GMA и серверные DLL этим импортом не поддерживаются.",
        "dontstarve"=>"ZIP одного мода с modinfo.lua. Включите его в меню модов; для кооператива проверьте совместимость и необходимость установки у других игроков.",
        "7days"=>"ZIP одного модлета с ModInfo.xml. Импорт в пользовательскую папку Mods. Проверьте версию игры; кодовые моды могут требовать запуск без EAC, лаунчер не меняет античит.",
        "bannerlord"=>"ZIP одного модуля с SubModule.xml. Папка модуля создаётся по его Id. Включите мод и зависимости в официальном лаунчере; DLL могут требовать отдельной проверки доверия Windows.",
        "civ"=>"ZIP одного мода с .modinfo. Включите его в меню дополнительных материалов игры. Зависимости и совместимость сохранений проверяются отдельно.",
        "pack"=>"ZIP с PACK-файлами Total War. Включите мод через менеджер игры; порядок и совместимость версий проверяются отдельно. Movie-пакеты могут загружаться автоматически.",
        "darkest"=>"ZIP одного мода с project.xml. Включите мод для нужного сохранения в меню игры; совместимость кампании и порядок не меняются автоматически.",_=>null};
    internal static (string Prefix,string Folder)? Structure(string layout,ZipArchiveEntry[] files)
    {
        string? marker=layout switch{"gmod"=>"addon.json","dontstarve"=>"modinfo.lua","7days"=>"ModInfo.xml","bannerlord"=>"SubModule.xml","civ"=>".modinfo","darkest"=>"project.xml",_=>null};
        if(marker==null)return null;
        var matches=files.Where(e=>layout=="civ"?e.Name.EndsWith(marker,StringComparison.OrdinalIgnoreCase):e.Name.Equals(marker,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Нужен ZIP одного мода с единственным "+marker);
        var entry=matches[0];if(entry.Length>512*1024)throw new InvalidDataException("Метаданные мода слишком большие.");string folder="";
        using(var input=entry.Open()){
            if(layout=="gmod"){using var reader=new StreamReader(input);if(string.IsNullOrWhiteSpace(JObject.Parse(reader.ReadToEnd()).Value<string>("title")))throw new InvalidDataException("В addon.json нет title.");}
            else if(layout!="dontstarve"){
                using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=512*1024});var xml=XDocument.Load(reader);
                string expected=layout switch{"7days"=>"ModInfo","bannerlord"=>"Module","civ"=>"Mod",_=>"project"};
                bool current7Days=layout=="7days"&&xml.Root?.Name.LocalName.Equals("xml",StringComparison.OrdinalIgnoreCase)==true&&xml.Root.Descendants().Any(e=>e.Name.LocalName=="Name"&&e.Attribute("value")!=null);
                if(xml.Root==null||!current7Days&&!xml.Root.Name.LocalName.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Неверная структура "+marker);
                if(layout=="bannerlord"){folder=xml.Root.Elements().FirstOrDefault(e=>e.Name.LocalName=="Id")?.Attribute("value")?.Value??"";if(!System.Text.RegularExpressions.Regex.IsMatch(folder,@"^[A-Za-z0-9_-]{1,100}$"))throw new InvalidDataException("Некорректный Id Bannerlord-модуля.");}
            }
        }
        string path=entry.FullName.Replace('\\','/');return(path[..(path.LastIndexOf('/')+1)],folder);
    }
    internal static bool IsArchiveFile(string layout,ZipArchiveEntry entry,ZipArchiveEntry[] files)
    {
        string extension=layout=="vpk"?".vpk":".pack";if(!entry.Name.EndsWith(extension,StringComparison.OrdinalIgnoreCase))return false;
        using var input=entry.Open();byte[] header=new byte[12];int count=input.ReadAtLeast(header,Math.Min(12,(int)Math.Min(entry.Length,12)),false);
        if(layout=="pack"){if(count<8||header[0]!='P'||header[1]!='F'||header[2]!='H'||header[3]<'0'||header[3]>'9')throw new InvalidDataException("Неверный заголовок PACK.");}
        else if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Name,@"_\d{3}\.vpk$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){
            if(count<12||BitConverter.ToUInt32(header)!=0x55AA1234||BitConverter.ToUInt32(header,4) is not (1 or 2))throw new InvalidDataException("Неверный заголовок VPK.");
        }else{string index=System.Text.RegularExpressions.Regex.Replace(entry.Name,@"_\d{3}\.vpk$","_dir.vpk",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(!files.Any(e=>e.Name.Equals(index,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Для частей VPK нужен соответствующий _dir.vpk.");}
        return true;
    }
}
