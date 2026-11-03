using System.Threading.Tasks;

namespace ducky
{
    public interface ICommand
    {
        string Name { get; }
        string Description { get; }

        Task<CommandResult> ExecuteAsync(CommandContext context, string[] args);
    }
}