using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameLauncherGUI;

internal sealed record NativeGraphicsSetting(string Key,string Name,string Group,string Value,string[] Choices,double? Minimum,double? Maximum,string Type);
internal static class CyberpunkGraphics
{
    private static JObject Parse(string text){var root=JObject.Parse(text,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(root["data"] is not JArray)throw new InvalidDataException("В UserSettings.json нет списка data.");return root;}
    private static IEnumerable<(string Group,JObject Option)> Options(JObject root)
    {
        foreach(var group in root["data"]!.OfType<JObject>()){
            string name=group.Value<string>("group_name")??"";if(name!="/graphics"&&!name.StartsWith("/graphics/",StringComparison.Ordinal)&&name!="/video/display")continue;
            foreach(var option in (group["options"] as JArray??new()).OfType<JObject>())yield return(name,option);
        }
    }
    internal static NativeGraphicsSetting[] Describe(string text)
    {
        var result=new List<NativeGraphicsSetting>();var root=Parse(text);
        foreach(var (group,option) in Options(root)){
            string name=option.Value<string>("name")??"",type=option.Value<string>("type")??"";if(name=="QuickPresets"||name=="OutputMonitor"||name.Length==0)continue;
            string[] choices=type=="bool"?new[]{"false","true"}:(option["values"] as JArray)?.Where(v=>v.Type is JTokenType.String or JTokenType.Integer).Select(v=>v.ToString()).ToArray()??Array.Empty<string>();
            double? min=option.Value<double?>("min_value"),max=option.Value<double?>("max_value");
            if(choices.Length==0&&(type is not("int" or "float")||min==null||max==null||min>max||!double.IsFinite(min.Value)||!double.IsFinite(max.Value)))continue;
            string value=option["value"] is JValue scalar?Convert.ToString(scalar.Value,CultureInfo.InvariantCulture)??"":"";if(type=="bool")value=value.ToLowerInvariant();
            if(result.Count>=100)break;result.Add(new(group+"|"+name,name,group,value,choices,min,max,type));
        }
        if(result.Select(v=>v.Key).Distinct().Count()!=result.Count)throw new InvalidDataException("Повторяющиеся параметры графики.");return result.ToArray();
    }
    internal static string Edit(string text,Dictionary<string,string> changes)
    {
        if(changes.Count>100)throw new InvalidDataException("Слишком много параметров.");var definitions=Describe(text).ToDictionary(p=>p.Key);var root=Parse(text);
        foreach(var (key,value) in changes){
            if(!definitions.TryGetValue(key,out var definition))throw new InvalidDataException("Параметр не поддерживается текущим конфигом: "+key);
            JToken chosen;
            if(definition.Choices.Length>0){int index=Array.IndexOf(definition.Choices,value);if(index<0)throw new InvalidDataException("Недопустимое значение "+definition.Name);chosen=definition.Type=="bool"?new JValue(bool.Parse(value)):definition.Type=="int_list"?new JValue(int.Parse(value,CultureInfo.InvariantCulture)):new JValue(value);}
            else{if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number)||number<definition.Minimum||number>definition.Maximum||definition.Type=="int"&&number!=Math.Truncate(number))throw new InvalidDataException("Значение вне диапазона: "+definition.Name);chosen=definition.Type=="int"?new JValue((int)number):new JValue(number);}
            var match=Options(root).Single(p=>p.Group+"|"+p.Option.Value<string>("name")==key).Option;match["value"]=chosen;if(match["index"]!=null&&definition.Choices.Length>0)match["index"]=Array.IndexOf(definition.Choices,value);
        }
        return root.ToString(Formatting.Indented);
    }
    internal static string Read(string path){ManagedPaths.NoLinks(path);if(!Path.GetFileName(path).Equals("UserSettings.json",StringComparison.OrdinalIgnoreCase)||new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Неверный файл настроек Cyberpunk.");return File.ReadAllText(path);}
    internal static void Apply(string path,Dictionary<string,string> changes)
    {
        string text=Read(path);string updated=Edit(text,changes);ManagedPaths.NoLinks(path+".playspace-backup");if(!File.Exists(path+".playspace-backup"))File.Copy(path,path+".playspace-backup",false);Write(path,System.Text.Encoding.UTF8.GetBytes(updated));
    }
    internal static void Restore(string path){Read(path);ManagedPaths.NoLinks(path+".playspace-backup");if(!File.Exists(path+".playspace-backup"))throw new IOException("Нет резервной копии настроек.");Write(path,File.ReadAllBytes(path+".playspace-backup"));}
    private static void Write(string path,byte[] bytes){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);File.Replace(temp,path,null);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
