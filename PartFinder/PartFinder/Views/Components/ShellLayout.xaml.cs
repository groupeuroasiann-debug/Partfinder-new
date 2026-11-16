using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using PartFinder.Services;
using PartFinder.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Windows.Foundation;
using Windows.Graphics;

namespace PartFinder.Views.Components;

public sealed partial class ShellLayout : UserControl
{
    private const double TopNavScrollStep = 140;

    public ShellLayout()
    {
        InitializeComponent();
        var vm = App.Services.GetRequiredService<ShellViewModel>();
        DataContext = vm;
        vm.NavigationItems.CollectionChanged += OnNavigationItemsCollectionChanged;
        Loaded += OnShellLayoutLoaded;
        AppTitleBarLayout.LayoutUpdated += OnAppTitleBarLayoutUpdated;
    }

    public UIElement TitleBarDragElement => TitleBarDragStrip;

    /// <summary>Legacy name used by older MainWindow title-bar code paths.</summary>
    public UIElement ShellTitleBarDragTarget => TitleBarDragElement;

    public void UpdateTitleBarInsets(double leftInset, double rightInset)
    {
        TitleBarLeftInsetColumn.Width = new GridLength(Math.Max(0, leftInset));
        TitleBarRightInsetColumn.Width = new GridLength(Math.Max(0, rightInset));
    }

    public void ResetShellTitleBarCaptionInsets() => UpdateTitleBarInsets(0, 0);

    public void UpdateShellTitleBarCaptionInsets(
        double leftInset,
        double rightInset,
        double rasterizationScale)
    {
        var scale = rasterizationScale > 0 ? rasterizationScale : 1.0;
        UpdateTitleBarInsets(leftInset / scale, rightInset / scale);
    }

    public RectInt32[] GetTitleBarPassthroughRects()
    {
        if (XamlRoot is null)
        {
            return Array.Empty<RectInt32>();
        }

        var scaleAdjustment = XamlRoot.RasterizationScale;
        var rects = new List<RectInt32>(3);
        TryAddPassthroughRect(rects, TopNavInteractiveRegion, scaleAdjustment);
        TryAddPassthroughRect(rects, TopNavSearchRegion, scaleAdjustment);
        TryAddPassthroughRect(rects, TopNavActionsRegion, scaleAdjustment);
        return rects.ToArray();
    }

    private static void TryAddPassthroughRect(List<RectInt32> rects, FrameworkElement element, double scaleAdjustment)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var transform = element.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        rects.Add(
            new RectInt32(
                (int)Math.Round(bounds.X * scaleAdjustment),
                (int)Math.Round(bounds.Y * scaleAdjustment),
                (int)Math.Round(bounds.Width * scaleAdjustment),
                (int)Math.Round(bounds.Height * scaleAdjustment)));
    }

    private void OnAccountSettingsMenuClicked(object sender, RoutedEventArgs e)
    {
        SelectNavigationPage(AppPage.Settings);
    }

    private async void OnBellClicked(object sender, RoutedEventArgs e)
    {
        SelectNavigationPage(AppPage.Alerts);

        if (DataContext is ShellViewModel vm)
        {
            await vm.MarkNotificationsSeenAsync().ConfigureAwait(true);
        }
    }

    private async void OnProfileLogoutMenuClicked(object sender, RoutedEventArgs e)
    {
        var adminSession = App.Services.GetRequiredService<AdminSessionStore>();
        var activityLogger = App.Services.GetRequiredService<ActivityLogger>();

        activityLogger.LogLogout(adminSession.Email ?? "unknown");
        await activityLogger.FlushAsync().ConfigureAwait(true);

        adminSession.Clear();
        // Do NOT delete setup-state.json — it holds orgCode/dbUri needed after re-login
        App.Services.GetRequiredService<CatalogBrowseSessionStore>().Reset();

        if (App.MainAppWindow is MainWindow main)
        {
            main.ResetToSetup();
        }
        else
        {
            Application.Current.Exit();
        }
    }

    private void OnTopNavItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        // Try to get item from DataContext
        if (element.DataContext is not NavItemViewModel item)
        {
            System.Diagnostics.Debug.WriteLine($"[ShellLayout] Nav item click failed: DataContext is {element.DataContext?.GetType().Name ?? "null"} (expected NavItemViewModel)");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ShellLayout] Nav item clicked: {item.Label} (Page: {item.Page})");

        if (DataContext is ShellViewModel vm)
        {
            vm.NavigateFromNavItem(item);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[ShellLayout] Nav item click failed: ShellLayout DataContext is not ShellViewModel.");
        }
    }

    private void SelectNavigationPage(AppPage page)
    {
        if (DataContext is not ShellViewModel vm)
        {
            return;
        }

        var item = vm.NavigationItems.FirstOrDefault(n => n.Page == page);
        if (item is not null)
        {
            vm.SelectedNavigationItem = item;
            return;
        }

        var navigation = App.Services.GetRequiredService<INavigationService>();
        _ = navigation.Navigate(page);
    }

    private void OnShellLayoutLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnShellLayoutLoaded;

        var themeService = App.Services.GetRequiredService<IThemeService>();
        themeService.AttachShell(this);

        if (Resources["AmbientDriftStoryboard"] is Storyboard ambientStoryboard)
        {
            ambientStoryboard.Begin();
        }

        App.Services.GetRequiredService<INavigationService>().Initialize(ContentFrame);

        NotifyTitleBarRegionsChanged();
        ScheduleTopNavScrollAffordanceUpdate();
    }

    private void OnNavigationItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleTopNavScrollAffordanceUpdate();
    }

    private void OnTopNavScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleTopNavScrollAffordanceUpdate();
    }

    private void OnTopNavScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            return;
        }

        UpdateTopNavScrollAffordances();
    }

    private void OnTopNavScrollLeftButtonClick(object sender, RoutedEventArgs e)
    {
        var offset = Math.Max(0, TopNavScrollViewer.HorizontalOffset - TopNavScrollStep);
        TopNavScrollViewer.ChangeView(offset, null, 1.0f);
    }

    private void OnTopNavScrollRightButtonClick(object sender, RoutedEventArgs e)
    {
        var maxOffset = TopNavScrollViewer.ScrollableWidth;
        var offset = Math.Min(maxOffset, TopNavScrollViewer.HorizontalOffset + TopNavScrollStep);
        TopNavScrollViewer.ChangeView(offset, null, 1.0f);
    }

    private void ScheduleTopNavScrollAffordanceUpdate()
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            UpdateTopNavScrollAffordances();
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTopNavScrollAffordances);
        });
    }

    private void UpdateTopNavScrollAffordances()
    {
        if (TopNavScrollViewer is null)
        {
            return;
        }

        var canScroll = TopNavScrollViewer.ScrollableWidth > 1;
        var atStart = TopNavScrollViewer.HorizontalOffset <= 1;
        var atEnd = TopNavScrollViewer.HorizontalOffset >= TopNavScrollViewer.ScrollableWidth - 1;

        TopNavScrollLeftButton.Visibility = canScroll && !atStart ? Visibility.Visible : Visibility.Collapsed;
        TopNavScrollRightButton.Visibility = canScroll && !atEnd ? Visibility.Visible : Visibility.Collapsed;
        TopNavFadeLeft.Visibility = canScroll && !atStart ? Visibility.Visible : Visibility.Collapsed;
        TopNavFadeRight.Visibility = canScroll && !atEnd ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppTitleBarLayoutUpdated(object? sender, object e)
    {
        NotifyTitleBarRegionsChanged();
        ScheduleTopNavScrollAffordanceUpdate();
    }

    private void NotifyTitleBarRegionsChanged()
    {
        if (App.MainAppWindow is not MainWindow main)
        {
            return;
        }

        if (Visibility != Visibility.Visible)
        {
            return;
        }

        main.RefreshShellTitleBarRegions();
    }
}
