
using System;
using Avalonia.Input;

namespace ducky;

public class KeyListener
{
    
    private readonly Func<TerminalView?> _getActiveTerminal;

    public KeyListener(Func<TerminalView?> getActiveTerminal, Config config)
    {
        _getActiveTerminal = getActiveTerminal;
        conf = config.Clone();
    }

    public void UpdateConfig(Config config) => conf = config.Clone();

    private bool leftCtrl, rightCtrl;
    private bool leftShift, rightShift;
    private bool leftAlt, rightAlt;
    private Config conf;

    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.LeftCtrl:   leftCtrl = true; return;
            case Key.RightCtrl:  rightCtrl = true; return;
            case Key.LeftShift:  leftShift = true; return;
            case Key.RightShift: rightShift = true; return;
            case Key.LeftAlt:    leftAlt = true; return;
            case Key.RightAlt:   rightAlt = true; return;
        }

        string key = "";

        if (leftCtrl) key += "leftctrl+";
        if (rightCtrl) key += "rightctrl+";
        if (leftShift) key += "leftshift+";
        if (rightShift) key += "rightshift+";
        if (leftAlt) key += "leftalt+";
        if (rightAlt) key += "rightalt+";

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            key += "meta+";

        key += GetKeyName(e.Key);
        
        //DuckyLog.Log($"key : {key} and conifig {conf.Copy}");


        string? function = KeyManager.GetFunction(key, conf);
        //Alert.Info($"{key} | {conf.DecreseFontSize}");
        if(function == "logtest") DuckyLog.Log("Keybindings for log test work in key listener!");
        
        //Alert.Info(function);

        var terminal = _getActiveTerminal();

        bool zas=true;
        int num=1;

        switch(function){
            case "copy":
                terminal?.CopyFun();
                break;
            case "paste":
                terminal?.PasteFun();
                break;
            case "incresefontsize":
                MainWindow.ChangeFontSize(zas);
                break;
            case "descresefontsize":
            zas = false;
                MainWindow.ChangeFontSize(zas);
                break;
            case "verticalsplit":
                MainWindow.VerticalSplit(zas);
                break;
            case "horizontalsplit":
                MainWindow.VerticalSplit(!zas);
                break;
            case "nextsplit":
                zas = true;
                MainWindow.NextFocus(zas);
                break;
            case "previussplit":
                zas = false;
                MainWindow.NextFocus(zas);
                break;
            case "closesplit":
                MainWindow.CloseSplit();
                break;
            case "nextws":
                zas = true;
                MainWindow.SwitchWorkspace(zas);
                break;
            case "previusws":
                zas = false;
                MainWindow.SwitchWorkspace(zas);
                break;
            case "temporaryterminal":
                MainWindow.OpenTemporaryTerminal();
                break;
            case "workspace1":
                   num=1;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace2":
                   num=2;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace3":
                   num=3;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace4":
                   num=4;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace5":
                   num=5;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace6":
                   num=6;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace7":
                   num=7;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace8":
                   num=8;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace9":
                   num=9;
                MainWindow.GoToWorkspace(num);
                break;
            case "workspace10":
                   num=10;
                MainWindow.GoToWorkspace(num);
                break;
            case "helpwindow":
                MainWindow.ShowShortcutsHelp();
                break;
        }

    }

    public void OnKeyUp(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.LeftCtrl:   leftCtrl = false; break;
            case Key.RightCtrl:  rightCtrl = false; break;
            case Key.LeftShift:  leftShift = false; break;
            case Key.RightShift: rightShift = false; break;
            case Key.LeftAlt:    leftAlt = false; break;
            case Key.RightAlt:   rightAlt = false; break;
        }
    }

    private static string GetKeyName(Key key)
    {
        // Številke v zgornji vrstici
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();

        // Številke na numerični tipkovnici
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)key - (int)Key.NumPad0).ToString();

        // Plus in minus
        return key switch
        {
            Key.Add => "add",
            Key.OemPlus => "add",
            Key.Subtract => "-",
            Key.OemMinus => "-",
            _ => key.ToString().ToLowerInvariant()
        };
    }
}
