using CommunityToolkit.Mvvm.ComponentModel;
using PartFinder.Models;
using PartFinder.Services;
using System.Collections.ObjectModel;

namespace PartFinder.ViewModels;

public partial class LinkedTemplatesViewModel : ViewModelBase
{
    private readonly ITemplateSchemaService _templates;
    private readonly BackendApiClient _api;
    private readonly ICurrentUserAccessService _access;
    private readonly IMasterDataRecordsService _records;
    private LinkedTemplateBrowseItem? _pendingLinkedBrowse;

    public LinkedTemplatesViewModel(
        ITemplateSchemaService templates,
        BackendApiClient api,
        ICurrentUserAccessService access,
        IMasterDataRecordsService records)
    {
        _templates = templates;
        _api = api;
        _access = access;
        _records = records;
    }

    public ObservableCollection<PartTemplateDefinition> Templates { get; } = [];
    public ObservableCollection<LinkedTemplateBrowseItem> LinkedTemplatesForBrowse { get; } = [];
    public ObservableCollection<RelationColumnPickItem> PreviewColumnPicks { get; } = [];
    public ObservableCollection<LinkedTemplatePreviewRow> PreviewRows { get; } = [];

    private readonly List<WorksheetRelationDto> _allRelations = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasBrowseSourceTemplate),
        nameof(ShowNoLinksForBrowseMessage))]
    private PartTemplateDefinition? _browseSourceTemplate;

    [ObservableProperty]
    private LinkedTemplateBrowseItem? _selectedLinkedBrowseItem;

    [ObservableProperty]
    private string _previewTitle = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasBrowseSourceTemplate => BrowseSourceTemplate is not null;
    public bool HasLinkedTemplatesForBrowse => LinkedTemplatesForBrowse.Count > 0;
    public bool ShowNoLinksForBrowseMessage => HasBrowseSourceTemplate && !HasLinkedTemplatesForBrowse;
    public bool HasPreviewData => PreviewRows.Count > 0;
    public string PendingLinkedTemplateName => _pendingLinkedBrowse?.TemplateName ?? string.Empty;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var list = await _templates.GetTemplatesAsync(ct).ConfigureAwait(true);
            var filtered = _access.FilterTemplatesForParts(list);
            Templates.Clear();
            foreach (var item in filtered)
            {
                Templates.Add(item);
            }

            await LoadAllRelationsAsync(ct).ConfigureAwait(true);
            BrowseSourceTemplate ??= Templates.FirstOrDefault();
            RefreshLinkedTemplatesForBrowse();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnBrowseSourceTemplateChanged(PartTemplateDefinition? value)
    {
        RefreshLinkedTemplatesForBrowse();
    }

    private async Task LoadAllRelationsAsync(CancellationToken ct = default)
    {
        _allRelations.Clear();
        var (ok, _, relations) = await _api.GetRelationsAsync(ct).ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        _allRelations.AddRange(relations);
    }

    private void RefreshLinkedTemplatesForBrowse()
    {
        LinkedTemplatesForBrowse.Clear();
        SelectedLinkedBrowseItem = null;
        PreviewRows.Clear();
        PreviewTitle = string.Empty;
        OnPropertyChanged(nameof(HasLinkedTemplatesForBrowse));
        OnPropertyChanged(nameof(ShowNoLinksForBrowseMessage));
        OnPropertyChanged(nameof(HasPreviewData));

        if (BrowseSourceTemplate is null)
        {
            return;
        }

        foreach (var relation in _allRelations.Where(
                     r => string.Equals(r.PrimaryTemplateId, BrowseSourceTemplate.Id, StringComparison.Ordinal)))
        {
            var lookup = Templates.FirstOrDefault(t => t.Id == relation.LookupTemplateId);
            LinkedTemplatesForBrowse.Add(new LinkedTemplateBrowseItem
            {
                RelationId = relation.Id,
                TemplateId = relation.LookupTemplateId,
                TemplateName = lookup?.Name ?? relation.LookupTemplateId,
                Relation = relation,
                LookupTemplate = lookup,
            });
        }

        OnPropertyChanged(nameof(HasLinkedTemplatesForBrowse));
        OnPropertyChanged(nameof(ShowNoLinksForBrowseMessage));
    }

    public void PreparePreviewColumnPicker(LinkedTemplateBrowseItem item)
    {
        SelectedLinkedBrowseItem = item;
        _pendingLinkedBrowse = item;
        PreviewColumnPicks.Clear();

        var template = item.LookupTemplate
                         ?? Templates.FirstOrDefault(t => t.Id == item.TemplateId);
        if (template is null)
        {
            return;
        }

        var displaySet = new HashSet<string>(item.Relation.DisplayColumns, StringComparer.OrdinalIgnoreCase);
        var matchKeys = new HashSet<string>(
            item.Relation.MatchKeys.Select(k => k.TargetColumn).Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.OrdinalIgnoreCase);
        if (matchKeys.Count == 0 && !string.IsNullOrWhiteSpace(item.Relation.TriggerColumn))
        {
            matchKeys.Add(item.Relation.TriggerColumn);
        }

        foreach (var field in template.Fields.OrderBy(f => f.DisplayOrder))
        {
            var pick = new RelationColumnPickItem(field.Label, isPrimary: false)
            {
                IsChecked = displaySet.Contains(field.Label) || matchKeys.Contains(field.Label),
            };
            PreviewColumnPicks.Add(pick);
        }

        OnPropertyChanged(nameof(PendingLinkedTemplateName));
    }

    public async Task LoadLinkedPreviewDataAsync(CancellationToken ct = default)
    {
        if (_pendingLinkedBrowse is null)
        {
            return;
        }

        var template = _pendingLinkedBrowse.LookupTemplate
                       ?? Templates.FirstOrDefault(t => t.Id == _pendingLinkedBrowse.TemplateId);
        if (template is null)
        {
            ErrorMessage = "Linked template not found.";
            return;
        }

        var selectedColumns = PreviewColumnPicks
            .Where(c => c.IsChecked)
            .Select(c => c.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedColumns.Count == 0)
        {
            ErrorMessage = "Select at least one column to preview.";
            return;
        }

        IsLoadingPreview = true;
        ErrorMessage = string.Empty;
        PreviewRows.Clear();
        PreviewTitle = $"{BrowseSourceTemplate?.Name} → {template.Name}";
        OnPropertyChanged(nameof(HasPreviewData));

        try
        {
            var rows = await _records.GetRowsAsync(template.Id, ct).ConfigureAwait(true);
            foreach (var row in rows.OrderBy(r => r.RowOrder))
            {
                var cells = selectedColumns
                    .Select(col => new PreviewColumnCell(col, GetRowCellByLabel(row, template, col)))
                    .ToList();
                PreviewRows.Add(new LinkedTemplatePreviewRow(cells));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingPreview = false;
            OnPropertyChanged(nameof(HasPreviewData));
        }
    }

    private static string GetRowCellByLabel(MasterDataRowRecord row, PartTemplateDefinition template, string columnLabel)
    {
        var field = template.Fields.FirstOrDefault(
            f => string.Equals(f.Label, columnLabel, StringComparison.OrdinalIgnoreCase));
        var key = field?.Key ?? columnLabel;
        return row.Values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }
}
