using System.Threading.Tasks;

namespace ducky;

public class TimerStartCommand : ICommand
{
    public string Name => "/timer-start";
    public string Description => "Zažene štoparico.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.TimerStart();
        });

        return CommandResult.Ok("Štoparica je zagnana.");
    }
}