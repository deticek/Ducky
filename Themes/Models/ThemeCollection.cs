using System.Collections.Generic;

namespace ducky;

internal sealed class ThemeCollection
{
    public string DefaultThemeId { get; set; } = "";
    public string BuiltInDefaultThemeId { get; set; } = "";

    public List<ThemeData> Themes { get; set; } = new();
}