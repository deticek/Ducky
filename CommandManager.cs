using System;

namespace ducky;

public static class CommandManagere
{
    public static bool Execute(string command)
    {
        //Alert.Info(command);
        switch (command.Trim().ToLower())
        {
            case "/timer-start":
                MainWindow.TimerStart();
                return true;

            case "/timer-countdown":
                MainWindow.CountdownStart();
                return true;

            case "/timer-pause":
                MainWindow.TimerPause();
                return true;

            case "/timer-reset":
                MainWindow.TimerReset();
                return true;

            case "/timer-close":
                MainWindow.CloseTimerWindow();
                return true;

            case "/reset-ducky-workspaces":
                MainWindow.ResetWorkspaceData();
                return true;
            case "/test":
                Alert.Info("test is working");
                return true;

            default:
                return false;
        }
    }
}