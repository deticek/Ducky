using NetCord.Services.ApplicationCommands;

namespace ducky.Discord.Commands;

public class PingCommand : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Check if the bot is online")]
    public string Execute()
    {
        return "Pong!";
    }
}