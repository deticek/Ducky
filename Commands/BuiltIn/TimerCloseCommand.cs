using System.Threading.Tasks;

namespace ducky;

public class TimerCloseCommand : ICommand
{
    public string Name => "/timer-close";
    public string Description => "Zapre timer okno.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.CloseTimerWindow();
        });

        return CommandResult.Ok("Timer okno je zaprto.");
    }
}