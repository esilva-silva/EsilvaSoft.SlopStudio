using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <param name="workspace">Application service that performs the confirmed operation.</param>
/// <param name="profile">Destination captured when the editor opened.</param>
/// <param name="database">Destination database.</param>
/// <param name="collection">Destination collection.</param>
/// <param name="original">Snapshot being edited or deleted; null for insertion.</param>
/// <param name="operation">Inserir, Editar or Excluir.</param>
/// <param name="rereadBeforeWrite">
/// True when the snapshot came from a result in memory: after confirmation the document is read again by <c>_id</c> and
/// compared before the write, which still uses the <c>$$ROOT</c> precondition.
/// </param>
/// <param name="writeBlockReason">Evaluated when saving; a non-null text blocks the write, e.g. a closed connection.</param>
public sealed partial class DocumentMutationViewModel(WorkspaceService workspace, ConnectionProfile profile, string database, string collection, ResultDocumentViewModel? original, string operation,
    bool rereadBeforeWrite = false, Func<string?>? writeBlockReason = null) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cancellation;
    public string Context => $"{profile.Name} › {database} › {collection} · {profile.RoutingLabel}";
    public string Operation => operation;
    public string ApplyLabel => operation switch { "Editar" => "Salvar…", "Excluir" => "Excluir…", _ => operation + "…" };
    public string Identity => original?.IdentityFilter ?? "Novo documento";
    public string Policy => profile.IsReadOnly
        ? "Somente leitura: esta conexão bloqueia gravações. A cópia pode ser revisada, mas não salva."
        : rereadBeforeWrite
            ? "Cópia editável do resultado; nada foi lido ou gravado ao abrir. Salvar exige confirmação, relê o documento por _id e usa precondição contra alterações concorrentes."
            : "Gravação exige confirmação; a precondição rejeita um documento alterado ou removido após a leitura.";
    public bool IsDelete => Operation == "Excluir";
    public string? WriteBlockReason => profile.IsReadOnly ? "Conexão somente leitura: gravação bloqueada." : writeBlockReason?.Invoke();
    public bool CanApply => !IsRunning && WriteBlockReason is null;
    // The editable text uses the UUID constructors; the precondition below keeps the canonical snapshot.
    [ObservableProperty] private string _text = original?.FormattedJson ?? "{}";
    [ObservableProperty] private string _status = (profile.IsReadOnly ? "Conexão somente leitura: gravação bloqueada." : writeBlockReason?.Invoke()) ?? "Revise o documento e o destino antes de confirmar.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] private bool _isRunning;
    public bool Succeeded { get; private set; }
    public DocumentWriteConflict Conflict { get; private set; }

    public async Task ExecuteConfirmedAsync()
    {
        if (IsRunning) return;
        profile.EnsureWriteAllowed();
        if (writeBlockReason?.Invoke() is { } blocked) { Status = blocked; return; }
        if (operation != "Inserir" && original?.IdentityFilter is null) throw new InvalidOperationException("O documento precisa incluir _id. Refaça a consulta sem excluir esse campo da projeção.");
        var text = Text;
        using var globalOperation = workspace.Operations.Begin($"{operation} documento — {Context}", ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(globalOperation.Token); _cancellation = cancellation;
        Succeeded = false; Conflict = DocumentWriteConflict.None; IsRunning = true;
        try
        {
            var snapshot = original?.Json;
            if (rereadBeforeWrite && original is not null && operation != "Inserir")
            {
                Status = "Relendo o documento antes de gravar…";
                var page = await workspace.QueryAsync(profile, new MongoQuery(database, collection, original.IdentityFilter!, Limit: 1), cancellation.Token);
                if (page.Documents.Count == 0)
                {
                    Conflict = DocumentWriteConflict.Removed;
                    Status = "O documento foi removido depois da leitura. Nada foi gravado; execute a consulta novamente.";
                    return;
                }
                if (!await Task.Run(() => ExtendedJsonComparer.AreEquivalent(page.Documents[0], original.Json), cancellation.Token))
                {
                    Conflict = DocumentWriteConflict.Changed;
                    Status = "O documento foi alterado no servidor depois da leitura. Nada foi gravado; execute a consulta novamente e revise a cópia.";
                    return;
                }
                snapshot = page.Documents[0];
            }
            var filter = original is null ? "{}" : "{\"$and\":[" + original.IdentityFilter + ",{\"$expr\":{\"$eq\":[\"$$ROOT\",{\"$literal\":" + snapshot + "}]}}]}";
            var result = operation switch
            {
                "Inserir" => await workspace.InsertAsync(profile, database, collection, text, cancellation.Token),
                "Editar" => await workspace.ReplaceAsync(profile, database, collection, filter, text, cancellation.Token),
                "Excluir" => await workspace.DeleteAsync(profile, database, collection, filter, cancellation.Token),
                _ => throw new InvalidOperationException("Operação de documento desconhecida.")
            };
            Succeeded = result.MatchedCount > 0; Status = Succeeded ? "Operação concluída. Atualize a página para consultar o estado atual." : "O documento mudou ou foi removido após a leitura. Atualize a página antes de tentar novamente.";
        }
        catch (OperationCanceledException) { Status = "Cancelado. Efeitos enviados ao servidor não são revertidos; confira o resultado."; }
        catch (Exception ex) { Status = "Falha: " + OperationErrorMessages.Describe(ex); }
        finally { globalOperation.Complete(cancellation.IsCancellationRequested ? ApplicationOperationStatus.Cancelled : Succeeded ? ApplicationOperationStatus.Success : Conflict != DocumentWriteConflict.None ? ApplicationOperationStatus.Warning : ApplicationOperationStatus.Error, Status); IsRunning = false; _cancellation = null; }
    }
    public void Cancel() => _cancellation?.Cancel();
    public void Dispose() => Cancel();
}
