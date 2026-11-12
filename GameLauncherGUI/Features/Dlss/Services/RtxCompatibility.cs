using System.Text.RegularExpressions;

namespace GameLauncherGUI;

internal sealed record RtxCompatibility(string Adapter,int Generation)
{
    internal bool SuperResolution=>Generation is >=20 and <=50;
    internal bool IsAmpere=>Generation==30;
    internal bool NativeFrameGeneration=>Generation>=40;
    internal static RtxCompatibility Detect(string adapter)
    {
        int generation=Regex.Matches(adapter??"",@"\bRTX\s*(20|30|40|50)\d{2}\b",RegexOptions.IgnoreCase)
            .Select(m=>int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
        return new(adapter??"Не определена",generation);
    }
    internal string Summary=>SuperResolution
        ? $"{Adapter}\nDLSS 4.5 Super Resolution доступен. "+(Generation<=30
            ? "Модели L/M могут снижать FPS сильнее, чем прежние модели. Генерация кадров здесь — через FSR/OptiScaler; штатная NVIDIA Frame Generation недоступна."
            : "Генерация кадров и масштабирование настраиваются отдельно.")
        : $"{Adapter}\nСовместимая GeForce RTX не определена. Установка режима RTX отключена. Проверь драйвер и доступность дискретной видеокарты.";
}
