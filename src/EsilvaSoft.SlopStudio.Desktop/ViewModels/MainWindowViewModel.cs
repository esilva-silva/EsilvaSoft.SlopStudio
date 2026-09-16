using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly HashSet<string> _knownFields = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _lastQueryDocuments = [];
    private Guid? _editingProfileId;
    private CancellationTokenSource? _operationCancellation;
    private readonly bool _autoLoadCollections;

    /// <summary>Invoked after a profile is persisted, for preferences stored outside the profile.</summary>
    public Func<ConnectionProfile, Task>? ProfileSaved { get; set; }
    /// <summary>Profile edited or duplicated by the open editor; null for a new profile.</summary>
    public Guid? ProfileEditorSourceId { get; private set; }

    public MainWindowViewModel(WorkspaceService workspace, bool autoLoadCollections = true)
    {
        _workspace = workspace;
        _autoLoadCollections = autoLoadCollections;
        LoadProfilesCommand.Execute(null);
        _ = LoadScriptHistoryAsync();
    }

    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    public ObservableCollection<string> Databases { get; } = [];

    public ObservableCollection<string> Collections { get; } = [];

    public ObservableCollection<QueryHistoryEntry> QueryHistory { get; } = [];

    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];

    public ObservableCollection<ScriptHistoryEntry> ScriptHistory { get; } = [];

    public ObservableCollection<AuditEntry> AuditEntries { get; } = [];

    public IReadOnlyList<CollectionValidationLevel> CollectionValidationLevels { get; } = Enum.GetValues<CollectionValidationLevel>();

    public IReadOnlyList<CollectionValidationAction> CollectionValidationActions { get; } = Enum.GetValues<CollectionValidationAction>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileDetails))]
    [NotifyPropertyChangedFor(nameof(CanExportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyPropertyChangedFor(nameof(CanLoadDatabaseStats))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabasesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadServerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTopologyCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCurrentOperationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadUsersCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabaseStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyPropertyChangedFor(nameof(CanLoadDatabaseStats))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabaseStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    private string? _selectedDatabase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyPropertyChangedFor(nameof(CanDropIndex))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    private string? _selectedCollection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private string _queryFilter = "{}";

    [ObservableProperty]
    private string _queryProjection = string.Empty;

    [ObservableProperty]
    private string _querySort = string.Empty;

    [ObservableProperty]
    private decimal? _queryLimit = 100;

    [ObservableProperty]
    private decimal? _schemaSampleMaximumDocuments = 200;

    [ObservableProperty]
    private decimal? _querySkip;

    [ObservableProperty]
    private string _queryHint = string.Empty;

    [ObservableProperty]
    private string _queryComment = string.Empty;

    [ObservableProperty]
    private decimal? _queryBatchSize;

    [ObservableProperty]
    private string _queryCollation = string.Empty;

    [ObservableProperty]
    private decimal? _queryMaxTimeMs;

    [ObservableProperty]
    private string _queryResults = "Selecione uma conexão, carregue os bancos e execute uma consulta.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportQueryResults))]
    [NotifyCanExecuteChangedFor(nameof(ExportQueryResultsCommand))]
    private string _queryExportPath = string.Empty;

    [ObservableProperty]
    private string _countResults = "A contagem exata respeita o filtro; a estimada considera a coleção inteira.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGetDistinctValues))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    private string _distinctField = string.Empty;

    [ObservableProperty]
    private decimal? _distinctMaximumValues = 1_000;

    [ObservableProperty]
    private string _distinctResults = "Informe um campo e execute valores distintos usando o filtro atual.";

    [ObservableProperty]
    private QueryHistoryEntry? _selectedQueryHistory;

    [ObservableProperty]
    private bool _queryHistoryEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSavedQueryCommand))]
    private SavedQuery? _selectedSavedQuery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveSavedQuery))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    private string _savedQueryName = string.Empty;

    [ObservableProperty]
    private bool _savedQueryIsFavorite;

    [ObservableProperty]
    private string _explainResults = "Execute Explain para visualizar o plano e as estatísticas da consulta.";

    [ObservableProperty]
    private string _aggregationPipeline = "[\n  { \"$match\": {} },\n  { \"$limit\": 100 }\n]";

    [ObservableProperty]
    private decimal? _aggregationLimit = 100;

    [ObservableProperty]
    private string _aggregationResults = "Selecione uma coleção e execute um pipeline de agregação.";

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
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    private string _newCollectionName = string.Empty;

    [ObservableProperty]
    private bool _newCollectionIsCapped;

    [ObservableProperty]
    private bool _newCollectionIsView;

    [ObservableProperty]
    private bool _newCollectionIsClustered;

    [ObservableProperty]
    private string _newCollectionClusteredIndexKey = "{ \"_id\": 1 }";

    [ObservableProperty]
    private string _newCollectionViewOn = string.Empty;

    [ObservableProperty]
    private string _newCollectionViewPipeline = "[]";

    [ObservableProperty]
    private string _newCollectionCollation = string.Empty;

    [ObservableProperty]
    private decimal? _newCollectionMaxSizeBytes;

    [ObservableProperty]
    private decimal? _newCollectionMaxDocuments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    private string _dropDatabaseConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationIdToKill = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationKillConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    private string _collectionIntegrityConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    private string _collectionCompactConfirmation = string.Empty;

    [ObservableProperty]
    private bool _collectionCompactForce;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    private string _renameCollectionName = string.Empty;

    [ObservableProperty]
    private bool _renameDropTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    private string _dropCollectionConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private string _viewUpdateConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private string _viewUpdatePipeline = "[]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    private string _collectionValidatorJson = "{\n  \"$jsonSchema\": {\n    \"bsonType\": \"object\"\n  }\n}";

    [ObservableProperty]
    private CollectionValidationLevel _collectionValidationLevel = CollectionValidationLevel.Strict;

    [ObservableProperty]
    private CollectionValidationAction _collectionValidationAction = CollectionValidationAction.Error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    private string _collectionValidationConfirmation = string.Empty;

    [ObservableProperty]
    private string _administrationResults = "Carregue o status do servidor ou as estatísticas do banco selecionado.";

    [ObservableProperty]
    private string _auditExportPath = string.Empty;

    [ObservableProperty]
    private string _autocompleteSuggestions = "Digite um campo ou operador no filtro e selecione Sugerir.";

    public ObservableCollection<MqlSuggestion> QuerySuggestions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyQuerySuggestionCommand))]
    private MqlSuggestion? _selectedQuerySuggestion;

    [ObservableProperty]
    private string _documentJson = "{\n  \n}";

    [ObservableProperty]
    private string _identifierSnippet = "Gere um identificador no modo e na representação UUID desta conexão.";

    [ObservableProperty]
    private string _identifierExtendedJsonSnippet = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private UuidRepresentation _uuidRepresentation = UuidRepresentation.Standard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private IdentifierRepresentationMode _identifierMode = IdentifierRepresentationMode.Standard;

    public string IdentifierLabel => $"Identificadores {IdentifierRepresentationService.DisplayName(IdentifierMode)} · UUID {UuidCodec.DisplayName(UuidRepresentation)} · subtype {UuidCodec.SubType(UuidRepresentation)} · Extended JSON canônico abaixo";

    [ObservableProperty]
    private string _identifierInput = "";

    [ObservableProperty]
    private string _identifierInterpretation = "Cole ObjectId(\"…\"), 24 dígitos hexadecimais, UUID(\"…\")/CGUUID/JUUID/GUUID ou um UUID.";

    [ObservableProperty]
    private string _bulkDocumentsJson = "[\n  {\n    \n  }\n]";

    [ObservableProperty]
    private decimal? _bulkMaximumDocuments = 1_000;

    [ObservableProperty]
    private bool _bulkOrdered = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewDocument))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    private string _mutationFilter = "{\n  \n}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDuplicatePreviewDocument))]
    [NotifyCanExecuteChangedFor(nameof(DuplicatePreviewDocumentCommand))]
    private string _documentPreview = "Informe um filtro e carregue uma prévia antes de alterar um documento.";

    [ObservableProperty]
    private bool _deleteManyDocuments;

    [ObservableProperty]
    private string _updateJson = "{\n  \"$set\": { }\n}";

    [ObservableProperty]
    private string _updateArrayFiltersJson = string.Empty;

    [ObservableProperty]
    private bool _updateUpsert;

    [ObservableProperty]
    private string _findAndModifyResult = "O documento retornado após a alteração aparecerá aqui.";

    [ObservableProperty]
    private string _indexKeys = "{ \"campo\": 1 }";

    [ObservableProperty]
    private string _indexName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropIndex))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    private string _indexNameToDrop = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexNameForVisibility = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexVisibilityConfirmation = string.Empty;

    [ObservableProperty]
    private bool _indexVisibilityHidden;

    [ObservableProperty]
    private bool _indexIsUnique;

    [ObservableProperty]
    private bool _indexIsSparse;

    [ObservableProperty]
    private bool _indexIsHidden;

    [ObservableProperty]
    private decimal? _indexExpireAfterSeconds;

    [ObservableProperty]
    private string _indexPartialFilter = string.Empty;

    [ObservableProperty]
    private string _indexCollation = string.Empty;

    [ObservableProperty]
    private string _indexWildcardProjection = string.Empty;

    [ObservableProperty]
    private string _indexResults = "Selecione uma coleção para listar seus índices.";

    [ObservableProperty]
    private string _scriptInput = "{}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveScript))]
    [NotifyCanExecuteChangedFor(nameof(SaveScriptCommand))]
    private string _scriptText = "// db começa no banco selecionado da conexão\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveScript))]
    [NotifyPropertyChangedFor(nameof(CanLoadScript))]
    [NotifyCanExecuteChangedFor(nameof(SaveScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadScriptCommand))]
    private string _scriptFilePath = string.Empty;

    [ObservableProperty]
    private string _scriptResults = "O resultado estruturado e o console do mongosh aparecem aqui.";

    [ObservableProperty]
    private ScriptHistoryEntry? _selectedScriptHistory;

    [ObservableProperty]
    private bool _scriptHistoryEnabled = true;

    [ObservableProperty]
    private bool _persistScriptInput;

    [ObservableProperty]
    private string _statusMessage = "Pronto.";

    [ObservableProperty]
    private string _footerMessage = "Workspace local: LiteDB. Credenciais persistidas serão adicionadas com cofre do sistema.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelOperation))]
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isOperationRunning;

    [ObservableProperty]
    private bool _isProfileEditorVisible;

    [ObservableProperty]
    private string _quickConnectionString = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileConnectionString = "mongodb://localhost:27017";

    [ObservableProperty]
    private string _newProfileDatabase = string.Empty;

    [ObservableProperty]
    private string _newProfileUsername = string.Empty;

    [ObservableProperty]
    private string _newProfilePassword = string.Empty;

    [ObservableProperty]
    private string _newProfileEnvironment = string.Empty;

    [ObservableProperty]
    private string _newProfileColor = string.Empty;

    [ObservableProperty]
    private string _newProfileTags = string.Empty;

    [ObservableProperty]
    private string _newProfileFolder = string.Empty;

    [ObservableProperty]
    private bool _newProfileIsReadOnly;

    [ObservableProperty]
    private bool _newProfileIsFavorite;

    public bool HasSelectedProfile => SelectedProfile is not null;

    public bool HasNoSelectedProfile => SelectedProfile is null;

    public string CollectionContextHint => SelectedProfile is null
        ? "Selecione uma conexão para começar."
        : string.IsNullOrWhiteSpace(SelectedDatabase)
            ? "1. Clique em Carregar bancos."
            : string.IsNullOrWhiteSpace(SelectedCollection)
                ? "2. Escolha uma coleção para habilitar a consulta."
                : $"Pronto: {SelectedDatabase}.{SelectedCollection}. Ajuste o filtro e execute.";

    public bool CanExecuteQuery => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedDatabase) && !string.IsNullOrWhiteSpace(SelectedCollection);

    public bool CanExportQueryResults => _lastQueryDocuments.Count > 0 && !string.IsNullOrWhiteSpace(QueryExportPath);

    public bool CanSaveSavedQuery => CanExecuteQuery && !string.IsNullOrWhiteSpace(SavedQueryName);

    public bool CanGetDistinctValues => CanExecuteQuery && !string.IsNullOrWhiteSpace(DistinctField);

    public bool CanDeleteSavedQuery => SelectedSavedQuery is not null;

    public bool CanPreviewDocument => CanExecuteQuery && !string.IsNullOrWhiteSpace(MutationFilter) && !string.Equals(MutationFilter.Trim(), "{}", StringComparison.Ordinal);

    public bool CanDuplicatePreviewDocument => CanExecuteQuery && DocumentPreview.TrimStart().StartsWith('{');

    public bool CanExportDatabase => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedDatabase);

    public bool CanCreateDatabase => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(NewDatabaseName)
        && !string.IsNullOrWhiteSpace(NewDatabaseInitialCollection)
        && string.Equals(NewDatabaseName.Trim(), NewDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabase => CanExportDatabase && string.Equals(SelectedDatabase, DropDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanKillOperation => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && string.Equals(OperationIdToKill.Trim(), OperationKillConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanValidateCollectionIntegrity => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionIntegrityConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanCompactCollection => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionCompactConfirmation.Trim(), StringComparison.Ordinal);

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

    public bool CanLoadCollectionStats => CanExecuteQuery;

    public bool CanDropIndex => CanExecuteQuery && !string.IsNullOrWhiteSpace(IndexNameToDrop);

    public bool CanSetIndexVisibility => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(IndexNameForVisibility)
        && string.Equals(IndexNameForVisibility.Trim(), IndexVisibilityConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanImportDatabase => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(ImportSourceDirectory);

    public bool CanCreateCollection => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(NewCollectionName);

    public bool CanRenameCollection => CanExecuteQuery && !string.IsNullOrWhiteSpace(RenameCollectionName);

    public bool CanDropCollection => CanExecuteQuery && string.Equals(SelectedCollection, DropCollectionConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanConfigureCollectionValidation => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(CollectionValidatorJson)
        && string.Equals(SelectedCollection, CollectionValidationConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanUpdateView => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(ViewUpdatePipeline)
        && string.Equals(SelectedCollection, ViewUpdateConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanSaveScript => !string.IsNullOrWhiteSpace(ScriptFilePath) && !string.IsNullOrWhiteSpace(ScriptText);

    public bool CanLoadScript => !string.IsNullOrWhiteSpace(ScriptFilePath);

    public bool CanCancelOperation => IsOperationRunning;

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    public string SelectedProfileDetails => SelectedProfile is null
        ? "Nenhuma conexão selecionada."
        : $"Pasta: {SelectedProfile.Folder ?? "sem pasta"} · Banco padrão: {SelectedProfile.DefaultDatabase ?? "não definido"} · {SelectedProfile.Environment ?? "sem ambiente"} · Última conexão: {(SelectedProfile.LastConnectedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "nunca")}";

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        Databases.Clear();
        Collections.Clear();
        _knownFields.Clear();
        QueryHistory.Clear();
        SavedQueries.Clear();
        SelectedSavedQuery = null;
        SelectedDatabase = value?.DefaultDatabase;
        SelectedCollection = null;
        if (QueryHistoryEnabled)
        {
            _ = LoadQueryHistoryAsync();
        }

        _ = LoadSavedQueriesAsync();
    }

    partial void OnSelectedQueryHistoryChanged(QueryHistoryEntry? value)
    {
        if (value is null)
        {
            return;
        }

        var query = value.ToQuery();
        SelectedDatabase = query.Database;
        SelectedCollection = query.Collection;
        QueryFilter = query.FilterJson;
        QueryProjection = query.ProjectionJson ?? string.Empty;
        QuerySort = query.SortJson ?? string.Empty;
        QueryHint = query.HintJson ?? string.Empty;
        QueryLimit = query.Limit;
        QuerySkip = query.Skip;
        QueryMaxTimeMs = query.MaxTimeMs;
        QueryComment = query.Comment ?? string.Empty;
        QueryBatchSize = query.BatchSize;
        QueryCollation = query.CollationJson ?? string.Empty;
        StatusMessage = "Consulta carregada do histórico local.";
    }

    partial void OnQueryHistoryEnabledChanged(bool value)
    {
        if (!value)
        {
            SelectedQueryHistory = null;
            QueryHistory.Clear();
            return;
        }

        _ = LoadQueryHistoryAsync();
    }

    partial void OnSelectedSavedQueryChanged(SavedQuery? value)
    {
        if (value is null)
        {
            return;
        }

        var query = value.ToQuery();
        SavedQueryName = value.Name;
        SavedQueryIsFavorite = value.IsFavorite;
        SelectedDatabase = query.Database;
        SelectedCollection = query.Collection;
        QueryFilter = query.FilterJson;
        QueryProjection = query.ProjectionJson ?? string.Empty;
        QuerySort = query.SortJson ?? string.Empty;
        QueryHint = query.HintJson ?? string.Empty;
        QueryLimit = query.Limit;
        QuerySkip = query.Skip;
        QueryMaxTimeMs = query.MaxTimeMs;
        StatusMessage = "Consulta salva carregada.";
    }

    partial void OnSelectedScriptHistoryChanged(ScriptHistoryEntry? value)
    {
        if (value is not null)
        {
            ScriptFilePath = value.Path;
            if (value.InputJson is not null)
            {
                ScriptInput = value.InputJson;
            }

            StatusMessage = "Caminho de script carregado do histórico local.";
        }
    }

    partial void OnScriptHistoryEnabledChanged(bool value)
    {
        if (!value)
        {
            SelectedScriptHistory = null;
            ScriptHistory.Clear();
            return;
        }

        _ = LoadScriptHistoryAsync();
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        Collections.Clear();
        SelectedCollection = null;
        ExecuteQueryCommand.NotifyCanExecuteChanged();
        ExportDatabaseCommand.NotifyCanExecuteChanged();

        if (_autoLoadCollections && SelectedProfile is not null && !string.IsNullOrWhiteSpace(value))
        {
            _ = LoadCollectionsAsync(value);
        }
    }

    partial void OnQueryFilterChanged(string value)
    {
        ExecuteQueryCommand.NotifyCanExecuteChanged();
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
    }

    [RelayCommand]
    private void ShowAllDocuments()
    {
        QueryFilter = "{}";
        QuerySkip = 0;
        StatusMessage = "Filtro definido como {}. A próxima consulta começa no primeiro documento.";
    }

    partial void OnSelectedCollectionChanged(string? value)
    {
        _knownFields.Clear();
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
        AutocompleteSuggestions = "Carregue uma amostra ou execute uma consulta para sugerir campos desta coleção.";
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var profiles = await _workspace.GetProfilesAsync(cancellationToken);
            Profiles.Clear();

            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile ??= Profiles.FirstOrDefault();
            StatusMessage = $"{Profiles.Count} conexão(ões) carregada(s).";
        });
    }

    [RelayCommand]
    private void ShowProfileEditor()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        _editingProfileId = null;
        ProfileEditorSourceId = null;
        NewProfileName = string.Empty;
        NewProfileConnectionString = "mongodb://localhost:27017";
        NewProfileDatabase = string.Empty;
        NewProfileEnvironment = string.Empty;
        NewProfileColor = string.Empty;
        NewProfileTags = string.Empty;
        NewProfileFolder = string.Empty;
        NewProfileIsReadOnly = false;
        NewProfileIsFavorite = false;
        IsProfileEditorVisible = true;
    }

    [RelayCommand]
    private void StartProfileFromConnectionString()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        try
        {
            var draft = ConnectionProfileDraft.FromConnectionString(QuickConnectionString);
            _editingProfileId = null;
            ProfileEditorSourceId = null;
            NewProfileName = draft.SuggestedName;
            NewProfileConnectionString = draft.ConnectionString;
            NewProfileDatabase = draft.DefaultDatabase ?? string.Empty;
            NewProfileEnvironment = string.Empty;
            NewProfileColor = string.Empty;
            NewProfileTags = string.Empty;
            NewProfileFolder = string.Empty;
            NewProfileIsReadOnly = false;
            NewProfileIsFavorite = false;
            QuickConnectionString = string.Empty;
            IsProfileEditorVisible = true;
            StatusMessage = "Perfil preenchido a partir da URI. Revise os campos antes de salvar.";
        }
        catch (ArgumentException exception)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void EditSelectedProfile()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        if (SelectedProfile is null)
        {
            return;
        }

        _editingProfileId = SelectedProfile.Id;
        ProfileEditorSourceId = SelectedProfile.Id;
        NewProfileName = SelectedProfile.Name;
        NewProfileConnectionString = SelectedProfile.ConnectionString;
        NewProfileDatabase = SelectedProfile.DefaultDatabase ?? string.Empty;
        NewProfileEnvironment = SelectedProfile.Environment ?? string.Empty;
        NewProfileColor = SelectedProfile.Color ?? string.Empty;
        NewProfileTags = SelectedProfile.Tags ?? string.Empty;
        NewProfileFolder = SelectedProfile.Folder ?? string.Empty;
        NewProfileIsReadOnly = SelectedProfile.IsReadOnly;
        NewProfileIsFavorite = SelectedProfile.IsFavorite;
        IsProfileEditorVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void DuplicateSelectedProfile()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        if (SelectedProfile is null)
        {
            return;
        }

        var copy = SelectedProfile.Duplicate($"{SelectedProfile.Name} - cópia");
        _editingProfileId = null;
        ProfileEditorSourceId = SelectedProfile.Id;
        NewProfileName = copy.Name;
        NewProfileConnectionString = copy.ConnectionString;
        NewProfileDatabase = copy.DefaultDatabase ?? string.Empty;
        NewProfileEnvironment = copy.Environment ?? string.Empty;
        NewProfileColor = copy.Color ?? string.Empty;
        NewProfileTags = copy.Tags ?? string.Empty;
        NewProfileFolder = copy.Folder ?? string.Empty;
        NewProfileIsReadOnly = copy.IsReadOnly;
        NewProfileIsFavorite = copy.IsFavorite;
        IsProfileEditorVisible = true;
        StatusMessage = "Revise o nome e salve a cópia da conexão.";
    }

    [RelayCommand]
    private void HideProfileEditor()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        _editingProfileId = null;
        IsProfileEditorVisible = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var profile = SelectedProfile;
        if (!await RunAsync(cancellationToken => _workspace.DeleteProfileAsync(profile.Id, cancellationToken)))
        {
            return;
        }

        Profiles.Remove(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        StatusMessage = $"Conexão {profile.Name} removida do workspace LiteDB.";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            var connectionString = string.IsNullOrWhiteSpace(NewProfileUsername)
                ? NewProfileConnectionString
                : AddCredentials(NewProfileConnectionString, NewProfileUsername, NewProfilePassword);
            var profile = ConnectionProfile.Create(NewProfileName, connectionString, NewProfileDatabase, NewProfileIsReadOnly, NewProfileIsFavorite, NewProfileEnvironment, NewProfileColor, NewProfileTags, NewProfileFolder);
            var editingId = _editingProfileId;
            if (editingId is not null)
            {
                profile = profile with { Id = editingId.Value };
            }

            if (!await RunAsync(cancellationToken => _workspace.SaveProfileAsync(profile, cancellationToken)))
            {
                return;
            }

            if (editingId is null)
            {
                Profiles.Add(profile);
            }
            else
            {
                var existingIndex = Profiles.IndexOf(Profiles.First(existing => existing.Id == editingId.Value));
                Profiles[existingIndex] = profile;
            }

            SelectedProfile = profile;
            string? preferenceError = null;
            if (ProfileSaved is { } saved)
            {
                try { await saved(profile); }
                catch (Exception exception) { preferenceError = exception.Message; }
            }
            NewProfileName = string.Empty;
            NewProfileConnectionString = "mongodb://localhost:27017";
            NewProfileDatabase = string.Empty;
            NewProfileUsername = string.Empty;
            NewProfilePassword = string.Empty;
            NewProfileEnvironment = string.Empty;
            NewProfileColor = string.Empty;
            NewProfileTags = string.Empty;
            NewProfileFolder = string.Empty;
            NewProfileIsReadOnly = false;
            NewProfileIsFavorite = false;
            _editingProfileId = null;
            IsProfileEditorVisible = false;
            StatusMessage = (editingId is null ? "Conexão salva no workspace LiteDB." : "Conexão atualizada no workspace LiteDB.")
                + (preferenceError is null ? "" : " Preferência UUID não salva: " + preferenceError);
        }
        catch (ArgumentException exception)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task TestSelectedConnectionAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.TestConnectionAsync(SelectedProfile, cancellationToken);
            if (result.IsSuccess)
            {
                var connectedProfile = SelectedProfile.MarkConnected();
                await _workspace.SaveProfileAsync(connectedProfile, cancellationToken);
                var profileIndex = Profiles.IndexOf(SelectedProfile);
                if (profileIndex >= 0)
                {
                    Profiles[profileIndex] = connectedProfile;
                }

                var selectedDatabase = SelectedDatabase;
                var selectedCollection = SelectedCollection;
                SelectedProfile = connectedProfile;
                SelectedDatabase = selectedDatabase;
                SelectedCollection = selectedCollection;
            }

            StatusMessage = result.IsSuccess
                ? $"Conectado a MongoDB {result.ServerVersion ?? "(versão não informada)"} em {result.Duration.TotalMilliseconds:F0} ms."
                : $"Falha ao conectar: {result.Message}";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadDatabasesAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var databases = await _workspace.GetDatabasesAsync(SelectedProfile, cancellationToken);
            Databases.Clear();

            foreach (var database in databases)
            {
                Databases.Add(database);
            }

            SelectedDatabase ??= Databases.FirstOrDefault();
            StatusMessage = $"{Databases.Count} banco(s) carregado(s).";
        });
    }

    [RelayCommand]
    private async Task LoadQueryHistoryAsync()
    {
        if (!QueryHistoryEnabled)
        {
            QueryHistory.Clear();
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentQueryHistoryAsync(SelectedProfile?.Id, cancellationToken: cancellationToken);
            QueryHistory.Clear();
            foreach (var entry in entries)
            {
                QueryHistory.Add(entry);
            }
        });
    }

    [RelayCommand]
    private async Task LoadSavedQueriesAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var queries = await _workspace.GetSavedQueriesAsync(SelectedProfile?.Id, cancellationToken);
            SavedQueries.Clear();
            foreach (var query in queries)
            {
                SavedQueries.Add(query);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveSavedQuery))]
    private async Task SaveSavedQueryAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        SavedQuery? saved = null;
        if (!await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                database,
                collection,
                QueryFilter,
                string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                decimal.ToInt32(QueryLimit ?? 100),
                decimal.ToInt32(QuerySkip ?? 0),
                string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            saved = SelectedSavedQuery is null
                ? SavedQuery.Create(SavedQueryName, profile.Id, query, SavedQueryIsFavorite)
                : new SavedQuery(SelectedSavedQuery.Id, SavedQueryName.Trim(), profile.Id, query.Database, query.Collection, query.FilterJson, query.ProjectionJson, query.SortJson, query.HintJson, query.Limit, query.Skip, query.MaxTimeMs, SavedQueryIsFavorite, DateTimeOffset.UtcNow).Validate();
            await _workspace.SaveSavedQueryAsync(saved, cancellationToken);
        }))
        {
            return;
        }

        await LoadSavedQueriesAsync();
        SelectedSavedQuery = SavedQueries.FirstOrDefault(item => item.Id == saved!.Id);
        StatusMessage = "Consulta salva no workspace LiteDB.";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSavedQuery))]
    private async Task DeleteSavedQueryAsync()
    {
        if (SelectedSavedQuery is null)
        {
            return;
        }

        var query = SelectedSavedQuery;
        if (!await RunAsync(cancellationToken => _workspace.DeleteSavedQueryAsync(query.Id, cancellationToken)))
        {
            return;
        }

        SelectedSavedQuery = null;
        SavedQueryName = string.Empty;
        SavedQueryIsFavorite = false;
        await LoadSavedQueriesAsync();
        StatusMessage = $"Consulta salva '{query.Name}' removida.";
    }

    [RelayCommand]
    private void NewSavedQuery()
    {
        SelectedSavedQuery = null;
        SavedQueryName = string.Empty;
        SavedQueryIsFavorite = false;
        StatusMessage = "Informe um nome para salvar a consulta atual.";
    }

    [RelayCommand]
    private async Task LoadScriptHistoryAsync()
    {
        if (!ScriptHistoryEnabled)
        {
            ScriptHistory.Clear();
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentScriptHistoryAsync(cancellationToken: cancellationToken);
            ScriptHistory.Clear();
            foreach (var entry in entries)
            {
                ScriptHistory.Add(entry);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteQueryAsync()
    {
        var profile = SelectedProfile;
        if (profile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            var result = await _workspace.QueryAsync(profile, query, cancellationToken);
            QueryResults = result.Documents.Count == 0
                ? "Nenhum documento encontrado."
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _lastQueryDocuments = result.Documents;
            ExportQueryResultsCommand.NotifyCanExecuteChanged();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            if (QueryHistoryEnabled)
            {
                var historyEntry = QueryHistoryEntry.Create(profile.Id, query);
                await _workspace.SaveQueryHistoryAsync(historyEntry, cancellationToken);
                QueryHistory.Insert(0, historyEntry);
                while (QueryHistory.Count > 50)
                {
                    QueryHistory.RemoveAt(QueryHistory.Count - 1);
                }
            }
            StatusMessage = $"{result.Documents.Count} documento(s) retornado(s) em {result.Duration.TotalMilliseconds:F0} ms.{(result.IsTruncated ? " Limite atingido." : string.Empty)}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExportQueryResults))]
    private async Task ExportQueryResultsAsync()
    {
        try
        {
            if (!string.Equals(Path.GetExtension(QueryExportPath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Informe um arquivo novo com extensão .json.", nameof(QueryExportPath));
            }

            var destination = Path.GetFullPath(QueryExportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var content = QueryResultExportSerializer.Serialize(_lastQueryDocuments);
            await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            StatusMessage = $"{_lastQueryDocuments.Count} documento(s) exportado(s) em Extended JSON.";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountExactDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: false);

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountEstimatedDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: true);

    private async Task CountDocumentsAsync(bool useEstimatedCount)
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new CollectionCountRequest(
                database,
                collection,
                useEstimatedCount ? "{}" : QueryFilter,
                useEstimatedCount,
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.CountDocumentsAsync(profile, request, cancellationToken);
            CountResults = result.IsEstimated
                ? $"Estimativa da coleção: {result.Count:N0} documento(s) em {result.Duration.TotalMilliseconds:F0} ms. Não usa filtro."
                : $"Contagem exata do filtro: {result.Count:N0} documento(s) em {result.Duration.TotalMilliseconds:F0} ms.";
            StatusMessage = result.IsEstimated ? "Estimativa da coleção concluída." : "Contagem exata concluída.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanGetDistinctValues))]
    private async Task GetDistinctValuesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DistinctValuesRequest(
                database,
                collection,
                DistinctField,
                QueryFilter,
                decimal.ToInt32(DistinctMaximumValues ?? 1_000),
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.GetDistinctValuesAsync(profile, request, cancellationToken);
            DistinctResults = result.Values.Count == 0
                ? "Nenhum valor distinto encontrado."
                : string.Join(Environment.NewLine, result.Values);
            StatusMessage = $"{result.Values.Count} valor(es) distinto(s) retornado(s) em {result.Duration.TotalMilliseconds:F0} ms." +
                (result.IsTruncated ? " Limite atingido." : string.Empty);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExplainQueryAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            ExplainResults = await _workspace.ExplainAsync(SelectedProfile, query, cancellationToken);
            StatusMessage = "Plano Explain carregado.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteAggregationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new AggregationQuery(
                database,
                collection,
                AggregationPipeline,
                decimal.ToInt32(AggregationLimit ?? 100));
            var result = await _workspace.AggregateAsync(profile, query, cancellationToken);
            AggregationResults = result.Documents.Count == 0
                ? "O pipeline não retornou documentos."
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            StatusMessage = $"Pipeline retornou {result.Documents.Count} documento(s) em {result.Duration.TotalMilliseconds:F0} ms.{(result.IsTruncated ? " Limite atingido." : string.Empty)}";
        });
    }

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

    [RelayCommand(CanExecute = nameof(CanCreateCollection))]
    private async Task CreateCollectionAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var name = NewCollectionName;
        var isView = NewCollectionIsView;
        var request = new CollectionCreateRequest(
            SelectedDatabase,
            name,
            NewCollectionIsCapped,
            NewCollectionMaxSizeBytes is null ? null : decimal.ToInt64(NewCollectionMaxSizeBytes.Value),
            NewCollectionMaxDocuments is null ? null : decimal.ToInt64(NewCollectionMaxDocuments.Value),
            NewCollectionIsView ? NewCollectionViewOn : null,
            NewCollectionIsView ? NewCollectionViewPipeline : null,
            string.IsNullOrWhiteSpace(NewCollectionCollation) ? null : NewCollectionCollation,
            NewCollectionIsClustered,
            NewCollectionIsClustered ? NewCollectionClusteredIndexKey : null);
        if (!await RunAsync(cancellationToken => _workspace.CreateCollectionAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        NewCollectionName = string.Empty;
        NewCollectionIsCapped = false;
        NewCollectionIsView = false;
        NewCollectionIsClustered = false;
        NewCollectionClusteredIndexKey = "{ \"_id\": 1 }";
        NewCollectionViewOn = string.Empty;
        NewCollectionViewPipeline = "[]";
        NewCollectionCollation = string.Empty;
        NewCollectionMaxSizeBytes = null;
        NewCollectionMaxDocuments = null;
        await LoadCollectionsAsync(SelectedDatabase);
        SelectedCollection = Collections.FirstOrDefault(collection => string.Equals(collection, name, StringComparison.Ordinal));
        await RecordAuditAsync(
            isView ? "view.create" : "collection.create",
            SelectedProfile,
            SelectedDatabase,
            name,
            isView ? "View criada." : "Coleção criada.");
        StatusMessage = isView ? $"View {name} criada." : $"Coleção {name} criada.";
    }

    [RelayCommand(CanExecute = nameof(CanRenameCollection))]
    private async Task RenameCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var sourceCollection))
        {
            return;
        }

        var targetCollection = RenameCollectionName;
        if (!await RunAsync(cancellationToken => _workspace.RenameCollectionAsync(
            profile,
            new CollectionRenameRequest(database, sourceCollection, targetCollection, RenameDropTarget),
            cancellationToken)))
        {
            return;
        }

        RenameCollectionName = string.Empty;
        RenameDropTarget = false;
        await LoadCollectionsAsync(database);
        SelectedCollection = Collections.FirstOrDefault(collection => string.Equals(collection, targetCollection, StringComparison.Ordinal));
        await RecordAuditAsync("collection.rename", profile, database, targetCollection, $"Coleção renomeada de {sourceCollection} para {targetCollection}.");
        StatusMessage = $"Coleção {sourceCollection} renomeada para {targetCollection}.";
    }

    [RelayCommand(CanExecute = nameof(CanUpdateView))]
    private async Task UpdateViewAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var view))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.UpdateViewAsync(
            profile,
            new ViewUpdateRequest(database, view, ViewUpdatePipeline, ViewUpdateConfirmation),
            cancellationToken)))
        {
            return;
        }

        ViewUpdateConfirmation = string.Empty;
        await RecordAuditAsync("view.update", profile, database, view, "Pipeline da view atualizado.");
        StatusMessage = $"Pipeline da view {view} atualizado.";
    }

    [RelayCommand(CanExecute = nameof(CanConfigureCollectionValidation))]
    private async Task ConfigureCollectionValidationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.ConfigureCollectionValidationAsync(
            profile,
            new CollectionValidationRequest(
                database,
                collection,
                CollectionValidatorJson,
                CollectionValidationLevel,
                CollectionValidationAction,
                CollectionValidationConfirmation),
            cancellationToken)))
        {
            return;
        }

        CollectionValidationConfirmation = string.Empty;
        await RecordAuditAsync(
            "collection.validation.configure",
            profile,
            database,
            collection,
            $"Validação configurada: nível {CollectionValidationLevel}, ação {CollectionValidationAction}.");
        StatusMessage = $"Validação da coleção {collection} atualizada.";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadCollectionValidationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        CollectionValidationInfo? validation = null;
        if (!await RunAsync(async cancellationToken =>
        {
            validation = await _workspace.GetCollectionValidationAsync(profile, database, collection, cancellationToken);
        }))
        {
            return;
        }

        CollectionValidatorJson = validation!.ValidatorJson;
        CollectionValidationLevel = validation.ValidationLevel;
        CollectionValidationAction = validation.ValidationAction;
        StatusMessage = $"Validação atual da coleção {collection} carregada.";
    }

    [RelayCommand(CanExecute = nameof(CanDropCollection))]
    private async Task DropCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.DropCollectionAsync(
            profile,
            new CollectionDropRequest(database, collection, DropCollectionConfirmation),
            cancellationToken)))
        {
            return;
        }

        DropCollectionConfirmation = string.Empty;
        await LoadCollectionsAsync(database);
        SelectedCollection = null;
        await RecordAuditAsync("collection.drop", profile, database, collection, "Coleção removida.");
        StatusMessage = $"Coleção {collection} removida.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadServerStatusAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetServerStatusAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Status do servidor carregado.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadCurrentOperationsAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetCurrentOperationsAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Operações correntes carregadas.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadProfilerStatusAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetProfilerStatusAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = "Configuração atual do profiler carregada.";
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadTopologyAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetTopologyAsync(SelectedProfile, cancellationToken);
            StatusMessage = "Topologia MongoDB carregada.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanKillOperation))]
    private async Task KillOperationAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var request = new OperationKillRequest(OperationIdToKill, OperationKillConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.KillOperationAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var operationId = request.GetOperationId();
        OperationIdToKill = string.Empty;
        OperationKillConfirmation = string.Empty;
        await RecordAuditAsync("operation.kill", SelectedProfile, "admin", null, $"Interrupção solicitada para a operação {operationId}.");
        StatusMessage = $"Interrupção solicitada para a operação {operationId}.";
    }

    [RelayCommand(CanExecute = nameof(CanValidateCollectionIntegrity))]
    private async Task ValidateCollectionIntegrityAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new CollectionIntegrityCheckRequest(database, collection, CollectionIntegrityConfirmation);
        if (!await RunAsync(async cancellationToken =>
            {
                AdministrationResults = await _workspace.ValidateCollectionIntegrityAsync(profile, request, cancellationToken);
                StatusMessage = $"Validação da coleção {collection} concluída.";
            }))
        {
            return;
        }

        CollectionIntegrityConfirmation = string.Empty;
        await RecordAuditAsync("collection.validate", profile, database, collection, "Integridade da coleção verificada.");
    }

    [RelayCommand(CanExecute = nameof(CanCompactCollection))]
    private async Task CompactCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new CollectionCompactRequest(database, collection, CollectionCompactConfirmation, CollectionCompactForce);
        if (!await RunAsync(async cancellationToken =>
            {
                AdministrationResults = await _workspace.CompactCollectionAsync(profile, request, cancellationToken);
                StatusMessage = $"Compactação da coleção {collection} concluída.";
            }))
        {
            return;
        }

        CollectionCompactConfirmation = string.Empty;
        CollectionCompactForce = false;
        await RecordAuditAsync("collection.compact", profile, database, collection, "Compactação solicitada.");
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

    [RelayCommand]
    private async Task LoadAuditAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentAuditAsync(cancellationToken: cancellationToken);
            AuditEntries.Clear();
            foreach (var entry in entries)
            {
                AuditEntries.Add(entry);
            }

            AdministrationResults = entries.Count == 0
                ? "Nenhuma ação auditada no workspace local."
                : string.Join(Environment.NewLine, entries.Select(entry => entry.DisplayText));
            StatusMessage = $"{entries.Count} ação(ões) locais de auditoria carregada(s).";
        });
    }

    [RelayCommand]
    private async Task ExportAuditAsync()
    {
        if (string.IsNullOrWhiteSpace(AuditExportPath))
        {
            SetError("Informe o caminho do arquivo JSON da auditoria.");
            return;
        }

        if (!string.Equals(Path.GetExtension(AuditExportPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            SetError("O arquivo de auditoria precisa usar a extensão .json.");
            return;
        }

        try
        {
            var entries = await _workspace.GetRecentAuditAsync(500);
            var json = AuditJsonSerializer.Serialize(entries);
            var directory = Path.GetDirectoryName(Path.GetFullPath(AuditExportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                AuditExportPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            StatusMessage = $"Auditoria exportada para {AuditExportPath}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetError(File.Exists(AuditExportPath)
                ? "O arquivo de auditoria já existe. Escolha outro caminho para não sobrescrever a evidência anterior."
                : $"Falha ao exportar auditoria: {exception.Message}");
        }
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

    [RelayCommand(CanExecute = nameof(CanLoadCollectionStats))]
    private async Task LoadCollectionStatsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetCollectionStatsAsync(profile, database, collection, cancellationToken);
            StatusMessage = $"Estatísticas da coleção {collection} carregadas.";
        });
    }

    [RelayCommand]
    private void SuggestMql()
    {
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
        AutocompleteSuggestions = "As sugestões contextuais ficam no editor: use Ctrl+. ou Ctrl+Espaço.";
    }

    private bool CanApplyQuerySuggestion() => SelectedQuerySuggestion is not null;

    [RelayCommand(CanExecute = nameof(CanApplyQuerySuggestion))]
    private void ApplyQuerySuggestion()
    {
        StatusMessage = "Aplique sugestões diretamente no editor com Ctrl+. ou Ctrl+Espaço.";
    }

    [RelayCommand]
    private void SuggestAggregation()
    {
        AutocompleteSuggestions = "As sugestões de agregação ficam no editor: use Ctrl+. ou Ctrl+Espaço.";
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task SampleSchemaFieldsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            _knownFields.Clear();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(sample.Documents));
            AutocompleteSuggestions = _knownFields.Count == 0
                ? "A amostra não contém campos que possam ser sugeridos."
                : $"{_knownFields.Count} campo(s) inferido(s) de {sample.Documents.Count} documento(s). Use Sugerir MQL no filtro.";
            StatusMessage = $"Amostra de schema carregada: {sample.Documents.Count} documento(s), {_knownFields.Count} campo(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InferCollectionValidatorAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            CollectionValidatorJson = MqlAutocompleteService.InferJsonSchema(sample.Documents);
            StatusMessage = $"Validador inferido de {sample.Documents.Count} documento(s); revise antes de aplicar.";
        });
    }

    /// <summary>ObjectId mode generates an ObjectId, UUID v4 mode a UUID v4 and Standard one of each; both lines write the same values.</summary>
    [RelayCommand]
    private void GenerateIdentifier()
    {
        var generated = IdentifierRepresentationService.Generate(new(IdentifierMode, UuidRepresentation));
        IdentifierSnippet = string.Join("\n", generated.Select(value => value.Script));
        IdentifierExtendedJsonSnippet = string.Join("\n", generated.Select(value => value.ExtendedJson));
        StatusMessage = $"Identificador gerado no modo {IdentifierRepresentationService.DisplayName(IdentifierMode)}; construtor e Extended JSON gravam os mesmos valores.";
    }

    /// <summary>Interprets pasted text with the central parser; wrappers and constructors keep their explicit BSON type.</summary>
    [RelayCommand]
    private void InterpretIdentifier()
    {
        try
        {
            var value = IdentifierRepresentationService.ParseIdentifier(IdentifierInput, new(IdentifierMode, UuidRepresentation));
            var kind = value.Kind switch
            {
                IdentifierKind.ObjectId => "ObjectId",
                IdentifierKind.Uuid => "UUID · Binary subtype " + (value.ExtendedJson.Contains("\"subType\":\"03\"", StringComparison.Ordinal) ? "3" : "4"),
                _ => "Binary subtype 3 · UUID legado de origem desconhecida"
            };
            var lines = new List<string>
            {
                "Tipo: " + kind + (value.IsExplicitType ? " (explícito)" : " (inferido do texto)"),
                "Valor: " + value.Text,
                "Extended JSON canônico: " + value.ExtendedJson
            };
            if (value.UuidEquivalent is { } uuid) lines.Add("UUID equivalente: " + uuid + " (representação alternativa; o ObjectId não é alterado)");
            lines.Add("Filtro: { _id: " + value.Text + " }");
            IdentifierInterpretation = string.Join("\n", lines);
        }
        catch (FormatException exception)
        {
            IdentifierInterpretation = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InsertDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.InsertAsync(profile, database, collection, DocumentJson, cancellationToken);
            StatusMessage = $"Documento inserido.{(result.InsertedId is null ? string.Empty : $" _id: {result.InsertedId}")}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanPreviewDocument))]
    private async Task PreviewDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var page = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, MutationFilter, Limit: 1), cancellationToken);
            DocumentPreview = page.Documents.Count == 0
                ? "Nenhum documento corresponde ao filtro atual."
                : page.Documents[0];
            StatusMessage = page.Documents.Count == 0
                ? "Prévia não encontrou documento."
                : "Prévia carregada; revise o documento antes de substituir ou atualizar campos.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanDuplicatePreviewDocument))]
    private void DuplicatePreviewDocument()
    {
        try
        {
            DocumentJson = DocumentDuplicateDraft.CreateWithoutId(DocumentPreview);
            StatusMessage = "Rascunho de cópia preparado sem o _id. Revise e use Inserir para criar o novo documento.";
        }
        catch (ArgumentException exception)
        {
            SetError(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InsertManyDocumentsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new BulkInsertRequest(database, collection, BulkDocumentsJson, decimal.ToInt32(BulkMaximumDocuments ?? 1_000), BulkOrdered);
            var count = await _workspace.InsertManyAsync(profile, request, cancellationToken);
            StatusMessage = $"Inserção em lote concluída: {count} documento(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ReplaceDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.ReplaceAsync(profile, database, collection, MutationFilter, DocumentJson, cancellationToken);
            StatusMessage = $"Substituição concluída: {result.MatchedCount} documento(s) encontrado(s), {result.ModifiedCount} modificado(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task UpdateDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DocumentUpdateRequest(
                database,
                collection,
                MutationFilter,
                UpdateJson,
                UpdateUpsert,
                string.IsNullOrWhiteSpace(UpdateArrayFiltersJson) ? null : UpdateArrayFiltersJson);
            var result = await _workspace.UpdateAsync(profile, request, cancellationToken);
            StatusMessage = $"Atualização parcial concluída: {result.MatchedCount} encontrado(s), {result.ModifiedCount} modificado(s)." +
                (result.InsertedId is null ? string.Empty : $" Upsertado: {result.InsertedId}.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task FindAndModifyAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DocumentUpdateRequest(database, collection, MutationFilter, UpdateJson, UpdateUpsert, string.IsNullOrWhiteSpace(UpdateArrayFiltersJson) ? null : UpdateArrayFiltersJson);
            var result = await _workspace.FindAndModifyAsync(profile, request, cancellationToken);
            FindAndModifyResult = result.DocumentJson ?? "Nenhum documento correspondeu ao filtro.";
            StatusMessage = result.DocumentJson is null ? "Find-and-modify não encontrou documento." : "Find-and-modify concluído; o documento após a alteração foi retornado.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task DeleteDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = DeleteManyDocuments
                ? await _workspace.DeleteManyAsync(profile, database, collection, MutationFilter, cancellationToken)
                : await _workspace.DeleteAsync(profile, database, collection, MutationFilter, cancellationToken);
            StatusMessage = $"Exclusão {(DeleteManyDocuments ? "em lote" : "de um documento")} concluída: {result.MatchedCount} documento(s) removido(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var indexes = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            IndexResults = indexes.Count == 0 ? "Nenhum índice encontrado." : string.Join(Environment.NewLine + Environment.NewLine, indexes);
            StatusMessage = $"{indexes.Count} índice(s) carregado(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexUsageStatsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var statistics = await _workspace.GetIndexUsageStatsAsync(profile, database, collection, cancellationToken);
            IndexResults = statistics.Count == 0 ? "Nenhuma estatística de uso retornada pelo servidor." : string.Join(Environment.NewLine + Environment.NewLine, statistics);
            StatusMessage = $"{statistics.Count} estatística(s) de uso de índice carregada(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task CreateIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new IndexCreateRequest(
                database,
                collection,
                IndexKeys,
                IndexName,
                IndexIsUnique,
                IndexIsSparse,
                IndexExpireAfterSeconds is null ? null : decimal.ToInt32(IndexExpireAfterSeconds.Value),
                string.IsNullOrWhiteSpace(IndexPartialFilter) ? null : IndexPartialFilter,
                string.IsNullOrWhiteSpace(IndexCollation) ? null : IndexCollation,
                IndexIsHidden,
                string.IsNullOrWhiteSpace(IndexWildcardProjection) ? null : IndexWildcardProjection);
            var name = await _workspace.CreateIndexAsync(profile, request, cancellationToken);
            StatusMessage = $"Índice {name} criado.";
            await LoadIndexesAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropIndex))]
    private async Task DropIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var name = IndexNameToDrop.Trim();
        if (!await RunAsync(async cancellationToken =>
        {
            await _workspace.DropIndexAsync(profile, new IndexDropRequest(database, collection, name), cancellationToken);
            StatusMessage = $"Índice {name} removido.";
        }))
        {
            return;
        }

        IndexNameToDrop = string.Empty;
        await LoadIndexesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSetIndexVisibility))]
    private async Task SetIndexVisibilityAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new IndexVisibilityRequest(database, collection, IndexNameForVisibility, IndexVisibilityConfirmation, IndexVisibilityHidden);
        if (!await RunAsync(cancellationToken => _workspace.SetIndexVisibilityAsync(profile, request, cancellationToken)))
        {
            return;
        }

        var name = request.Name.Trim();
        IndexNameForVisibility = string.Empty;
        IndexVisibilityConfirmation = string.Empty;
        await RecordAuditAsync("index.visibility", profile, database, collection, $"Visibilidade do índice {name} alterada.");
        await LoadIndexesAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task ExecuteScriptAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.ExecuteScriptAsync(SelectedProfile, ScriptText, ScriptInput, SelectedDatabase, cancellationToken);
            ScriptResults = BuildScriptOutput(result);
            StatusMessage = result.ExitCode == 0
                ? $"Script concluído em {result.Duration.TotalMilliseconds:F0} ms."
                : $"Script concluído com código {result.ExitCode}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveScript))]
    private async Task SaveScriptAsync()
    {
        if (!await RunAsync(async cancellationToken =>
        {
            await _workspace.SaveScriptAsync(ScriptFilePath, ScriptText, cancellationToken);
            await RememberScriptAsync(cancellationToken);
        }))
        {
            return;
        }

        StatusMessage = $"Script salvo em {Path.GetFullPath(ScriptFilePath)}.";
    }

    [RelayCommand(CanExecute = nameof(CanLoadScript))]
    private async Task LoadScriptAsync()
    {
        var loaded = string.Empty;
        if (!await RunAsync(async cancellationToken =>
        {
            loaded = await _workspace.LoadScriptAsync(ScriptFilePath, cancellationToken);
            await RememberScriptAsync(cancellationToken);
        }))
        {
            return;
        }

        ScriptText = loaded;
        StatusMessage = $"Script carregado de {Path.GetFullPath(ScriptFilePath)}.";
    }

    private async Task RememberScriptAsync(CancellationToken cancellationToken)
    {
        if (!ScriptHistoryEnabled)
        {
            return;
        }

        var entry = ScriptHistoryEntry.Create(ScriptFilePath, inputJson: PersistScriptInput ? ScriptInput : null);
        await _workspace.SaveScriptHistoryAsync(entry, cancellationToken);
        var existing = ScriptHistory.FirstOrDefault(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ScriptHistory.Remove(existing);
        }

        ScriptHistory.Insert(0, entry);
        while (ScriptHistory.Count > 50)
        {
            ScriptHistory.RemoveAt(ScriptHistory.Count - 1);
        }
    }

    private async Task RecordAuditAsync(string action, ConnectionProfile? profile, string? database, string? collection, string summary)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile?.Id, database, collection, summary));
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ação concluída, mas a auditoria local não foi registrada: {exception.Message}";
        }
    }

    private async Task LoadCollectionsAsync(string database)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var collections = await _workspace.GetCollectionsAsync(SelectedProfile, database, cancellationToken);
            Collections.Clear();

            foreach (var collection in collections)
            {
                Collections.Add(collection);
            }

            StatusMessage = $"{Collections.Count} coleção(ões) carregada(s) em {database}.";
        });
    }

    private bool TryGetCollectionContext(out ConnectionProfile profile, out string database, out string collection)
    {
        profile = SelectedProfile!;
        database = SelectedDatabase!;
        collection = SelectedCollection!;
        return profile is not null && !string.IsNullOrWhiteSpace(database) && !string.IsNullOrWhiteSpace(collection);
    }

    private static string AddCredentials(string connectionString, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            throw new ArgumentException("A URI MongoDB é inválida.", nameof(connectionString));
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = connectionString.IndexOfAny(['/', '?'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = connectionString.Length;
        }

        var authority = connectionString[authorityStart..authorityEnd];
        var host = authority[(authority.LastIndexOf('@') + 1)..];
        return connectionString[..authorityStart] + Uri.EscapeDataString(username.Trim()) + ":" + (string.IsNullOrEmpty(password) ? "${MONGODB_PASSWORD}" : Uri.EscapeDataString(password)) + "@" + host + connectionString[authorityEnd..];
    }

    private static string BuildScriptOutput(ScriptExecutionResult result)
    {
        var sections = new List<string>();

        if (result.Results.Count > 0)
        {
            sections.Add("RESULTADOS EJSON" + Environment.NewLine + string.Join(Environment.NewLine, result.Results));
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            sections.Add("CONSOLE" + Environment.NewLine + result.StandardOutput);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            sections.Add("ERROS" + Environment.NewLine + result.StandardError);
        }

        return sections.Count == 0 ? "O script não retornou resultados nem mensagens." : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private async Task<bool> RunAsync(Func<CancellationToken, Task> operation)
    {
        var ownsCancellation = _operationCancellation is null;
        var cancellation = _operationCancellation ?? new CancellationTokenSource();

        if (ownsCancellation)
        {
            _operationCancellation = cancellation;
            IsOperationRunning = true;
            FooterMessage = "Operação em andamento…";
        }

        var keepTerminalFooter = false;
        try
        {
            await operation(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Operação cancelada pelo usuário.";
            FooterMessage = "Efeitos já enviados ao MongoDB não são revertidos automaticamente.";
            keepTerminalFooter = true;
            return false;
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            keepTerminalFooter = true;
            return false;
        }
        finally
        {
            if (ownsCancellation)
            {
                _operationCancellation = null;
                IsOperationRunning = false;
                cancellation.Dispose();
                if (!keepTerminalFooter)
                {
                    FooterMessage = "Workspace local: LiteDB. Credenciais persistidas serão adicionadas com cofre do sistema.";
                }
            }
        }
    }

    private void SetError(string message)
    {
        StatusMessage = $"Erro: {message}";
        FooterMessage = "A operação não foi concluída. Consulte a mensagem acima.";
    }
}
