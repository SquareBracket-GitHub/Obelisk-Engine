using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

if (args[0] == "echo")
{
    Console.WriteLine(JsonSerializer.Serialize(args[1..]));
    Console.Error.WriteLine("child-stderr");
    return 17;
}
File.WriteAllText(args[1], Environment.ProcessId.ToString());
if (args[0] is "tree" or "parent-exit")
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("wait"); start.ArgumentList.Add(args[2]);
    using var child = Process.Start(start)!;
    var deadline = Stopwatch.StartNew();
    while (!File.Exists(args[2]))
    {
        if (deadline.Elapsed > TimeSpan.FromSeconds(10)) return 99;
        await Task.Delay(10);
    }
    if (args[0] == "parent-exit") return 23;
}
await Task.Delay(Timeout.Infinite);
return 0;
