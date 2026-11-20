using PartFinder.Models;

namespace PartFinder.Services;

/// <summary>
/// RBAC policy for the desktop shell. WMS workbook access requires a successful
/// <c>GET /wms/v1/session/profile</c> (NestJS WmsSessionGuard), not desktop role alone.
/// </summary>
public sealed class CurrentUserAccessService : ICurrentUserAccessService
{
    private readonly ILocalSetupContext _setupContext;
    private readonly AdminSessionStore _adminSession;
    private readonly IOrgUserDirectoryService _users;
    private readonly WmsApiClient _wms;

    private UserAccessCapabilities _capabilities = UserAccessCapabilities.FailClosed;
    private bool _isOrganizationAdministrator;
    private string? _wmsWorkbookAccessDeniedReason;

    public CurrentUserAccessService(
        ILocalSetupContext setupContext,
        AdminSessionStore adminSession,
        IOrgUserDirectoryService users,
        WmsApiClient wms)
    {
        _setupContext = setupContext;
        _adminSession = adminSession;
        _users = users;
        _wms = wms;
    }

    public UserAccessCapabilities Capabilities => _capabilities;

    public bool IsOrganizationAdministrator => _isOrganizationAdministrator;

    public string? WmsWorkbookAccessDeniedReason => _wmsWorkbookAccessDeniedReason;

    public bool CanViewAllAuditHistory => _capabilities.CanManageInventory;

    public string? GetCurrentUserEmail() => ResolveCurrentEmail();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _wmsWorkbookAccessDeniedReason = null;
            _setupContext.Refresh();
            _adminSession.Load();

            var email = ResolveCurrentEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                _isOrganizationAdministrator = true;
                _capabilities = UserAccessCapabilities.FullAdmin;
                await EnrichWmsWorkbookAccessAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var record = await _users.FindByEmailAsync(email, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                // No record in org_app_users means org creator (org_admins). WMS still gated by API.
                _isOrganizationAdministrator = true;
                _capabilities = UserAccessCapabilities.FullAdmin;
                await EnrichWmsWorkbookAccessAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(record.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                _isOrganizationAdministrator = true;
                _capabilities = new UserAccessCapabilities
                {
                    CanAccessMasterData = true,
                    CanAccessDashboard = true,
                    CanAccessParts = true,
                    CanAccessTemplates = true,
                    CanAccessSettings = true,
                    CanAccessUserManagement = false,

                    CanAddTemplate = true,
                    CanViewTemplate = true,
                    CanEditTemplate = true,
                    CanDeleteTemplate = true,

                    CanCopyMasterData = true,
                    CanViewMasterData = true,
                    CanEditMasterData = true,
                    CanAddMasterData = true,
                    CanDeleteMasterData = true,

                    PartsAllTemplates = true,
                    AllowedTemplateIds = Array.Empty<string>(),

                    CanViewInventory = true,
                    CanManageInventory = true,
                    CanAccessWmsWorkbook = false,
                };
                await EnrichWmsWorkbookAccessAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(record.Role, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                _isOrganizationAdministrator = false;
                _capabilities = new UserAccessCapabilities
                {
                    CanAccessMasterData = true,
                    CanAccessDashboard = true,
                    CanAccessParts = true,
                    CanAccessTemplates = true,
                    CanAccessSettings = true,
                    CanAccessUserManagement = false,

                    CanAddTemplate = record.TemplatePermissions?.Add ?? false,
                    CanViewTemplate = record.TemplatePermissions?.View ?? false,
                    CanEditTemplate = record.TemplatePermissions?.Edit ?? false,
                    CanDeleteTemplate = record.TemplatePermissions?.Delete ?? false,

                    CanCopyMasterData = record.MasterDataPermissions?.Copy ?? false,
                    CanViewMasterData = record.MasterDataPermissions?.View ?? false,
                    CanEditMasterData = record.MasterDataPermissions?.Edit ?? false,
                    CanAddMasterData = false,
                    CanDeleteMasterData = false,

                    PartsAllTemplates = record.PartsAllTemplates,
                    AllowedTemplateIds = record.AllowedTemplateIds,

                    CanViewInventory = true,
                    CanManageInventory = false,
                    CanAccessWmsWorkbook = false,
                };
                await EnrichWmsWorkbookAccessAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            _isOrganizationAdministrator = false;
            _capabilities = UserAccessCapabilities.FailClosed;
            await EnrichWmsWorkbookAccessAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogClient.Post("warn", "CurrentUserAccessService.RefreshAsync", ex.Message);
            _isOrganizationAdministrator = false;
            _capabilities = UserAccessCapabilities.FailClosed;
            _wmsWorkbookAccessDeniedReason =
                "Could not verify permissions. Sign in again or contact your organisation admin.";
        }
    }

    private async Task EnrichWmsWorkbookAccessAsync(CancellationToken cancellationToken)
    {
        _wmsWorkbookAccessDeniedReason = null;
        _adminSession.Load();
        if (!_adminSession.HasSession)
        {
            _capabilities = _capabilities.WithWmsWorkbook(false);
            return;
        }

        var profile = await _wms.GetSessionProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile.Ok && profile.Data is not null)
        {
            _capabilities = _capabilities.WithWmsWorkbook(true);
            return;
        }

        _capabilities = _capabilities.WithWmsWorkbook(false);
        if (IsWmsAccessDeniedError(profile.Error))
        {
            _wmsWorkbookAccessDeniedReason =
                string.IsNullOrWhiteSpace(profile.Error)
                    ? "Warehouse Management access is not granted for this account."
                    : profile.Error;
            return;
        }

        _wmsWorkbookAccessDeniedReason = string.IsNullOrWhiteSpace(profile.Error)
            ? "Could not reach the WMS API (GET /wms/v1/session/profile). " +
              "For staging acceptance use org 900152, a WMS operator account, and start the app with " +
              "PARTFINDER_STAGING_API=1 so the client uses appsettings.Staging.json."
            : profile.Error;
    }

    private static bool IsWmsAccessDeniedError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("403", StringComparison.Ordinal) ||
               error.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("No WMS operator grants", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("Warehouse Management is not enabled", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("not a member of this organization", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PartTemplateDefinition> FilterTemplatesForParts(IReadOnlyList<PartTemplateDefinition> templates)
    {
        var cap = _capabilities;
        IEnumerable<PartTemplateDefinition> q = templates;

        if (!cap.CanAccessMasterData)
        {
            q = q.Where(t => !string.Equals(
                t.Id,
                MongoTemplateSchemaService.MasterDataTemplateId,
                StringComparison.OrdinalIgnoreCase));
        }

        if (!cap.PartsAllTemplates && cap.AllowedTemplateIds.Count > 0)
        {
            var allow = new HashSet<string>(cap.AllowedTemplateIds, StringComparer.OrdinalIgnoreCase);
            q = q.Where(t => allow.Contains(t.Id));
        }

        return q.ToList();
    }

    private string? ResolveCurrentEmail()
    {
        var a = _adminSession.Email?.Trim();
        if (!string.IsNullOrEmpty(a))
        {
            return a;
        }

        _setupContext.Refresh();
        return _setupContext.AdminEmail?.Trim();
    }
}