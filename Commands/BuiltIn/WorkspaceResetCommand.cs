using System.Threading.Tasks;

namespace ducky;

public class WorkspaceResetCommand : ICommand
{
    public string Name => "/reset-ducky-workspaces";
    public string Description => "Ponastavi vse workspace.";

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        await MainWindow.RunOnUiThread(() =>
        {
            MainWindow.ResetWorkspaceData();
        });

        return CommandResult.Ok("Workspace so ponastavljeni.");
    }
}