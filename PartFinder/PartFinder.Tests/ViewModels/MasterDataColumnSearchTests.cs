using Microsoft.VisualStudio.TestTools.UnitTesting;
using PartFinder.Models;
using PartFinder.ViewModels;

namespace PartFinder.Tests.ViewModels;

[TestClass]
public sealed class MasterDataColumnSearchTests
{
    private static MasterDataRowViewModel MakeRow(params (string Key, string Value)[] cells)
    {
        var row = new MasterDataRowViewModel();
        foreach (var (key, value) in cells)
        {
            row.Cells.Add(new MasterDataCellViewModel
            {
                FieldKey = key,
                FieldType = TemplateFieldType.Text,
                Text = value,
            });
        }

        return row;
    }

    [TestMethod]
    public void FilterColumnSuggestions_PrioritizesPrefixMatchesThenSubstringMatches()
    {
        var rows = new[]
        {
            MakeRow(("part", "Steel Bolt M8")),
            MakeRow(("part", "Bolt M6")),
            MakeRow(("part", "Bolt M8")),
            MakeRow(("part", "bolt m6")), // duplicate case-insensitive
            MakeRow(("part", "Washer M8")),
            MakeRow(("part", "")),
        };

        var suggestions = MasterDataViewModel.FilterColumnSuggestions(rows, "part", "bolt");

        Assert.AreEqual(3, suggestions.Count);
        Assert.AreEqual("Bolt M6", suggestions[0]);
        Assert.AreEqual("Bolt M8", suggestions[1]);
        Assert.AreEqual("Steel Bolt M8", suggestions[2]);
    }

    [TestMethod]
    public void FilterColumnSuggestions_RespectsMaxSuggestions()
    {
        var rows = Enumerable.Range(1, 25)
            .Select(i => MakeRow(("sku", $"ITEM-{i:D2}")))
            .ToArray();

        var suggestions = MasterDataViewModel.FilterColumnSuggestions(rows, "sku", "ITEM", maxSuggestions: 10);

        Assert.AreEqual(10, suggestions.Count);
    }

    [TestMethod]
    public void FilterColumnSuggestions_EmptyQuery_ReturnsDistinctSortedValues()
    {
        var rows = new[]
        {
            MakeRow(("cat", "Resistors")),
            MakeRow(("cat", "Capacitors")),
            MakeRow(("cat", "Resistors")),
        };

        var suggestions = MasterDataViewModel.FilterColumnSuggestions(rows, "cat", "");

        Assert.AreEqual(2, suggestions.Count);
        Assert.AreEqual("Capacitors", suggestions[0]);
        Assert.AreEqual("Resistors", suggestions[1]);
    }
}
