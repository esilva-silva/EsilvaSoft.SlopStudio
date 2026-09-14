using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class DatabaseDropRequestTests
{
    [Test]
    public void ValidateAcceptsUserDatabaseWithExactConfirmation()
    {
        var request = new DatabaseDropRequest("catalogo", "catalogo");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("admin")]
    [TestCase("config")]
    [TestCase("local")]
    public void ValidateRejectsProtectedDatabase(string database)
    {
        var request = new DatabaseDropRequest(database, database);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsDifferentConfirmation()
    {
        var request = new DatabaseDropRequest("catalogo", "Catalogo");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
