using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Obelisk.Cli;

if (args[0] == "unicode-output")
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.WriteLine("복원 완료: 한글 경로 / 日本語 / 🎮");
    Console.Error.WriteLine("오류: 파일을 찾을 수 없습니다");
    return 17;
}
if (args[0] == "encoding-runner")
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    Console.OutputEncoding = Encoding.GetEncoding(int.Parse(args[1]));
    var output = new StringWriter(); var error = new StringWriter();
    await using var child = new ProcessRunner().Start(new ProcessCommand("dotnet",
        [Assembly.GetExecutingAssembly().Location, "unicode-output"], Directory.GetCurrentDirectory()), output, error);
    var exit = await child.Completion;
    Console.WriteLine(JsonSerializer.Serialize(new { output = output.ToString(), error = error.ToString(), exit }));
    return 0;
}

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
