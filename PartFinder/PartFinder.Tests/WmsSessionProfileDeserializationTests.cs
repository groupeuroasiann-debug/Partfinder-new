using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PartFinder.Services;

namespace PartFinder.Tests;

[TestClass]
public sealed class WmsSessionProfileDeserializationTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void Session_profile_deserializes_admin_all_scope()
    {
        const string json = """
            {
              "email": "wms-admin-staging@partfinder.acceptance",
              "role": "Admin",
              "isWmsAdmin": true,
              "operatorPermissions": { "receiving": true, "qc": true, "putaway": true, "supervise": true },
              "allowedWarehouseIds": "all",
              "allowedOwnerIds": "all"
            }
            """;

        var profile = JsonSerializer.Deserialize<WmsSessionProfileDto>(json, Json);
        Assert.IsNotNull(profile);
        Assert.IsTrue(profile.IsWmsAdmin);
        Assert.AreEqual(0, profile.AllowedWarehouseIds.Length);
        Assert.AreEqual(0, profile.AllowedOwnerIds.Length);
    }

    [TestMethod]
    public void Session_profile_deserializes_operator_id_arrays()
    {
        const string json = """
            {
              "email": "operator-a-staging@partfinder.acceptance",
              "role": "Employee",
              "isWmsAdmin": false,
              "operatorPermissions": { "receiving": true, "qc": true, "putaway": true, "supervise": false },
              "allowedWarehouseIds": ["wh-1"],
              "allowedOwnerIds": ["owner-a"]
            }
            """;

        var profile = JsonSerializer.Deserialize<WmsSessionProfileDto>(json, Json);
        Assert.IsNotNull(profile);
        Assert.IsFalse(profile.IsWmsAdmin);
        Assert.AreEqual(1, profile.AllowedWarehouseIds.Length);
        Assert.AreEqual("wh-1", profile.AllowedWarehouseIds[0]);
    }
}