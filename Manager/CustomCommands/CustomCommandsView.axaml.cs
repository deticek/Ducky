using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace ducky;

public partial class CommandsView : UserControl
{
    private List<CustomCommand> commands = [];
    private string? expandedCommandId;
    private bool isBuilding;
    private IStorageProvider StorageProvider =>
        TopLevel.GetTopLevel(this)?.StorageProvider ??
        throw new InvalidOperationException("The file picker is not available.");

    public CommandsView()
    {
        InitializeComponent();
        commands = CustomCommandService.Load();
        RefreshCommands();
        if (CustomCommandService.LastLoadError is { } loadError)
            SetStatus("Could not load saved commands: " + loadError);
    }

    private async void CreateCommand_Click(object? sender, RoutedEventArgs e)
    {
        string baseName = "new-command";
        string name = baseName;
        int suffix = 2;
        while (commands.Any(command =>
                   NormalizeName(command.Name).Equals(
                       NormalizeName(name),
                       StringComparison.OrdinalIgnoreCase)))
            name = baseName + "-" + suffix++;

        var command = new CustomCommand
        {
            Name = name,
            TerminalCommand = "",
            Script = string.Empty,
            Order = commands.Count
        };
        commands.Add(command);
        expandedCommandId = command.Id;
        await SaveCommands();
        RefreshCommands();
    }

    private async void ImportCommands_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> files =
                await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Import custom commands",
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("JSON files")
                            {
                                Patterns = ["*.json"]
                            }
                        ]
                    });
            if (files.Count == 0)
                return;

            CustomCommandService.Import(files[0].Path.LocalPath);
            commands = CustomCommandService.Load();
            expandedCommandId = null;
            RefreshCommands();
            await SaveCommands();
            SetStatus($"Imported {commands.Count} custom command(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Import failed: " + ex.Message);
        }
    }

    private async void ExportCommands_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export custom commands",
                    SuggestedFileName = "ducky-custom-commands.json",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("JSON files")
                        {
                            Patterns = ["*.json"]
                        }
                    ]
                });
            if (file is null)
                return;

            CustomCommandService.Export(file.Path.LocalPath, commands);
            SetStatus($"Exported {commands.Count} custom command(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Export failed: " + ex.Message);
        }
    }

    private void SearchInput_TextChanged(object? sender, TextChangedEventArgs e) =>
        RefreshCommands();

    private void RefreshCommands()
    {
        if (this.FindControl<StackPanel>("CommandsPanel") is not { } panel)
            return;

        isBuilding = true;
        panel.Children.Clear();
        if (this.FindControl<TextBlock>("EmptyCommandsText") is { } emptyText)
            emptyText.IsVisible = commands.Count == 0;
        string search = this.FindControl<TextBox>("SearchInput")?.Text?.Trim() ?? "";
        List<CustomCommand> matchingCommands = commands
            .OrderBy(command => command.Order)
            .Where(command => command.Name.Contains(
                search,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (CustomCommand command in matchingCommands)
            panel.Children.Add(BuildCommandCard(command));

        if (matchingCommands.Count == 0 && commands.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No commands match your search.",
                Classes = { "muted" },
                Margin = new Thickness(10, 12)
            });
        }
        isBuilding = false;
    }

    private Expander BuildCommandCard(CustomCommand command)
    {
        var title = new TextBlock
        {
            Text = command.Name,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var trigger = new TextBlock
        {
            Text = command.Trigger,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { title, trigger }
        };
        Grid.SetColumn(trigger, 1);

        var editor = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(10, 8)
        };

        var nameBox = new TextBox
        {
            Text = command.Name,
            PlaceholderText = "Command name"
        };
        editor.Children.Add(LabeledControl("Command name", nameBox));
        nameBox.LostFocus += async (_, _) =>
        {
            string candidate = nameBox.Text?.Trim() ?? "";
            CustomCommand? duplicate = commands.FirstOrDefault(other =>
                other.Id != command.Id &&
                NormalizeName(other.Name).Equals(
                    NormalizeName(candidate),
                    StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(candidate) || duplicate is not null)
            {
                SetStatus(string.IsNullOrWhiteSpace(candidate)
                    ? "Command name cannot be empty."
                    : "That name is already used by another command or Ducky command.");
                nameBox.Text = command.Name;
                return;
            }

            command.Name = candidate;
            title.Text = candidate;
            await PersistAndRefresh();
        };

        var triggerBox = new ComboBox
        {
            ItemsSource = new[]
            {
                CustomCommandTriggers.OnCommand,
                CustomCommandTriggers.OnStartup,
                CustomCommandTriggers.OnExit,
                CustomCommandTriggers.OnTerminalOpen,
                CustomCommandTriggers.OnPaneOpen
            },
            SelectedItem = command.Trigger,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var terminalCommandBox = new TextBox
        {
            Text = command.TerminalCommand ?? "",
            PlaceholderText = "Command to enter in the terminal"
        };
        StackPanel terminalCommandEditor =
            LabeledControl("Terminal command", terminalCommandBox);
        terminalCommandEditor.IsVisible =
            command.Trigger.Equals(
                CustomCommandTriggers.OnCommand,
                StringComparison.OrdinalIgnoreCase);
        terminalCommandBox.LostFocus += async (_, _) =>
        {
            command.TerminalCommand = terminalCommandBox.Text?.Trim() ?? "";
            await PersistAndRefresh();
        };
        triggerBox.SelectionChanged += async (_, _) =>
        {
            if (triggerBox.SelectedItem is string selected)
            {
                command.Trigger = selected;
                trigger.Text = selected;
                terminalCommandEditor.IsVisible = selected.Equals(
                    CustomCommandTriggers.OnCommand,
                    StringComparison.OrdinalIgnoreCase);
                await PersistAndRefresh();
            }
        };
        editor.Children.Add(LabeledControl("Trigger", triggerBox));
        editor.Children.Add(terminalCommandEditor);

        var shellBox = new ComboBox
        {
            ItemsSource = new[]
            {
                CustomCommandShells.Default,
                CustomCommandShells.Cmd,
                CustomCommandShells.PowerShell,
                CustomCommandShells.Custom
            },
            SelectedItem = command.Shell,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var customShellPathBox = new TextBox
        {
            Text = command.CustomShellPath,
            PlaceholderText = "Full path to shell executable"
        };
        var customShellBrowse = new Button { Content = "Browse..." };
        var customShellPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { customShellPathBox, customShellBrowse }
        };
        customShellPathBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        customShellPanel.IsVisible =
            command.Shell.Equals(CustomCommandShells.Custom, StringComparison.OrdinalIgnoreCase);
        shellBox.SelectionChanged += async (_, _) =>
        {
            if (shellBox.SelectedItem is string selected)
            {
                command.Shell = selected;
                customShellPanel.IsVisible = selected.Equals(
                    CustomCommandShells.Custom,
                    StringComparison.OrdinalIgnoreCase);
                await PersistAndRefresh();
            }
        };
        editor.Children.Add(LabeledControl("Shell", shellBox));
        editor.Children.Add(LabeledControl("Custom shell path", customShellPanel));
        customShellPathBox.LostFocus += async (_, _) =>
        {
            command.CustomShellPath = customShellPathBox.Text?.Trim() ?? "";
            await PersistAndRefresh();
        };
        customShellBrowse.Click += async (_, _) =>
        {
            IReadOnlyList<IStorageFile> files =
                await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Select shell executable",
                        AllowMultiple = false,
                        FileTypeFilter =
                        [
                            new FilePickerFileType("Executable files")
                            {
                                Patterns = ["*.exe", "*"]
                            }
                        ]
                    });
            if (files.Count == 0)
                return;

            customShellPathBox.Text = files[0].Path.LocalPath;
            command.CustomShellPath = customShellPathBox.Text;
            await PersistAndRefresh();
        };

        var scriptBox = new TextBox
        {
            Text = command.Script,
            PlaceholderText = "Enter one command per line",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 120,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        scriptBox.LostFocus += async (_, _) =>
        {
            command.Script = scriptBox.Text ?? "";
            await PersistAndRefresh();
        };
        editor.Children.Add(LabeledControl("Command script", scriptBox));

        var enabledBox = new CheckBox
        {
            Content = "Enabled",
            IsChecked = command.IsEnabled
        };
        enabledBox.Click += async (_, _) =>
        {
            command.IsEnabled = enabledBox.IsChecked == true;
            await PersistAndRefresh();
        };
        editor.Children.Add(enabledBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var moveUp = new Button { Content = "Move up" };
        var moveDown = new Button { Content = "Move down" };
        var save = new Button
        {
            Content = "Save changes",
            Classes = { "primary" }
        };
        var delete = new Button { Content = "Delete" };
        moveUp.Click += async (_, _) => await MoveCommand(command, -1);
        moveDown.Click += async (_, _) => await MoveCommand(command, 1);
        save.Click += async (_, _) =>
        {
            string candidateName = nameBox.Text?.Trim() ?? "";
            CustomCommand? duplicate = commands.FirstOrDefault(other =>
                other.Id != command.Id &&
                other.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            string terminalCommand = terminalCommandBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                SetStatus("Command name cannot be empty.");
                return;
            }
            if (duplicate is not null)
            {
                SetStatus("A command with this manager name already exists.");
                return;
            }
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                string.IsNullOrWhiteSpace(terminalCommand))
            {
                SetStatus("Enter the terminal command for the On command trigger.");
                return;
            }
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                MainWindow.Commands.IsRegisteredName(
                    NormalizeTerminalCommand(terminalCommand)))
            {
                SetStatus("The terminal command cannot override a built-in Ducky command.");
                return;
            }
            if (command.Trigger == CustomCommandTriggers.OnCommand &&
                terminalCommand.Any(char.IsWhiteSpace))
            {
                SetStatus("Enter a single terminal command without arguments.");
                return;
            }
            if (string.IsNullOrWhiteSpace(scriptBox.Text))
            {
                SetStatus("Command script cannot be empty.");
                return;
            }

            command.Name = candidateName;
            command.TerminalCommand = terminalCommand;
            command.Script = scriptBox.Text;
            command.CustomShellPath = customShellPathBox.Text?.Trim() ?? "";
            title.Text = candidateName;
            if (await PersistAndRefresh())
                SetStatus("Command saved.");
        };
        delete.Click += async (_, _) =>
        {
            commands.Remove(command);
            expandedCommandId = null;
            UpdateOrder();
            await PersistAndRefresh();
            RefreshCommands();
            SetStatus("Command deleted.");
        };
        actions.Children.Add(save);
        actions.Children.Add(moveUp);
        actions.Children.Add(moveDown);
        actions.Children.Add(delete);
        editor.Children.Add(actions);

        var expander = new Expander
        {
            Header = header,
            Content = editor,
            IsExpanded = command.Id == expandedCommandId,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0)
        };
        expander.Expanded += (_, _) => expandedCommandId = command.Id;
        expander.Collapsed += (_, _) =>
        {
            if (expandedCommandId == command.Id)
                expandedCommandId = null;
        };
        return expander;
    }

    private async System.Threading.Tasks.Task MoveCommand(
        CustomCommand command,
        int delta)
    {
        int index = commands.IndexOf(command);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= commands.Count)
            return;

        commands.RemoveAt(index);
        commands.Insert(target, command);
        UpdateOrder();
        expandedCommandId = command.Id;
        await PersistAndRefresh();
        RefreshCommands();
    }

    private System.Threading.Tasks.Task<bool> PersistAndRefresh()
    {
        if (isBuilding)
            return System.Threading.Tasks.Task.FromResult(false);

        UpdateOrder();
        try
        {
            CustomCommandService.Save(commands);
            return System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            SetStatus("Could not save commands: " + ex.Message);
            return System.Threading.Tasks.Task.FromResult(false);
        }
    }

    private async System.Threading.Tasks.Task SaveCommands()
    {
        try
        {
            UpdateOrder();
            CustomCommandService.Save(commands);
            SetStatus("Commands saved.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save commands: " + ex.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void UpdateOrder()
    {
        for (int i = 0; i < commands.Count; i++)
            commands[i].Order = i;
    }

    private static StackPanel LabeledControl(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(control);
        return panel;
    }

    private static string NormalizeName(string name) =>
        name.Trim();

    private static string NormalizeTerminalCommand(string command) =>
        command.Trim().TrimStart('/');

    private void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("StatusText") is { } status)
            status.Text = text;
    }
}
