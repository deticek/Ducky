using System;

namespace ducky;

internal static class CommandManager
{
    public static bool Execute(string command)
    {
        switch (command.Trim())
        {
            case "/reset-ducky-workspaces":
                MainWindow.ResetWorkspaceData();
                return true;

            default:
                return false;
        }
    }
}