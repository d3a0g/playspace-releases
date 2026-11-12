namespace GameLauncherGUI;

internal sealed record GraphicsParameter(string Key,string Name,string Description);
internal static class GraphicsParameters
{
    internal static readonly GraphicsParameter[] Unreal={
        new("ViewDistanceQuality","Дальность прорисовки","Дальность отображения объектов."),new("AntiAliasingQuality","Сглаживание","Качество метода сглаживания игры."),new("ShadowQuality","Тени","Детализация и дальность теней."),new("GlobalIlluminationQuality","Глобальное освещение","Поддержка зависит от версии движка и игры."),new("ReflectionQuality","Отражения","Качество отражений в поддерживаемом рендере."),new("PostProcessQuality","Постобработка","Обработка изображения и эффекты камеры."),new("TextureQuality","Текстуры","Потребление видеопамяти и качество текстур."),new("EffectsQuality","Эффекты","Частицы и визуальные эффекты."),new("FoliageQuality","Растительность","Плотность и дальность растительности."),new("ShadingQuality","Шейдинг","Качество материалов и шейдеров.")};
    internal static Dictionary<string,int> Sanitize(Dictionary<string,int>? values)=>values?.Where(p=>Unreal.Any(k=>k.Key==p.Key)&&p.Value is >=0 and <=3).ToDictionary(p=>p.Key,p=>p.Value)??new();
}
