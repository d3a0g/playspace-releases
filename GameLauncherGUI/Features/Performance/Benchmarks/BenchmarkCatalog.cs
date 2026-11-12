using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GameLauncherGUI;

internal sealed record BenchmarkObservation(string GameId,string Game,string Gpu,string Cpu,string Version,string Preset,int Width,int Height,double Fps,string RayTracing,string Upscaling,string FrameGeneration,string Method,string Publisher,string Source,string Observation);
internal sealed record BenchmarkGroup(BenchmarkObservation[] Samples)
{
    internal double Mean=>Samples.Average(s=>s.Fps);
    internal double Minimum=>Samples.Min(s=>s.Fps);
    internal double Maximum=>Samples.Max(s=>s.Fps);
    internal bool MixedCpu=>Samples.Select(s=>s.Cpu).Distinct().Count()>1;
}
internal static class BenchmarkCatalog
{
    private static readonly Lazy<BenchmarkObservation[]> catalog=new(()=>{
        using var stream=typeof(BenchmarkCatalog).Assembly.GetManifestResourceStream("LauncherPlaySpace.Benchmarks")??throw new IOException("Нет базы тестов.");using var reader=new StreamReader(stream);var records=JsonConvert.DeserializeObject<BenchmarkObservation[]>(reader.ReadToEnd())??Array.Empty<BenchmarkObservation>();
        if(records.Any(r=>r.Fps<=0||!double.IsFinite(r.Fps)||r.Width<640||r.Height<480||!Uri.TryCreate(r.Source,UriKind.Absolute,out var uri)||uri.Scheme!="https"))throw new InvalidDataException("Некорректная база тестов.");return records;
    });
    internal static BenchmarkObservation[] All=>catalog.Value;
    internal static string GpuKey(string raw)
    {
        if(raw.Contains("Laptop",StringComparison.OrdinalIgnoreCase)||raw.Contains("Mobile",StringComparison.OrdinalIgnoreCase))return "laptop";
        var matches=Regex.Matches(raw,@"\b(RTX|GTX|RX)\s*(\d{3,4})\s*(Ti|SUPER|XT)?\b",RegexOptions.IgnoreCase);if(matches.Count>1)return "multiple";var match=matches.FirstOrDefault();return match!=null?string.Join(" ",new[]{match.Groups[1].Value,match.Groups[2].Value,match.Groups[3].Value}.Where(v=>v.Length>0)).ToUpperInvariant():raw.Trim();
    }
    internal static BenchmarkGroup[] Group(IEnumerable<BenchmarkObservation> samples)=>samples.GroupBy(s=>(s.GameId,s.Gpu,s.Version,s.Preset,s.Width,s.Height,s.RayTracing,s.Upscaling,s.FrameGeneration,s.Method))
        .Select(group=>new BenchmarkGroup(group.GroupBy(s=>(s.Publisher,s.Observation)).Select(g=>g.First()).ToArray())).ToArray();
    internal static string Summary(GameInfo game,PcSnapshot pc,PerformanceProfile profile)
    {
        string key=GpuKey(pc.Gpu);int width=profile.ResolutionWidth==0?pc.Width:profile.ResolutionWidth,height=profile.ResolutionHeight==0?pc.Height:profile.ResolutionHeight;
        var samples=All.Where(s=>s.GameId==game.SteamAppId&&s.Gpu==key&&s.Width==width&&s.Height==height).ToArray();
        var lines=new List<string>{"ВНЕШНИЕ ТЕСТЫ · СПРАВОЧНЫЕ ДАННЫЕ",$"Твой ПК: {pc.Cpu} / {pc.Gpu}",$"Выбрано: {width} × {height}. Замер на этом ПК: "+(profile.MeasuredFps>0?profile.MeasuredFps+" FPS":"не введён")};
        lines.Add(GamePerformanceController.Backend(game)==PerformanceBackend.Cyberpunk?"Индивидуальные настройки игры могут отличаться от пресетов тестов ниже.":$"В профиле: {profile.Quality}, рендер {profile.RenderScale}%. Ниже оригинальные пресеты публикаций — это не расчёт FPS для выбранного сочетания настроек.");
        if(samples.Length==0)lines.Add("Для этой игры, GPU и разрешения проверяемых результатов в текущей базе нет. Лаунчер не подставляет выдуманный прогноз. Открой источники или введи свой средний FPS слева.");
        foreach(var group in Group(samples)){
            var first=group.Samples[0];lines.Add($"{first.Game} · {first.Version}\n{first.Preset} · {first.Width} × {first.Height}\n"+(group.Samples.Length>1?$"Среднее {group.Mean:0.0} FPS · диапазон {group.Minimum:0.0}–{group.Maximum:0.0} · {group.Samples.Length} замера":"Один опубликованный замер: "+first.Fps.ToString("0.0")+" FPS")+"\nCPU тестов: "+string.Join(" / ",group.Samples.Select(s=>s.Cpu).Distinct())+(group.MixedCpu?"\nРазные CPU: это ориентир для GPU, а не оценка твоей системы.":"")+"\nRT: "+first.RayTracing+" · масштабирование: "+first.Upscaling+" · FG: "+first.FrameGeneration+"\nИсточник: "+first.Publisher);
        }
        lines.Add("Сопоставляются одна версия игры, пресет, разрешение, режимы RT/масштабирования/FG и методика. Публикация и её видеоверсия не считаются независимыми тестами. Неуказанные условия не считаются подтверждёнными.");
        lines.Add("Исторические результаты могут отличаться от текущей версии игры, драйвера, сцены и настроек. Целевой FPS — пожелание; среднее из внешних тестов не гарантирует такой FPS на твоём ПК. Мобильные GPU и Ti/XT не приравниваются к обычной версии.");return string.Join("\n\n",lines);
    }
}
internal sealed class BenchmarkSourcesForm : StudioDialog
{
    internal BenchmarkSourcesForm()
    {
        Text="PLAYSPACE / Источники и методика";Size=new(1180,820);MinimumSize=new(980,720);
        var sections=new SectionNavigation();ContentHost.Controls.Add(sections);
        sections.Add("Конфигурации ПК",new BenchmarkBrowser(EvidenceView.Rigs));
        sections.Add("Замеры FPS",new BenchmarkBrowser());
        sections.Add("Статистика Steam",new BenchmarkBrowser(EvidenceView.Steam));
        sections.Add("Обзоры и видео",new BenchmarkBrowser(EvidenceView.Sources));
        var method=SettingsPage.Create("Как читать результаты","Опубликованные измерения и статистика оборудования решают разные задачи.");
        method.Controls.Add(SettingsPage.Label($"В базе: {BenchmarkCatalog.All.Length} замеров, {HardwareEvidence.Rigs.Length} сочетаний CPU/GPU, {BenchmarkCatalog.All.Select(r=>r.GameId).Distinct().Count()} игр. RAM стенда остаётся неизвестной, если её нет в использованной таблице.",100));
        method.Controls.Add(SettingsPage.Label("Steam Hardware Survey — добровольная анонимная статистика отдельных компонентов за октябрь 2026. Steam не публикует здесь связанные конфигурации CPU/GPU/RAM или сведения о конкретных игроках. Частота GPU не является частотой всей конфигурации.",130));
        method.Controls.Add(SettingsPage.Label("Численные FPS взяты из таблиц первичных обзоров Notebookcheck и GamesRadar. Видео YouTube доступны для проверки методики; значения из недоступных видеокадров не выдумываются и не добавляются повторно как независимые тесты.",120));
        method.Controls.Add(SettingsPage.Label("Разные версии, разрешения, пресеты, RT, масштабирование и генерация кадров не объединяются. Неуказанные условия остаются неизвестными. Исторические замеры не прогнозируют FPS на вашем ПК; даже одинаковый GPU не означает одинаковый CPU, драйвер и сцену.",130));
        sections.Add("Методика",method);
    }
}