using System.Reflection;
using System.Runtime.InteropServices;

namespace GameLauncherGUI;

internal static class LauncherShortcuts
{
    internal static string ValidateTarget(string executable)
    {
        if(!Path.IsPathFullyQualified(executable)||executable.StartsWith("\\\\",StringComparison.Ordinal)||!Path.GetFileName(executable).Equals("LauncherPlaySpace.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Запустите LauncherPlaySpace.exe из постоянной распакованной папки. Ярлык для dotnet.exe или временной сборки не создаётся.");
        string path=Path.GetFullPath(executable);if(!File.Exists(path))throw new FileNotFoundException("EXE лаунчера не найден.");return path;
    }
    internal static string Create(string executable,string destination)
    {
        executable=ValidateTarget(executable);destination=Path.GetFullPath(destination);Directory.CreateDirectory(destination);
        // Always create a fresh ordinary shortcut. Never rewrite a downloaded/pinned shortcut,
        // strip Zone.Identifier, or change application-control policies.
        string path=Path.Combine(destination,"LauncherPlaySpace.lnk");int suffix=1;
        while(File.Exists(path)){if(suffix>50)throw new IOException("В этой папке уже много ярлыков PlaySpace. Выберите другую папку или удалите лишние ярлыки вручную.");path=Path.Combine(destination,$"LauncherPlaySpace (локальный {suffix++}).lnk");}
        object? shell=null,shortcut=null;
        try{
            var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Компонент Windows для создания ярлыка недоступен.");shell=Activator.CreateInstance(type)??throw new IOException("Windows не создала объект ярлыка.");
            shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path})??throw new IOException("Не удалось создать ярлык.");
            void Set(string name,string value)=>shortcut.GetType().InvokeMember(name,BindingFlags.SetProperty,null,shortcut,new object[]{value});
            Set("TargetPath",executable);Set("Arguments","");Set("WorkingDirectory",Path.GetDirectoryName(executable)!);Set("IconLocation",executable+",0");Set("Description","LauncherPlaySpace — библиотека игр");
            shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
            if(!File.Exists(path))throw new IOException("Windows не сохранила ярлык.");return path;
        }catch(TargetInvocationException){throw new IOException("Windows не позволила создать ярлык. Проверьте доступ к выбранной папке.");}
        finally{if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
    internal static string CreateForCurrentProcess(bool desktop)
    {
        string executable=Environment.ProcessPath??throw new IOException("Не удалось определить EXE лаунчера.");
        string destination=desktop?Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"LauncherPlaySpace");
        return Create(executable,destination);
    }
}
