using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ducky;

internal static class CustomCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath =>
        Path.Combine(DuckyPaths.DataDirectory, "custom-commands.json");
    public static string? LastLoadError { get; private set; }

    public static List<CustomCommand> Load()
    {
        LastLoadError = null;
        if (!File.Exists(FilePath))
            return new List<CustomCommand>();

        try
        {
            List<CustomCommand>? commands =
                JsonSerializer.Deserialize<List<CustomCommand>>(
                    File.ReadAllText(FilePath),
                    JsonOptions);
            if (commands is null)
                throw new InvalidDataException(
                    "The custom commands file did not contain a command list.");

            NormalizeOrders(commands);
            MigrateTerminalCommands(commands);
            ValidateCollection(commands, requireScript: false);
            return commands;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            DuckyLog.Log("Could not load custom commands: " + ex);
            return new List<CustomCommand>();
        }
    }

    public static void Save(IReadOnlyList<CustomCommand> commands)
    {
        ValidateCollection(commands, requireScript: false);
        for (int index = 0; index < commands.Count; index++)
            commands[index].Order = index;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(commands, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }

    public static void Import(string path)
    {
        List<CustomCommand>? imported =
            JsonSerializer.Deserialize<List<CustomCommand>>(
                File.ReadAllText(path),
                JsonOptions);
        if (imported is null)
            throw new InvalidDataException(
                "The selected file does not contain a command list.");

        MigrateTerminalCommands(imported);
        ValidateCollection(imported, requireScript: true);
        NormalizeOrders(imported);
        Save(imported);
    }

    public static void Export(string path)
    {
        List<CustomCommand> commands = Load();
        Export(path, commands);
    }

    public static void Export(
        string path,
        IReadOnlyList<CustomCommand> commands)
    {
        ValidateCollection(commands, requireScript: false);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(commands, JsonOptions));
    }

    public static bool HasEnabledTrigger(string trigger) =>
        Load().Any(command =>
            command.IsEnabled &&
            command.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));

    public static async Task RunTriggerAsync(
        string trigger,
        TerminalView? terminal)
    {
        if (terminal is null)
        {
            DuckyLog.Log(
                $"Custom command trigger '{trigger}' was skipped because no terminal is active.");
            return;
        }

        foreach (CustomCommand command in Load()
                     .Where(command => command.IsEnabled &&
                         command.Trigger.Equals(
                             trigger,
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(command => command.Order))
        {
            try
            {
                await ExecuteAsync(command, terminal);
            }
            catch (Exception ex)
            {
                DuckyLog.Log(
                    $"Custom command '{command.Name}' failed on trigger '{trigger}': {ex}");
                terminal.ShowCustomCommandMessage(
                    $"Custom command '{command.Name}' failed: {ex.Message}");
            }
        }
    }

    public static CustomCommand? FindOnCommand(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.Length == 0)
            return null;

        int separator = trimmed.IndexOfAny([' ', '\t']);
        string enteredCommand = separator < 0 ? trimmed : trimmed[..separator];
        enteredCommand = NormalizeCommandName(enteredCommand);
        if (enteredCommand.Length == 0)
            return null;

        return Load().FirstOrDefault(command =>
            command.IsEnabled &&
            command.Trigger.Equals(
                CustomCommandTriggers.OnCommand,
                StringComparison.OrdinalIgnoreCase) &&
            NormalizeCommandName(command.TerminalCommand ?? "").Equals(
                enteredCommand,
                StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPotentialOnCommandInput(string input)
    {
        string entered = input.TrimStart();
        if (entered.Length == 0)
            return false;

        int separator = entered.IndexOfAny([' ', '\t']);
        string enteredCommand = separator < 0
            ? entered
            : entered[..separator];
        enteredCommand = NormalizeCommandName(enteredCommand);
        if (enteredCommand.Length == 0)
            return false;

        foreach (CustomCommand command in Load())
        {
            if (!command.IsEnabled ||
                !command.Trigger.Equals(
                    CustomCommandTriggers.OnCommand,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string terminalCommand =
                NormalizeCommandName(command.TerminalCommand ?? "");
            if (separator < 0
                    ? terminalCommand.StartsWith(
                        enteredCommand,
                        StringComparison.OrdinalIgnoreCase)
                    : terminalCommand.Equals(
                        enteredCommand,
                        StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task ExecuteCommandAsync(
        CustomCommand command,
        TerminalView terminal) =>
        await ExecuteAsync(command, terminal);

    public static void ValidateCollection(
        IReadOnlyList<CustomCommand> commands,
        bool requireScript)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> terminalCommands = new(StringComparer.OrdinalIgnoreCase);
        foreach (CustomCommand command in commands)
        {
            if (command is null)
                throw new InvalidDataException(
                    "The command list contains an empty entry.");

            string name = command.Name?.Trim() ?? "";
            if (name.Length == 0)
                throw new InvalidDataException("Command name is required.");
            if (!names.Add(name))
                throw new InvalidDataException(
                    $"Duplicate manager command name: {name}");
            if (!IsValidTrigger(command.Trigger))
                throw new InvalidDataException(
                    $"Command '{name}' has an unsupported trigger.");
            if (!IsValidShell(command.Shell))
                throw new InvalidDataException(
                    $"Command '{name}' has an unsupported shell.");
            if (requireScript &&
                command.Trigger == CustomCommandTriggers.OnCommand &&
                string.IsNullOrWhiteSpace(command.TerminalCommand))
            {
                throw new InvalidDataException(
                    $"Command '{name}' needs a terminal command.");
            }
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                !string.IsNullOrWhiteSpace(command.TerminalCommand) &&
                !terminalCommands.Add(
                    NormalizeCommandName(command.TerminalCommand)))
            {
                throw new InvalidDataException(
                    $"Duplicate terminal command: {command.TerminalCommand}");
            }
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                !string.IsNullOrWhiteSpace(command.TerminalCommand) &&
                MainWindow.Commands.IsRegisteredName(
                    NormalizeCommandName(command.TerminalCommand)))
            {
                throw new InvalidDataException(
                    $"'{command.TerminalCommand}' conflicts with a built-in Ducky command.");
            }
            if (requireScript && string.IsNullOrWhiteSpace(command.Script))
                throw new InvalidDataException(
                    $"Command '{name}' has an empty script.");
        }
    }

    private static async Task ExecuteAsync(
        CustomCommand command,
        TerminalView terminal)
    {
        if (!command.IsEnabled)
            return;
        if (string.IsNullOrWhiteSpace(command.Script))
            throw new InvalidDataException(
                $"Command '{command.Name}' has an empty script.");

        ShellProfile? shellProfile = GetShellProfile(command);
        await terminal.ExecuteCustomScriptAsync(command.Script, shellProfile);
    }

    private static ShellProfile? GetShellProfile(CustomCommand command)
    {
        if (command.Shell == CustomCommandShells.Default)
            return null;

        if (command.Shell == CustomCommandShells.Cmd)
        {
            return ShellRegistry.Find("cmd.exe") ??
                   new ShellProfile
                   {
                       Id = "builtin:cmd",
                       Name = "Command Prompt",
                       Executable = "cmd.exe"
                   };
        }

        if (command.Shell == CustomCommandShells.PowerShell)
        {
            return ShellRegistry.Find("powershell.exe") ??
                   ShellRegistry.Find("pwsh.exe") ??
                   throw new FileNotFoundException(
                       "PowerShell was not found. Add it in Settings > Terminal Shells.");
        }

        string path = command.CustomShellPath.Trim();
        if (!Path.IsPathRooted(path) || !File.Exists(path))
            throw new FileNotFoundException(
                $"Custom shell executable does not exist: '{path}'.",
                path);

        return new ShellProfile
        {
            Id = "custom:" + Path.GetFullPath(path),
            Name = Path.GetFileNameWithoutExtension(path),
            Executable = Path.GetFileName(path),
            ExecutablePath = Path.GetFullPath(path)
        };
    }

    private static bool IsValidTrigger(string trigger) =>
        trigger is CustomCommandTriggers.OnCommand or
            CustomCommandTriggers.OnStartup or
            CustomCommandTriggers.OnExit or
            CustomCommandTriggers.OnTerminalOpen or
            CustomCommandTriggers.OnPaneOpen;

    private static bool IsValidShell(string shell) =>
        shell is CustomCommandShells.Default or
            CustomCommandShells.Cmd or
            CustomCommandShells.PowerShell or
            CustomCommandShells.Custom;

    private static string NormalizeCommandName(string name)
    {
        string normalized = name.Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized[1..];
        return normalized;
    }

    private static void NormalizeOrders(List<CustomCommand> commands)
    {
        commands.Sort((left, right) => left.Order.CompareTo(right.Order));
        for (int index = 0; index < commands.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(commands[index].Id))
                commands[index].Id = Guid.NewGuid().ToString();
            commands[index].Name = commands[index].Name?.Trim() ?? "";
            commands[index].Order = index;
        }
    }

    private static void MigrateTerminalCommands(
        IEnumerable<CustomCommand> commands)
    {
        foreach (CustomCommand command in commands)
        {
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                command.TerminalCommand is null)
            {
                command.TerminalCommand = command.Name;
            }
        }
    }
}