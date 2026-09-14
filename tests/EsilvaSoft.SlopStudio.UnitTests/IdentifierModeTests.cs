using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class IdentifierModeTests
{
    private const string Hex = "66e3bd5b3bc3f54c840d73ac";
    // Independent fixture: the 12 ObjectId bytes followed by 4 zero bytes, grouped 8-4-4-4-12 by hand.
    private const string Equivalent = "66e3bd5b-3bc3-f54c-840d-73ac00000000";
    private const string Sample = "00112233-4455-6677-8899-aabbccddeeff";
    private static readonly IdentifierRepresentationMode[] ModeNames = [IdentifierRepresentationMode.Standard, IdentifierRepresentationMode.ObjectId, IdentifierRepresentationMode.UuidV4];

    private static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    private static readonly string Mixed = "{\"_id\":{\"$oid\":\"" + Hex + "\"},\"uuid\":" + Binary("00112233445566778899aabbccddeeff", "04")
        + ",\"legacy\":" + Binary("33221100554477668899aabbccddeeff", "03") + ",\"texto\":\"" + Hex + "\",\"textoUuid\":\"" + Sample + "\"}";

    private static IdentifierDisplayOptions Options(IdentifierRepresentationMode mode, UuidRepresentation uuid = UuidRepresentation.Standard) => new(mode, uuid);

    [Test]
    public void ModesAreThreeNamedValuesAndStandardMeansObjectIdPlusUuid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdentifierRepresentationService.All, Is.EqualTo(ModeNames));
            Assert.That(Enum.GetNames<IdentifierRepresentationMode>(), Has.None.Contains("StandardUuid"));
            Assert.That(JsonSerializer.Serialize(IdentifierRepresentationMode.UuidV4), Is.EqualTo("\"UuidV4\""));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.Standard), Is.EqualTo("Standard · ObjectId + UUID v4"));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.ObjectId), Is.EqualTo("ObjectId · MongoDB ObjectId"));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.UuidV4), Is.EqualTo("UUID v4 · BSON subtype 4"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.Standard), Does.StartWith("Aceita ObjectId e UUID v4.").And.Contain("preserva o tipo BSON original"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.ObjectId), Does.StartWith("Prioriza MongoDB ObjectId"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.UuidV4), Does.StartWith("Prioriza UUID padrão BSON subtype 4.").And.Contain("ObjectIds podem ser representados como UUID"));
            Assert.Throws<ArgumentOutOfRangeException>(() => IdentifierRepresentationService.DisplayName((IdentifierRepresentationMode)9));
        });
    }

    [Test]
    public void ObjectIdToUuidIsDeterministicReversibleAndMatchesIndependentBytes()
    {
        var uuid = IdentifierRepresentationService.ObjectIdToUuid(Hex);
        Assert.Multiple(() =>
        {
            Assert.That(uuid.ToString("D"), Is.EqualTo(Equivalent));
            Assert.That(Convert.ToHexStringLower(uuid.ToByteArray(bigEndian: true)), Is.EqualTo(Hex + "00000000"));
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid(Hex.ToUpperInvariant()), Is.EqualTo(uuid), "Maiúsculas não mudam os bytes.");
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid(Hex), Is.EqualTo(uuid), "Mesmo ObjectId, mesmo UUID.");
            Assert.That(IdentifierRepresentationService.TryUuidToObjectId(uuid, out var back), Is.True);
            Assert.That(back, Is.EqualTo(Hex));
            Assert.That(IdentifierRepresentationService.TryUuidToObjectId(Guid.Parse(Sample), out _), Is.False);
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid("000000000000000000000000"), Is.EqualTo(Guid.Empty));
            Assert.Throws<FormatException>(() => IdentifierRepresentationService.ObjectIdToUuid("66e3bd5b3bc3f54c840d73a"));
            Assert.Throws<FormatException>(() => IdentifierRepresentationService.ObjectIdToUuid("zze3bd5b3bc3f54c840d73ac"));
        });
    }

    [TestCase("_id", UuidRepresentation.Standard, IdentifierKind.ObjectId)]
    [TestCase("uuid", UuidRepresentation.Standard, IdentifierKind.Uuid)]
    [TestCase("legacy", UuidRepresentation.Standard, IdentifierKind.UnknownLegacyUuid)]
    [TestCase("legacy", UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid)]
    [TestCase("texto", UuidRepresentation.Standard, IdentifierKind.None)]
    [TestCase("textoUuid", UuidRepresentation.Standard, IdentifierKind.None)]
    public void BsonTypeHasPriorityAndLookalikeStringsStayStrings(string field, UuidRepresentation uuid, IdentifierKind expected)
    {
        using var document = JsonDocument.Parse(Mixed);
        Assert.That(IdentifierRepresentationService.DetectType(document.RootElement.GetProperty(field), uuid), Is.EqualTo(expected));
    }

    [TestCase(Hex, IdentifierKind.ObjectId)]
    [TestCase(Sample, IdentifierKind.Uuid)]
    [TestCase("00112233445566778899aabbccddeeff", IdentifierKind.Uuid)]
    [TestCase("ObjectId(\"" + Hex + "\")", IdentifierKind.ObjectId)]
    [TestCase("GUUID(\"" + Sample + "\")", IdentifierKind.Uuid)]
    [TestCase("66e3bd5b3bc3f54c840d73a", IdentifierKind.None)]
    [TestCase("texto", IdentifierKind.None)]
    public void TextualDetectionIsUsedOnlyWithoutBsonValues(string text, IdentifierKind expected)
    {
        foreach (var mode in ModeNames)
            Assert.That(IdentifierRepresentationService.DetectType(text, Options(mode)), Is.EqualTo(expected), mode.ToString());
    }

    private static IEnumerable<TestCaseData> ParsedForms() =>
        ParsedFormCases().Select(data => data.SetArgDisplayNames((string)data.Arguments[0]!, data.Arguments[1]!.ToString()!));

    private static IEnumerable<TestCaseData> ParsedFormCases()
    {
        var objectId = new BsonObjectId(ObjectId.Parse(Hex));
        var standard = new BsonBinaryData(Guid.Parse(Sample), GuidRepresentation.Standard);
        var csharp = new BsonBinaryData(Guid.Parse(Sample), GuidRepresentation.CSharpLegacy);
        yield return new TestCaseData("ObjectId(\"" + Hex.ToUpperInvariant() + "\")", UuidRepresentation.Standard, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData(" " + Hex + " ", UuidRepresentation.Standard, IdentifierKind.ObjectId, false, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("new ObjectId('" + Hex + "')", UuidRepresentation.Standard, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("{\"$oid\":\"" + Hex + "\"}", UuidRepresentation.JavaLegacy, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("UUID(\"" + Sample + "\")", UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, true, "UUID(\"" + Sample + "\")", standard);
        yield return new TestCaseData(Sample, UuidRepresentation.Standard, IdentifierKind.Uuid, false, "UUID(\"" + Sample + "\")", standard);
        yield return new TestCaseData(Sample.ToUpperInvariant().Replace("-", "", StringComparison.Ordinal), UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, false, "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")", csharp);
        yield return new TestCaseData(Binary("33221100554477668899aabbccddeeff", "03"), UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, true, "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")", csharp);
    }

    [TestCaseSource(nameof(ParsedForms))]
    public void ParseIdentifierAcceptsRequiredFormsInEveryModeWithDriverIdenticalBson(string text, UuidRepresentation uuid, IdentifierKind kind, bool isExplicit, string display, BsonValue oracle)
    {
        foreach (var mode in ModeNames)
        {
            var value = IdentifierRepresentationService.ParseIdentifier(text, Options(mode, uuid));
            Assert.Multiple(() =>
            {
                Assert.That((value.Kind, value.IsExplicitType, value.Text), Is.EqualTo((kind, isExplicit, display)), mode.ToString());
                Assert.That(BsonDocument.Parse("{\"v\":" + value.ExtendedJson + "}")["v"], Is.EqualTo(oracle));
                Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors("{\"v\":" + value.Text + "}"))["v"], Is.EqualTo(oracle));
                Assert.That(value.UuidEquivalent, kind == IdentifierKind.ObjectId ? Is.EqualTo(Equivalent) : Is.Null);
            });
        }
    }

    [TestCase("", "não é um identificador reconhecido")]
    [TestCase("66e3bd5b3bc3f54c840d73a", "não é um identificador reconhecido")]
    [TestCase("66e3bd5b3bc3f54c840d73acd", "não é um identificador reconhecido")]
    [TestCase("zze3bd5b3bc3f54c840d73ac", "não é um identificador reconhecido")]
    [TestCase("\"66e3bd5b3bc3f54c840d73ac\"", "não é um identificador reconhecido")]
    [TestCase("{\"a\":1}", "não é um identificador reconhecido")]
    [TestCase("{\"$oid\":\"123\"}", "não é um identificador reconhecido")]
    [TestCase("ObjectId(\"123\")", "ObjectId(\"123\") não contém um ObjectId válido. Use 24 dígitos hexadecimais. (linha 1, coluna 1)")]
    [TestCase("ObjectId(123)", "ObjectId(...) exige um único texto entre aspas")]
    [TestCase("UUID(\"123\")", "UUID(\"123\") não contém um UUID válido")]
    [TestCase("ObjectId(\"" + Hex + "\") extra", "não é um identificador reconhecido")]
    public void ParseIdentifierRejectsInvalidValuesWithVisibleReason(string text, string message)
    {
        var exception = Assert.Throws<FormatException>(() => IdentifierRepresentationService.ParseIdentifier(text, IdentifierDisplayOptions.Default));
        Assert.That(exception!.Message, Does.Contain(message));
        Assert.That(IdentifierRepresentationService.TryParseIdentifier(text, IdentifierDisplayOptions.Default, out _), Is.False);
    }

    [TestCase(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard, 1)]
    [TestCase(IdentifierRepresentationMode.ObjectId, UuidRepresentation.CSharpLegacy, 0)]
    [TestCase(IdentifierRepresentationMode.UuidV4, UuidRepresentation.JavaLegacy, 0)]
    public void HumanJsonUsesConstructorsForBothTypesAndRestoresIdenticalBson(IdentifierRepresentationMode mode, UuidRepresentation uuid, int unknown)
    {
        var display = IdentifierRepresentationService.FormatForDisplay(ExtendedJsonFormatter.Format(Mixed), Options(mode, uuid));
        Assert.Multiple(() =>
        {
            Assert.That(display.Text, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\"),"));
            Assert.That(display.Text, Does.Contain("\"texto\": \"" + Hex + "\"").And.Contain("\"textoUuid\": \"" + Sample + "\""), "Strings parecidas continuam strings.");
            Assert.That(display.Text, Does.Not.Contain(Equivalent), "O JSON exibido continua reinterpretável; a alternativa só aparece na árvore.");
            Assert.That(display.UnknownLegacyCount, Is.EqualTo(unknown));
            Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(display.Text)), Is.EqualTo(BsonDocument.Parse(Mixed)));
            Assert.That(UuidCodec.FormatForDisplay(Mixed, uuid).Text, Does.Contain("{\"$oid\":\"" + Hex + "\"}"), "O codec UUID continua limitado a UUIDs.");
        });
    }

    [Test]
    public void RewriteLeavesStringsCommentsAndMemberAccessUntouched()
    {
        const string text = "{\"a\":\"ObjectId(\\\"bad\\\")\", /* ObjectId(\"bad\") */ \"b\": x.ObjectId(\"bad\"), // ObjectId(\"bad\")\n \"c\": 'ObjectId(\"bad\")'}";
        Assert.That(IdentifierRepresentationService.RewriteConstructors(text), Is.EqualTo(text));
        Assert.That(IdentifierRepresentationService.RewriteConstructors("[anew ObjectId(\"" + Hex + "\")]"), Is.EqualTo("[anew {\"$oid\":\"" + Hex + "\"}]"));
        Assert.That(UuidCodec.RewriteConstructors("{\"_id\":ObjectId(\"bad\")}"), Is.EqualTo("{\"_id\":ObjectId(\"bad\")}"), "O codec UUID não interpreta ObjectId.");
    }

    [Test]
    public void TreeDescriptionsKeepTheStoredTypeAndOnlyUuidV4AppendsTheAlternative()
    {
        using var document = JsonDocument.Parse(Mixed);
        var id = document.RootElement.GetProperty("_id");
        var uuid = document.RootElement.GetProperty("uuid");
        foreach (var mode in ModeNames)
        {
            var description = ExtendedJsonValue.Describe(id, Options(mode));
            var expected = "ObjectId(\"" + Hex + "\")" + (mode == IdentifierRepresentationMode.UuidV4 ? " · UUID " + Equivalent : "");
            Assert.Multiple(() =>
            {
                Assert.That((description.TypeName, description.Display), Is.EqualTo(("ObjectId", expected)), mode.ToString());
                Assert.That(ExtendedJsonValue.Describe(uuid, Options(mode)).Display, Is.EqualTo("UUID(\"" + Sample + "\")"));
            });
        }
    }

    [TestCase(IdentifierRepresentationMode.Standard, new[] { IdentifierKind.ObjectId, IdentifierKind.Uuid })]
    [TestCase(IdentifierRepresentationMode.ObjectId, new[] { IdentifierKind.ObjectId })]
    [TestCase(IdentifierRepresentationMode.UuidV4, new[] { IdentifierKind.Uuid })]
    public void GenerationFollowsTheModeWithValidObjectIdsAndUuidV4(IdentifierRepresentationMode mode, IdentifierKind[] kinds)
    {
        var generated = IdentifierRepresentationService.Generate(Options(mode, UuidRepresentation.GoStandard));
        Assert.That(generated.Select(value => value.Kind), Is.EqualTo(kinds));
        foreach (var value in generated)
        {
            Assert.That(IdentifierRepresentationService.RewriteConstructors(value.Script), Is.EqualTo(value.ExtendedJson));
            var parsed = IdentifierRepresentationService.ParseIdentifier(value.Script, Options(mode, UuidRepresentation.GoStandard));
            if (value.Kind == IdentifierKind.ObjectId)
            {
                var objectId = BsonDocument.Parse("{\"v\":" + value.ExtendedJson + "}")["v"].AsObjectId;
                Assert.That(objectId.CreationTime, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(2)));
            }
            else
            {
                var guid = Guid.Parse(value.Script["GUUID(\"".Length..^2]);
                Assert.That((guid.Version, guid.Variant >> 2), Is.EqualTo((4, 2)), "UUID v4 com variante RFC 4122.");
                Assert.That(value.ExtendedJson, Does.Contain("\"subType\":\"04\""));
            }
            Assert.That(parsed.ExtendedJson, Is.EqualTo(value.ExtendedJson));
        }
        Assert.That(IdentifierRepresentationService.NewObjectId(), Is.Not.EqualTo(IdentifierRepresentationService.NewObjectId()));
        Assert.That(IdentifierRepresentationService.NewObjectId(DateTimeOffset.FromUnixTimeSeconds(0x66e3bd5b))[..8], Is.EqualTo("66e3bd5b"));
    }

    [Test]
    public void CrudScriptPlaceholdersFollowModeAndUuidRepresentation()
    {
        var objectId = ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col", identifiers: Options(IdentifierRepresentationMode.ObjectId, UuidRepresentation.CSharpLegacy));
        var uuid = ExplorerScripts.Create(ExplorerScriptOperation.Update, "db", "col", identifiers: Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy));
        var standard = ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col", identifiers: Options(IdentifierRepresentationMode.Standard, UuidRepresentation.JavaLegacy));
        Assert.Multiple(() =>
        {
            Assert.That(objectId, Does.Contain(".deleteMany({ _id: ObjectId(\"000000000000000000000000\") });").And.Not.Contain("UUID"));
            Assert.That(uuid, Does.Contain(".updateMany({ _id: CGUUID(\"00000000-0000-4000-8000-000000000000\") }, ").And.Not.Contain("ObjectId"));
            Assert.That(standard, Does.Contain("_id: ObjectId(\"000000000000000000000000\") /* ou JUUID(\"00000000-0000-4000-8000-000000000000\") */ }"));
            Assert.That(ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col"), Does.Contain("_id: ObjectId(\"000000000000000000000000\") /* ou UUID("));
            Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors("{\"_id\":" + IdentifierRepresentationService.ScriptIdentifierPlaceholder(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)) + "}"))["_id"].AsBsonBinaryData.SubType,
                Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(IdentifierRepresentationService.PlaceholderUuid.Version, Is.EqualTo(4));
        });
    }

    [Test]
    public void PolicySnapshotCarriesTheGlobalModeSeparatelyFromUuidOverrides()
    {
        var profile = Guid.NewGuid();
        var policy = new UuidDisplayPolicy(UuidRepresentation.JavaLegacy, new Dictionary<Guid, UuidRepresentation> { [profile] = UuidRepresentation.CSharpLegacy }, IdentifierRepresentationMode.UuidV4);
        Assert.Multiple(() =>
        {
            Assert.That(policy.ResolveOptions(profile), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)));
            Assert.That(policy.ResolveOptions(null), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.JavaLegacy)));
            Assert.That(UuidDisplayPolicy.Default.Mode, Is.EqualTo(IdentifierRepresentationMode.Standard));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UuidDisplayPolicy(UuidRepresentation.Standard, null, (IdentifierRepresentationMode)7));
        });
    }

    [TestCase("Standard")]
    [TestCase("JavaLegacy")]
    public async Task PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode(string uuid)
    {
        var path = NewDatabasePath();
        WriteRawSession(path, SessionJsonWithoutIdentifierMode(preferences => preferences["UuidRepresentation"] = uuid));
        using var context = new WorkspaceTestContext();
        var expected = Enum.Parse<UuidRepresentation>(uuid);
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = await repository.LoadSessionAsync();
            Assert.That((loaded.Preferences.IdentifierMode, loaded.Preferences.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.Standard, expected)));
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That((workspace.IdentifierMode, workspace.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.Standard, expected)));
            await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.UuidV4);
        }
        Assert.That(ReadRawSession(path), Does.Contain("\"IdentifierMode\":\"UuidV4\"").And.Contain("\"UuidRepresentation\":\"" + uuid + "\"").And.Contain("\"Theme\":\"Escuro\""));
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            using var restored = new WorkspaceViewModel(context.Workspace, repository);
            await restored.InitializeAsync();
            Assert.Multiple(() =>
            {
                Assert.That((restored.IdentifierMode, restored.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.UuidV4, expected)));
                Assert.That(restored.ActiveTab!.UuidPolicy.ResolveOptions(null), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, expected)));
                Assert.That(restored.IdentifierPreferences.Mode, Is.EqualTo(IdentifierRepresentationMode.UuidV4));
            });
        }
    }

    [TestCase("\"StandardUuid\"")]
    [TestCase("7")]
    public async Task UnknownPersistedIdentifierModeIsVisibleAndNeverOverwritten(string value)
    {
        var path = NewDatabasePath();
        var original = SessionJsonWithoutIdentifierMode(preferences => preferences["IdentifierMode"] = JsonNode.Parse(value));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.CatchAsync(() => repository.LoadSessionAsync());
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar"));
            Assert.CatchAsync<InvalidOperationException>(() => workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.ObjectId));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Mode, Is.EqualTo(IdentifierRepresentationMode.ObjectId), "A escolha permanece aplicada somente em memória.");
            Assert.CatchAsync(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }
        Assert.That(ReadRawSession(path), Is.EqualTo(original));
    }

    [Test]
    public async Task FailedModeSaveStaysVisibleAndThePreviewAdaptsToEachMode()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository();
        using var workspace = new WorkspaceViewModel(context.Workspace, repository);
        await workspace.InitializeAsync();
        var preferences = workspace.IdentifierPreferences;
        Assert.That((preferences.Mode, preferences.IsObjectIdPreviewVisible, preferences.IsUuidPreviewVisible), Is.EqualTo((IdentifierRepresentationMode.Standard, true, true)));
        preferences.SelectedChoice = preferences.Choices.Single(choice => choice.Value == IdentifierRepresentationMode.UuidV4);
        await preferences.ApplyTask;
        Assert.Multiple(() =>
        {
            Assert.That(preferences.HasError, Is.True);
            Assert.That(preferences.Status, Does.Contain("não salvo").And.Contain("disco indisponível"));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Mode, Is.EqualTo(IdentifierRepresentationMode.UuidV4));
            Assert.That(workspace.UuidRepresentation, Is.EqualTo(UuidRepresentation.Standard), "O modo não altera a representação UUID.");
            Assert.That(preferences.Description, Is.EqualTo(IdentifierRepresentationService.Description(IdentifierRepresentationMode.UuidV4)));
            Assert.That((preferences.ObjectIdPreview.Count, preferences.UuidPreview.Count, workspace.UuidPreferences.IsPreviewVisible), Is.EqualTo((0, 1, true)));
        });
        repository.FailSave = false;
        preferences.SelectedChoice = preferences.Choices.Single(choice => choice.Value == IdentifierRepresentationMode.ObjectId);
        await preferences.ApplyTask;
        Assert.Multiple(() =>
        {
            Assert.That((preferences.HasError, workspace.IdentifierMode), Is.EqualTo((false, IdentifierRepresentationMode.ObjectId)));
            Assert.That(preferences.ObjectIdPreview.Select(row => row.Code), Is.EqualTo(new[] { "ObjectId(\"" + Hex + "\")", Hex, Equivalent }));
            Assert.That((preferences.UuidPreview.Count, workspace.UuidPreferences.IsPreviewVisible), Is.EqualTo((0, false)));
        });
        await workspace.SetUuidRepresentationAsync(UuidRepresentation.CSharpLegacy);
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.Standard);
        preferences.Load(workspace.IdentifierMode);
        Assert.That(preferences.UuidPreview.Single().Code, Is.EqualTo("CGUUID(\"0F8FAD5B-D9CB-469F-A165-70867728950E\")"));
    }

    [Test]
    public async Task ModeChangeRerendersResultsButEditorCopiesAndExportKeepTheStoredTypes()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        string? replaced = null, precondition = null;
        context.Mongo.Handler = (method, args) =>
        {
            if (method == "QueryAsync") return Task.FromResult(new QueryPage([Mixed], TimeSpan.Zero, false));
            if (method == "ReplaceAsync") { precondition = (string)args[3]!; replaced = (string)args[4]!; return Task.FromResult(new DocumentMutationResult(1, 1)); }
            throw new InvalidOperationException(method);
        };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "db", Collection = "col", Mode = "Consulta JSON", Text = "{}", IsConnected = true };
        await tab.ExecuteCommand.ExecuteAsync(null);
        var standardResults = tab.Results;
        Assert.That(standardResults, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\")"));
        Assert.That(tab.SelectedDocument!.IdentityUuidEquivalent, Is.Null);

        tab.UuidPolicy = new UuidDisplayPolicy(UuidRepresentation.Standard, null, IdentifierRepresentationMode.UuidV4);
        var document = tab.SelectedDocument!;
        var documentNode = tab.ResultTree.First(node => node.IsDocument);
        documentNode.EnsureChildren();
        Assert.Multiple(() =>
        {
            Assert.That(tab.Results, Is.EqualTo(standardResults), "O JSON exibido não muda de tipo com o modo.");
            Assert.That(document.IdentityText, Is.EqualTo("_id: ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(documentNode.Children.Single(node => node.Name == "_id").Value, Is.EqualTo("ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(document.Fields[0].Label, Is.EqualTo("_id: ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(document.Fields[0].Children, Is.Empty);
            Assert.That(document.IdentityValueText, Is.EqualTo("ObjectId(\"" + Hex + "\")"));
            Assert.That(document.IdentityUuidEquivalent, Is.EqualTo(Equivalent));
            Assert.That(document.IdentityScript, Is.EqualTo("db.getCollection(\"col\").find({ _id: ObjectId(\"" + Hex + "\") })"));
            Assert.That(document.Json, Is.EqualTo(Mixed));
            Assert.That(tab.Metrics, Does.Contain("IDs UUID v4 · UUID Standard"));
            Assert.That(QueryResultExportSerializer.Serialize(tab.Documents), Does.Contain("$oid").And.Not.Contain("ObjectId(").And.Not.Contain(Equivalent));
        });
        using var editor = await tab.CreateDocumentMutationAsync("Editar");
        Assert.That(editor.Text, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\")").And.Not.Contain(Equivalent));
        await editor.ExecuteConfirmedAsync();
        var written = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(replaced!));
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(BsonDocument.Parse(Mixed)));
            Assert.That(written["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(precondition, Does.Contain("$oid").And.Not.Contain("ObjectId("));
        });
    }

    [Test]
    public async Task ExplorerScriptsToolsConsoleAndImportUseTheCentralRules()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Legacy", "mongodb://host/db");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        await workspace.SetProfileUuidRepresentationAsync(profile.Id, UuidRepresentation.CSharpLegacy);
        var node = new ExplorerNodeViewModel(context.Workspace, profile, "col", "db", "col");
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.UuidV4);
        Assert.That(workspace.OpenScript(node, ExplorerScriptOperation.Delete).Text, Does.Contain("_id: CGUUID(\"00000000-0000-4000-8000-000000000000\")"));
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.ObjectId);
        Assert.That(workspace.OpenScript(node, ExplorerScriptOperation.Update).Text, Does.Contain("_id: ObjectId(\"000000000000000000000000\") }").And.Not.Contain("UUID"));

        var tools = new MainWindowViewModel(context.Workspace, autoLoadCollections: false) { IdentifierMode = IdentifierRepresentationMode.Standard, UuidRepresentation = UuidRepresentation.Standard };
        tools.GenerateIdentifierCommand.Execute(null);
        var scripts = tools.IdentifierSnippet.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(scripts, Has.Length.EqualTo(2));
            Assert.That(scripts[0], Does.Match("^ObjectId\\(\"[0-9a-f]{24}\"\\)$"));
            Assert.That(scripts[1], Does.Match("^UUID\\(\"[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\"\\)$"));
            Assert.That(string.Join("\n", scripts.Select(IdentifierRepresentationService.RewriteConstructors)), Is.EqualTo(tools.IdentifierExtendedJsonSnippet));
            Assert.That(tools.IdentifierLabel, Does.StartWith("Identificadores Standard · UUID Standard"));
        });
        tools.IdentifierInput = Hex;
        tools.InterpretIdentifierCommand.Execute(null);
        Assert.That(tools.IdentifierInterpretation, Does.Contain("Tipo: ObjectId (inferido do texto)").And.Contain("UUID equivalente: " + Equivalent).And.Contain("Filtro: { _id: ObjectId(\"" + Hex + "\") }"));
        tools.IdentifierInput = "JUUID(\"bad\")";
        tools.InterpretIdentifierCommand.Execute(null);
        Assert.That(tools.IdentifierInterpretation, Does.Contain("JUUID(\"bad\") não contém um UUID válido"));

        workspace.BindActiveTab(profile, "db", "col");
        var display = new ResultDocumentViewModel(Mixed, 0, Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)).DisplayJson;
        workspace.OpenDocumentInEditor(workspace.ActiveTab!, display);
        Assert.That(workspace.ActiveTab!.Text, Does.Contain("ObjectId(").And.Contain("CGUUID("));
        var runtime = new ConsoleRuntime(context.Repository, context.Repository, new SessionConnectionSecretStore(), new WorkspaceConsoleSession((IMongoWorkspaceService)context.Mongo), context.Repository, context.Repository);
        var result = await runtime.ExecuteAsync(new(profile, "db", workspace.ActiveTab.Text, SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(BsonDocument.Parse(result.Results[^1].Json), Is.EqualTo(BsonDocument.Parse(Mixed)));

        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "identifier-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "collection-001.extended.json");
            await File.WriteAllTextAsync(file, "[{\"_id\": ObjectId(\"" + Hex + "\"), \"t\": \"ObjectId('x')\"}, {\"_id\": new ObjectId('" + Hex.ToUpperInvariant() + "')}]");
            var documents = await MongoWorkspaceService.ReadExportDocumentsAsync(file, CancellationToken.None);
            Assert.That(documents[0]["_id"], Is.EqualTo(new BsonObjectId(ObjectId.Parse(Hex))));
            Assert.That(documents[1]["_id"], Is.EqualTo(documents[0]["_id"]));
            Assert.That(documents[0]["t"].AsString, Is.EqualTo("ObjectId('x')"));
            await File.WriteAllTextAsync(file, "[{\"_id\": ObjectId(\"12\")}]");
            Assert.That(Assert.ThrowsAsync<ArgumentException>(() => MongoWorkspaceService.ReadExportDocumentsAsync(file, CancellationToken.None))!.Message, Does.Contain("ObjectId(\"12\") não contém um ObjectId válido"));
        }
        finally { Directory.Delete(directory, true); }
        var service = new MongoWorkspaceService();
        Assert.That(Assert.ThrowsAsync<ArgumentException>(() => service.InsertAsync(ConnectionProfile.Create("Local", "mongodb://localhost:27017"), "catalogo", "clientes", "{ \"_id\": ObjectId(\"12\") }"))!.Message,
            Does.Contain("ObjectId(\"12\") não contém um ObjectId válido"), "Falha antes de abrir conexão.");
    }

    private static string NewDatabasePath() =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "identifier-session-" + Guid.NewGuid().ToString("N"), "workspace.db");

    private static string SessionJsonWithoutIdentifierMode(Action<JsonObject>? change = null)
    {
        var session = JsonSerializer.SerializeToNode(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } })!.AsObject();
        var preferences = session["Preferences"]!.AsObject();
        preferences.Remove(nameof(WorkspacePreferences.IdentifierMode));
        change?.Invoke(preferences);
        return session.ToJsonString();
    }

    private static void WriteRawSession(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
    }

    private static string ReadRawSession(string path)
    {
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        return database.GetCollection("workspaceSession").FindById("current")["json"].AsString;
    }
}
