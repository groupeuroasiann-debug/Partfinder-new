using System.Collections.ObjectModel;
using PartFinder.Models;
using PartFinder.Services;

namespace PartFinder.ViewModels;

public partial class WmsWorkbookViewModel
{
    public string HeaderOrgCode { get; private set; } = string.Empty;
    public string HeaderWarehouseName { get; private set; } = "—";
    public string HeaderUserEmail { get; private set; } = string.Empty;
    public string HeaderRoleDisplay { get; private set; } = string.Empty;
    public bool ShowStagingBanner { get; private set; }
    public string StagingBannerText { get; private set; } = string.Empty;

    public string SectionTitle { get; private set; } = "Dashboard";
    public string SectionDescription { get; private set; } =
        "Connection health, staging gate status, and quick counts for receiving workflows.";

    public int DashboardExpectedCount { get; private set; }
    public int DashboardGoodsReceiptCount { get; private set; }
    public int DashboardQcPendingCount { get; private set; }
    public int DashboardPutawayPendingCount { get; private set; }

    private static readonly IReadOnlyDictionary<string, (string Title, string Description)> SectionCopy =
        new Dictionary<string, (string, string)>
        {
            [WmsWorkbookSections.Dashboard] = (
                "Dashboard",
                "Overview of WMS connectivity, staging gate, and receiving workload counts from the live API."),
            [WmsWorkbookSections.ExpectedReceipts] = (
                "Expected receipts",
                "Register products that are expected to arrive at your warehouse (ASN / PO). Required: stock owner, SKU lines, and expected quantities."),
            [WmsWorkbookSections.GoodsReceiving] = (
                "Goods receiving",
                "Confirm the quantity physically received against an expected receipt. Select warehouse, receiving location, and lines before confirming."),
            [WmsWorkbookSections.ReceivedItemDetails] = (
                "Received item details",
                "Review receipt lines with quantities, QC status, lots, and serials."),
            [WmsWorkbookSections.QualityControl] = (
                "Quality control",
                "Inspect received goods and record quantities accepted or rejected. Blocked stock stays at receiving until disposition."),
            [WmsWorkbookSections.PutawayTasks] = (
                "Put-away tasks",
                "Move accepted goods from receiving into an authorized storage location."),
            [WmsWorkbookSections.LocationAssignment] = (
                "Location assignment",
                "View put-away tasks and choose an authorized storage bin destination."),
            [WmsWorkbookSections.ReceivingHistory] = (
                "Receiving history",
                "Audit trail of receiving actions (read-only)."),
            [WmsWorkbookSections.ReceivingDiscrepancies] = (
                "Discrepancies",
                "Shortages, overages, damage, and blocked lines flagged during receiving."),
            [WmsWorkbookSections.WarehouseMaster] = (
                "Warehouse master",
                "Sites and codes for your organization. Admin setup."),
            [WmsWorkbookSections.Locations] = (
                "Zones & locations",
                "Define receiving, storage, and staging bin paths."),
            [WmsWorkbookSections.StockOwners] = (
                "3PL stock owners",
                "Companies that own inventory in this warehouse."),
            [WmsWorkbookSections.StockBalances] = (
                "Stock balances",
                "Read-only on-hand by SKU and location."),
            [WmsWorkbookSections.MovementLedger] = (
                "Movement ledger",
                "Posted stock transactions (read-only)."),
            [WmsWorkbookSections.Administration] = (
                "Administration",
                "Staging gate evidence, operator grants, and health diagnostics."),
        };

    public void UpdateHeaderWarehouse()
    {
        HeaderWarehouseName = SelectedWarehouse?.Name ?? SelectedWarehouse?.Code ?? "—";
        OnPropertyChanged(nameof(HeaderWarehouseName));
    }

    public void RefreshHeaderChrome()
    {
        _setupContext.Refresh();
        _sessionStore.Load();
        HeaderOrgCode = _setupContext.OrgCode?.Trim() ?? string.Empty;
        HeaderUserEmail = _access.GetCurrentUserEmail() ?? _sessionStore.Email ?? "—";
        ShowStagingBanner = LicenseApiClient.GetBaseUrl()
            .Contains("partfinder-backend-staging", StringComparison.OrdinalIgnoreCase);
        StagingBannerText = ShowStagingBanner
            ? "Staging environment — org 900152 test data. Live ERP posting is disabled."
            : string.Empty;
        OnPropertyChanged(nameof(HeaderOrgCode));
        OnPropertyChanged(nameof(HeaderUserEmail));
        OnPropertyChanged(nameof(ShowStagingBanner));
        OnPropertyChanged(nameof(StagingBannerText));
    }

    public void UpdateSectionChrome(string? sectionId)
    {
        var id = sectionId ?? WmsWorkbookSections.Dashboard;
        if (SectionCopy.TryGetValue(id, out var copy))
        {
            SectionTitle = copy.Title;
            SectionDescription = copy.Description;
        }
        else
        {
            SectionTitle = id.Replace('_', ' ');
            SectionDescription = string.Empty;
        }

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
    }

    public void RefreshDashboardCounts()
    {
        DashboardExpectedCount = ExpectedReceiptRows.Count;
        DashboardGoodsReceiptCount = GoodsReceiptRows.Count;
        DashboardQcPendingCount = QcRows.Count;
        DashboardPutawayPendingCount = PutawayRows.Count(r =>
            string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(DashboardExpectedCount));
        OnPropertyChanged(nameof(DashboardGoodsReceiptCount));
        OnPropertyChanged(nameof(DashboardQcPendingCount));
        OnPropertyChanged(nameof(DashboardPutawayPendingCount));
    }
}