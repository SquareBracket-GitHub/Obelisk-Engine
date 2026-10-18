using System.Text;
using System.Text.RegularExpressions;

namespace Obelisk.Cli;

/// <summary>Creates a complete project atomically, without merging into an existing path.</summary>
public sealed class ProjectGenerator(Action<string, string>? writeFile = null)
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "__arglist", "__makeref", "__reftype", "__refvalue",
        "global", "file", "record", "required", "scoped", "var", "dynamic", "async", "await",
        "yield", "partial", "when", "where", "init", "nameof", "unmanaged", "notnull", "nint", "nuint",
    };
    private static readonly (string Destination, string Resource)[] Files =
    [
        ("Program.cs", "Program.cs.template"), ("Game.cs", "Game.cs.template"),
        ("{{Name}}.csproj", "Project.csproj.template"), ("obelisk.json", "obelisk.json.template"),
        (".gitignore", "gitignore.template"), ("README.md", "README.md.template"),
        ("assets/README.txt", "assets.template"), ("plugins/README.txt", "plugins.template"),
    ];

    public static bool IsValidName(string? name) => name is not null &&
        Regex.IsMatch(name, "\\A[A-Za-z_][A-Za-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant) &&
        !Keywords.Contains(name) && !Regex.IsMatch(name, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string Create(string workingDirectory, string name)
    {
        if (!IsValidName(name)) throw new ArgumentException("Invalid project name.", nameof(name));
        var parent = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException($"Working directory '{parent}' does not exist.");
        var target = Path.Combine(parent, name);
        if (Path.Exists(target)) throw new IOException($"Path '{target}' already exists. Choose another name or move the existing path.");
        var staging = Path.Combine(parent, $".obelisk-{name}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var (destination, resource) in Files)
            {
                using var stream = typeof(ProjectGenerator).Assembly.GetManifestResourceStream($"Templates/{resource}")
                    ?? throw new InvalidOperationException($"Bundled template '{resource}' is missing. Reinstall Obelisk.Cli {CliApplication.Version}.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = Expand(reader.ReadToEnd(), name);
                var path = Path.Combine(staging, Expand(destination, name).Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (writeFile is null) File.WriteAllText(path, content, new UTF8Encoding(false));
                else writeFile(path, content);
            }
            Publish(staging, target);
            return target;
        }
        catch (Exception error)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { throw new IOException($"Project creation failed: {error.Message}. Temporary directory '{staging}' could not be removed: {cleanup.Message}", error); }
            throw;
        }
    }

    private static void Publish(string staging, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(staging, target); // Atomic rename; never merge or replace a raced target.
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() &&
                error is IOException or UnauthorizedAccessException &&
                (error.HResult & 0xffff) is 5 or 32 or 33 &&
                attempt < 7 && Directory.Exists(staging) && !Path.Exists(target))
            {
                // A reader without FileShare.Delete can temporarily block renaming its parent.
                // Wait at most 1.4 seconds; persistent permission/lock errors still reach cleanup.
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static string Expand(string text, string name) => text.Replace("{{Name}}", name, StringComparison.Ordinal)
        .Replace("{{EngineVersion}}", CliApplication.EngineVersion, StringComparison.Ordinal);
}
