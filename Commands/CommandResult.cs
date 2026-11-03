namespace ducky;

public class CommandResult
{
    public bool Success { get; }
    public string Message { get; }

    public CommandResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public static CommandResult Ok(string message)
    {
        return new CommandResult(true, message);
    }

    public static CommandResult Error(string message)
    {
        return new CommandResult(false, message);
    }
}