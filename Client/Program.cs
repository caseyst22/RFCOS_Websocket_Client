using Client;
using System.CommandLine;

class Program
{

    string mask = "3314052B4C000042";
    static async Task Main(string[] args)
    {
        var logOption = new Option<bool>
        (
            name: "--log",
            description: "Write activity to log file.",
            getDefaultValue: () => false 
        )
        { IsRequired = false };

        var environmentOption = new Option<string>
        (
            name: "--environment",
            description: "Wave Environment to communicate with (dev, qa, prod)."
        )
        { IsRequired = true, Arity = ArgumentArity.ZeroOrMore }
        .FromAmong( "dev", "qa", "prod" );

        var maskOption = new Option<IEnumerable<string>>
        (
            name: "--masks",
            description: "Tag ID prefixes that will be accepted."
        )
        { IsRequired = false, AllowMultipleArgumentsPerToken = true };

        var rootCommand = new RootCommand("Run Client")
        {
            logOption,
            environmentOption,
            maskOption
        };
        rootCommand.SetHandler(async (logOptionValue, environmentOptionValue, maskOptionValue) =>
        {
            string accessCode = "";
            switch (environmentOptionValue)
            {
                case "dev":
                    accessCode = AccessCodes.dev;
                    break;
                case "qa":
                    accessCode = AccessCodes.qa;
                    break;
                case "prod":
                    accessCode = AccessCodes.prod;
                    break;
            }

            TagBlinkReader reader = new TagBlinkReader(logOptionValue, accessCode, Enumerable.ToList(maskOptionValue));
            await reader.ReadRFServAsync();
        },
        logOption, environmentOption, maskOption);

        await rootCommand.InvokeAsync(args);
    }

}