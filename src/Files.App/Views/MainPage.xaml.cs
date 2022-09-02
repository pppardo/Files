using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using CommunityToolkit.WinUI.UI.Controls;
using Files.Backend.Extensions;
using Files.Backend.Services.Settings;
using Files.Shared.Enums;
using Files.Shared.EventArguments;
using Files.App.DataModels.NavigationControlItems;
using Files.App.Extensions;
using Files.App.Filesystem;
using Files.App.Helpers;
using Files.App.UserControls;
using Files.App.UserControls.MultitaskingControl;
using Files.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Graphics;
using Windows.Services.Store;
using Windows.Storage;

namespace Files.App.Views
{
    /// <summary>
    /// The root page of Files
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        public IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetService<IUserSettingsService>();

        public MainViewModel MainViewModel => App.MainViewModel;

        public MainPageViewModel ViewModel
        {
            get => (MainPageViewModel)DataContext;
            set => DataContext = value;
        }

        public SidebarViewModel SidebarAdaptiveViewModel = new SidebarViewModel();

        public OngoingTasksViewModel OngoingTasksViewModel => App.OngoingTasksViewModel;

        public ICommand ToggleFullScreenAcceleratorCommand { get; }

        private ICommand ToggleCompactOverlayCommand { get; }
        private ICommand SetCompactOverlayCommand { get; }

        private ICommand ToggleSidebarCollapsedStateCommand => new RelayCommand<KeyboardAcceleratorInvokedEventArgs>(x => ToggleSidebarCollapsedState(x));

        public MainPage()
        {
            InitializeComponent();

            TitleBar_LayoutMetricsChanged(null, null); //WINUI3, event does not exist. Is this ok?
            var flowDirectionSetting = /*
                TODO ResourceContext.GetForCurrentView and ResourceContext.GetForViewIndependentUse do not exist in Windows App SDK
                Use your ResourceManager instance to create a ResourceContext as below. If you already have a ResourceManager instance,
                replace the new instance created below with correct instance.
                Read: https://docs.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/mrtcore
            */new Microsoft.Windows.ApplicationModel.Resources.ResourceManager().CreateResourceContext().QualifierValues["LayoutDirection"];

            if (flowDirectionSetting == "RTL")
            {
                FlowDirection = FlowDirection.RightToLeft;
            }
            AllowDrop = true;

            ToggleFullScreenAcceleratorCommand = new RelayCommand<KeyboardAcceleratorInvokedEventArgs>(ToggleFullScreenAccelerator);
            ToggleCompactOverlayCommand = new RelayCommand(ToggleCompactOverlay);
            SetCompactOverlayCommand = new RelayCommand<bool>(SetCompactOverlay);

            if (SystemInformation.Instance.TotalLaunchCount >= 15 & Package.Current.Id.Name == "49306atecsolution.FilesUWP" && !UserSettingsService.ApplicationSettingsService.WasPromptedToReview)
            {
                PromptForReview();
                UserSettingsService.ApplicationSettingsService.WasPromptedToReview = true;
            }

            UserSettingsService.OnSettingChangedEvent += UserSettingsService_OnSettingChangedEvent;
        }

        private async void PromptForReview()
        {
            var AskForReviewDialog = new ContentDialog
            {
                Title = "ReviewFiles".ToLocalized(),
                Content = "ReviewFilesContent".ToLocalized(),
                PrimaryButtonText = "Yes".ToLocalized(),
                SecondaryButtonText = "No".ToLocalized()
            };

            var result = await this.SetContentDialogRoot(AskForReviewDialog).ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    var storeContext = StoreContext.GetDefault();
                    await storeContext.RequestRateAndReviewAppAsync();
                }
                catch (Exception) { }
            }
        }

        // WINUI3
        private ContentDialog SetContentDialogRoot(ContentDialog contentDialog)
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
            {
                contentDialog.XamlRoot = App.Window.Content.XamlRoot;
            }
            return contentDialog;
        }

        private void UserSettingsService_OnSettingChangedEvent(object sender, SettingChangedEventArgs e)
        {
            switch (e.SettingName)
            {
                case nameof(IPaneSettingsService.Content):
                    LoadPaneChanged();
                    break;
            }
        }

        private void TitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                RightPaddingColumn.Width = new GridLength(App.GetAppWindow(App.Window).TitleBar.RightInset);
            }
        }

        private void HorizontalMultitaskingControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                horizontalMultitaskingControl.DragArea.SizeChanged += (_, _) => SetRectDragRegion();
                SetRectDragRegion();
            }
            else
            {
                App.Window.SetTitleBar(horizontalMultitaskingControl.DragArea);
            }

            if (!(ViewModel.MultitaskingControl is HorizontalMultitaskingControl))
            {
                ViewModel.MultitaskingControl = horizontalMultitaskingControl;
                ViewModel.MultitaskingControls.Add(horizontalMultitaskingControl);
                ViewModel.MultitaskingControl.CurrentInstanceChanged += MultitaskingControl_CurrentInstanceChanged;
            }
        }

        private void SetRectDragRegion()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
                return;

            const bool WORKAROUND = true;
            if (WORKAROUND)
            {
                const short LeftButton = 0x01;

                if ((NativeWinApiHelper.GetKeyState(LeftButton) & 0xFF00) == 0xFF00)
                    return;                    

                App.Window.AppWindow.TitleBar.ResetToDefault();
                App.Window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

                // Set window buttons background to transparent
                App.Window.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                App.Window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }

            const uint MDT_Effective_DPI = 0;

            var displayArea = DisplayArea.GetFromWindowId(App.Window.AppWindow.Id, DisplayAreaFallback.Primary);
            var hMonitor = Win32Interop.GetMonitorFromDisplayId(displayArea.DisplayId);
            var hr = NativeWinApiHelper.GetDpiForMonitor(hMonitor, MDT_Effective_DPI, out var dpiX, out _);
            if (hr != 0)
                return;
            
            var scalePercent = (uint)(((long)dpiX * 100 + (96 >> 1)) / 96); // wtf
            var scaleAdjustment = scalePercent / 100.0;
            var dragArea = horizontalMultitaskingControl.DragArea;

            var x = (int)((horizontalMultitaskingControl.ActualWidth - dragArea.ActualWidth) * scaleAdjustment);
            var y = 0;
            var width = (int)(dragArea.ActualWidth * scaleAdjustment);
            var height = (int)(horizontalMultitaskingControl.TitlebarArea.ActualHeight * scaleAdjustment);

            var dragRect = new RectInt32(x, y, width, height);
            App.Window.AppWindow.TitleBar.SetDragRectangles(new[] { dragRect });
        }

        public void TabItemContent_ContentChanged(object sender, TabItemArguments e)
        {
            if (SidebarAdaptiveViewModel.PaneHolder != null)
            {
                var paneArgs = e.NavigationArg as PaneNavigationArguments;
                SidebarAdaptiveViewModel.UpdateSidebarSelectedItemFromArgs(SidebarAdaptiveViewModel.PaneHolder.IsLeftPaneActive ?
                    paneArgs.LeftPaneNavPathParam : paneArgs.RightPaneNavPathParam);
                UpdateStatusBarProperties();
                LoadPaneChanged();
                UpdateNavToolbarProperties();
                ViewModel.UpdateInstanceProperties(paneArgs);
            }
        }

        public void MultitaskingControl_CurrentInstanceChanged(object sender, CurrentInstanceChangedEventArgs e)
        {
            if (SidebarAdaptiveViewModel.PaneHolder != null)
            {
                SidebarAdaptiveViewModel.PaneHolder.PropertyChanged -= PaneHolder_PropertyChanged;
            }
            var navArgs = e.CurrentInstance.TabItemArguments?.NavigationArg;
            SidebarAdaptiveViewModel.PaneHolder = e.CurrentInstance as IPaneHolder;
            SidebarAdaptiveViewModel.PaneHolder.PropertyChanged += PaneHolder_PropertyChanged;
            SidebarAdaptiveViewModel.NotifyInstanceRelatedPropertiesChanged((navArgs as PaneNavigationArguments).LeftPaneNavPathParam);
            UpdateStatusBarProperties();
            UpdateNavToolbarProperties();
            LoadPaneChanged();
            ViewModel.UpdateInstanceProperties(navArgs);
            e.CurrentInstance.ContentChanged -= TabItemContent_ContentChanged;
            e.CurrentInstance.ContentChanged += TabItemContent_ContentChanged;
        }

        private void PaneHolder_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SidebarAdaptiveViewModel.NotifyInstanceRelatedPropertiesChanged(SidebarAdaptiveViewModel.PaneHolder.ActivePane?.TabItemArguments?.NavigationArg?.ToString());
            UpdateStatusBarProperties();
            UpdateNavToolbarProperties();
            LoadPaneChanged();
        }

        private void UpdateStatusBarProperties()
        {
            if (StatusBarControl != null)
            {
                StatusBarControl.DirectoryPropertiesViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.SlimContentPage?.DirectoryPropertiesViewModel;
                StatusBarControl.SelectedItemsPropertiesViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.SlimContentPage?.SelectedItemsPropertiesViewModel;
            }
        }

        private void UpdateNavToolbarProperties()
        {
            if (NavToolbar != null)
            {
                NavToolbar.ViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.ToolbarViewModel;
            }

            if (InnerNavigationToolbar != null)
            {
                InnerNavigationToolbar.ViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.ToolbarViewModel;
                InnerNavigationToolbar.ShowMultiPaneControls = SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneEnabled ?? false;
                InnerNavigationToolbar.IsMultiPaneActive = SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneActive ?? false;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            ViewModel.OnNavigatedTo(e);
            SidebarControl.SidebarItemInvoked += SidebarControl_SidebarItemInvoked;
            SidebarControl.SidebarItemPropertiesInvoked += SidebarControl_SidebarItemPropertiesInvoked;
            SidebarControl.SidebarItemDropped += SidebarControl_SidebarItemDropped;
            SidebarControl.SidebarItemNewPaneInvoked += SidebarControl_SidebarItemNewPaneInvoked;
        }

        private async void SidebarControl_SidebarItemDropped(object sender, SidebarItemDroppedEventArgs e)
        {
            await SidebarAdaptiveViewModel.FilesystemHelpers.PerformOperationTypeAsync(e.AcceptedOperation, e.Package, e.ItemPath, false, true);
            e.SignalEvent?.Set();
        }

        private async void SidebarControl_SidebarItemPropertiesInvoked(object sender, SidebarItemPropertiesInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is DriveItem)
            {
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(e.InvokedItemDataContext, SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
            else if (e.InvokedItemDataContext is LibraryLocationItem library)
            {
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(new LibraryItem(library), SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
            else if (e.InvokedItemDataContext is LocationItem locationItem)
            {
                ListedItem listedItem = new ListedItem(null)
                {
                    ItemPath = locationItem.Path,
                    ItemNameRaw = locationItem.Text,
                    PrimaryItemAttribute = StorageItemTypes.Folder,
                    ItemType = "FileFolderListItem".GetLocalizedResource(),
                };
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(listedItem, SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
        }

        private void SidebarControl_SidebarItemNewPaneInvoked(object sender, SidebarItemNewPaneInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is INavigationControlItem navItem)
            {
                SidebarAdaptiveViewModel.PaneHolder.OpenPathInNewPane(navItem.Path);
            }
        }

        private void SidebarControl_SidebarItemInvoked(object sender, SidebarItemInvokedEventArgs e)
        {
            var invokedItemContainer = e.InvokedItemContainer;

            string navigationPath; // path to navigate
            Type sourcePageType = null; // type of page to navigate

            switch ((invokedItemContainer.DataContext as INavigationControlItem).ItemType)
            {
                case NavigationControlItemType.Location:
                    {
                        var ItemPath = (invokedItemContainer.DataContext as INavigationControlItem).Path; // Get the path of the invoked item

                        if (string.IsNullOrEmpty(ItemPath)) // Section item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }
                        else if (ItemPath.Equals("Home".GetLocalizedResource(), StringComparison.OrdinalIgnoreCase)) // Home item
                        {
                            if (ItemPath.Equals(SidebarAdaptiveViewModel.SidebarSelectedItem?.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                return; // return if already selected
                            }

                            navigationPath = "Home".GetLocalizedResource();
                            sourcePageType = typeof(WidgetsPage);
                        }
                        else // Any other item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }

                        break;
                    }

                case NavigationControlItemType.FileTag:
                    var tagPath = (invokedItemContainer.DataContext as INavigationControlItem).Path; // Get the path of the invoked item
                    if (SidebarAdaptiveViewModel.PaneHolder?.ActivePane is IShellPage shp)
                    {
                        shp.NavigateToPath(tagPath, new NavigationArguments()
                        {
                            IsSearchResultPage = true,
                            SearchPathParam = "Home".GetLocalizedResource(),
                            SearchQuery = tagPath,
                            AssociatedTabInstance = shp,
                            NavPathParam = tagPath
                        });
                    }
                    return;

                default:
                    {
                        navigationPath = invokedItemContainer.Tag?.ToString();
                        break;
                    }
            }

            if (SidebarAdaptiveViewModel.PaneHolder?.ActivePane is IShellPage shellPage)
            {
                shellPage.NavigateToPath(navigationPath, sourcePageType);
            }
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
            if (SidebarControl != null)
            {
                SidebarControl.SidebarItemInvoked -= SidebarControl_SidebarItemInvoked;
                SidebarControl.SidebarItemPropertiesInvoked -= SidebarControl_SidebarItemPropertiesInvoked;
                SidebarControl.SidebarItemDropped -= SidebarControl_SidebarItemDropped;
                SidebarControl.SidebarItemNewPaneInvoked -= SidebarControl_SidebarItemNewPaneInvoked;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Defers the status bar loading until after the page has loaded to improve startup perf
            FindName(nameof(StatusBarControl));
            FindName(nameof(InnerNavigationToolbar));
            FindName(nameof(horizontalMultitaskingControl));
            FindName(nameof(NavToolbar));

            // the adaptive triggers do not evaluate on app startup, manually checking and calling GoToState here fixes https://github.com/files-community/Files/issues/5801
            if (App.Window.Bounds.Width < CollapseSearchBoxAdaptiveTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(CollapseSearchBoxState), true);
            }

            if (App.Window.Bounds.Width < MinimalSidebarAdaptiveTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(MinimalSidebarState), true);
            }

            if (App.Window.Bounds.Width < CollapseHorizontalTabViewTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(HorizontalTabViewCollapsed), true);
            }
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            switch (Pane?.Position)
            {
                case PanePositions.Right when ContentColumn.ActualWidth == ContentColumn.MinWidth:
                    UserSettingsService.PaneSettingsService.VerticalSizePx += e.NewSize.Width - e.PreviousSize.Width;
                    UpdatePositioning();
                    break;
                case PanePositions.Bottom when ContentRow.ActualHeight == ContentRow.MinHeight:
                    UserSettingsService.PaneSettingsService.HorizontalSizePx += e.NewSize.Height - e.PreviousSize.Height;
                    UpdatePositioning();
                    break;
            }
        }

        private void ToggleFullScreenAccelerator(KeyboardAcceleratorInvokedEventArgs e)
        {
            var view = App.GetAppWindow(App.Window);

            if (view.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                view.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            else
            {
                view.SetPresenter(AppWindowPresenterKind.FullScreen);
            }

            e.Handled = true;
        }

        private void ToggleSidebarCollapsedState(KeyboardAcceleratorInvokedEventArgs e)
        {
            SidebarAdaptiveViewModel.IsSidebarOpen = !SidebarAdaptiveViewModel.IsSidebarOpen;

            e.Handled = true;
        }

        private void SidebarControl_Loaded(object sender, RoutedEventArgs e)
        {
            SidebarAdaptiveViewModel.UpdateTabControlMargin(); // Set the correct tab margin on startup
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => LoadPaneChanged();

        /// <summary>
        /// Call this function to update the positioning of the preview pane.
        /// This is a workaround as the VisualStateManager causes problems.
        /// </summary>
        private void UpdatePositioning()
        {
            if (Pane is null || !IsPaneEnabled)
            {
                PaneRow.MinHeight = 0;
                PaneRow.MaxHeight = double.MaxValue;
                PaneRow.Height = new GridLength(0);
                PaneColumn.MinWidth = 0;
                PaneColumn.MaxWidth = double.MaxValue;
                PaneColumn.Width = new GridLength(0);
            }
            else
            {
                Pane.UpdatePosition(RootGrid.ActualWidth, RootGrid.ActualHeight);
                switch (Pane.Position)
                {
                    case PanePositions.None:
                        PaneRow.MinHeight = 0;
                        PaneRow.Height = new GridLength(0);
                        PaneColumn.MinWidth = 0;
                        PaneColumn.Width = new GridLength(0);
                        break;
                    case PanePositions.Right:
                        Pane.SetValue(Grid.RowProperty, 1);
                        Pane.SetValue(Grid.ColumnProperty, 2);
                        PaneSplitter.SetValue(Grid.RowProperty, 1);
                        PaneSplitter.SetValue(Grid.ColumnProperty, 1);
                        PaneSplitter.Width = 2;
                        PaneSplitter.Height = RootGrid.ActualHeight;
                        PaneSplitter.GripperCursor = GridSplitter.GripperCursorType.SizeWestEast;
                        PaneColumn.MinWidth = Pane.MinWidth;
                        PaneColumn.MaxWidth = Pane.MaxWidth;
                        PaneColumn.Width = new GridLength(UserSettingsService.PaneSettingsService.VerticalSizePx, GridUnitType.Pixel);
                        PaneRow.MinHeight = 0;
                        PaneRow.MaxHeight = double.MaxValue;
                        PaneRow.Height = new GridLength(0);
                        break;
                    case PanePositions.Bottom:
                        Pane.SetValue(Grid.RowProperty, 3);
                        Pane.SetValue(Grid.ColumnProperty, 0);
                        PaneSplitter.SetValue(Grid.RowProperty, 2);
                        PaneSplitter.SetValue(Grid.ColumnProperty, 0);
                        PaneSplitter.Height = 2;
                        PaneSplitter.Width = RootGrid.ActualWidth;
                        PaneSplitter.GripperCursor = GridSplitter.GripperCursorType.SizeNorthSouth;
                        PaneColumn.MinWidth = 0;
                        PaneColumn.MaxWidth = double.MaxValue;
                        PaneColumn.Width = new GridLength(0);
                        PaneRow.MinHeight = Pane.MinHeight;
                        PaneRow.MaxHeight = Pane.MaxHeight;
                        PaneRow.Height = new GridLength(UserSettingsService.PaneSettingsService.HorizontalSizePx, GridUnitType.Pixel);
                        break;
                }
            }
        }

        private void PaneSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            switch (Pane?.Position)
            {
                case PanePositions.Right:
                    UserSettingsService.PaneSettingsService.VerticalSizePx = Pane.ActualWidth;
                    break;
                case PanePositions.Bottom:
                    UserSettingsService.PaneSettingsService.HorizontalSizePx = Pane.ActualHeight;
                    break;
            }
        }

        public bool IsPaneEnabled => UserSettingsService.PaneSettingsService.Content switch
        {
            PaneContents.Preview => IsPreviewPaneEnabled,
            _ => false,
        };

        public bool IsPreviewPaneEnabled
        {
            get
            {
                var isHomePage = !(SidebarAdaptiveViewModel.PaneHolder?.ActivePane?.InstanceViewModel?.IsPageTypeNotHome ?? false);
                var isMultiPane = SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneActive ?? false;
                var isBigEnough = App.Window.Bounds.Width > 450 && App.Window.Bounds.Height > 400;
                var isEnabled = (!isHomePage || isMultiPane) && isBigEnough;

                return isEnabled;
            }
        }

        private void LoadPaneChanged()
        {
            OnPropertyChanged(nameof(IsPaneEnabled));
            OnPropertyChanged(nameof(IsPreviewPaneEnabled));
            UpdatePositioning();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ToggleCompactOverlay() => SetCompactOverlay(App.GetAppWindow(App.Window).Presenter.Kind != AppWindowPresenterKind.CompactOverlay);

        private void SetCompactOverlay(bool isCompact)
        {
            var view = App.GetAppWindow(App.Window);

            if (!isCompact)
            {
                IsCompactOverlay = true;
                view.SetPresenter(AppWindowPresenterKind.Overlapped);
            }
            else
            {
                IsCompactOverlay = false;
                view.SetPresenter(AppWindowPresenterKind.CompactOverlay);
                view.Resize(new SizeInt32(400, 350));
            }
        }

        private bool isCompactOverlay;

        public bool IsCompactOverlay
        {
            get => isCompactOverlay;
            set
            {
                if (value != isCompactOverlay)
                {
                    isCompactOverlay = value;
                    OnPropertyChanged(nameof(IsCompactOverlay));
                }
            }
        }

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // prevents the arrow key events from navigating the list instead of switching compact overlay
            if (EnterCompactOverlayKeyboardAccelerator.CheckIsPressed() || ExitCompactOverlayKeyboardAccelerator.CheckIsPressed())
            {
                Focus(FocusState.Keyboard);
            }
        }

        private void NavToolbar_Loaded(object sender, RoutedEventArgs e) => UpdateNavToolbarProperties();
    }
}