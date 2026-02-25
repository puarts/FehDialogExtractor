using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FehDialogExtractor
{
    public partial class MainWindow : Window
    {
        private string _currentImagePath;
        private Image? _previewImage;
        private TextBox? _extractedTextBox;

        public MainWindow()
        {
            InitializeComponent();
            // Resolve controls by name instead of relying on generated fields
            _previewImage = FindName("PreviewImage") as Image;
            _extractedTextBox = FindName("ExtractedTextBox") as TextBox;
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
                // Use reflection to call Tesseract APIs so the project can compile even if Tesseract types
                // are not resolvable at compile time in this environment.
                var asm = Assembly.Load(new AssemblyName("Tesseract"));
                if (asm == null)
                {
                    MessageBox.Show(this, "Tesseract assembly not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var tessDataPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

                // Get types
                var engineType = asm.GetType("Tesseract.TesseractEngine");
                var pixType = asm.GetType("Tesseract.Pix");
                var engineModeType = asm.GetType("Tesseract.EngineMode");

                if (engineType == null || pixType == null)
                {
                    MessageBox.Show(this, "Tesseract types not found in assembly.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create engine instance: new TesseractEngine(tessDataPath, "eng", EngineMode.Default)
                object? engine = null;
                if (engineModeType != null)
                {
                    var defaultField = engineModeType.GetField("Default");
                    object? engineModeValue = defaultField?.GetValue(null);
                    engine = Activator.CreateInstance(engineType, new object?[] { tessDataPath, "eng", engineModeValue });
                }
                else
                {
                    // If EngineMode not available, try constructor with two args
                    engine = Activator.CreateInstance(engineType, new object?[] { tessDataPath, "eng" });
                }

                if (engine == null)
                {
                    MessageBox.Show(this, "Failed to create Tesseract engine instance.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load image: Pix.LoadFromFile(path)
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

                // Call engine.Process(pix)
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

                // GetText method
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

                // Dispose engine if IDisposable
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