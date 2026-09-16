using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private decimal? _exportDocumentsPerCollectionLimit = 100_000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseInitialCollection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseConfirmation = string.Empty;

    [ObservableProperty]
    private string _exportResults = "Selecione um banco para exportá-lo em Extended JSON.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    private string _importSourceDirectory = string.Empty;

    [ObservableProperty]
    private string _importResults = "Informe a pasta da exportação e escolha o banco de destino.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    private string _dropDatabaseConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserRoles = "[{ \"role\": \"readWrite\", \"db\": \"banco-selecionado\" }]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    private string _databaseUsernameToDrop = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    private string _databaseUserDropConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    private string _databaseRoleUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    private string _databaseRolePayload = "[{ \"role\": \"read\", \"db\": \"banco-selecionado\" }]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    private string _databaseRoleConfirmation = string.Empty;

    [ObservableProperty]
    private bool _revokeDatabaseRoles;

    public bool CanExportDatabase => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedDatabase);

    public bool CanCreateDatabase => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(NewDatabaseName)
        && !string.IsNullOrWhiteSpace(NewDatabaseInitialCollection)
        && string.Equals(NewDatabaseName.Trim(), NewDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabase => CanExportDatabase && string.Equals(SelectedDatabase, DropDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanCreateDatabaseUser => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(NewDatabaseUsername)
        && !string.IsNullOrWhiteSpace(NewDatabaseUserPassword)
        && string.Equals(NewDatabaseUsername.Trim(), NewDatabaseUserConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabaseUser => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && string.Equals(DatabaseUsernameToDrop.Trim(), DatabaseUserDropConfirmation.Trim(), StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(DatabaseUsernameToDrop);

    public bool CanUpdateDatabaseUserRoles => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(DatabaseRoleUsername)
        && !string.IsNullOrWhiteSpace(DatabaseRolePayload)
        && string.Equals(DatabaseRoleUsername.Trim(), DatabaseRoleConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanLoadDatabaseStats => CanExportDatabase;

    public bool CanImportDatabase => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(ImportSourceDirectory);

    [RelayCommand(CanExecute = nameof(CanExportDatabase))]
    private async Task ExportDatabaseAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DatabaseExportRequest(SelectedDatabase, decimal.ToInt32(ExportDocumentsPerCollectionLimit ?? 100_000));
            var result = await _workspace.ExportDatabaseAsync(SelectedProfile, request, cancellationToken);
            ExportResults = $"{result.CollectionCount} coleção(ões), {result.DocumentCount} documento(s)." + Environment.NewLine
                + $"Pasta: {result.OutputDirectory}" + Environment.NewLine
                + "Manifesto: manifest.json" + (result.IsTruncated ? Environment.NewLine + "Atenção: ao menos uma coleção atingiu o limite configurado." : string.Empty);
            StatusMessage = $"Exportação concluída: {result.DocumentCount} documento(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanImportDatabase))]
    private async Task ImportDatabaseAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(ImportSourceDirectory))
        {
            return;
        }

        if (!await RunAsync(async cancellationToken =>
            {
                var request = new DatabaseImportRequest(ImportSourceDirectory, SelectedDatabase);
                var result = await _workspace.ImportDatabaseAsync(SelectedProfile, request, cancellationToken);
                ImportResults = $"{result.CollectionCount} coleção(ões), {result.DocumentCount} documento(s) importado(s) por upsert de _id." + Environment.NewLine
                    + $"Origem: {result.SourceDirectory}" + Environment.NewLine
                    + $"Destino: {result.TargetDatabase}";
                StatusMessage = $"Importação concluída: {result.DocumentCount} documento(s).";
            }))
        {
            return;
        }

        await LoadCollectionsAsync(SelectedDatabase);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadUsersAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetUsersAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Usuários MongoDB carregados.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadRolesAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetRolesAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Papéis MongoDB carregados.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCreateDatabaseUser))]
    private async Task CreateDatabaseUserAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var request = new DatabaseUserCreateRequest(
            SelectedDatabase,
            NewDatabaseUsername,
            NewDatabaseUserPassword,
            NewDatabaseUserRoles,
            NewDatabaseUserConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.CreateUserAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var username = request.Username.Trim();
        NewDatabaseUsername = string.Empty;
        NewDatabaseUserPassword = string.Empty;
        NewDatabaseUserRoles = "[{ \"role\": \"readWrite\", \"db\": \"banco-selecionado\" }]";
        NewDatabaseUserConfirmation = string.Empty;
        await RecordAuditAsync("user.create", SelectedProfile, SelectedDatabase, null, $"Usuário {username} criado.");
        StatusMessage = $"Usuário {username} criado no banco {SelectedDatabase}.";
    }

    [RelayCommand(CanExecute = nameof(CanDropDatabaseUser))]
    private async Task DropDatabaseUserAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var request = new DatabaseUserDropRequest(SelectedDatabase, DatabaseUsernameToDrop, DatabaseUserDropConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.DropUserAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var username = request.Username.Trim();
        DatabaseUsernameToDrop = string.Empty;
        DatabaseUserDropConfirmation = string.Empty;
        await RecordAuditAsync("user.drop", SelectedProfile, SelectedDatabase, null, $"Usuário {username} removido.");
        StatusMessage = $"Usuário {username} removido do banco {SelectedDatabase}.";
    }

    [RelayCommand(CanExecute = nameof(CanUpdateDatabaseUserRoles))]
    private async Task UpdateDatabaseUserRolesAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var request = new DatabaseUserRoleRequest(
            SelectedDatabase,
            DatabaseRoleUsername,
            DatabaseRolePayload,
            DatabaseRoleConfirmation,
            RevokeDatabaseRoles);
        if (!await RunAsync(cancellationToken => _workspace.UpdateUserRolesAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var username = request.Username.Trim();
        var action = request.Revoke ? "user.roles.revoke" : "user.roles.grant";
        DatabaseRoleUsername = string.Empty;
        DatabaseRolePayload = "[{ \"role\": \"read\", \"db\": \"banco-selecionado\" }]";
        DatabaseRoleConfirmation = string.Empty;
        await RecordAuditAsync(action, SelectedProfile, SelectedDatabase, null, $"Papéis do usuário {username} atualizados.");
        StatusMessage = $"Papéis do usuário {username} atualizados.";
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadDatabaseStatsAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetDatabaseStatsAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = $"Estatísticas de {SelectedDatabase} carregadas.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropDatabase))]
    private async Task DropDatabaseAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var database = SelectedDatabase;
        if (!await RunAsync(cancellationToken => _workspace.DropDatabaseAsync(
            SelectedProfile,
            new DatabaseDropRequest(database, DropDatabaseConfirmation),
            cancellationToken)))
        {
            return;
        }

        DropDatabaseConfirmation = string.Empty;
        Collections.Clear();
        SelectedCollection = null;
        SelectedDatabase = null;
        await LoadDatabasesAsync();
        await RecordAuditAsync("database.drop", SelectedProfile, database, null, "Banco removido.");
        StatusMessage = $"Banco {database} removido.";
    }

    [RelayCommand(CanExecute = nameof(CanCreateDatabase))]
    private async Task CreateDatabaseAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var request = new DatabaseCreateRequest(NewDatabaseName, NewDatabaseInitialCollection, NewDatabaseConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.CreateDatabaseAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var database = request.Database.Trim();
        var collection = request.InitialCollection.Trim();
        NewDatabaseName = string.Empty;
        NewDatabaseInitialCollection = string.Empty;
        NewDatabaseConfirmation = string.Empty;
        await LoadDatabasesAsync();
        SelectedDatabase = database;
        await RecordAuditAsync("database.create", SelectedProfile, database, collection, "Banco criado com coleção inicial.");
        StatusMessage = $"Banco {database} criado com a coleção {collection}.";
    }
}
