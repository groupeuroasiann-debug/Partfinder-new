using CommunityToolkit.Mvvm.ComponentModel;
using PartFinder.Services;

namespace PartFinder.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    public required string Label { get; init; }

    public required string IconGlyph { get; init; }

    public required AppPage Page { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Optional extended tooltip; falls back to <see cref="Label"/> in the shell.</summary>
    public string? Tooltip { get; init; }

    public string ToolTipDisplay => string.IsNullOrWhiteSpace(Tooltip) ? Label : Tooltip!;

    [ObservableProperty]
    private bool isSelected;
}