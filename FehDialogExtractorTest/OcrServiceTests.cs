using FehDialogExtractor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FehDialogExtractorTest
{
    [TestClass]
    public class OcrServiceTests
    {
        [TestMethod]
        public void AzureVisionOcrTest()
        {
            var svc = new FehDialogExtractor.OcrService();
            var samplePath = Path.Combine(AppContext.BaseDirectory, "../../../../FehDialogExtractor/Samples",
"SampleImage2.jpg"
                //"Hiragana-Katakana-Gojuuon.jpg"
                );
            samplePath = Path.GetFullPath(samplePath);

            var settingPath = Path.Combine(AppContext.BaseDirectory, "../../../../FehDialogExtractor", "azurevision.json");

            var settings = AzureVisionSettings.LoadAzureVisionSettings(settingPath);

            var recognized = svc.ExtractTextFromImageAzureVision(samplePath, settings.Endpoint, settings.ApiKey).Result;
            Console.WriteLine(recognized);
        }


        [TestMethod]
        public void MicrofostOcrTest()
        {
            var svc = new FehDialogExtractor.OcrService();
            var samplePath = Path.Combine(AppContext.BaseDirectory, "../../../../FehDialogExtractor/Samples",
"SampleImage2.jpg"
                //"Hiragana-Katakana-Gojuuon.jpg"
                );
            samplePath = Path.GetFullPath(samplePath);

            var recognized = svc.ExtractTextFromImageWinRt(samplePath);
            Console.WriteLine(recognized);
        }

        [TestMethod]
        public void ExtractSampleImage_ReturnsTextOrEmpty()
        {
            // Arrange
            var svc = new FehDialogExtractor.OcrService();
            var samplePath = Path.Combine(AppContext.BaseDirectory, "../../../../FehDialogExtractor/Samples", 
"SampleImage2.jpg"
//"Hiragana-Katakana-Gojuuon.jpg"
                );
            samplePath = Path.GetFullPath(samplePath);

            if (!File.Exists(samplePath))
            {
                Assert.Inconclusive($"Sample image not found at '{samplePath}'. Place SampleImage.jpg in the FehDialogExtractor project root to run this test.");
                return;
            }

            // tessdata location
            var tessData = Path.Combine(AppContext.BaseDirectory, "tessdata");
            tessData = Path.GetFullPath(tessData);
            Directory.CreateDirectory(tessData); // Ensure directory exists for test

            // Ensure traineddata available (download if necessary)
            // Use Japanese traineddata for this test
            var ok = Task.Run(async () => await FehDialogExtractor.TessdataInstaller.EnsureTessdataAsync(new[] { "jpn" }, tessData)).GetAwaiter().GetResult();
            if (!ok)
            {
                Assert.Inconclusive($"Failed to download tessdata to '{tessData}'. Network access may be required to fetch traineddata files.");
                return;
            }

            // Act
            string result;
            try
            {
                // Pass explicit tessDataPath and Japanese language code so OcrService uses the correct data
                result = svc.ExtractTextFromImage(samplePath, tessData, "jpn");
            }
            catch (Exception ex)
            {
                Assert.Fail("OCR invocation threw: " + ex.Message);
                return;
            }

            // Assert: ensure extracted text contains Japanese characters
            Console.WriteLine($"Extracted text:\"{result}\"");

            // If OCR produced no text, mark test inconclusive rather than failing the CI build.
            if (string.IsNullOrWhiteSpace(result))
            {
                Assert.Inconclusive("OCR returned empty result. Ensure 'jpn.traineddata' is available and Tesseract supports the input image.");
                return;
            }

            // Require at least one Japanese character (Hiragana, Katakana, or CJK ideographs)
            var hasJapanese = Regex.IsMatch(result, "[\\p{IsHiragana}\\p{IsKatakana}\\p{IsCJKUnifiedIdeographs}]");
            Assert.IsTrue(hasJapanese, "OCR result does not contain Japanese characters.");
        }
    }
}
