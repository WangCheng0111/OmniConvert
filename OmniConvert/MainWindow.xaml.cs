using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OmniConvert.ViewModels;
using System.ComponentModel;
using Windows.ApplicationModel;
using Windows.Graphics;

namespace OmniConvert
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();

        public MainWindow()
        {
            this.InitializeComponent();

            _captionButtons = new[] { SettingsButton, MinimizeButton, MaximizeButton, CloseButton };

            _appWindow = this.AppWindow;
            _appWindow.SetIcon("Assets/Tiles/GalleryIcon.ico");
            InitializeNonClientInput();
            InitializeWindowSubclass();
            Activated += MainWindow_Activated;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            AppTitleBar.Loaded += AppTitleBar_Loaded;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            settingsHost.CloseRequested += SettingsHost_CloseRequested;

            ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

            TitleBarTextBlock.Text = AppInfo.Current.DisplayInfo.DisplayName;

            CenterWindow();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.IsSettingsOpen))
            {
                return;
            }

            if (ViewModel.IsSettingsOpen)
            {
                settingsHost.Show();
            }
            else
            {
                settingsHost.Hide();
            }
        }

        private void SettingsHost_CloseRequested(object? sender, System.EventArgs e)
        {
            ViewModel.CloseSettingsCommand.Execute(null);
        }

        private void CenterWindow()
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var width = (int)(workArea.Width * 0.60);
            var height = (int)(workArea.Height * 0.64);
            var winX = workArea.X + (workArea.Width - width) / 2;
            var winY = workArea.Y + (workArea.Height - height) / 2;
            AppWindow.MoveAndResize(new RectInt32(winX, winY, width, height));
        }
    }
}
