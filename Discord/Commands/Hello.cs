using NetCord.Services.ApplicationCommands;

namespace ducky.Discord.Commands;

public class HelloCommand : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("hello", "Hello command")]
    public string Execute()
    {
        return "Hello. I am ducky. Your personal discord bot which can run shell command true discord!";
    }
}