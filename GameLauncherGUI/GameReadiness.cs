using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameLauncherGUI;

internal sealed record ReadinessItem(string Name,bool Blocking,string Detail);
internal static class GameReadiness
{
    internal static IReadOnlyList<ReadinessItem> Inspect(GameInfo game,Func<bool> running,string? dataFolder=null)
    {
        var result=new List<ReadinessItem>();bool steam=!string.IsNullOrEmpty(game.SteamAppId);
        if(steam&&(!game.SteamAppId.All(char.IsAsciiDigit)||!uint.TryParse(game.SteamAppId,out uint app)||app==0))result.Add(new("Steam ID",true,"Некорректный идентификатор игры Steam."));
        if(running())result.Add(new("Процесс игры",true,"Игра уже запущена. Закройте её перед повторным запуском или изменением файлов."));
        if(!steam&&!File.Exists(game.ExePath))result.Add(new("Файл запуска",true,"Файл игры не найден. Выберите новый EXE в окне управления игрой."));
        else if(File.Exists(game.ExePath))result.Add(new("Файл запуска",false,"Локальный файл запуска найден."));
        else result.Add(new("Запуск через Steam",false,"Запуск передаётся клиенту Steam. Локальная установка этой проверкой не подтверждается."));
        if(game.Care?.BackupBeforeLaunch==true){try{new GameBackupStore(game.Care,dataFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameLauncherPRO")).Source();result.Add(new("Автокопия",false,"Папка для резервной копии доступна."));}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){result.Add(new("Автокопия",true,"Папка сохранений недоступна или не подходит. Проверьте её в окне управления игрой."));}}
        if(game.Performance?.ApplyOnLaunch==true&&GamePerformanceController.Backend(game)==PerformanceBackend.None)result.Add(new("Профиль производительности",true,"Для выбранного профиля нет адаптера применения. Отключите применение профиля или настройте поддерживаемый вариант."));
        string? folder=File.Exists(game.ExePath)?Path.GetDirectoryName(game.ExePath):game.InstallDirectory;
        if(Directory.Exists(folder))try{long free=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder!))!).AvailableFreeSpace;result.Add(new("Свободное место",false,free<512L*1024*1024?"На диске осталось менее 512 МБ. Освободите место для сохранений и обновлений.":$"На диске доступно {free/1073741824.0:N1} ГБ. Это не оценка требований игры."));}catch(IOException){result.Add(new("Свободное место",false,"Не удалось оценить доступное место на диске."));}
        result.Add(new("Границы проверки",false,"Проверка не подтверждает наличие всех библиотек, совместимость модов, FPS или доступ к серверам."));return result;
    }
    internal static string[] Arguments(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return Array.Empty<string>();if(raw.Length>4096||raw.Contains('\0')||raw.Contains('\r')||raw.Contains('\n'))throw new ArgumentException("Параметры запуска должны быть одной строкой длиной до 4096 символов.");
        IntPtr argv=CommandLineToArgvW("game.exe "+raw,out int count);if(argv==IntPtr.Zero)throw new ArgumentException("Не удалось прочитать параметры запуска.");
        try{return Enumerable.Range(1,count-1).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size))??"").ToArray();}finally{LocalFree(argv);}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CommandLineToArgvW(string command,out int count);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr pointer);
}
internal sealed class GameModeState
{
    private readonly HashSet<string> watched=new(StringComparer.OrdinalIgnoreCase);
    internal void Arm(string identity){watched.Add(identity);}
    internal bool Observe(IEnumerable<string> running,IEnumerable<string> pending)
    {
        bool armed=watched.Count>0;var active=running.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stillPending=pending.ToHashSet(StringComparer.OrdinalIgnoreCase);watched.RemoveWhere(id=>!active.Contains(id)&&!stillPending.Contains(id));
        return armed&&watched.Count==0;
    }
}
