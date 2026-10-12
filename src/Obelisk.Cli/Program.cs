using Obelisk.Cli;

return CliApplication.Run(args, new CliContext(Directory.GetCurrentDirectory(), Console.Out, Console.Error));
