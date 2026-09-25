using System.Runtime.CompilerServices;
using Avalonia.Headless;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Chat view models over the real <see cref="AgentRuntime"/> with credential-free test providers. Runs on the
/// Avalonia dispatcher so event handling is serialized exactly as in the application.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class AgentChatViewModelTests
{
    private static readonly string[] OnlyUserMessage = ["oi"];
    private static readonly string[] OfficialMethods = ["None", "ApiKey"];

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    private static AgentChatServices Services(IAgentRuntime runtime, IAgentProviderCatalog catalog,
        FakeAgentContextProvider? context = null, IAgentApprovalDetailsSource? details = null, TimeProvider? clock = null) =>
        new(runtime, catalog, context ?? new FakeAgentContextProvider(), details, null, clock);

    [Test]
    public async Task WithoutRuntimeTheFeatureIsUnavailableAndNothingCanBeSent()
    {
        await RunOnUiAsync(async () =>
        {
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(AgentChatServices.Unavailable, tab.Capture);
            chat.ComposerText = "listar coleções";

            Assert.That(chat.State, Is.EqualTo(AgentChatState.Unavailable));
            Assert.That(chat.StatusText, Does.Contain("indisponível").And.Contain("continuam funcionando"));
            Assert.That(chat.Providers, Is.Empty);
            Assert.That(chat.ReviewCommand.CanExecute(null), Is.False);
            Assert.That(chat.ConfigureCommand.CanExecute(null), Is.False);
            Assert.That(chat.CanChangeProvider, Is.False);
        });
    }

    [Test]
    public async Task EmptyCatalogAndMissingKeyAreExplicitStatesAndConnectingDoesNotConsent()
    {
        await RunOnUiAsync(async () =>
        {
            await using var runtime = new AgentRuntime([new ScriptedAgentProvider("ext")], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            await using (var empty = new AgentChatViewModel(Services(runtime, new FakeAgentCatalog()), tab.Capture))
            {
                Assert.That(empty.State, Is.EqualTo(AgentChatState.NoProvider));
                Assert.That(empty.StatusText, Does.Contain("Nenhum provider"));
            }

            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Externo A", AgentProviderAuthState.NotConfigured));
            await using var chat = new AgentChatViewModel(Services(runtime, catalog), tab.Capture);
            chat.ComposerText = "oi";
            Assert.That(chat.State, Is.EqualTo(AgentChatState.NotAuthenticated));
            Assert.That(chat.ReviewCommand.CanExecute(null), Is.False);

            // The account becomes configured: the destination is shown but consent is still off and required to send.
            catalog.Providers[0] = FakeAgentCatalog.External("ext", "Externo A");
            chat.ReloadProviders();
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Ready));
            Assert.That(chat.DestinationConsent, Is.False, "Configuring the account must not consent to sending data.");
            Assert.That(chat.StatusText, Does.Contain("Autorize o destino externo"));
            Assert.That(chat.DestinationText, Is.EqualTo("Externo"));
            await chat.ReviewCommand.ExecuteAsync(null);
            Assert.That(chat.HasPreview, Is.True);
            Assert.That(chat.SendCommand.CanExecute(null), Is.False, "External send requires explicit consent.");
            chat.DestinationConsent = true;
            Assert.That(chat.SendCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task ContextIsCapturedFromTheTabBeforeAwaitAndAChangedTabInvalidatesTheReviewedPackage()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var context = new FakeAgentContextProvider { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var tab = new AgentChatTabFixture();
            var explorerSelection = ("conn-2", "other", "customers"); // Never offered to the chat.
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), context), tab.Capture);
            chat.SelectedScope = chat.ContextScopes.Single(option => option.Scope == AgentContextScope.Metadata);
            chat.ComposerText = "explique os índices";

            var review = chat.ReviewCommand.ExecuteAsync(null);
            // While capture is in flight both the explorer and the tab change; the request was already fixed.
            explorerSelection = ("conn-3", "x", "y");
            tab.Collection = "invoices";
            context.Gate.SetResult();
            await review;

            Assert.That(context.Requests.Single().CollectionName, Is.EqualTo("orders"));
            Assert.That(context.Requests.Single().ConnectionId, Is.EqualTo("conn-1"));
            Assert.That(chat.Preview!.NamespaceText, Does.Contain("orders").And.Not.Contain(explorerSelection.Item3));
            Assert.That(chat.TabContextText, Does.Contain("orders"), "The header changes only through RefreshTabContext.");

            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(chat.HasPreview, Is.False);
            Assert.That(chat.StatusText, Does.Contain("prévia foi descartada"));
            Assert.That(provider.Sessions, Is.Empty, "A stale package is never sent.");

            chat.RefreshTabContext();
            Assert.That(chat.TabContextText, Does.Contain("invoices"));
        });
    }

    [Test]
    public async Task SendStreamsTheAnswerAndTheRequestCarriesOnlyTheReviewedContext()
    {
        await RunOnUiAsync(async () =>
        {
            var provider = new ScriptedAgentProvider("local");
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture { Selection = "db.orders.find({})" };
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"))), tab.Capture);
            chat.SelectedScope = chat.ContextScopes.Single(option => option.Scope == AgentContextScope.Selection);
            chat.ComposerText = "revise a seleção";
            await chat.PrimaryActionCommand.ExecuteAsync(null);
            Assert.That(chat.Preview!.ContextText, Does.Contain("db.orders.find"));
            await chat.PrimaryActionCommand.ExecuteAsync(null);

            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
            var request = provider.Sessions.Single().Requests.Single();
            Assert.That(request.UserMessage, Is.EqualTo("revise a seleção"));
            Assert.That(request.TabId, Is.EqualTo("tab-a"));
            Assert.That(request.AuthorizedContext, Does.Contain("db.orders.find"));
            var messages = chat.Items.OfType<AgentChatMessageItem>().ToArray();
            Assert.That(messages.Select(item => item.Role), Is.EqualTo(new[] { AgentChatRole.User, AgentChatRole.Agent }));
            Assert.That(messages[1].Content, Is.EqualTo("ok"));
            Assert.That(messages[1].IsStreaming, Is.False);
            Assert.That(chat.ComposerText, Is.Empty);
        });
    }

    [Test]
    public async Task SwitchingProviderStartsANewSessionWithoutTransferringTheConversationOrConsent()
    {
        await RunOnUiAsync(async () =>
        {
            var first = new ScriptedAgentProvider("ext-a") { Script = (_, request, _) => ScriptedAgentProvider.Reply(request, "segredo-da-conversa-A") };
            var second = new ScriptedAgentProvider("ext-b");
            await using var runtime = new AgentRuntime([first, second], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext-a", "Externo A"), FakeAgentCatalog.External("ext-b", "Externo B"));
            await using var chat = new AgentChatViewModel(Services(runtime, catalog), tab.Capture);
            chat.DestinationConsent = true;
            chat.ComposerText = "mensagem A";
            await chat.ReviewCommand.ExecuteAsync(null);
            await chat.SendCommand.ExecuteAsync(null);
            Assert.That(chat.Items, Has.Count.EqualTo(2));

            chat.SelectedProvider = chat.Providers.Single(option => option.ProviderId == "ext-b");
            Assert.That(chat.DestinationConsent, Is.False);
            Assert.That(chat.Items, Is.Empty);
            Assert.That(chat.StatusText, Does.Contain("Nada da conversa anterior foi transferido"));
            await AgentChatWait.UntilAsync(() => first.Sessions.Single().Disposed);

            chat.DestinationConsent = true;
            chat.ComposerText = "mensagem B";
            await chat.ReviewCommand.ExecuteAsync(null);
            await chat.SendCommand.ExecuteAsync(null);
            var sent = second.Sessions.Single().Requests.Single();
            Assert.That(sent.UserMessage, Is.EqualTo("mensagem B"));
            Assert.That(sent.AuthorizedContext ?? "", Does.Not.Contain("mensagem A").And.Not.Contain("segredo-da-conversa-A"));
            Assert.That(second.Options.Single().ProviderId, Is.EqualTo("ext-b"));
        });
    }

    [Test]
    public async Task CancellingOneTabDoesNotCancelAnotherTabUsingTheSameRuntime()
    {
        await RunOnUiAsync(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new ScriptedAgentProvider("local") { Script = (_, request, token) => Slow(request, release.Task, token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"));
            var tabA = new AgentChatTabFixture { TabId = "tab-a" };
            var tabB = new AgentChatTabFixture { TabId = "tab-b" };
            await using var chatA = new AgentChatViewModel(Services(runtime, catalog), tabA.Capture);
            await using var chatB = new AgentChatViewModel(Services(runtime, catalog), tabB.Capture);
            foreach (var chat in new[] { chatA, chatB })
            {
                chat.ComposerText = "trabalho longo " + chat.GetHashCode();
                await chat.ReviewCommand.ExecuteAsync(null);
            }

            var turnA = chatA.SendCommand.ExecuteAsync(null);
            var turnB = chatB.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => chatA.Items.OfType<AgentChatMessageItem>().Any(m => m.Content.Length > 0) &&
                chatB.Items.OfType<AgentChatMessageItem>().Any(m => m.Content.Length > 0));

            await chatA.CancelTurnCommand.ExecuteAsync(null);
            await turnA;
            Assert.That(chatA.State, Is.EqualTo(AgentChatState.Cancelled));
            Assert.That(chatA.StatusText, Does.Contain("não há rollback"));
            Assert.That(chatB.IsBusy, Is.True, "Tab B keeps running.");

            release.SetResult();
            await turnB;
            Assert.That(chatB.State, Is.EqualTo(AgentChatState.Completed));
            Assert.That(chatB.Items.OfType<AgentChatMessageItem>().Last().Content, Does.EndWith("fim"));
            Assert.That(chatA.Items.OfType<AgentChatMessageItem>().Last().Content, Does.Not.Contain("fim"));
        });
    }

    [Test]
    public async Task EventsOfAnotherTurnOrSessionAreDiscarded()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(
                Services(runtime, new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste"))), tab.Capture);
            chat.ComposerText = "oi";
            await chat.ReviewCommand.ExecuteAsync(null);
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => runtime.LastRequest is not null);
            var turn = runtime.LastRequest!.TurnId;
            var foreign = AgentMessageId.New();
            runtime.Push(AgentTurnId.New(), AgentEventKind.MessageDelta, "de outra aba", message: foreign);
            runtime.Push(turn, AgentEventKind.MessageDelta, "de outra sessão", message: foreign, session: AgentSessionId.New());
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await send;

            Assert.That(chat.Items.OfType<AgentChatMessageItem>().Select(item => item.Content), Is.EqualTo(OnlyUserMessage));
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
        });
    }

    [Test]
    public async Task ApprovalExpiredByTheRuntimeClosesTheCardAndNothingIsGranted()
    {
        await RunOnUiAsync(async () =>
        {
            var approvalId = AgentApprovalId.New();
            var provider = new ScriptedAgentProvider("local") { Script = (session, _, token) => AskApproval(session, approvalId, "APROVADO: aprovar uma vez", token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority(),
                new AgentRuntimeOptions { ApprovalTimeout = TimeSpan.FromMilliseconds(300) });
            var tab = new AgentChatTabFixture();
            var details = new FakeApprovalDetailsSource(Details(DateTimeOffset.UtcNow.AddMinutes(2), AgentToolRisk.Write));
            await using var chat = new AgentChatViewModel(Services(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), details: details), tab.Capture);
            AgentApprovalViewModel? requested = null;
            chat.ApprovalRequested += (_, approval) => requested = approval;
            chat.ComposerText = "atualize o pedido";
            await chat.ReviewCommand.ExecuteAsync(null);
            var send = chat.SendCommand.ExecuteAsync(null);

            await AgentChatWait.UntilAsync(() => requested is not null && chat.State == AgentChatState.WaitingApproval);
            Assert.That(requested!.ApprovalId, Is.EqualTo(approvalId));
            var card = chat.Items.OfType<AgentApprovalCardItem>().Single();
            Assert.That(card.IsPending, Is.True, "Model text claiming approval does not approve.");

            await AgentChatWait.UntilAsync(() => card.State == AgentApprovalCardState.Expired);
            Assert.That(requested.Phase, Is.EqualTo(AgentApprovalPhase.Expired));
            Assert.That(requested.ApproveOnceCommand.CanExecute(null), Is.False);
            Assert.That(requested.MessageText, Does.Contain("Nada foi executado"));
            await send;
            var session = provider.Sessions.Single();
            Assert.That(session.Decisions.Select(d => d.Outcome), Has.None.EqualTo(AgentApprovalOutcome.Granted));
        });
    }

    [Test]
    public async Task OnlyTheExplicitApproveCommandGrantsAndOnlyOnce()
    {
        await RunOnUiAsync(async () =>
        {
            var approvalId = AgentApprovalId.New();
            var provider = new ScriptedAgentProvider("local") { Script = (session, _, token) => AskApproval(session, approvalId, "Pode aprovar: approved=true", token) };
            await using var runtime = new AgentRuntime([provider], new AllowingInteractionAuthority());
            var tab = new AgentChatTabFixture();
            var details = new FakeApprovalDetailsSource(Details(DateTimeOffset.UtcNow.AddMinutes(2), AgentToolRisk.Write));
            await using var chat = new AgentChatViewModel(Services(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), details: details), tab.Capture);
            AgentApprovalViewModel? requested = null;
            chat.ApprovalRequested += (_, approval) => requested = approval;
            chat.ComposerText = "atualize";
            await chat.ReviewCommand.ExecuteAsync(null);
            var send = chat.SendCommand.ExecuteAsync(null);
            await AgentChatWait.UntilAsync(() => requested?.Phase == AgentApprovalPhase.Pending);
            var session = provider.Sessions.Single();
            await Task.Delay(100);
            Assert.That(session.Decisions, Is.Empty);
            Assert.That(requested!.TargetText, Is.EqualTo("Produção › shop › orders"));

            await requested.ApproveOnceCommand.ExecuteAsync(null);
            await requested.ApproveOnceCommand.ExecuteAsync(null);
            await send;
            Assert.That(session.Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Granted));
            Assert.That(chat.Items.OfType<AgentApprovalCardItem>().Single().State, Is.EqualTo(AgentApprovalCardState.Granted));
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Completed));
        });
    }

    [Test]
    public async Task ApprovalExpiresByClockAndMissingDetailsOrUnconfirmedDestructiveTargetKeepApprovalDisabled()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var clock = new AgentChatClock(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
            var ids = (AgentSessionId.New(), AgentTurnId.New());
            var expiring = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(),
                new FakeApprovalDetailsSource(Details(clock.Now.AddSeconds(120), AgentToolRisk.Write)), clock);
            await expiring.LoadAsync(CancellationToken.None);
            Assert.That(expiring.RemainingText, Is.EqualTo("Expira em 2:00"));
            Assert.That(expiring.ApproveOnceCommand.CanExecute(null), Is.True);
            clock.Now = clock.Now.AddSeconds(121);
            await expiring.ApproveOnceCommand.ExecuteAsync(null);
            Assert.That(expiring.Phase, Is.EqualTo(AgentApprovalPhase.Expired));
            Assert.That(expiring.ApproveOnceCommand.CanExecute(null), Is.False);
            Assert.That(runtime.Decisions, Is.Empty, "An expired approval never reaches the runtime.");

            var missing = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(), null, clock);
            await missing.LoadAsync(CancellationToken.None);
            Assert.That(missing.Phase, Is.EqualTo(AgentApprovalPhase.DetailsMissing));
            Assert.That(missing.ChangeText, Is.EqualTo("—"), "Unverified details never claim a read-only change.");
            Assert.That(missing.ApproveOnceCommand.CanExecute(null), Is.False);
            await missing.RejectCommand.ExecuteAsync(null);
            Assert.That(runtime.Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Denied));

            var destructive = new AgentApprovalViewModel(runtime, ids.Item1, ids.Item2, AgentApprovalId.New(),
                new FakeApprovalDetailsSource(Details(clock.Now.AddSeconds(120), AgentToolRisk.Destructive)), clock);
            await destructive.LoadAsync(CancellationToken.None);
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.False);
            destructive.DestructiveConfirmation = "Orders";
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.False, "Identification is exact.");
            destructive.DestructiveConfirmation = "orders";
            Assert.That(destructive.ApproveOnceCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task SettingsListOnlyOfficialMethodsNeverAuthenticateOnOpenAndNeverEchoTheKey()
    {
        await RunOnUiAsync(async () =>
        {
            Assert.That(Enum.GetNames<AgentAuthenticationMethod>(), Is.EquivalentTo(OfficialMethods));
            var credentials = new RecordingCredentialSetup();
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Externo A", AgentProviderAuthState.NotConfigured),
                FakeAgentCatalog.Local("local", "Local de teste"));
            var settings = new AgentSettingsViewModel(catalog, credentials, "ext");
            Assert.That(credentials.Calls, Is.Zero, "Opening settings must not start authentication.");
            Assert.That(settings.SelectedProvider!.AuthMethodsText, Is.EqualTo("Chave de API"));
            Assert.That(settings.RequiresApiKey, Is.True);

            const string key = "sk-test-CANARY-0123456789";
            settings.ApiKey = key;
            await settings.SaveApiKeyCommand.ExecuteAsync(null);
            Assert.That(credentials.LastKeySeen, Is.EqualTo(key));
            Assert.That(credentials.LastBuffer!.All(ch => ch == '\0'), Is.True, "The key buffer is cleared after the call.");
            Assert.That(settings.ApiKey, Is.Empty);
            Assert.That(settings.StatusText, Is.EqualTo("Chave salva no cofre do sistema."));

            credentials.Failure = new InvalidOperationException("vault said " + key);
            settings.ApiKey = key;
            await settings.SaveApiKeyCommand.ExecuteAsync(null);
            Assert.That(settings.StatusText, Does.Not.Contain("CANARY"));
            Assert.That(settings.IsStatusError, Is.True);

            settings.SelectedProvider = settings.Providers.Single(p => p.ProviderId == "local");
            Assert.That(settings.RequiresApiKey, Is.False);
            Assert.That(settings.SelectedProvider.AuthMethodsText, Is.EqualTo("Sem conta (local)"));
            Assert.That(settings.SaveApiKeyCommand.CanExecute(null), Is.False);

            var withoutVault = new AgentSettingsViewModel(catalog, null, "ext");
            withoutVault.ApiKey = "x";
            Assert.That(withoutVault.SaveApiKeyCommand.CanExecute(null), Is.False);
            Assert.That(withoutVault.CredentialSetupNote, Does.Contain("indisponível"));
        });
    }

    [Test]
    public void TheDesktopHasNoProviderSdkNorSubscriptionLoginTexts()
    {
        var desktop = typeof(AgentChatViewModel).Assembly;
        Assert.That(desktop.GetReferencedAssemblies().Select(name => name.Name),
            Has.None.Matches<string>(name => name!.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)));
        var table = (System.Collections.IEnumerable)typeof(LocalizationViewModel)
            .GetField("AgentTranslations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var agentKeys = table.Cast<System.Runtime.CompilerServices.ITuple>().Select(item => (string)item[0]!).ToArray();
        Assert.That(agentKeys, Has.Length.GreaterThan(100));
        foreach (var language in new[] { "pt-BR", "en", "es", "zh-CN" })
        {
            var localization = new LocalizationViewModel { Language = language };
            foreach (var key in agentKeys)
            {
                Assert.That(localization.HasTranslation(key), Is.True, $"{language}/{key}");
                var text = localization.Resolve(key);
                Assert.That(text, Does.Not.Contain("ChatGPT").And.Not.Contain("Claude").And.Not.StartWith("[["), $"{language}/{key}");
            }
        }
    }

    private static AgentApprovalDetails Details(DateTimeOffset expires, AgentToolRisk risk) =>
        new("mongo_update_one", "Produção", "shop", "orders", "{ \"_id\": ObjectId(\"65a1f0c2e4b0a1b2c3d4e5f6\") }",
            "{ \"$set\": { \"status\": \"shipped\" } }", 1, risk, expires);

    private static async IAsyncEnumerable<AgentProviderEvent> Slow(AgentTurnRequest request, Task release,
        [EnumeratorCancellation] CancellationToken token)
    {
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, "início ", MessageId: id);
        await release.WaitAsync(token);
        yield return new(AgentEventKind.MessageDelta, "fim", MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> AskApproval(ScriptedAgentSession session, AgentApprovalId approvalId,
        string modelText, [EnumeratorCancellation] CancellationToken token)
    {
        var id = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: id);
        yield return new(AgentEventKind.MessageDelta, modelText, MessageId: id);
        yield return new(AgentEventKind.MessageCompleted, MessageId: id);
        yield return new(AgentEventKind.ApprovalRequested, ApprovalId: approvalId);
        await session.DecisionReceived.Task.WaitAsync(token);
    }
}
