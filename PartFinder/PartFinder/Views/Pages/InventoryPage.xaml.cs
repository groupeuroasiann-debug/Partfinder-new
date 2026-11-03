using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PartFinder.Services;
using PartFinder.ViewModels;

namespace PartFinder.Views.Pages;

public sealed partial class InventoryPage : Page
{
    public InventoryViewModel ViewModel { get; }

    private readonly MongoInventoryService _inventoryService;
    private readonly MongoAuditService _auditService;
    private readonly AdminSessionStore _session;

    public InventoryPage()
    {
        ViewModel = App.Services.GetRequiredService<InventoryViewModel>();
        _inventoryService = App.Services.GetRequiredService<MongoInventoryService>();
        _auditService = App.Services.GetRequiredService<MongoAuditService>();
        _session = App.Services.GetRequiredService<AdminSessionStore>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FormMode == InventoryFormMode.None)
        {
            await ViewModel.LoadInventoryAsync();
        }
    }

    private void OnAddStockClick(object sender, RoutedEventArgs e)
    {
        ItemFormPanel.PrepareForAdd();
        ViewModel.BeginAddForm();
    }

    private void OnEditInventoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InventoryItem item)
        {
            ItemFormPanel.PrepareForEdit(item);
            ViewModel.BeginEditForm(item);
        }
    }

    private void OnCancelFormClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseForm();
    }

    private async void OnSaveFormClick(object sender, RoutedEventArgs e)
    {
        ViewModel.FormStatusMessage = string.Empty;

        if (ViewModel.FormMode == InventoryFormMode.Add)
        {
            await SaveNewItemAsync();
        }
        else if (ViewModel.FormMode == InventoryFormMode.Edit && ViewModel.SelectedItem is not null)
        {
            await SaveEditedItemAsync(ViewModel.SelectedItem);
        }
    }

    private async Task SaveNewItemAsync()
    {
        var partName = ItemFormPanel.PartName;
        if (string.IsNullOrWhiteSpace(partName))
        {
            ViewModel.FormStatusMessage = "Part name is required.";
            return;
        }

        if (!int.TryParse(ItemFormPanel.QuantityText, out var qty) || qty < 0)
        {
            ViewModel.FormStatusMessage = "Quantity must be a valid non-negative number.";
            return;
        }

        int.TryParse(ItemFormPanel.MinStockText, out var minStock);
        double.TryParse(ItemFormPanel.UnitPriceText, out var unitPrice);

        var doc = new InventoryDoc
        {
            PartName = partName,
            PartId = ItemFormPanel.PartId,
            Category = ItemFormPanel.Category,
            Supplier = ItemFormPanel.Supplier,
            Location = ItemFormPanel.Location,
            Quantity = qty,
            MinStock = minStock > 0 ? minStock : 10,
            UnitPrice = unitPrice,
            Notes = ItemFormPanel.Notes,
            LastUpdated = DateTime.UtcNow,
        };

        try
        {
            await _inventoryService.UpsertAsync(doc).ConfigureAwait(true);

            _session.Load();
            await _auditService.LogAsync(new AuditDoc
            {
                EventType = "Stock Change",
                Action = "Item Added",
                Details = $"{partName} — {qty} units added to inventory",
                User = _session.Email ?? "Admin",
                Timestamp = DateTime.UtcNow,
            }).ConfigureAwait(true);

            ViewModel.CloseForm();
            await ViewModel.LoadInventoryAsync().ConfigureAwait(true);
            ViewModel.StatusMessage = $"{partName} ({qty} units) saved to inventory.";
        }
        catch (Exception ex)
        {
            ViewModel.FormStatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    private async Task SaveEditedItemAsync(InventoryItem item)
    {
        if (!int.TryParse(ItemFormPanel.QuantityText, out var newQty) || newQty < 0)
        {
            ViewModel.FormStatusMessage = "Quantity must be a valid non-negative number.";
            return;
        }

        int.TryParse(ItemFormPanel.MinStockText, out var newMinStock);
        double.TryParse(ItemFormPanel.UnitPriceText, out var newUnitPrice);

        var allDocs = await _inventoryService.GetAllAsync().ConfigureAwait(true);
        var doc = allDocs.FirstOrDefault(d => d.MongoId.ToString() == item.Id);
        if (doc is null)
        {
            ViewModel.FormStatusMessage = "Item not found in database.";
            return;
        }

        var oldQty = doc.Quantity;
        doc.Quantity = newQty;
        doc.MinStock = newMinStock > 0 ? newMinStock : doc.MinStock;
        doc.UnitPrice = newUnitPrice > 0 ? newUnitPrice : doc.UnitPrice;
        doc.Location = string.IsNullOrWhiteSpace(ItemFormPanel.Location) ? doc.Location : ItemFormPanel.Location;
        doc.Supplier = string.IsNullOrWhiteSpace(ItemFormPanel.Supplier) ? doc.Supplier : ItemFormPanel.Supplier;
        doc.LastUpdated = DateTime.UtcNow;

        try
        {
            await _inventoryService.UpsertAsync(doc).ConfigureAwait(true);

            _session.Load();
            var change = newQty > oldQty ? $"+{newQty - oldQty}" : $"{newQty - oldQty}";
            await _auditService.LogAsync(new AuditDoc
            {
                EventType = "Stock Change",
                Action = "Stock Updated",
                Details = $"{item.PartName} — {oldQty} → {newQty} units ({change})",
                User = _session.Email ?? "Admin",
                Timestamp = DateTime.UtcNow,
            }).ConfigureAwait(true);

            ViewModel.CloseForm();
            await ViewModel.LoadInventoryAsync().ConfigureAwait(true);
            ViewModel.StatusMessage = $"{item.PartName} updated to {newQty} units.";
        }
        catch (Exception ex)
        {
            ViewModel.FormStatusMessage = $"Failed to update: {ex.Message}";
        }
    }
}
