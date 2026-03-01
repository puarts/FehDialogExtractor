using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System.Windows.Input;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FehDialogExtractor
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ICommand NavigateCommand { get; }
        public ICommand CaptureCommand { get; }
        public ICommand OpenImageCommand { get; }
        public ICommand ExtractCommand { get; }
        public ICommand SaveCommand { get; }

        private string? _currentImagePath;
        private string _extractedText = string.Empty;
        private BitmapImage? _previewImage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ExtractedText
        {
            get => _extractedText;
            set
            {
                if (_extractedText != value)
                {
                    _extractedText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExtractedText)));
                }
            }
        }

        private string _urlText = string.Empty;
        public string UrlText
        {
            get => _urlText;
            set
            {
                if (_urlText != value)
                {
                    _urlText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UrlText)));
                }
            }
        }

        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            private set
            {
                if (_previewImage != value)
                {
                    _previewImage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
                }
            }
        }

        public MainViewModel()
        {
            NavigateCommand = new AsyncRelayCommand(async p =>
            {
                var webView = p as WebView2;
                if (webView != null)
                    await NavigateToUrlAsync(webView, UrlText);
            });

            CaptureCommand = new AsyncRelayCommand(async p =>
            {
                var webView = p as WebView2;
                if (webView != null)
                {
                    var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../azurevision.json");
                    try
                    {
                        ExtractedText = await CaptureAndExtractFromWebViewAsync(webView, settingsPath);
                    }
                    catch (Exception exception)
                    {
                        ExtractedText = $"Error during capture and OCR: {exception.Message}";
                    }
                }
            });

            OpenImageCommand = new RelayCommand(p =>
            {
                var ofd = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*" };
                if (ofd.ShowDialog() == true)
                {
                    LoadImage(ofd.FileName);
                }
            });

            ExtractCommand = new AsyncRelayCommand(async p => await ExtractTextFromCurrentImageAsync());

            SaveCommand = new RelayCommand(p =>
            {
                var sfd = new SaveFileDialog { Filter = "Text File|*.txt|All Files|*.*" };
                if (sfd.ShowDialog() == true)
                {
                    SaveTextToFile(sfd.FileName);
                }
            });
        }

        public void LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("Image file not found.", path);

            _currentImagePath = path;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_currentImagePath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            PreviewImage = bmp;
        }

        public async Task NavigateToUrlAsync(WebView2? webView, string url)
        {
            if (webView == null)
                throw new ArgumentNullException(nameof(webView));

            if (string.IsNullOrWhiteSpace(url))
                return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            if (webView.CoreWebView2 == null)
                await webView.EnsureCoreWebView2Async();

            webView.CoreWebView2.Navigate(url);
        }

        public async Task<string> CaptureAndExtractFromWebViewAsync(WebView2? webView, string azureSettingsPath)
        {
            if (webView == null)
                throw new ArgumentNullException(nameof(webView));

            if (webView.CoreWebView2 == null)
                await webView.EnsureCoreWebView2Async();

            using var ms = new MemoryStream();
            await webView.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;

            var ocr = new OcrService();
            var settings = AzureVisionSettings.LoadAzureVisionSettings(azureSettingsPath);
            var text = await ocr.ExtractTextFromImageAzureVision(ms, settings.Endpoint, settings.ApiKey);
            return (text ?? string.Empty)
                .Replace("、" + Environment.NewLine, "、")
                .Replace("かつ" + Environment.NewLine, "かつ")
                .Replace("で" + Environment.NewLine, "で")
                .Replace("の" + Environment.NewLine, "の")
                ;
        }

        public async Task<string?> ExtractTextFromCurrentImageAsync()
        {
            if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath))
                throw new FileNotFoundException("Open an image first.");

            // Use reflection as before to avoid direct dependency at compile time
            var asm = Assembly.Load(new AssemblyName("Tesseract"));
            if (asm == null)
                throw new InvalidOperationException("Tesseract assembly not found.");

            var tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

            var engineType = asm.GetType("Tesseract.TesseractEngine");
            var pixType = asm.GetType("Tesseract.Pix");
            var engineModeType = asm.GetType("Tesseract.EngineMode");

            if (engineType == null || pixType == null)
                throw new InvalidOperationException("Tesseract types not found in assembly.");

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
                throw new InvalidOperationException("Failed to create Tesseract engine instance.");

            var loadMethod = pixType.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);
            if (loadMethod == null)
                throw new InvalidOperationException("Pix.LoadFromFile not found.");

            var pix = loadMethod.Invoke(null, new object?[] { _currentImagePath });
            if (pix == null)
                throw new InvalidOperationException("Failed to load image into Pix.");

            var processMethod = engineType.GetMethod("Process", new Type[] { pixType });
            if (processMethod == null)
                throw new InvalidOperationException("Engine.Process(Pix) not found.");

            using var page = processMethod.Invoke(engine, new object?[] { pix }) as IDisposable;
            if (page == null)
                throw new InvalidOperationException("Failed to process image.");

            var pageType = page.GetType();
            var getTextMethod = pageType.GetMethod("GetText", BindingFlags.Public | BindingFlags.Instance);
            if (getTextMethod == null)
                throw new InvalidOperationException("Page.GetText not found.");

            var text = getTextMethod.Invoke(page, Array.Empty<object>()) as string;
            ExtractedText = text ?? string.Empty;

            if (engine is IDisposable disposableEngine)
                disposableEngine.Dispose();

            return text;
        }

        public void SaveTextToFile(string path)
        {
            File.WriteAllText(path, ExtractedText ?? string.Empty);
        }
    }
}
