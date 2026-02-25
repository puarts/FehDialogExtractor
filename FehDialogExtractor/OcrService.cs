using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Tesseract;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Linq;

namespace FehDialogExtractor
{
    public class OcrService
    {
        public string ExtractTextFromImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) throw new ArgumentNullException(nameof(imagePath));
            if (!File.Exists(imagePath)) throw new FileNotFoundException("Image not found", imagePath);
            try
            {
                // Open the image file as a WinRT random access stream
                using var fs = File.OpenRead(imagePath);
                IRandomAccessStream rand = fs.AsRandomAccessStream();

                // Create a decoder and get a SoftwareBitmap
                var decoder = BitmapDecoder.CreateAsync(rand).AsTask().GetAwaiter().GetResult();
                using var softwareBitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();

                // Convert to Gray8 which works well for OCR; if conversion fails try Bgra8.
                SoftwareBitmap bitmapForOcr;
                try
                {
                    bitmapForOcr = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);
                }
                catch
                {
                    bitmapForOcr = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                using (bitmapForOcr)
                {
                    // Create OCR engine - prefer user profile languages, fall back to English
                    var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en"));

                    var ocrResult = ocrEngine.RecognizeAsync(bitmapForOcr).AsTask().GetAwaiter().GetResult();

                    return ocrResult?.Text ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WinRT OCR invocation failed.", ex);
            }
        }

        public async Task<string> ExtractTextFromImageAzureVision(string imagePath)
        {
            var settings = LoadAzureVisionSettings();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", settings.ApiKey);

            // 1. Read API を呼び出し（非同期ジョブ）
            var uri = $"{settings.Endpoint}vision/v3.2/read/analyze?language=ja&readingOrder=basic";

            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            using var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");


            // OCR リクエスト送信
            var response = await client.PostAsync(uri, content);
            response.EnsureSuccessStatusCode();

            // 結果取得用の Operation-Location ヘッダ
            var operationLocation = response.Headers.GetValues("Operation-Location").FirstOrDefault();

            // 2. 結果が出るまでポーリング
            string result;
            while (true)
            {
                await Task.Delay(1000);
                var resultResponse = await client.GetAsync(operationLocation);
                result = await resultResponse.Content.ReadAsStringAsync();

                if (result.Contains("\"status\":\"succeeded\"") || result.Contains("\"status\":\"failed\""))
                    break;
            }

            // Parse JSON and extract recognized text (lines[].text) from analyzeResult.readResults
            try
            {
                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                var sb = new StringBuilder();

                if (root.TryGetProperty("analyzeResult", out var analyze) &&
                    analyze.TryGetProperty("readResults", out var readResults) &&
                    readResults.ValueKind == JsonValueKind.Array)
                {
                    foreach (var page in readResults.EnumerateArray())
                    {
                        if (page.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var line in lines.EnumerateArray())
                            {
                                if (line.TryGetProperty("text", out var textEl))
                                {
                                    var txt = textEl.GetString() ?? string.Empty;
                                    if (sb.Length > 0) sb.AppendLine();
                                    sb.Append(txt);
                                }
                            }
                        }
                    }
                }

                return sb.ToString();
            }
            catch (JsonException)
            {
                // If parsing fails, return raw result
                return result;
            }
        }

        /// <summary>
        /// WinRT OCR を言語指定で実行します。日本語指定は例: "ja" または "ja-JP" を渡してください。
        /// 既定は "ja"（日本語）です。
        /// </summary>
        /// <param name="imagePath">画像ファイルのフルパス</param>
        /// <param name="language">BCP-47 形式の言語タグ（例: "ja"、"ja-JP"、"en"）</param>
        /// <returns>抽出したテキスト（空文字列あり得る）</returns>
        public string ExtractTextFromImageWinRt(string imagePath, string language = "ja")
        {
            if (string.IsNullOrEmpty(imagePath)) throw new ArgumentNullException(nameof(imagePath));
            if (!File.Exists(imagePath)) throw new FileNotFoundException("Image not found", imagePath);

            try
            {
                using var fs = File.OpenRead(imagePath);
                IRandomAccessStream rand = fs.AsRandomAccessStream();

                var decoder = BitmapDecoder.CreateAsync(rand).AsTask().GetAwaiter().GetResult();
                using var softwareBitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();

                // Prepare bitmap for OCR
                SoftwareBitmap bitmapForOcr;
                try
                {
                    bitmapForOcr = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);
                }
                catch
                {
                    bitmapForOcr = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                }

                bool skipsGrayScaleConversion = true;
                using (bitmapForOcr)
                {
                    using var grayScaleBitmapForOcr2 = ResizeAndConvertToGray(bitmapForOcr);
                    var grayScaleBitmapForOcr = skipsGrayScaleConversion ? bitmapForOcr : grayScaleBitmapForOcr2;

                    // Try to create engine with requested language first, then fallback to user profile languages
                    OcrEngine? ocrEngine = null;
                    try
                    {
                        var lang = new Language(language);
                        ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                    }
                    catch
                    {
                        // ignore language parse errors - will fallback below
                    }

                    ocrEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();

                    if (ocrEngine == null)
                    {
                        throw new InvalidOperationException($"Failed to create OcrEngine for language '{language}' or user profile languages.");
                    }

                    var ocrResult = ocrEngine.RecognizeAsync(grayScaleBitmapForOcr).AsTask().GetAwaiter().GetResult();
                    var recognized = ocrResult?.Text ?? string.Empty;
                    return recognized.Replace(" ", "");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("WinRT OCR invocation failed.", ex);
            }
        }

        /// <summary>
        /// Extracts text from the given image using the Tesseract assembly.
        /// This method uses reflection so the code can provide clear runtime errors
        /// when Tesseract or tessdata are missing.
        /// </summary>
        /// <param name="imagePath">Full path to image file.</param>
        /// <param name="tessDataPath">Optional tessdata folder path. If null the executable directory + "tessdata" is used.</param>
        /// <param name="language">Language code for Tesseract (default: "eng").</param>
        /// <returns>Extracted text (may be empty string).</returns>
        public string ExtractTextFromImage(string imagePath, string? tessDataPath = null, string language = "eng")
        {
            if (string.IsNullOrEmpty(imagePath)) throw new ArgumentNullException(nameof(imagePath));
            if (!File.Exists(imagePath)) throw new FileNotFoundException("Image not found", imagePath);

            tessDataPath ??= Path.Combine(AppContext.BaseDirectory, "tessdata");
            if (!Directory.Exists(tessDataPath)) throw new DirectoryNotFoundException($"tessdata folder not found: {tessDataPath}");

            try
            {
                // Use Tesseract native API directly instead of reflection.
                // Explicitly specify EngineMode.Default for compatibility with versions that require it.
                using var engine = new TesseractEngine(tessDataPath, language, EngineMode.Default);
                using var pix = Pix.LoadFromFile(imagePath);
                using var page = engine.Process(pix);

                var text = page.GetText();

                return text ?? string.Empty;
            }
            catch (Exception ex)
            {
                // Wrap any Tesseract exceptions for clearer diagnostics
                throw new InvalidOperationException("Tesseract invocation failed.", ex);
            }
        }

        // 解像度を scale 倍にして Gray8 の SoftwareBitmap を返す（同期呼び出し）。
        // scale: 1 以上の整数（例: 2）
        private SoftwareBitmap ResizeAndConvertToGray(SoftwareBitmap src, int scale = 2)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (scale <= 1) return SoftwareBitmap.Convert(src, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);

            // Encoder にセットするため Bgra8 に変換
            var toEncode = src.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? src
                : SoftwareBitmap.Convert(src, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            using var mem = new InMemoryRandomAccessStream();
            var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem).AsTask().GetAwaiter().GetResult();
            encoder.SetSoftwareBitmap(toEncode);

            encoder.BitmapTransform.ScaledWidth = (uint)(src.PixelWidth * scale);
            encoder.BitmapTransform.ScaledHeight = (uint)(src.PixelHeight * scale);

            // フラッシュしてストリームに書き込む
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

            mem.Seek(0);
            var decoder = BitmapDecoder.CreateAsync(mem).AsTask().GetAwaiter().GetResult();
            var resized = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();

            var gray = SoftwareBitmap.Convert(resized, BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore);
            return gray;
        }

        // Load Azure Vision settings from JSON file located in the application base directory.
        private AzureVisionSettings LoadAzureVisionSettings()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "azurevision.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"Azure Vision configuration file not found: {path}");

            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var settings = JsonSerializer.Deserialize<AzureVisionSettings>(json, opts);
            if (settings == null || string.IsNullOrEmpty(settings.Endpoint) || string.IsNullOrEmpty(settings.ApiKey))
                throw new InvalidOperationException($"Azure Vision configuration is invalid: {path}");

            return settings;
        }

        private sealed record AzureVisionSettings(string Endpoint, string ApiKey);
    }
}
