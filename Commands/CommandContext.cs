namespace ducky;

public class CommandContext
{
    public string Source { get; }

    public CommandContext(string source)
    {
        Source = source;
    }
}