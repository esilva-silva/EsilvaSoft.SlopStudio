using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class LocalScriptFileServiceTests
{
    [Test]
    public async Task SaveAndLoadRoundTripPreservesScript()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"slopdataadmin-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "consulta.js");
        const string script = "const resultado = await db.clientes.find({ ativo: true }).toArray();";

        try
        {
            var service = new LocalScriptFileService();
            await service.SaveAsync(path, script);

            Assert.That(await service.LoadAsync(path), Is.EqualTo(script));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public void SaveWithNonJavaScriptExtensionThrows()
    {
        var service = new LocalScriptFileService();

        Assert.That(() => service.SaveAsync(Path.Combine(Path.GetTempPath(), "consulta.txt"), "db.test.find()"), Throws.TypeOf<ArgumentException>());
    }
}
