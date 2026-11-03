using System.Threading.Tasks;

namespace ducky;

public class TimerResetCommand : ICommand
{
    public string Name => "/timer-reset";
    public string Description => "Ponastavi timer.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.TimerReset();
        });

        return CommandResult.Ok("Timer je ponastavljen.");
    }
}