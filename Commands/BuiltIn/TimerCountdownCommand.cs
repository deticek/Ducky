using System.Threading.Tasks;

namespace ducky;

public class TimerCountdownCommand : ICommand
{
    public string Name => "/timer-countdown";
    public string Description => "Zažene odštevalnik.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.CountdownStart();
        });

        return CommandResult.Ok("Odštevalnik je zagnan.");
    }
}