using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PartFinder.Helpers;
using PartFinder.ViewModels;

namespace PartFinder.Views.Pages;

public sealed partial class LinkedTemplatesPage : Page
{
    public LinkedTemplatesPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<LinkedTemplatesViewModel>();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PageEntranceStoryboard.Begin();

        if (DataContext is LinkedTemplatesViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void OnLinkedTemplateBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LinkedTemplateBrowseItem item }
            || DataContext is not LinkedTemplatesViewModel vm
            || XamlRoot is null)
        {
            return;
        }

        vm.PreparePreviewColumnPicker(item);
        await ShowLinkedTemplateColumnDialogAsync(vm).ConfigureAwait(true);
    }

    private async Task ShowLinkedTemplateColumnDialogAsync(LinkedTemplatesViewModel vm)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var columnPanel = new StackPanel { Spacing = 6 };
        foreach (var pick in vm.PreviewColumnPicks)
        {
            var check = new CheckBox
            {
                Content = pick.ColumnName,
                IsChecked = pick.IsChecked,
                FontSize = 13,
            };
            check.Checked += (_, _) => pick.IsChecked = true;
            check.Unchecked += (_, _) => pick.IsChecked = false;
            columnPanel.Children.Add(check);
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = columnPanel,
        };

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(
            new TextBlock
            {
                Text = $"Choose columns from \"{vm.PendingLinkedTemplateName}\" to preview in the grid.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextSecondaryBrush"],
                FontSize = 12,
            });
        content.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            Title = "Select columns",
            Content = content,
            PrimaryButtonText = "Select",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        UiResourceHelper.ApplyLightContentDialog(dialog);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await vm.LoadLinkedPreviewDataAsync().ConfigureAwait(true);
    }
}
