using Client;
using System.CommandLine;
using System.CommandLine.Parsing;

class Program
{
    static async Task Main(string[] args)
    {
        var logOption = new Option<bool>
        (
            name: "--log",
            description: "Write activity to log file.",
            getDefaultValue: () => false 
        )
        {
            IsRequired = false
        };

        var environmentOption = new Option<string>
        (
            name: "--environment",
            description: "Wave Environment to communicate with (dev, qa, prod)."
        )
        { 
            IsRequired = true,
            Arity = ArgumentArity.ExactlyOne
        }
        .FromAmong( "dev", "qa", "prod" );

        var maskOption = new Option<IEnumerable<string>>
        (
            name: "--masks",
            description: "Tag ID prefixes that will be accepted."
        )
        {
            IsRequired = false,
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true
        };

        var rootCommand = new RootCommand()
        {
            logOption,
            environmentOption,
            maskOption
        };

        rootCommand.SetHandler(async (bool log, string environment, IEnumerable<string> masks) =>
        {
            string accessCode = environment switch
            {
                "dev" => AccessCodes.dev,
                "qa" => AccessCodes.qa,
                "prod" => AccessCodes.prod,
                _ => throw new ArgumentException($"Invalid environment value: {environment}")
            };

            TagBlinkReader reader = new TagBlinkReader(log, accessCode, masks.ToList());
            await reader.ReadRFServAsync();
        },
        logOption, environmentOption, maskOption);

        await rootCommand.InvokeAsync(args);
    }
}