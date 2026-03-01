using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FehDialogExtractor
{
    public partial class MainWindow : Window
    {
        private string _currentImagePath;
        private Image? _previewImage;
        private TextBox? _extractedTextBox;
        private WebView2? _webViewControl;
        private MainViewModel _viewModel;

        public MainWindow()
        {
            // テーマ初期化（OS の設定に従う）
            ThemeManager.Initialize(followOs: true);

            _viewModel = new MainViewModel();
            InitializeComponent();

            // Set DataContext for bindings
            DataContext = _viewModel;

            // Resolve controls by name instead of relying on generated fields
            _previewImage = FindName("PreviewImage") as Image;
            _extractedTextBox = FindName("ExtractedTextBox") as TextBox;
            _webViewControl = FindName("webView") as WebView2;

            // Wire up title bar and window buttons (avoid XAML event references)
            var titleBar = FindName("TitleBar") as Border;
            if (titleBar != null)
                titleBar.MouseDown += TitleBar_MouseDown;

            var minBtn = FindName("MinimizeButton") as Button;
            if (minBtn != null)
                minBtn.Click += MinimizeButton_Click;

            var maxBtn = FindName("MaximizeButton") as Button;
            if (maxBtn != null)
                maxBtn.Click += MaximizeButton_Click;

            var closeBtn = FindName("CloseButton") as Button;
            if (closeBtn != null)
                closeBtn.Click += CloseButton_Click;

            // If a preview image was loaded in the view model, reflect it into the UI
            if (_viewModel.PreviewImage != null && _previewImage != null)
                _previewImage.Source = _viewModel.PreviewImage;

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.UrlText = "https://www.youtube.com/watch?v=M4tG0AuhgRM";
            await _viewModel.NavigateToUrlAsync(webView, _viewModel.UrlText);
        }

        // Ctrl+T でテーマ切替
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ThemeManager.ToggleTheme();
                e.Handled = true;
            }
        }

        // Title bar drag and double-click to maximize
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ToggleWindowState();
                }
                else
                {
                    try { DragMove(); } catch { }
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }
    }
}