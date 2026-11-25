using Microsoft.VisualStudio.TestTools.UnitTesting;
using PartFinder.Models;
using PartFinder.Services;

namespace PartFinder.Tests.Services;

[TestClass]
public sealed class ClosedXmlExcelTemplateServiceTests
{
    private readonly ClosedXmlExcelTemplateService _service = new();

    [TestMethod]
    public async Task ParseImportFileInferSchemaAsync_ValidCsv_InfersFieldsAndRows()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var csvContent = "Part Name,Part Number,Quantity\nWidget A,W-100,50\nWidget B,W-200,75\n";
            await File.WriteAllTextAsync(tempFile, csvContent);

            var result = await _service.ParseImportFileInferSchemaAsync(tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Fields.Count);
            Assert.AreEqual("part_name", result.Fields[0].Key);
            Assert.AreEqual("Part Name", result.Fields[0].Label);
            Assert.AreEqual("part_number", result.Fields[1].Key);
            Assert.AreEqual("Part Number", result.Fields[1].Label);
            Assert.AreEqual("quantity", result.Fields[2].Key);
            Assert.AreEqual("Quantity", result.Fields[2].Label);

            Assert.AreEqual(2, result.Rows.Count);
            Assert.AreEqual("Widget A", result.Rows[0]["part_name"]);
            Assert.AreEqual("W-100", result.Rows[0]["part_number"]);
            Assert.AreEqual("50", result.Rows[0]["quantity"]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ParseImportFileAsync_ValidCsv_MapsToTemplateFields()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var csvContent = "SKU,Description,Unit Price\nSKU-001,Bolt M8,1.25\nSKU-002,Nut M8,0.75\n";
            await File.WriteAllTextAsync(tempFile, csvContent);

            var template = new PartTemplateDefinition
            {
                Id = "test-template",
                Name = "Parts Catalog",
                Fields = new[]
                {
                    new TemplateFieldDefinition { Key = "sku", Label = "SKU", DisplayOrder = 0 },
                    new TemplateFieldDefinition { Key = "desc", Label = "Description", DisplayOrder = 1 },
                    new TemplateFieldDefinition { Key = "price", Label = "Unit Price", DisplayOrder = 2 },
                }
            };

            var result = await _service.ParseImportFileAsync(template, tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Rows.Count);
            Assert.AreEqual("SKU-001", result.Rows[0]["sku"]);
            Assert.AreEqual("Bolt M8", result.Rows[0]["desc"]);
            Assert.AreEqual("1.25", result.Rows[0]["price"]);
            Assert.AreEqual(0, result.Warnings.Count);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ParseImportFileInferSchemaAsync_CsvWithTrailingBlankRows_StopsAtBlankStreak()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Col A,Col B");
            sb.AppendLine("Row 1,Val 1");
            sb.AppendLine("Row 2,Val 2");
            // Add 100 empty lines
            for (var i = 0; i < 100; i++)
            {
                sb.AppendLine(" , ");
            }

            await File.WriteAllTextAsync(tempFile, sb.ToString());

            var result = await _service.ParseImportFileInferSchemaAsync(tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Rows.Count);
            Assert.IsTrue(result.EmptyRowsSkipped > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ParseImportFileInferSchemaAsync_EmptyCsv_ReturnsWarning()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(tempFile, "");

            var result = await _service.ParseImportFileInferSchemaAsync(tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Fields.Count);
            Assert.AreEqual(0, result.Rows.Count);
            Assert.IsTrue(result.Warnings.Count > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ParseImportFileInferSchemaAsync_SemicolonDelimitedCsv_InfersFieldsAndRows()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            var csvContent = "Artikelnummer;Bezeichnung;Menge\nART-101;Schraube M4;100\nART-102;Mutter M4;250\n";
            await File.WriteAllTextAsync(tempFile, csvContent);

            var result = await _service.ParseImportFileInferSchemaAsync(tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Fields.Count);
            Assert.AreEqual("Artikelnummer", result.Fields[0].Label);
            Assert.AreEqual("Bezeichnung", result.Fields[1].Label);
            Assert.AreEqual("Menge", result.Fields[2].Label);
            Assert.AreEqual(2, result.Rows.Count);
            Assert.AreEqual("ART-101", result.Rows[0][result.Fields[0].Key]);
            Assert.AreEqual("Schraube M4", result.Rows[0][result.Fields[1].Key]);
            Assert.AreEqual("100", result.Rows[0][result.Fields[2].Key]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ParseImportFileInferSchemaAsync_CsvHeaderOnRow2_DetectsHeaderRow()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        try
        {
            // Row 1 is a title/metadata row, row 2 is headers
            var csvContent = "Exported on 2026-11-25\nPartID,Description,Count\nP-1,Resistor,10\n";
            await File.WriteAllTextAsync(tempFile, csvContent);

            var result = await _service.ParseImportFileInferSchemaAsync(tempFile);

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Fields.Count);
            Assert.AreEqual("PartID", result.Fields[0].Label);
            Assert.AreEqual("Description", result.Fields[1].Label);
            Assert.AreEqual("Count", result.Fields[2].Label);
            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("P-1", result.Rows[0][result.Fields[0].Key]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
