using System.Diagnostics;

namespace Obelisk.Cli;

public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

public interface IChildProcess : IAsyncDisposable
{
    int Id { get; }
    Task<int> Completion { get; }
    Task StopAsync();
}

public interface IProcessRunner
{
    IChildProcess Start(ProcessCommand command, TextWriter output, TextWriter error);
}

/// <summary>Owns a child process tree and drains both output streams without shell interpolation.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public IChildProcess Start(ProcessCommand command, TextWriter output, TextWriter error)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in command.Arguments) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        WindowsJob? job = null;
        try
        {
            if (OperatingSystem.IsWindows()) job = new WindowsJob();
            if (!process.Start()) throw new InvalidOperationException($"Could not start '{command.FileName}'.");
            if (OperatingSystem.IsWindows()) job!.Assign(process);
            return new OwnedProcess(process, job, output, error);
        }
        catch (Exception failure)
        {
            if (OperatingSystem.IsWindows()) job?.Dispose();
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } }
            catch (InvalidOperationException) { }
            process.Dispose();
            throw new InvalidOperationException($"Could not start '{command.FileName}': {failure.Message}. Install the .NET 10 SDK and check PATH/process permissions.", failure);
        }
    }

    private sealed class OwnedProcess : IChildProcess
    {
        private readonly Process _process;
        private readonly WindowsJob? _job;
        private readonly Task _stdout;
        private readonly Task _stderr;
        private bool _disposed;
        public int Id { get; }
        public Task<int> Completion { get; }

        public OwnedProcess(Process process, WindowsJob? job, TextWriter output, TextWriter error)
        {
            _process = process; _job = job; Id = process.Id;
            _stdout = Pump(process.StandardOutput, output);
            _stderr = Pump(process.StandardError, error);
            Completion = Wait();
        }

        private async Task<int> Wait()
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            // Descendants can hold inherited output pipes open after their parent exits.
            if (OperatingSystem.IsWindows() && _job is not null) await _job.StopAsync().ConfigureAwait(false);
            await Task.WhenAll(_stdout, _stderr).ConfigureAwait(false);
            return _process.ExitCode;
        }

        public async Task StopAsync()
        {
            if (_disposed) return;
            if (OperatingSystem.IsWindows() && _job is not null) await _job.StopAsync().ConfigureAwait(false);
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await Completion.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            try { await StopAsync().ConfigureAwait(false); }
            finally { _disposed = true; _process.Dispose(); }
        }

        private static async Task Pump(StreamReader reader, TextWriter writer)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }
}
