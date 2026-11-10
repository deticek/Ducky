using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetCord.Services.ApplicationCommands;

namespace ducky.Discord.Commands;

public class Command : ApplicationCommandModule<ApplicationCommandContext>
{
    private const int MaxResponseLength = 1800;
    private const int MaxDirectoryEntries = 80;

    [SlashCommand(
        "command",
        "Run a limited Ducky command (dir, ping, or selected Git actions)")]
    public async Task<string> ExecuteAsync(
        [SlashCommandParameter(
            Name = "input",
            Description = "For example: dir /a, ping example.com, or git status",
            MaxLength = 500)]
        string input)
    {
        try
        {
            if (!DiscordCommandAccess.IsAllowed(
                    Context.Interaction.User.Id.ToString()))
            {
                return "You are not on Ducky's Discord command allowlist.";
            }

            if (!TryTokenize(input, out List<string> args) || args.Count == 0)
                return Usage();

            string command = args[0].ToLowerInvariant();
            return command switch
            {
                "dir" => ListDirectory(args),
                "ping" => await PingHostAsync(args),
                "git" => await RunGitCommandAsync(args),
                _ => "Supported commands: `dir`, `ping <host>`, and the limited `git` actions. " + Usage()
            };
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Discord command execution failed: " + ex);
            return "Command failed: " + ex.Message;
        }
    }

    private static string Usage() =>
        "Usage: `/command input:dir [/a] [/b]`, " +
        "`/command input:ping <host>`, or `/command input:git status|add .|commit -m <message>|push`.";

    private static string ListDirectory(IReadOnlyList<string> args)
    {
        bool includeHidden = false;
        bool namesOnly = false;
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument.Equals("/a", StringComparison.OrdinalIgnoreCase))
                includeHidden = true;
            else if (argument.Equals("/b", StringComparison.OrdinalIgnoreCase))
                namesOnly = true;
            else
                return "Usage: `dir [/a] [/b]`. Directory listing is limited to Ducky's working directory.";
        }

        try
        {
            string path = Environment.CurrentDirectory;
            StringBuilder output = new();
            int count = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                string name = Path.GetFileName(entry);
                if (!includeHidden && name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (count++ == MaxDirectoryEntries)
                {
                    output.AppendLine("… output truncated");
                    break;
                }

                if (namesOnly)
                {
                    output.AppendLine(name);
                    continue;
                }

                bool isDirectory = Directory.Exists(entry);
                long size = isDirectory ? 0 : new FileInfo(entry).Length;
                output.AppendLine(isDirectory
                    ? $"<DIR>  {name}"
                    : $"{size,10}  {name}");
            }

            string result = output.Length == 0 ? "(empty directory)" : output.ToString();
            return Limit(result);
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Discord dir command failed: " + ex);
            return "Could not list directory: " + ex.Message;
        }
    }

    private static async Task<string> PingHostAsync(IReadOnlyList<string> args)
    {
        if (args.Count != 2 ||
            Uri.CheckHostName(args[1]) == UriHostNameType.Unknown)
        {
            return "Usage: `ping <host>`.";
        }

        try
        {
            using Ping ping = new();
            PingReply reply = await ping.SendPingAsync(args[1], 3000);
            return reply.Status == IPStatus.Success
                ? $"Reply from `{reply.Address}`: time={reply.RoundtripTime}ms, TTL={reply.Options?.Ttl ?? 0}"
                : $"Ping to `{args[1]}` failed: {reply.Status}.";
        }
        catch (Exception ex)
        {
            DuckyLog.Log("Discord ping command failed: " + ex);
            return "Ping failed: " + ex.Message;
        }
    }

    private static async Task<string> RunGitCommandAsync(
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return Usage();

        string subcommand = args[1].ToLowerInvariant();
        List<string> gitArgs = new();
        string responsePrefix = "";
        switch (subcommand)
        {
            case "status" when args.Count == 2:
                gitArgs.AddRange(["status", "--short", "--branch"]);
                break;
            case "add" when args.Count == 3 && args[2] == ".":
                gitArgs.AddRange(["add", "--update", "--", "."]);
                responsePrefix = "Staged tracked changes only (new files were not added).\n";
                break;
            case "commit" when args.Count == 4 &&
                               args[2] == "-m" &&
                               !string.IsNullOrWhiteSpace(args[3]):
                gitArgs.AddRange(["commit", "-m", args[3]]);
                break;
            case "push" when args.Count == 2:
                gitArgs.Add("push");
                break;
            default:
                return "Supported Git actions: `git status`, `git add .` (tracked files only), " +
                       "`git commit -m \"message\"`, and `git push`.";
        }

        ProcessResult result = await RunProcessAsync(
            "git",
            gitArgs,
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(30));
        StringBuilder output = new();
        AppendBounded(output, result.StandardOutput);
        AppendBounded(output, result.StandardError);
        string commandOutput = output.ToString().Trim();
        if (commandOutput.Length == 0)
            commandOutput = result.ExitCode == 0 ? "Completed successfully." : "Git failed.";

        string prefix = result.ExitCode == 0 ? responsePrefix : "Git failed:\n";
        return Limit(prefix + commandOutput);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        using Process process = new();
        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        process.StartInfo = startInfo;
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        object outputLock = new();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;
            lock (outputLock)
                AppendBounded(stdout, eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
                return;
            lock (outputLock)
                AppendBounded(stderr, eventArgs.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start {executable}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using CancellationTokenSource timeoutSource = new(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not finish in time.");
        }

        lock (outputLock)
        {
            return new ProcessResult(
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString());
        }
    }

    private static void AppendBounded(StringBuilder destination, string value)
    {
        if (destination.Length >= MaxResponseLength ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        int remaining = MaxResponseLength - destination.Length;
        destination.AppendLine(value.Length <= remaining
            ? value
            : value[..remaining]);
    }

    private static string Limit(string output)
    {
        string safeOutput = output
            .Replace("@everyone", "@\u200Beveryone", StringComparison.OrdinalIgnoreCase)
            .Replace("@here", "@\u200Bhere", StringComparison.OrdinalIgnoreCase);
        return safeOutput.Length <= MaxResponseLength
            ? safeOutput
            : safeOutput[..MaxResponseLength] + "\n… output truncated";
    }

    private static bool TryTokenize(string input, out List<string> args)
    {
        args = new List<string>();
        StringBuilder token = new();
        bool inQuotes = false;
        bool tokenStarted = false;
        foreach (char character in input)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (tokenStarted)
                {
                    args.Add(token.ToString());
                    token.Clear();
                    tokenStarted = false;
                }
            }
            else
            {
                token.Append(character);
                tokenStarted = true;
            }
        }

        if (inQuotes)
            return false;
        if (tokenStarted)
            args.Add(token.ToString());
        return true;
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
