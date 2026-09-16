using System.Reflection;

namespace EsilvaSoft.SlopStudio.UnitTests;

public class MongoTestProxy : DispatchProxy
{
    public Func<string, object?[], object?>? Handler { get; set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler!(targetMethod!.Name, args ?? []);
}
