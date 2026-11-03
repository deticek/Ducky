using System.Threading.Tasks;

namespace ducky;

public class TimerPauseCommand : ICommand
{
    public string Name => "/timer-pause";
    public string Description => "Začasno ustavi ali nadaljuje timer.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.TimerPause();
        });

        return CommandResult.Ok("Stanje timerja je spremenjeno.");
    }
}