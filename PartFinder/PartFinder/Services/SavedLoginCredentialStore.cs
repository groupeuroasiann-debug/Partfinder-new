using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Security.Credentials;

namespace PartFinder.Services;

/// <summary>
/// Stores refresh tokens in Windows Credential Locker; org/email in a small local profile file (no secrets).
/// </summary>
public sealed class SavedLoginCredentialStore
{
    private const string VaultResource = "PartFinder";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _profilePath;

    public SavedLoginCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PartFinder");
        _profilePath = Path.Combine(dir, "saved-login-profile.json");
    }

    public bool HasSavedProfile()
    {
        var profile = LoadProfile();
        return profile is not null
               && !string.IsNullOrWhiteSpace(profile.OrgCode)
               && !string.IsNullOrWhiteSpace(profile.Email)
               && TryReadRefreshToken(profile) is not null;
    }

    public SavedLoginProfile? LoadProfile()
    {
        try
        {
            if (!File.Exists(_profilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SavedLoginProfile>(
                File.ReadAllText(_profilePath),
                Json);
        }
        catch
        {
            return null;
        }
    }

    public string? TryReadRefreshToken(SavedLoginProfile profile)
    {
        try
        {
            var vault = new PasswordVault();
            var userName = BuildCredentialUserName(profile.OrgCode, profile.Email);
            var cred = vault.Retrieve(VaultResource, userName);
            var token = cred?.Password?.Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string orgCode, string email, string refreshToken)
    {
        var code = orgCode.Trim();
        var mail = email.Trim().ToLowerInvariant();
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            throw new ArgumentException("Invalid organisation code.");
        }

        if (string.IsNullOrWhiteSpace(mail) || string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Email and refresh token are required.");
        }

        ClearInternal(skipProfile: true);

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(
            VaultResource,
            BuildCredentialUserName(code, mail),
            refreshToken));

        Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
        File.WriteAllText(
            _profilePath,
            JsonSerializer.Serialize(
                new SavedLoginProfile { OrgCode = code, Email = mail },
                Json));
    }

    public bool ReplaceRefreshToken(string orgCode, string email, string newRefreshToken)
    {
        var profile = LoadProfile();
        if (profile is null)
        {
            return false;
        }

        if (!string.Equals(profile.OrgCode, orgCode.Trim(), StringComparison.Ordinal)
            || !string.Equals(profile.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Save(orgCode, email, newRefreshToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Clear()
    {
        ClearInternal(skipProfile: false);
    }

    private void ClearInternal(bool skipProfile)
    {
        try
        {
            var vault = new PasswordVault();
            var toRemove = vault.RetrieveAll()
                .Where(c =>
                    string.Equals(c.Resource, VaultResource, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var item in toRemove)
            {
                try
                {
                    vault.Remove(item);
                }
                catch
                {
                    // ignore single removal failures
                }
            }
        }
        catch
        {
            // Credential locker unavailable
        }

        if (!skipProfile)
        {
            try
            {
                if (File.Exists(_profilePath))
                {
                    File.Delete(_profilePath);
                }
            }
            catch
            {
            }
        }
    }

    private static string BuildCredentialUserName(string orgCode, string email) =>
        $"{orgCode.Trim()}|{email.Trim().ToLowerInvariant()}";
}

public sealed class SavedLoginProfile
{
    public string? OrgCode { get; set; }
    public string? Email { get; set; }
}