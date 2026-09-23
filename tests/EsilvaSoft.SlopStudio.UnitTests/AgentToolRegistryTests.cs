using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentToolRegistryTests
{
    private static readonly Guid PrincipalId = Guid.Parse("a1fba9ce-c3c4-4ee0-8c74-3e74cfc8f4a1");
    private static readonly string[] SummaryPropertyNames = ["id", "name", "readOnly"];
    private static readonly Guid SessionId = Guid.Parse("a6284a92-6942-4bd6-846a-cf879b08f17e");
    private static readonly Guid TurnId = Guid.Parse("6b8097c6-8313-4ed6-9a02-2e657fd9a387");
    private static AgentOutputDestination LocalDestination => AgentOutputDestination.Local();
    private static AgentOutputDataScope Metadata => AgentOutputDataScope.Metadata;

    [Test]
    public async Task ListConnectionsRequiresClosedEmptyObjectAndKnownTool()
    {
        var profiles = new StubProfileRepository([]);
        var policy = Policy(4);
        var registry = Registry(profiles, new SequencePolicyProvider(policy), new AgentPermissionEvaluator(new SequencePolicyProvider(policy)));
        var principal = Principal(4);

        var extra = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{\"approved\":true}");
        var control = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{\"principalId\":\"spoof\"}");
        var unknown = await registry.InvokeAsync(principal, Context(), LocalDestination, Metadata, "run_command", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(extra.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(control.ErrorCode, Is.EqualTo("InvalidArguments"));
            Assert.That(unknown.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task MissingOrUnreadablePolicyDeniesBeforeEnumeratingProfiles()
    {
        var profiles = new StubProfileRepository([Connection("restrita")]);
        var missingProvider = new SequencePolicyProvider([null]);
        var missing = Registry(profiles, missingProvider, new AgentPermissionEvaluator(missingProvider));
        var unreadableProvider = new SequencePolicyProvider(true);
        var unreadable = Registry(profiles, unreadableProvider, new AgentPermissionEvaluator(unreadableProvider));

        var missingResult = await missing.InvokeAsync(Principal(5), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var unreadableResult = await unreadable.InvokeAsync(Principal(5), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(missingResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(unreadableResult.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ListConnectionsReturnsOnlyProfilesWithExplicitConnectionGrantAndAllowlistedFields()
    {
        var allowed = Connection("permitida") with
        {
            ConnectionString = "mongodb://user:uri-canary@mongo-host:27017",
            Environment = "environment-canary",
            TargetHost = "target-canary",
            Tags = "tag-canary",
            Folder = "folder-canary"
        };
        var denied = Connection("negada");
        var policy = Policy(8, Grant(allowed));
        var provider = new SequencePolicyProvider(policy);
        var profiles = new StubProfileRepository([allowed, denied]);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(8), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        var root = output.RootElement;
        var items = root.GetProperty("connections").EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Length.EqualTo(1));
            Assert.That(items[0].GetProperty("id").GetString(), Is.EqualTo(allowed.Id.ToString()));
            Assert.That(items[0].GetProperty("name").GetString(), Is.EqualTo("permitida"));
            Assert.That(items[0].GetProperty("readOnly").GetBoolean(), Is.False);
            Assert.That(items[0].EnumerateObject().Select(property => property.Name), Is.EquivalentTo(SummaryPropertyNames));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("uri-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("mongo-host"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("environment-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("target-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("tag-canary"));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("folder-canary"));
        });
    }

    [Test]
    public async Task ListConnectionsCapsAuthorizedProfilesAndMarksTruncation()
    {
        var all = Enumerable.Range(0, 201).Select(index => Connection($"conexao-{index:D3}")).ToArray();
        var policy = Policy(9, all.Select(Grant).ToArray());
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository(all), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(9), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("connections").GetArrayLength(), Is.EqualTo(200));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ListConnectionsTruncatesAtUtf8ItemBoundariesAndNeverReturnsPartialJson()
    {
        var small = Connection("pequena");
        var tooLarge = Connection(string.Concat(Enumerable.Repeat("😀", 70_000)));
        var policy = Policy(14, Grant(small), Grant(tooLarge));
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository([small, tooLarge]), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(14), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(output.RootElement.GetProperty("connections").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(), Is.EqualTo("pequena"));
            Assert.That(output.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task ListConnectionsDeniesCorruptProfileCollectionWithoutReturningPartialOutput()
    {
        var invalidProfiles = new ConnectionProfile?[]
        {
            null,
            Connection("nome-nulo") with { Name = null! },
            Connection("nome-invalido") with { Name = "nome\uD800" },
            Connection("id-vazio") with { Id = Guid.Empty },
            Connection("generation-ausente") with { SourceGenerationId = null },
            Connection("generation-vazia") with { SourceGenerationId = Guid.Empty }
        };

        foreach (var invalidProfile in invalidProfiles)
        {
            var provider = new SequencePolicyProvider(Policy(15));
            var profiles = new StubProfileRepository([Connection("válida"), invalidProfile!]);
            var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

            var result = await registry.InvokeAsync(Principal(15), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"), $"Perfil inválido: {invalidProfile?.Name ?? "null"}");
            Assert.That(result.StructuredContentJson, Is.Null, "dados parciais não podem escapar");
        }

        var nullListProvider = new SequencePolicyProvider(Policy(15));
        var nullListRegistry = Registry(new StubProfileRepository(returnNull: true), nullListProvider, new AgentPermissionEvaluator(nullListProvider));
        var nullListResult = await nullListRegistry.InvokeAsync(Principal(15), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        Assert.That(nullListResult.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(nullListResult.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task ListConnectionsEnforcesDeadlineOnPolicyAndProfileReads()
    {
        var policyTimeoutRegistry = Registry(
            new StubProfileRepository([Connection("nao-enumerada")]),
            new BlockingPolicyProvider(),
            new AgentPermissionEvaluator(new BlockingPolicyProvider()),
            TimeSpan.FromMilliseconds(20));
        var policyTimeout = await policyTimeoutRegistry.InvokeAsync(Principal(16), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        var profileRepository = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Array.Empty<ConnectionProfile>();
            }
        };
        var policy = Policy(17);
        var profileProvider = new SequencePolicyProvider(policy);
        var profileTimeoutRegistry = Registry(profileRepository, profileProvider, new AgentPermissionEvaluator(profileProvider), TimeSpan.FromMilliseconds(20));
        var profileTimeout = await profileTimeoutRegistry.InvokeAsync(Principal(17), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(policyTimeout.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(profileTimeout.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(policyTimeout.StructuredContentJson, Is.Null);
            Assert.That(profileTimeout.StructuredContentJson, Is.Null);
        });
    }

    [Test]
    public async Task ListConnectionsDeadlineReturnsWhenRepositoryIgnoresCancellationAndObservesLateFault()
    {
        var lateOperation = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = _ => lateOperation.Task
        };
        var policy = Policy(20);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(30));

        var result = await registry.InvokeAsync(Principal(20), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}").WaitAsync(TimeSpan.FromSeconds(2));
        lateOperation.TrySetException(new InvalidOperationException("falha tardia não deve escapar"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(profiles.GetAllCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task CallerCancellationRemainsCancellationWhenRepositoryIgnoresToken()
    {
        var lateOperation = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new StubProfileRepository([Connection("nao-retornada")])
        {
            GetAllHandler = _ => lateOperation.Task
        };
        var policy = Policy(21);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var invocation = registry.InvokeAsync(Principal(21), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
        lateOperation.TrySetException(new InvalidOperationException("falha tardia não deve escapar"));
    }

    [Test]
    public async Task ListConnectionsRevalidatesPolicyRevisionBeforeReturningOutput()
    {
        var profile = Connection("permitida");
        var provider = new SequencePolicyProvider(Policy(10, Grant(profile)), Policy(10, Grant(profile)), Policy(11));
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(10), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task ListConnectionsHonorsCancellationBeforeProfileAccess()
    {
        var profiles = new StubProfileRepository([Connection("local")]);
        var policy = Policy(12, Grant(profiles.Profiles[0]));
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => registry.InvokeAsync(
            Principal(12), Context(), LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token));
        Assert.That(profiles.GetAllCalls, Is.Zero);
    }

    [TestCase("generation")]
    [TestCase("name")]
    [TestCase("readOnly")]
    [TestCase("id")]
    [TestCase("removed")]
    [TestCase("duplicate")]
    [TestCase("invalidName")]
    [TestCase("missingGeneration")]
    [TestCase("nullProfile")]
    [TestCase("nullCollection")]
    public async Task ListConnectionsDeniesChangedProjectedProfilesBeforeReturningOutput(string change)
    {
        var profile = Connection("permitida");
        IReadOnlyList<ConnectionProfile> final = change switch
        {
            "generation" => [profile with { SourceGenerationId = Guid.NewGuid() }],
            "name" => [profile with { Name = "renomeada" }],
            "readOnly" => [profile with { IsReadOnly = !profile.IsReadOnly }],
            "id" => [profile with { Id = Guid.NewGuid() }],
            "removed" => [],
            "duplicate" => [profile, profile],
            "invalidName" => [profile with { Name = "inválido\uD800" }],
            "missingGeneration" => [profile with { SourceGenerationId = null }],
            "nullProfile" => [null!],
            "nullCollection" => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var profiles = new SequenceProfileRepository([profile], final);
        var provider = new SequencePolicyProvider(Policy(22, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(22), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ListConnectionsAcceptsUnchangedProjectionWhenFinalOrderOrUnexposedFieldsChange()
    {
        var first = Connection("primeira");
        var second = Connection("segunda");
        var profiles = new SequenceProfileRepository([first, second],
            [second with { Tags = "não expor" }, first with { IsFavorite = true }]);
        var provider = new SequencePolicyProvider(Policy(23, Grant(first), Grant(second)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(23), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.Succeeded, Is.True);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var connections = json.RootElement.GetProperty("connections");
        Assert.That(connections.GetArrayLength(), Is.EqualTo(2));
        Assert.That(connections[0].GetProperty("name").GetString(), Is.EqualTo(first.Name));
        Assert.That(connections[1].GetProperty("name").GetString(), Is.EqualTo(second.Name));
        Assert.That(result.StructuredContentJson, Does.Not.Contain("não expor"));
        Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task FinalProfileReadFailureDoesNotExposeRepositoryError()
    {
        var profile = Connection("permitida");
        var profiles = new SequenceProfileRepository([profile], [profile])
        {
            FinalReadHandler = _ => throw new InvalidOperationException("mongodb://private:secret@host")
        };
        var provider = new SequencePolicyProvider(Policy(24, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(24), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
    }

    [Test]
    public async Task FinalProfileReadHonorsDeadlineWhenRepositoryIgnoresCancellation()
    {
        var profile = Connection("permitida");
        var lateRead = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new SequenceProfileRepository([profile], [profile]) { FinalReadHandler = _ => lateRead.Task };
        var provider = new SequencePolicyProvider(Policy(25, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider), TimeSpan.FromMilliseconds(30));

        var result = await registry.InvokeAsync(Principal(25), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}").WaitAsync(TimeSpan.FromSeconds(2));
        lateRead.TrySetException(new InvalidOperationException("falha tardia privada"));

        Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task FinalProfileReadHonorsCallerCancellationWhenRepositoryIgnoresToken()
    {
        var profile = Connection("permitida");
        var enteredFinalRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateRead = new TaskCompletionSource<IReadOnlyList<ConnectionProfile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profiles = new SequenceProfileRepository([profile], [profile])
        {
            FinalReadHandler = _ => { enteredFinalRead.SetResult(); return lateRead.Task; }
        };
        var provider = new SequencePolicyProvider(Policy(26, Grant(profile)));
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));
        using var cancellation = new CancellationTokenSource();

        var invocation = registry.InvokeAsync(Principal(26), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}", cancellation.Token);
        await enteredFinalRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
        lateRead.TrySetException(new InvalidOperationException("falha tardia privada"));
    }

    [Test]
    public async Task ListConnectionsRequiresInvocationAndExplicitOutputScopeBeforeProfileEnumeration()
    {
        var profiles = new StubProfileRepository([]);
        var policy = Policy(18);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var noContext = await registry.InvokeAsync(Principal(18), null, LocalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var noDestination = await registry.InvokeAsync(Principal(18), Context(), null, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var noScope = await registry.InvokeAsync(Principal(18), Context(), LocalDestination, null, AgentToolRegistry.ListConnectionsToolName, "{}");
        var wrongScope = await registry.InvokeAsync(Principal(18), Context(), LocalDestination, AgentOutputDataScope.DocumentValues, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(noContext.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(noDestination.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(noScope.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(wrongScope.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(profiles.GetAllCalls, Is.Zero);
        });
    }

    [Test]
    public async Task ExternalOutputRequiresMatchingProviderAndExactGenerationGrant()
    {
        var profile = Connection("externa");
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var externalContext = new AgentInvocationContext("provider-a", Guid.NewGuid(), SessionId, TurnId);
        var mismatchedContext = new AgentInvocationContext("provider-b", Guid.NewGuid(), SessionId, TurnId);
        var grant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value,
            AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id), externalDestination, Metadata);
        var policy = Policy(19, grant);
        var provider = new SequencePolicyProvider(policy);
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var mismatch = await registry.InvokeAsync(Principal(19), mismatchedContext, externalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
        var matching = await registry.InvokeAsync(Principal(19), externalContext, externalDestination, Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(mismatch.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(matching.Succeeded, Is.True);
    }

    [Test]
    public void ListConnectionsDescriptorPublishesClosedSchemasAndCanonicalReadMetadataGrant()
    {
        var policy = Policy(13);
        var registry = Registry(new StubProfileRepository([]), new SequencePolicyProvider(policy), new AgentPermissionEvaluator(new SequencePolicyProvider(policy)));
        var descriptor = registry.FindDescriptor(AgentToolRegistry.ListConnectionsToolName)!;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.Risk, Is.EqualTo(AgentToolRisk.ReadOnly));
            Assert.That(descriptor.RequiredPermissions, Is.EqualTo(new[] { AgentPermission.ReadMetadata }));
            Assert.That(registry.GetInputSchemaJson(descriptor.Name), Does.Contain("\"additionalProperties\":false"));
            Assert.That(registry.GetOutputSchemaJson(descriptor.Name), Does.Contain("\"additionalProperties\":false"));
        });
    }

    [TestCase("mongodb://user:uri-password-canary@private-host:27017", "uri-password-canary")]
    [TestCase("sk-proj-token-canary-0123456789abcdefghijklmnopqrstuv", "token-canary")]
    [TestCase("arbitrary-secret-canary-7F41B9", "arbitrary-secret-canary")]
    [TestCase("Produção", "Produção")]
    public async Task ExternalConnectionNamesAlwaysUseAliasesWhileLocalNamesRemainUnchanged(string name, string canary)
    {
        var profile = Connection(name);
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var externalContext = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var externalGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            externalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(27, Grant(profile), externalGrant));
        var registry = Registry(new StubProfileRepository([profile]), provider, new AgentPermissionEvaluator(provider));

        var external = await registry.InvokeAsync(Principal(27), externalContext, externalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");
        var local = await registry.InvokeAsync(Principal(27), Context(), LocalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(external.Succeeded, Is.True);
        Assert.That(local.Succeeded, Is.True);
        using var externalJson = JsonDocument.Parse(external.StructuredContentJson!);
        using var localJson = JsonDocument.Parse(local.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(externalJson.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(),
                Is.EqualTo($"Conexão {profile.Id:D}"));
            Assert.That(external.StructuredContentJson, Does.Not.Contain(canary));
            Assert.That(localJson.RootElement.GetProperty("connections")[0].GetProperty("name").GetString(), Is.EqualTo(name));
            Assert.That(Encoding.UTF8.GetByteCount(external.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
        });
    }

    [Test]
    public async Task ExternalAliasStillRevalidatesOriginalNameBeforeReturningOutput()
    {
        var profile = Connection("nome original");
        var externalDestination = AgentOutputDestination.ProviderExternal("provider-a");
        var externalContext = new AgentInvocationContext("provider-a", null, SessionId, TurnId);
        var externalGrant = new AgentPermissionGrant(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId),
            profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata, AgentNamespaceScope.ForConnection(profile.Id),
            externalDestination, Metadata);
        var provider = new SequencePolicyProvider(Policy(28, externalGrant));
        var profiles = new SequenceProfileRepository([profile], [profile with { Name = "nome alterado" }]);
        var registry = Registry(profiles, provider, new AgentPermissionEvaluator(provider));

        var result = await registry.InvokeAsync(Principal(28), externalContext, externalDestination, Metadata,
            AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(result.StructuredContentJson, Is.Null);
        Assert.That(profiles.GetAllCalls, Is.EqualTo(2));
    }

    private static AgentToolRegistry Registry(
        IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies,
        IAgentPermissionEvaluator evaluator,
        TimeSpan? timeout = null) => new(profiles, policies, evaluator, timeout);

    private static ConnectionProfile Connection(string name) =>
        ConnectionProfile.Create(name, "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentPrincipal Principal(long revision) => new(PrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant Grant(ConnectionProfile profile) =>
        new(PrincipalId, AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
            AgentNamespaceScope.ForConnection(profile.Id), LocalDestination, Metadata);

    private static AgentAuthorizationPolicySnapshot Policy(long revision, params AgentPermissionGrant[] grants) =>
        AgentAuthorizationPolicySnapshot.Load(PrincipalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

    private sealed class StubProfileRepository(IReadOnlyList<ConnectionProfile>? profiles = null, bool returnNull = false) : IConnectionProfileRepository
    {
        public int GetAllCalls { get; private set; }
        public IReadOnlyList<ConnectionProfile> Profiles { get; } = profiles ?? [];
        public Func<CancellationToken, Task<IReadOnlyList<ConnectionProfile>>>? GetAllHandler { get; init; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetAllCalls++;
            if (GetAllHandler is { } handler) return handler(cancellationToken);
            if (returnNull) return Task.FromResult<IReadOnlyList<ConnectionProfile>>(null!);
            return Task.FromResult(Profiles);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SequencePolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly AgentAuthorizationPolicySnapshot?[] _snapshots;
        private readonly bool _throws;
        private int _index;

        public SequencePolicyProvider(params AgentAuthorizationPolicySnapshot?[] snapshots) => _snapshots = snapshots;

        public SequencePolicyProvider(bool throws)
        {
            _snapshots = [];
            _throws = throws;
        }

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throws) throw new InvalidOperationException("private policy store detail");
            var index = Interlocked.Increment(ref _index) - 1;
            return Task.FromResult(_snapshots[Math.Min(index, _snapshots.Length - 1)]);
        }
    }

    private sealed class SequenceProfileRepository(
        IReadOnlyList<ConnectionProfile> initial,
        IReadOnlyList<ConnectionProfile> final) : IConnectionProfileRepository
    {
        public int GetAllCalls { get; private set; }
        public Func<CancellationToken, Task<IReadOnlyList<ConnectionProfile>>>? FinalReadHandler { get; init; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetAllCalls++;
            if (GetAllCalls == 1) return Task.FromResult(initial);
            return FinalReadHandler?.Invoke(cancellationToken) ?? Task.FromResult(final);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        public async Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
