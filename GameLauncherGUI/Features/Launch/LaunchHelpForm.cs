namespace GameLauncherGUI;

internal sealed class LaunchHelpForm : StudioDialog
{
    internal LaunchHelpForm(GameInfo game,LaunchIssue issue,Action plainLaunch,Action performance,Action mods,Action gameCare,Action updates,Action tracking,Action disable)
    {
        Text="PLAYSPACE / Помощь с запуском";Size=new(890,730);MinimumSize=new(760,640);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,Padding=new Padding(24)};layout.RowStyles.Add(new(SizeType.Absolute,66));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,206));ContentHost.Controls.Add(layout);
        layout.Controls.Add(new SmoothLabel{Dock=DockStyle.Fill,Text=game.Name,Font=new Font("Segoe UI",19,FontStyle.Bold)},0,0);
        var description=new FormattedDescription{Dock=DockStyle.Fill,AccessibleName="Помощь при запуске игры",Text=issue.Detail+"\n\nЧто проверить:\n1. Откройте игровой клиент: вход в аккаунт, очередь обновлений, сообщения об ошибке.\n2. Убедитесь, что отслеживается основной EXE, а не загрузчик.\n3. Если проблема появилась после изменения настроек или модов, проверьте их и используйте доступный откат с подтверждением.\n\nПовторный запуск без подготовки не применяет новые настройки графики и автоматические параметры модов. Сохранённые аргументы запуска сохраняются: они могут быть нужны игровому клиенту. Уже изменённые конфиги и установленные файлы модов остаются на месте — этот режим не восстанавливает оригинальную игру."};layout.Controls.Add(description,0,1);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4};for(int i=0;i<2;i++)actions.ColumnStyles.Add(new(SizeType.Percent,50));for(int i=0;i<4;i++)actions.RowStyles.Add(new(SizeType.Percent,25));layout.Controls.Add(actions,0,2);
        var entries=new (string,Action)[]{("Повторить без подготовки",()=>{Close();plainLaunch();}),("Обновления и игровой клиент",updates),("Настройки графики / откат",performance),("Моды и наборы",mods),("Файлы, копии и диагностика",gameCare),("Выбрать процесс игры",tracking),("Не показывать автоматически",()=>{disable();Close();}),("Это был обычный выход",Close)};
        for(int i=0;i<entries.Length;i++){var entry=entries[i];var button=new ModernButton(entry.Item1,i==0?VisualTheme.Accent:Color.FromArgb(35,40,63)){Dock=DockStyle.Fill};button.Click+=(s,e)=>{try{entry.Item2();}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception){description.Text="Не удалось выполнить действие: "+ex.Message;}};actions.Controls.Add(button,i%2,i/2);}
    }
}
