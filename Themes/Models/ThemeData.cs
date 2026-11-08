using System;
using System.Collections.Generic;

namespace ducky;

internal sealed class ThemeData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Theme";
    public bool IsBuiltInDefault { get; set; }

    // Terminal
    public string BackgroundColor { get; set; } = "#11111B";
    public double TerminalOpacity { get; set; } = 100;
    public string TextColor { get; set; } = "#FFFFFF";
    public string CursorColor { get; set; } = "#FFFFFF";
    public string SelectionColor { get; set; } = "#45475A";
    public string SelectionTextColor { get; set; } = "#FFFFFF";
    public string NavbarBackgroundColor { get; set; } = "#202020";
    public string NavbarTitleTextColor { get; set; } = "#FFFFFF";
    public string NavbarButtonColor { get; set; } = "#F4D03F";
    public string NavbarButtonTextColor { get; set; } = "#202020";
    public string WorkspaceLabelBackgroundColor { get; set; } = "#11111B";
    public string WorkspaceLabelTextColor { get; set; } = "#FFFFFF";
    public string ManagerBackgroundColor { get; set; } = "#202127";
    public string ManagerSidebarColor { get; set; } = "#252730";
    public string ManagerCardColor { get; set; } = "#252730";
    public string ManagerTextColor { get; set; } = "#F1F0EC";
    public string ManagerAccentColor { get; set; } = "#EBDAC3";
    public string ManagerButtonColor { get; set; } = "#3B3D47";
    public string ManagerButtonTextColor { get; set; } = "#F1F0EC";

    // Font
    public string FontFamily { get; set; } = "Cascadia Mono";
    public double FontSize { get; set; } = 15;
    public string FontWeight { get; set; } = "Normal";
    public double LineHeight { get; set; } = 1.0;

    // Terminal appearance
    public string CursorStyle { get; set; } = "Block";
    public bool CursorBlink { get; set; } = true;
    public double CursorThickness { get; set; } = 1;

    // ANSI colors
    public Dictionary<string, string> Colors { get; set; } = new()
    {
        { "Black", "#45475A" },
        { "Red", "#F38BA8" },
        { "Green", "#A6E3A1" },
        { "Yellow", "#F9E2AF" },
        { "Blue", "#89B4FA" },
        { "Magenta", "#F5C2E7" },
        { "Cyan", "#94E2D5" },
        { "White", "#BAC2DE" },

        { "BrightBlack", "#585B70" },
        { "BrightRed", "#F38BA8" },
        { "BrightGreen", "#A6E3A1" },
        { "BrightYellow", "#F9E2AF" },
        { "BrightBlue", "#89B4FA" },
        { "BrightMagenta", "#F5C2E7" },
        { "BrightCyan", "#94E2D5" },
        { "BrightWhite", "#A6ADC8" }
    };
}