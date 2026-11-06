using System.Diagnostics;
using System.Text;

namespace GameLauncherGUI;

internal sealed class NetworkDiagnosticsForm : StudioDialog
{
    private sealed class DisplayProgress(Action<DiagnosticCheck> display) : IProgress<DiagnosticCheck>
    {
        public void Report(DiagnosticCheck value)=>display(value);
    }
    private readonly ModernSelect target=new(){Dock=DockStyle.Fill};
    private readonly ModernButton run=new("Начать проверку",VisualTheme.Accent){Dock=DockStyle.Fill};
    private readonly ModernButton cancel=new("Остановить",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill,Enabled=false};
    private readonly ModernButton report=new("Просмотреть отчёт",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill,Enabled=false};
    private readonly Label summary=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(172,181,207),Text="Выберите сервис и начните проверку. Подключение и настройки ПК не изменятся."};
    private readonly FormattedDescription results=new(){Dock=DockStyle.Fill,Text="Здесь появятся результаты: подключение ПК, DNS, TCP 443, HTTPS и состояние WARP.\n\nПроверяется публичный адрес выбранного сервиса. Проверки не подтверждают вход в аккаунт, работу матчмейкинга, UDP и игровых серверов.\n\nСетевые запросы начнутся только после нажатия «Начать проверку»."};
    private readonly Func<DiagnosticTarget,IProgress<DiagnosticCheck>,CancellationToken,Task<NetworkDiagnosticReport>> diagnose;
    private CancellationTokenSource? attempt;private NetworkDiagnosticReport? last;private bool busy,disposed;private int generation;
    internal NetworkDiagnosticsForm(Func<DiagnosticTarget,IProgress<DiagnosticCheck>,CancellationToken,Task<NetworkDiagnosticReport>>? diagnose=null)
    {
        var service=new NetworkDiagnostics();this.diagnose=diagnose??((t,p,c)=>service.Run(t,p,c));
        Text="PLAYSPACE / Диагностика подключения";Size=new Size(980,800);MinimumSize=new Size(820,680);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(28)};
        foreach(float height in new[]{84F,46F,58F})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new StudioHeading("Почему не подключается?","Проверки сети и понятный отчёт для поддержки","ДИАГНОСТИКА / БЕЗ ИЗМЕНЕНИЯ НАСТРОЕК"){Dock=DockStyle.Fill},0,0);
        target.Items.AddRange(DiagnosticTarget.All.Cast<object>().ToArray());target.SelectedIndex=0;layout.Controls.Add(target,0,1);layout.Controls.Add(summary,0,2);
        var card=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(16),Margin=new Padding(0,4,0,12)};card.Controls.Add(results);layout.Controls.Add(card,0,3);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};foreach(int _ in Enumerable.Range(0,3))actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.Controls.Add(run,0,0);actions.Controls.Add(cancel,1,0);actions.Controls.Add(report,2,0);layout.Controls.Add(actions,0,4);
        var back=new ModernButton("← Вернуться к управлению WARP",Color.FromArgb(28,34,49)){Dock=DockStyle.Fill};back.Click+=(s,e)=>Close();layout.Controls.Add(back,0,5);
        run.Click+=async(s,e)=>await RunChecks();cancel.Click+=(s,e)=>attempt?.Cancel();
        report.Click+=(s,e)=>{if(last!=null){using var preview=new DiagnosticReportForm(last.ToSafeText());preview.ShowDialog(this);}};
        target.SelectedIndexChanged+=(s,e)=>{if(!busy){last=null;report.Enabled=false;summary.Text="Сервис изменён. Запустите новую проверку.";results.Text="Проверки ещё не запускались для выбранного сервиса.";}};
        FormClosing+=(s,e)=>attempt?.Cancel();
    }
    private async Task RunChecks()
    {
        if(busy||disposed||target.SelectedIndex<0)return;busy=true;last=null;report.Enabled=false;run.Enabled=false;cancel.Enabled=true;target.Enabled=false;
        attempt?.Dispose();attempt=new CancellationTokenSource();var token=attempt.Token;int version=++generation;var checks=new List<DiagnosticCheck>();results.Text="Запускаем проверки…";summary.Text="Проверяем выбранный сервис. Это может занять до минуты.";
        void Display(DiagnosticCheck check){if(!disposed&&busy&&version==generation&&!token.IsCancellationRequested){checks.Add(check);results.Text=NetworkDiagnosticReport.DisplayChecks(checks);summary.Text=$"Выполнено проверок: {checks.Count} / 7";}}
        var progress=new DisplayProgress(check=>{
            if(disposed||!IsHandleCreated)return;
            if(InvokeRequired){try{BeginInvoke(new Action(()=>Display(check)));}catch(InvalidOperationException)when(disposed||IsDisposed||Disposing){}}
            else Display(check);
        });
        try{var completed=await diagnose(DiagnosticTarget.All[target.SelectedIndex],progress,token);token.ThrowIfCancellationRequested();if(!disposed){last=completed;results.Text=NetworkDiagnosticReport.DisplayChecks(completed.Checks);summary.Text=completed.Summary;}}
        catch(OperationCanceledException){if(!disposed){last=null;summary.Text="Диагностика остановлена. Неполный отчёт не сохраняется; можно повторить проверку.";}}
        catch(Exception ex)when(ex is IOException or InvalidDataException or System.Net.Http.HttpRequestException or System.Net.NetworkInformation.NetworkInformationException or InvalidOperationException or UnauthorizedAccessException){if(!disposed){last=null;summary.Text="Не удалось завершить диагностику. Повторите проверку; отчёт не создан.";}}
        finally{busy=false;if(!disposed){run.Enabled=true;cancel.Enabled=false;target.Enabled=true;report.Enabled=last!=null;}}
    }
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;attempt?.Cancel();attempt?.Dispose();}base.Dispose(disposing);}
}
internal sealed class DiagnosticReportForm : StudioDialog
{
    internal DiagnosticReportForm(string text)
    {
        Text="PLAYSPACE / Отчёт для поддержки";Size=new Size(940,780);MinimumSize=new Size(800,640);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(28)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));ContentHost.Controls.Add(layout);
        var notice=new Label{Text="Полное содержимое отчёта показано ниже. Вы сами решаете, сохранять и отправлять ли его.",Dock=DockStyle.Fill};layout.Controls.Add(notice,0,0);
        var card=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(16)};card.Controls.Add(new FormattedDescription{Text=text,Dock=DockStyle.Fill});layout.Controls.Add(card,0,1);
        var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};foreach(int _ in Enumerable.Range(0,3))buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));
        var save=new ModernButton("Сохранить TXT…",VisualTheme.Accent){Dock=DockStyle.Fill};var copy=new ModernButton("Копировать",Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};var support=new ModernButton("Открыть поддержку ↗",Color.FromArgb(28,44,55)){Dock=DockStyle.Fill};buttons.Controls.Add(save,0,0);buttons.Controls.Add(copy,1,0);buttons.Controls.Add(support,2,0);layout.Controls.Add(buttons,0,2);
        var status=new Label{Text="Открытие бота не отправляет отчёт. Приложите сохранённый файл или скопированный текст самостоятельно.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(165,177,202)};layout.Controls.Add(status,0,3);
        save.Click+=(s,e)=>{using var dialog=new SaveFileDialog{Title="Сохранить отчёт PlaySpace",Filter="Текстовый отчёт (*.txt)|*.txt",FileName=$"PlaySpace-network-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt",DefaultExt="txt",AddExtension=true};if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{File.WriteAllText(dialog.FileName,text,new UTF8Encoding(false));status.Text="Отчёт сохранён. Передайте его в поддержку, если хотите.";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){status.Text="Не удалось сохранить файл. Выберите другую папку.";}};
        copy.Click+=(s,e)=>{try{Clipboard.SetText(text);status.Text="Отчёт скопирован в буфер обмена.";}catch(System.Runtime.InteropServices.ExternalException){status.Text="Буфер обмена занят. Попробуйте ещё раз.";}};
        support.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo("https://t.me/PlaySpaceSupport_bot"){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){status.Text="Откройте @PlaySpaceSupport_bot в Telegram.";}};
    }
}
