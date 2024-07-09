using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Diagnostics;
using Archiver.Commands;

namespace Archiver;

public class EntryPoint
{
    public static async Task Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("ARCHIVER_DEBUG") is not null)
        {
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(1);
            }
        }

        var root = new RootCommand();
        root.AddCommand(new IndexCommand());
        root.AddCommand(new SearchCommand());
        root.AddCommand(new CompressCommand());

        var commands = new CommandLineBuilder(root);
        commands.UseDefaults();

        var parser = commands.Build();
        await parser.InvokeAsync(args);
    }
}
