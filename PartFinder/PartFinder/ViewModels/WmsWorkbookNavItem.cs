using CommunityToolkit.Mvvm.ComponentModel;

namespace PartFinder.ViewModels;

public partial class WmsWorkbookNavItem : ObservableObject
{
    public bool IsGroupHeader { get; init; }
    public string? GroupTitle { get; init; }
    public string? SectionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public bool IsOperational { get; init; }
    public string? RoadmapPhase { get; init; }
    public string IconGlyph { get; init; } = "\uE81C";
    public string TooltipText { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}