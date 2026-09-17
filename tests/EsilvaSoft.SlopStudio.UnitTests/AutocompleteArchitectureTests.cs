using EsilvaSoft.SlopStudio.LocalAi.Core;
using System.Reflection;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;

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
            .Where(type => type.Namespace == typeof(LanguageDefinition).Namespace)
            .SelectMany(type => type.GetFields(all).Select(field => (type, field.FieldType))
                .Concat(type.GetProperties(all).Select(property => (type, property.PropertyType)))
                .Concat(type.GetMethods(all).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)).Select(used => (type, used)))
                .Concat(type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters()).Select(parameter => (type, parameter.ParameterType))))
            .Where(pair => forbidden.Contains(pair.Item2))
            .Select(pair => pair.type.FullName + " → " + pair.Item2.Name)
            .ToArray();
        Assert.That(offenders, Is.Empty);
    }
}
