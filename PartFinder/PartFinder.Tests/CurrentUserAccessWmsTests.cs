using Microsoft.VisualStudio.TestTools.UnitTesting;
using PartFinder.Services;

namespace PartFinder.Tests;

[TestClass]
public sealed class CurrentUserAccessWmsTests
{
    [TestMethod]
    public void Employee_capabilities_default_wms_workbook_false()
    {
        var cap = new UserAccessCapabilities
        {
            CanAccessDashboard = true,
            CanAccessTemplates = true,
            CanViewInventory = true,
            CanAccessWmsWorkbook = false,
        };

        Assert.IsFalse(cap.CanAccessWmsWorkbook);
    }

    [TestMethod]
    public void FullAdmin_template_does_not_grant_wms_until_enriched()
    {
        Assert.IsFalse(UserAccessCapabilities.FullAdmin.CanAccessWmsWorkbook);
    }

    [TestMethod]
    public void WithWmsWorkbook_sets_flag_only()
    {
        var baseCap = UserAccessCapabilities.FailClosed;
        var granted = baseCap.WithWmsWorkbook(true);
        Assert.IsTrue(granted.CanAccessWmsWorkbook);
        Assert.IsFalse(baseCap.CanAccessWmsWorkbook);
        Assert.IsTrue(granted.CanAccessDashboard);
    }
}