using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ducky;

public class CommandManager
{
    private readonly Dictionary<string, ICommand> commands = new();

    public void Register(ICommand command)
    {
        string name = command.Name.ToLowerInvariant();

        if (commands.ContainsKey(name))
            return;

        commands.Add(name, command);
    }

    public bool IsDuckyCommand(string input)
    {
        return !string.IsNullOrWhiteSpace(input) &&
               input.TrimStart().StartsWith("/");
    }

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(input))
            return CommandResult.Error("Ukaz je prazen.");

        string[] parts = input.Trim().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        string name = parts[0].ToLowerInvariant();

        if (!name.StartsWith("/"))
            return CommandResult.Error("To ni Ducky ukaz.");

        if (!commands.ContainsKey(name))
            return CommandResult.Error("Neznan Ducky ukaz: " + name);

        string[] args = new string[parts.Length - 1];

        for (int i = 1; i < parts.Length; i++)
            args[i - 1] = parts[i];

        try
        {
            return await commands[name].ExecuteAsync(context, args);
        }
        catch (Exception ex)
        {
            return CommandResult.Error(
                "Napaka pri izvajanju ukaza: " + ex.Message);
        }
    }
}