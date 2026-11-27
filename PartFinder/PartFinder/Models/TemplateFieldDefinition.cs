namespace PartFinder.Models;

public sealed class TemplateFieldDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public TemplateFieldType Type { get; init; }
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
    public string? ValidationPattern { get; init; }
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>When <see cref="Type"/> is <see cref="TemplateFieldType.RecordLink"/>, id of the target template.</summary>
    public string? LinkedTemplateId { get; init; }

    /// <summary>Optional field key on the target template used as the visible label; default = first column.</summary>
    public string? LinkedDisplayFieldKey { get; init; }
}

public static class TemplateFieldLabelHelper
{
    public static bool IsDefaultColumnLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return true;
        }

        var trimmed = label.Trim();
        if (!trimmed.StartsWith("Column ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = trimmed["Column ".Length..].Trim();
        return int.TryParse(suffix, out var n) && n >= 1;
    }

    public static List<TemplateFieldDefinition> NormalizeDefaultLabels(IEnumerable<TemplateFieldDefinition> fields)
    {
        var ordered = fields.OrderBy(f => f.DisplayOrder).ToList();
        var result = new List<TemplateFieldDefinition>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var f = ordered[i];
            var label = IsDefaultColumnLabel(f.Label) ? $"Column {i + 1}" : f.Label;
            if (string.Equals(label, f.Label, StringComparison.Ordinal) && f.DisplayOrder == i)
            {
                result.Add(f);
            }
            else
            {
                result.Add(new TemplateFieldDefinition
                {
                    Key = f.Key,
                    Label = label,
                    Type = f.Type,
                    IsRequired = f.IsRequired,
                    DisplayOrder = i,
                    ValidationPattern = f.ValidationPattern,
                    Options = f.Options,
                    LinkedTemplateId = f.LinkedTemplateId,
                    LinkedDisplayFieldKey = f.LinkedDisplayFieldKey,
                });
            }
        }

        return result;
    }
}
