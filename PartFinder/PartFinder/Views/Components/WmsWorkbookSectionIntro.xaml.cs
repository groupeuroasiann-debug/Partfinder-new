using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PartFinder.Views.Components;

public sealed partial class WmsWorkbookSectionIntro : UserControl
{
    public WmsWorkbookSectionIntro() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(WmsWorkbookSectionIntro), new(string.Empty));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(WmsWorkbookSectionIntro), new(string.Empty));
}