using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PartFinder.Models;
using System.Security.Cryptography;
using System.Text;

namespace PartFinder.Services;

/// <summary>Org-scoped users (invites) in tenant MongoDB.</summary>
public sealed class MongoOrgUserDirectoryService : IOrgUserDirectoryService
{
    public const string CollectionName = "org_app_users";

    private readonly ILocalSetupContext _setupContext;

    public MongoOrgUserDirectoryService(ILocalSetupContext setupContext)
    {
        _setupContext = setupContext;
    }

    private IMongoCollection<OrgAppUserDoc>? TryGetCollection()
        => GetCollection(rawUriOverride: null, orgCodeOverride: null);

    private IMongoCollection<OrgAppUserDoc>? GetCollection(string? rawUriOverride, string? orgCodeOverride)
    {
        var orgCode = orgCodeOverride ?? _setupContext.OrgCode;
        string? raw = rawUriOverride?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!_setupContext.TryGetTenantMongoUri(out raw) || string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
        }

        var cs = MongoConnectionStringUtil.Prepare(raw, orgCode);
        var client = new MongoClient(cs);
        var db = MongoConnectionStringUtil.GetTenantDatabase(client, cs, orgCode);
        return db.GetCollection<OrgAppUserDoc>(CollectionName);
    }

    public Task<IReadOnlyList<OrgAppUserSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
        => ListUsersInternalAsync(TryGetCollection(), cancellationToken);

    public Task<IReadOnlyList<OrgAppUserSummary>> ListUsersFromTenantUriAsync(
        string tenantMongoUri,
        string orgCode,
        CancellationToken cancellationToken = default)
        => ListUsersInternalAsync(GetCollection(tenantMongoUri, orgCode), cancellationToken);

    private async Task<IReadOnlyList<OrgAppUserSummary>> ListUsersInternalAsync(
        IMongoCollection<OrgAppUserDoc>? coll,
        CancellationToken cancellationToken)
    {
        if (coll is null)
        {
            return Array.Empty<OrgAppUserSummary>();
        }

        try
        {
            var list = await coll.Find(FilterDefinition<OrgAppUserDoc>.Empty)
                .SortByDescending(d => d.InvitedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return list.Select(Map).ToList();
        }
        catch (Exception ex)
        {
            DebugLogClient.Post("warn", "MongoOrgUserDirectoryService.ListUsersInternalAsync", ex.Message);
            try
            {
                return await ListUsersFromBsonAsync(coll.Database, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception bsonEx)
            {
                DebugLogClient.Post("warn", "MongoOrgUserDirectoryService.ListUsersFromBsonAsync", bsonEx.Message);
                return Array.Empty<OrgAppUserSummary>();
            }
        }
    }

    private static async Task<IReadOnlyList<OrgAppUserSummary>> ListUsersFromBsonAsync(
        IMongoDatabase db,
        CancellationToken cancellationToken)
    {
        var coll = db.GetCollection<BsonDocument>(CollectionName);
        var docs = await coll.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("invitedAtUtc"))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return docs.Select(MapBsonDocument).Where(u => !string.IsNullOrWhiteSpace(u.Email)).ToList();
    }

    private static OrgAppUserSummary MapBsonDocument(BsonDocument doc)
    {
        var id = doc.TryGetValue("_id", out var idVal) ? idVal.ToString() ?? string.Empty : string.Empty;
        var tempHash = doc.TryGetValue("temporaryPasswordHash", out var th) && th.IsString ? th.AsString : string.Empty;
        DateTime invitedAt = default;
        if (doc.TryGetValue("invitedAtUtc", out var inv))
        {
            if (inv.IsValidDateTime)
            {
                invitedAt = inv.ToUniversalTime();
            }
            else if (inv.BsonType == BsonType.Document && inv.AsBsonDocument.TryGetValue("$date", out var dateVal))
            {
                if (dateVal.IsValidDateTime)
                {
                    invitedAt = dateVal.ToUniversalTime();
                }
            }
        }

        var allowed = new List<string>();
        if (doc.TryGetValue("allowedTemplateIds", out var allowedVal) && allowedVal.IsBsonArray)
        {
            foreach (var item in allowedVal.AsBsonArray)
            {
                if (item.IsString)
                {
                    allowed.Add(item.AsString);
                }
            }
        }

        TemplatePermissionsDto? templatePerms = null;
        if (doc.TryGetValue("templatePermissions", out var tp) && tp.IsBsonDocument)
        {
            try { templatePerms = BsonSerializer.Deserialize<TemplatePermissionsDto>(tp.AsBsonDocument); }
            catch { /* ignore */ }
        }

        MasterDataPermissionsDto? masterPerms = null;
        if (doc.TryGetValue("masterDataPermissions", out var mp) && mp.IsBsonDocument)
        {
            try { masterPerms = BsonSerializer.Deserialize<MasterDataPermissionsDto>(mp.AsBsonDocument); }
            catch { /* ignore */ }
        }

        return new OrgAppUserSummary
        {
            Id = id,
            Name = ReadBsonString(doc, "name"),
            Email = ReadBsonString(doc, "email"),
            Role = string.IsNullOrWhiteSpace(ReadBsonString(doc, "role")) ? "Employee" : ReadBsonString(doc, "role"),
            PartsAllTemplates = doc.TryGetValue("partsAllTemplates", out var pat) && pat.IsBoolean && pat.AsBoolean,
            AllowedTemplateIds = allowed,
            InvitedAtUtc = invitedAt,
            TemplatePermissions = templatePerms,
            MasterDataPermissions = masterPerms,
            Status = string.IsNullOrWhiteSpace(tempHash) ? "Active" : "Pending",
        };
    }

    public async Task<OrgAppUserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var coll = TryGetCollection();
            if (coll is null)
            {
                return null;
            }

            var em = email.Trim();
            if (string.IsNullOrEmpty(em))
            {
                return null;
            }

            var doc = await coll.Find(x => x.EmailNormalized == NormalizeEmail(em))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return doc is null ? null : Map(doc);
        }
        catch (Exception ex)
        {
            DebugLogClient.Post("warn", "MongoOrgUserDirectoryService.FindByEmailAsync", ex.Message);
            return null;
        }
    }

    public async Task<InviteUserResult> InviteUserAsync(
        string name,
        string email,
        string role,
        bool partsAllTemplates,
        IReadOnlyList<string> allowedTemplateIds,
        TemplatePermissionsDto? templatePermissions = null,
        MasterDataPermissionsDto? masterDataPermissions = null,
        CancellationToken cancellationToken = default)
    {
        var coll = TryGetCollection()
                   ?? throw new InvalidOperationException(
                       "No tenant database is configured. Complete setup and ensure setup-state.json contains dbUri.");

        var em = email.Trim();
        if (string.IsNullOrEmpty(em))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        var existing = await coll.Find(x => x.EmailNormalized == NormalizeEmail(em))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException("A user with this email is already invited.");
        }

        var tempPassword = GenerateTemporaryPassword();
        var passwordHash = HashPassword(tempPassword);

        var doc = new OrgAppUserDoc
        {
            Name = name.Trim(),
            Email = em,
            EmailNormalized = NormalizeEmail(em),
            Role = role.Trim(),
            PartsAllTemplates = partsAllTemplates,
            AllowedTemplateIds = allowedTemplateIds
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TemplatePermissions = templatePermissions,
            MasterDataPermissions = masterDataPermissions,
            TemporaryPasswordHash = passwordHash,
            InvitedAtUtc = DateTime.UtcNow,
        };

        await coll.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);

        var orgCode = _setupContext.OrgCode?.Trim() ?? string.Empty;
        var emailSent = false;
        string? emailError = null;
        try
        {
            InviteEmailClient.SendInvite(
                em,
                doc.Name,
                orgCode,
                doc.Role,
                tempPassword);
            emailSent = true;
        }
        catch (Exception ex)
        {
            emailError = ex.Message;
        }

        return new InviteUserResult
        {
            Email = em,
            OrganizationCode = orgCode,
            TemporaryPassword = tempPassword,
            EmailSent = emailSent,
            EmailError = emailError,
        };
    }

    public Task EnsureInvitedUserRecordAsync(
        string name,
        string email,
        string role,
        bool partsAllTemplates,
        IReadOnlyList<string> allowedTemplateIds,
        string temporaryPassword,
        TemplatePermissionsDto? templatePermissions = null,
        MasterDataPermissionsDto? masterDataPermissions = null,
        CancellationToken cancellationToken = default)
        => EnsureInvitedUserRecordInternalAsync(
            TryGetCollection(),
            name,
            email,
            role,
            partsAllTemplates,
            allowedTemplateIds,
            temporaryPassword,
            templatePermissions,
            masterDataPermissions,
            cancellationToken);

    public Task EnsureInvitedUserRecordForTenantUriAsync(
        string tenantMongoUri,
        string orgCode,
        string name,
        string email,
        string role,
        bool partsAllTemplates,
        IReadOnlyList<string> allowedTemplateIds,
        string temporaryPassword,
        TemplatePermissionsDto? templatePermissions = null,
        MasterDataPermissionsDto? masterDataPermissions = null,
        CancellationToken cancellationToken = default)
        => EnsureInvitedUserRecordInternalAsync(
            GetCollection(tenantMongoUri, orgCode),
            name,
            email,
            role,
            partsAllTemplates,
            allowedTemplateIds,
            temporaryPassword,
            templatePermissions,
            masterDataPermissions,
            cancellationToken);

    private static async Task EnsureInvitedUserRecordInternalAsync(
        IMongoCollection<OrgAppUserDoc>? coll,
        string name,
        string email,
        string role,
        bool partsAllTemplates,
        IReadOnlyList<string> allowedTemplateIds,
        string temporaryPassword,
        TemplatePermissionsDto? templatePermissions,
        MasterDataPermissionsDto? masterDataPermissions,
        CancellationToken cancellationToken)
    {
        if (coll is null)
        {
            return;
        }

        var em = email.Trim();
        if (string.IsNullOrEmpty(em) || string.IsNullOrWhiteSpace(temporaryPassword))
        {
            return;
        }

        var normalized = NormalizeEmail(em);
        var existing = await coll.Find(x => x.EmailNormalized == normalized)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var doc = new OrgAppUserDoc
        {
            Name = name.Trim(),
            Email = em,
            EmailNormalized = normalized,
            Role = role.Trim(),
            PartsAllTemplates = partsAllTemplates,
            AllowedTemplateIds = allowedTemplateIds
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TemplatePermissions = templatePermissions,
            MasterDataPermissions = masterDataPermissions,
            TemporaryPasswordHash = HashPassword(temporaryPassword),
            InvitedAtUtc = DateTime.UtcNow,
        };

        await coll.InsertOneAsync(doc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ValidateInviteCredentialsAsync(
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        var coll = TryGetCollection();
        if (coll is null)
        {
            return false;
        }

        var em = email.Trim();
        if (string.IsNullOrEmpty(em) || string.IsNullOrWhiteSpace(temporaryPassword))
        {
            return false;
        }

        var doc = await coll.Find(x => x.EmailNormalized == NormalizeEmail(em))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (doc is null)
        {
            return false;
        }

        return VerifyPassword(temporaryPassword, doc.TemporaryPasswordHash);
    }

    public async Task<bool> UpdateUserAsync(
        string id,
        string name,
        string role,
        bool partsAllTemplates,
        IReadOnlyList<string> allowedTemplateIds,
        TemplatePermissionsDto? templatePermissions = null,
        MasterDataPermissionsDto? masterDataPermissions = null,
        CancellationToken cancellationToken = default)
    {
        var coll = TryGetCollection();
        if (coll is null || !ObjectId.TryParse(id, out var objectId))
        {
            return false;
        }

        // Use raw BsonValue fields because TemplatePermissions/MasterDataPermissions
        // are [BsonIgnore] computed properties — the driver cannot build expressions for them.
        BsonValue templatePermsBson = templatePermissions == null
            ? BsonNull.Value
            : templatePermissions.ToBsonDocument();

        BsonValue masterPermsBson = masterDataPermissions == null
            ? BsonNull.Value
            : masterDataPermissions.ToBsonDocument();

        var update = Builders<OrgAppUserDoc>.Update
            .Set(x => x.Name, name.Trim())
            .Set(x => x.Role, role.Trim())
            .Set(x => x.PartsAllTemplates, partsAllTemplates)
            .Set(x => x.AllowedTemplateIds, allowedTemplateIds
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .Set(x => x.TemplatePermissionsRaw, templatePermsBson)
            .Set(x => x.MasterDataPermissionsRaw, masterPermsBson);

        var result = await coll.UpdateOneAsync(
            x => x.Id == objectId,
            update,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteUserAsync(string id, CancellationToken cancellationToken = default)
    {
        var coll = TryGetCollection();
        if (coll is null || !ObjectId.TryParse(id, out var objectId))
        {
            return false;
        }

        var result = await coll.DeleteOneAsync(
            x => x.Id == objectId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    private static OrgAppUserSummary Map(OrgAppUserDoc d) =>
        new()
        {
            Id = d.Id.ToString(),
            Name = d.Name,
            Email = d.Email,
            Role = d.Role,
            PartsAllTemplates = d.PartsAllTemplates,
            AllowedTemplateIds = d.AllowedTemplateIds,
            InvitedAtUtc = d.InvitedAtUtc,
            TemplatePermissions = d.TemplatePermissions,
            MasterDataPermissions = d.MasterDataPermissions,
            Status = string.IsNullOrWhiteSpace(d.TemporaryPasswordHash) ? "Active" : "Pending",
        };

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string ReadBsonString(BsonDocument doc, string key)
    {
        return doc.TryGetValue(key, out var v) && v.IsString ? v.AsString : string.Empty;
    }

    [BsonIgnoreExtraElements]
    private sealed class OrgAppUserDoc
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("emailNormalized")]
        public string EmailNormalized { get; set; } = string.Empty;

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Admin or Employee</summary>
        [BsonElement("role")]
        public string Role { get; set; } = "Employee";

        [BsonElement("partsAllTemplates")]
        public bool PartsAllTemplates { get; set; }

        [BsonElement("allowedTemplateIds")]
        public List<string> AllowedTemplateIds { get; set; } = [];

        [BsonElement("temporaryPasswordHash")]
        public string TemporaryPasswordHash { get; set; } = string.Empty;

        [BsonElement("invitedAtUtc")]
        public DateTime InvitedAtUtc { get; set; }

        [BsonElement("templatePermissions")]
        public BsonValue? TemplatePermissionsRaw { get; set; }

        [BsonIgnore]
        public TemplatePermissionsDto? TemplatePermissions
        {
            get
            {
                if (TemplatePermissionsRaw == null || !TemplatePermissionsRaw.IsBsonDocument) return null;
                try { return BsonSerializer.Deserialize<TemplatePermissionsDto>(TemplatePermissionsRaw.AsBsonDocument); }
                catch { return null; }
            }
            set => TemplatePermissionsRaw = value == null ? BsonNull.Value : value.ToBsonDocument();
        }

        [BsonElement("masterDataPermissions")]
        public BsonValue? MasterDataPermissionsRaw { get; set; }

        [BsonIgnore]
        public MasterDataPermissionsDto? MasterDataPermissions
        {
            get
            {
                if (MasterDataPermissionsRaw == null || !MasterDataPermissionsRaw.IsBsonDocument) return null;
                try { return BsonSerializer.Deserialize<MasterDataPermissionsDto>(MasterDataPermissionsRaw.AsBsonDocument); }
                catch { return null; }
            }
            set => MasterDataPermissionsRaw = value == null ? BsonNull.Value : value.ToBsonDocument();
        }
    }

    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        Span<char> chars = stackalloc char[12];
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return chars.ToString();
    }

    private static string HashPassword(string password)
    {
        const int iterations = 120_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return $"pbkdf2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations < 10_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
