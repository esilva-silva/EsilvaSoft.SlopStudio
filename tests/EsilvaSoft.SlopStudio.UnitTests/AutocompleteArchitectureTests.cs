using EsilvaSoft.SlopStudio.LocalAi.Core;
using System.Reflection;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AutocompleteArchitectureTests
{
    [Test]
    public void ApplicationDependsOnlyOnTheBaseLibraryAndCore()
    {
        var references = typeof(LanguageDefinition).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.That(references.Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name is not ("netstandard" or "mscorlib" or "EsilvaSoft.SlopStudio.Core")), Is.Empty);
    }

    [Test]
    public void LanguageLayerNeverReferencesTheAiRuntime()
    {
        Type[] forbidden = [typeof(ILocalAiModelService), typeof(ILocalModelRuntime), typeof(ITokenizer), typeof(IAutocompleteService)];
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = typeof(LanguageDefinition).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(LanguageRoot, StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => (type, parameter.ParameterType))))
            .Where(pair => forbidden.Contains(pair.Item2))
            .Select(pair => pair.type.FullName + " → " + pair.Item2.Name)
            .ToArray();
        Assert.That(offenders, Is.Empty);
    }
    /// <summary>Raiz do núcleo determinístico; os subespaços .Context/.Completion/.Syntax/.Text contam como núcleo.</summary>
    private const string LanguageRoot = "EsilvaSoft.SlopStudio.Autocomplete.Core";

    [Test]
    public void LanguageCoreCoversEverySubNamespace()
    {
        // Guarda do próprio guarda: se o núcleo deixasse de ter subespaços, o filtro por prefixo varreria nada e os
        // testes de isolamento passariam vazios para sempre.
        var namespaces = typeof(LanguageDefinition).Assembly.GetTypes()
            .Select(type => type.Namespace).Where(name => name?.StartsWith(LanguageRoot, StringComparison.Ordinal) == true)
            .Distinct().ToArray();
        Assert.That(namespaces, Does.Contain(LanguageRoot + ".Context").And.Contain(LanguageRoot + ".Completion")
            .And.Contain(LanguageRoot + ".Syntax").And.Contain(LanguageRoot + ".Text"));
    }

    [Test]
    public void LanguageCoreNeverReferencesInfrastructureOrRuntimeAssemblies()
    {
        // A permissão genérica a System* deixaria passar um runtime gerenciado futuro; esta negativa é explícita por
        // nome de assembly e não depende de o tipo proibido já existir no código de hoje.
        string[] forbidden = ["Microsoft.ML", "MongoDB", "LiteDB", "Avalonia", "Jint", "Acornima",
            "EsilvaSoft.SlopStudio.Application", "EsilvaSoft.SlopStudio.Infrastructure",
            "EsilvaSoft.SlopStudio.Desktop", "EsilvaSoft.SlopStudio.LocalAi"];
        var references = typeof(LanguageDefinition).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
        Assert.That(references.Where(name => forbidden.Any(bad => name.Contains(bad, StringComparison.Ordinal))), Is.Empty);
    }

    [Test]
    public void LanguageCoreNeverTouchesTheNetwork()
    {
        // Funcionar offline é requisito do autocomplete determinístico, e "não começa com System" não barra System.Net.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        static bool IsNetwork(Type type) => type.Namespace?.StartsWith("System.Net", StringComparison.Ordinal) == true;
        var offenders = typeof(LanguageDefinition).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used))))
            .Where(pair => IsNetwork(pair.Item2))
            .Select(pair => pair.type.FullName + " -> " + pair.Item2.FullName).ToArray();
        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void TheTextOnlyShapeWalkerOverloadStaysAvailable()
    {
        // A sobrecarga por texto é contrato público; a sobrecarga nova por tokens foi acrescentada ao lado dela.
        var overload = typeof(ShapeWalker).GetMethod(nameof(ShapeWalker.Walk), BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(int), typeof(CompletionCursorRole), typeof(LanguageDefinition), typeof(CancellationToken)]);
        Assert.That(overload, Is.Not.Null);
    }
}
