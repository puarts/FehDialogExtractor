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

        public MainWindow()
        {
            // テーマ初期化（OS の設定に従う）
            ThemeManager.Initialize(followOs: true);

            InitializeComponent();

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

        private async void NavigateButton_Click(object sender, RoutedEventArgs e)
        {
            var tb = FindName("UrlTextBox") as TextBox;
            await NavigateToUrlAsync(tb?.Text ?? string.Empty);
        }

        private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var tb = sender as TextBox;
                await NavigateToUrlAsync(tb?.Text ?? string.Empty);
                e.Handled = true;
            }
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_webViewControl == null)
                    _webViewControl = FindName("webView") as WebView2;

                if (_webViewControl == null)
                {
                    MessageBox.Show(this, "WebView control not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_webViewControl.CoreWebView2 == null)
                    await _webViewControl.EnsureCoreWebView2Async();

                var sfd = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "webview_capture.png" };
                using var ms = new System.IO.MemoryStream();
                await _webViewControl.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                ms.Position = 0;

                try
                {
                    var ocr = new OcrService();

                    var settings = AzureVisionSettings.LoadAzureVisionSettings("F:\\repos\\FehDialogExtractor\\FehDialogExtractor\\azurevision.json");
                    var text = await ocr.ExtractTextFromImageAzureVision(ms, settings.Endpoint, settings.ApiKey);
                    if (_extractedTextBox == null)
                        _extractedTextBox = FindName("ExtractedTextBox") as TextBox;

                    if (_extractedTextBox != null)
                        _extractedTextBox.Text = text ?? string.Empty;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "OCR failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Capture failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async Task NavigateToUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            try
            {
                if (_webViewControl == null)
                    _webViewControl = FindName("webView") as WebView2;

                if (_webViewControl != null)
                {
                    if (_webViewControl.CoreWebView2 == null)
                        await _webViewControl.EnsureCoreWebView2Async();

                    _webViewControl.CoreWebView2.Navigate(url);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Navigation failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenImageButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*";
            if (ofd.ShowDialog(this) == true)
            {
                _currentImagePath = ofd.FileName;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_currentImagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (_previewImage != null)
                    _previewImage.Source = bmp;
            }
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath) || !System.IO.File.Exists(_currentImagePath))
            {
                MessageBox.Show(this, "Open an image first.", "No image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var asm = Assembly.Load(new AssemblyName("Tesseract"));
                if (asm == null)
                {
                    MessageBox.Show(this, "Tesseract assembly not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var tessDataPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                var engineType = asm.GetType("Tesseract.TesseractEngine");
                var pixType = asm.GetType("Tesseract.Pix");
                var engineModeType = asm.GetType("Tesseract.EngineMode");

                if (engineType == null || pixType == null)
                {
                    MessageBox.Show(this, "Tesseract types not found in assembly.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                object? engine = null;
                if (engineModeType != null)
                {
                    var defaultField = engineModeType.GetField("Default");
                    object? engineModeValue = defaultField?.GetValue(null);
                    engine = Activator.CreateInstance(engineType, new object?[] { tessDataPath, "eng", engineModeValue });
                }
                else
                {
                    engine = Activator.CreateInstance(engineType, new object?[] { tessDataPath, "eng" });
                }

                if (engine == null)
                {
                    MessageBox.Show(this, "Failed to create Tesseract engine instance.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var loadMethod = pixType.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);
                if (loadMethod == null)
                {
                    MessageBox.Show(this, "Pix.LoadFromFile not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var pix = loadMethod.Invoke(null, new object?[] { _currentImagePath });
                if (pix == null)
                {
                    MessageBox.Show(this, "Failed to load image into Pix.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var processMethod = engineType.GetMethod("Process", new Type[] { pixType });
                if (processMethod == null)
                {
                    MessageBox.Show(this, "Engine.Process(Pix) not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                using var page = processMethod.Invoke(engine, new object?[] { pix }) as IDisposable;
                if (page == null)
                {
                    MessageBox.Show(this, "Failed to process image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var pageType = page.GetType();
                var getTextMethod = pageType.GetMethod("GetText", BindingFlags.Public | BindingFlags.Instance);
                if (getTextMethod == null)
                {
                    MessageBox.Show(this, "Page.GetText not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var text = getTextMethod.Invoke(page, Array.Empty<object>()) as string;
                if (_extractedTextBox != null)
                    _extractedTextBox.Text = text ?? string.Empty;

                if (engine is IDisposable disposableEngine)
                    disposableEngine.Dispose();
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show(this, "tessdata not found. Place language traineddata files under a 'tessdata' folder next to the executable.", "Data Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "OCR failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "Text File|*.txt|All Files|*.*";
            if (sfd.ShowDialog(this) == true)
            {
                System.IO.File.WriteAllText(sfd.FileName, _extractedTextBox?.Text ?? string.Empty);
            }
        }
    }
}