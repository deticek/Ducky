using System.Threading.Tasks;

namespace ducky;

public class TestCommand : ICommand
{
    public string Name => "/test";
    public string Description => "Preveri delovanje Ducky ukaznega sistema.";

    public Task<CommandResult> ExecuteAsync(
        CommandContext context,
        string[] args)
    {
        return Task.FromResult(
            CommandResult.Ok("Test je uspešno deloval! Vir: " + context.Source));
    }
}