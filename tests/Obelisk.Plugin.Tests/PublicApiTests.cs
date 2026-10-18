using System.Reflection;
using System.Text.Json;

namespace Obelisk.Plugin.Tests;

public sealed class PublicApiTests
{
    private static readonly Assembly[] Libraries =
    [
        typeof(Abstractions.IGame).Assembly, typeof(Graphics2D.Canvas2D).Assembly,
        typeof(Assets.AssetStore).Assembly, typeof(PluginHost).Assembly,
        typeof(Runtime.ApplicationHost).Assembly, typeof(Platform.Sdl3.Sdl3ApplicationPlatform).Assembly,
        typeof(Plugins.Images.ImagePlugin).Assembly, typeof(Vanilla.ObeliskApp).Assembly,
    ];
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void PublicSurfaceMatchesReviewedV01Baseline()
    {
        var actual = new SortedSet<string>(StringComparer.Ordinal);
        var nullable = new NullabilityInfoContext();
        foreach (var assembly in Libraries)
            foreach (var type in assembly.GetExportedTypes())
            {
                var name = $"{assembly.GetName().Name}/{type.FullName}";
                actual.Add($"type {name}: {type.Attributes}; base {TypeName(type.BaseType)}; interfaces {string.Join(",", type.GetInterfaces().Select(t => TypeName(t)).Order(StringComparer.Ordinal))}".TrimEnd());
                foreach (var member in type.GetMembers(Declared))
                {
                    var value = member switch
                    {
                        ConstructorInfo c => $"ctor ({Parameters(c)})",
                        MethodInfo m when !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal) =>
                            $"method {(m.IsStatic ? "static " : "")}{(m.IsVirtual ? "virtual " : "")}{TypeName(m.ReturnType, nullable.Create(m.ReturnParameter))} {m.Name}<{string.Join(",", m.GetGenericArguments().Select(Constraint))}>({Parameters(m)})",
                        PropertyInfo p => $"property {(p.GetMethod?.IsStatic == true || p.SetMethod?.IsStatic == true ? "static " : "")}{TypeName(p.PropertyType, nullable.Create(p))} {p.Name}({string.Join(",", p.GetIndexParameters().Select(Parameter))}) {{ {(p.GetMethod?.IsPublic == true ? "get; " : "")}{Setter(p)} }} read={nullable.Create(p).ReadState} write={nullable.Create(p).WriteState}",
                        FieldInfo f => $"field {(f.IsStatic ? "static " : "")}{(f.IsInitOnly ? "readonly " : "")}{TypeName(f.FieldType, nullable.Create(f))} {f.Name}{(f.IsLiteral ? " = " + JsonSerializer.Serialize(f.GetRawConstantValue()) : "")}",
                        EventInfo e => $"event {TypeName(e.EventHandlerType)} {e.Name}",
                        _ => null,
                    };
                    if (value is not null) actual.Add($"{name} :: {value}");
                }
            }
        var lines = actual.ToArray();
        // Diagnostic output is ignored build output, never an automatic baseline update.
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "PublicApi.actual.txt"), lines);
        Assert.Equal(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PublicApi.v0.1.txt")), lines);

        string Parameter(ParameterInfo p) => $"{(p.IsOut ? "out " : p.IsIn ? "in " : "")}{TypeName(p.ParameterType, nullable.Create(p))} {p.Name}{(p.IsOptional ? " = " + JsonSerializer.Serialize(p.DefaultValue) : "")}";
        string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(Parameter));
    }

    [Fact]
    public void ExportedSignaturesDoNotExposeSdlOrNativePointers()
    {
        foreach (var type in Libraries.SelectMany(a => a.GetExportedTypes()))
            foreach (var member in type.GetMembers(Declared))
            {
                var types = member switch
                {
                    MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
                    ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
                    PropertyInfo p => p.GetIndexParameters().Select(x => x.ParameterType).Append(p.PropertyType),
                    FieldInfo f => [f.FieldType],
                    _ => Enumerable.Empty<Type>(),
                };
                foreach (var signature in types.SelectMany(Flatten))
                    Assert.True(signature != typeof(nint) && signature != typeof(nuint) && !signature.IsPointer &&
                        !(signature.Namespace?.StartsWith("SDL3", StringComparison.Ordinal) ?? false), $"Native type {signature} escaped through {type.FullName}.{member.Name}.");
            }
    }

    private static string Setter(PropertyInfo p) => p.SetMethod?.IsPublic != true ? "" :
        p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) ? "init;" : "set;";

    private static string Constraint(Type t) => $"{t.Name}:{t.GenericParameterAttributes}:{string.Join("&", t.GetGenericParameterConstraints().Select(x => TypeName(x)).Order(StringComparer.Ordinal))}";

    private static string TypeName(Type? type, NullabilityInfo? nullable = null)
    {
        if (type is null) return "none";
        if (type.HasElementType) return TypeName(type.GetElementType(), nullable?.ElementType) + (type.IsArray ? "[]" : type.IsByRef ? "&" : "*");
        var name = type.IsGenericType ? (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0] + "<" +
            string.Join(",", type.GetGenericArguments().Select((a, i) => TypeName(a, nullable?.GenericTypeArguments.ElementAtOrDefault(i)))) + ">" : type.FullName ?? type.Name;
        return name + (!type.IsValueType && nullable?.ReadState == NullabilityState.Nullable ? "?" : "");
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType) foreach (var nested in Flatten(type.GetElementType()!)) yield return nested;
        foreach (var argument in type.GetGenericArguments()) foreach (var nested in Flatten(argument)) yield return nested;
    }
}
