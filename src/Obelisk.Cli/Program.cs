using Obelisk.Cli;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    return await CliApplication.RunAsync(args, new CliContext(Directory.GetCurrentDirectory(), Console.Out, Console.Error),
        cancellation: cancellation.Token);
}
finally { Console.CancelKeyPress -= cancel; }
