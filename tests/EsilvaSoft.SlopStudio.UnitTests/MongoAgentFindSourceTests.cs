using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoAgentFindSourceTests
{
    [Test]
    public void LiteralParserPreservesCanonicalBsonAndEnvironmentText()
    {
        const string ejson = """
            {"_id":{"$oid":"507f1f77bcf86cd799439011"},"long":{"$numberLong":"9007199254740993"},"decimal":{"$numberDecimal":"123.450"},"date":{"$date":{"$numberLong":"1700000000000"}},"uuid":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}},"legacy":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"03"}},"literal":"ENV.PRIVATE_CANARY"}
            """;

        var parsed = MongoAgentFindSource.ParseLiteral(ejson);

        Assert.Multiple(() =>
        {
            Assert.That(parsed["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(parsed["long"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
            Assert.That(parsed["decimal"].BsonType, Is.EqualTo(BsonType.Decimal128));
            Assert.That(parsed["date"].BsonType, Is.EqualTo(BsonType.DateTime));
            Assert.That(parsed["uuid"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(parsed["legacy"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(parsed["literal"].AsString, Is.EqualTo("ENV.PRIVATE_CANARY"));
        });
    }

    [Test]
    public void LiteralParserRejectsConstructorsCodeAndDuplicateProperties()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => MongoAgentFindSource.ParseLiteral("ObjectId('507f1f77bcf86cd799439011')"),
                Throws.Exception);
            Assert.That(() => MongoAgentFindSource.ParseLiteral("{\"$code\":\"return true\"}"),
                Throws.Exception);
            Assert.That(() => MongoAgentFindSource.ParseLiteral("{\"a\":1,\"a\":2}"),
                Throws.Exception);
        });
    }
}
