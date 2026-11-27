using Microsoft.VisualStudio.TestTools.UnitTesting;
using PartFinder.Models;

namespace PartFinder.Tests.Models;

[TestClass]
public sealed class TemplateFieldLabelHelperTests
{
    [TestMethod]
    public void NormalizeDefaultLabels_FixesGapsAndDuplicatesWhilePreservingCustomNames()
    {
        var input = new[]
        {
            new TemplateFieldDefinition { Key = "k1", Label = "Brand", DisplayOrder = 0 },
            new TemplateFieldDefinition { Key = "k2", Label = "Column 3", DisplayOrder = 1 },
            new TemplateFieldDefinition { Key = "k3", Label = "Column 4", DisplayOrder = 2 },
            new TemplateFieldDefinition { Key = "k4", Label = "OD", DisplayOrder = 3 },
            new TemplateFieldDefinition { Key = "k5", Label = "Column 12", DisplayOrder = 4 },
            new TemplateFieldDefinition { Key = "k6", Label = "Column 12", DisplayOrder = 5 },
        };

        var normalized = TemplateFieldLabelHelper.NormalizeDefaultLabels(input);

        Assert.AreEqual(6, normalized.Count);
        Assert.AreEqual("Brand", normalized[0].Label);
        Assert.AreEqual("Column 2", normalized[1].Label);
        Assert.AreEqual("Column 3", normalized[2].Label);
        Assert.AreEqual("OD", normalized[3].Label);
        Assert.AreEqual("Column 5", normalized[4].Label);
        Assert.AreEqual("Column 6", normalized[5].Label);
    }

    [TestMethod]
    public void NormalizeDefaultLabels_EmptyLabelsBecomeSequentialFromOne()
    {
        var input = new[]
        {
            new TemplateFieldDefinition { Key = "a", Label = "", DisplayOrder = 0 },
            new TemplateFieldDefinition { Key = "b", Label = "   ", DisplayOrder = 1 },
        };

        var normalized = TemplateFieldLabelHelper.NormalizeDefaultLabels(input);

        Assert.AreEqual("Column 1", normalized[0].Label);
        Assert.AreEqual("Column 2", normalized[1].Label);
    }
}
