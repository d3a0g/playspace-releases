using System.Diagnostics;

namespace GameLauncherGUI;

internal sealed record ModCompatibilityResult(bool CanInstall,string Detail);
internal static class ModCompatibility
{
    internal static ModCompatibilityResult Evaluate(ModPackage package,IReadOnlyList<ModPackage> catalog,ModGameAdapter? adapter,ModState installed,GameInfo? game,string? root)
    {
        if(adapter==null||game==null||root==null)return new(false,"Папка игры не подтверждена. Укажите установленный EXE в библиотеке.");
        if(package.Latest==null)return new(false,"У пакета нет активной версии.");
        string version="не определена";try{if(File.Exists(game.ExePath))version=FileVersionInfo.GetVersionInfo(game.ExePath).FileVersion??version;}catch(Exception ex)when(ex is IOException or ArgumentException){}
        if(File.Exists(Path.Combine(root,".playspace-mods","pending.json")))return new(false,"Есть незавершённая операция с модами. Откройте установленные моды для восстановления перед новой установкой.");
        var supported=package.Latest.SupportedGameVersions??Array.Empty<string>();
        if(supported.Length>0&&!supported.Contains(version,StringComparer.OrdinalIgnoreCase))return new(false,$"Версия EXE: {version}. Автор указал поддержку только: {string.Join(", ",supported)}.");
        try{var plan=ModInstaller.Resolve(package,catalog,adapter,installed);return new(true,$"Версия EXE игры: {version}.\r\n"+(supported.Length==0?"Поддерживаемая версия игры не указана автором. Совместимость не подтверждена.":"Версия EXE соответствует данным пакета.")+ $"\r\nПроверка зависимостей пройдена; новых пакетов: {plan.Count}.\r\nФайловые конфликты проверяются после загрузки, до применения. Игра должна быть закрыта.");}
        catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){return new(false,ex.Message);}
    }
}
