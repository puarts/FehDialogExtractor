using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace FehDialogExtractor
{
    public static class TessdataInstaller
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Ensure the specified traineddata language files exist in destination folder.
        /// Downloads from the official tesseract-ocr/tessdata repository if missing.
        /// Returns true if all requested files are present after the call.
        /// </summary>
        public static async Task<bool> EnsureTessdataAsync(string[] languages, string destFolder)
        {
            if (languages == null || languages.Length == 0) throw new ArgumentException("languages required");
            if (string.IsNullOrEmpty(destFolder)) throw new ArgumentNullException(nameof(destFolder));

            Directory.CreateDirectory(destFolder);

            foreach (var lang in languages)
            {
                var fileName = lang + ".traineddata";
                var destPath = Path.Combine(destFolder, fileName);
                if (File.Exists(destPath)) continue;

                // Raw file URL on GitHub
                var url = $"https://github.com/tesseract-ocr/tessdata/raw/main/{fileName}";
                try
                {
                    using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        // Try legacy tessdata_best location
                        var altUrl = $"https://github.com/tesseract-ocr/tessdata_best/raw/main/{fileName}";
                        using var altResp = await _httpClient.GetAsync(altUrl).ConfigureAwait(false);
                        if (!altResp.IsSuccessStatusCode) return false;

                        var altBytes = await altResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        await File.WriteAllBytesAsync(destPath, altBytes).ConfigureAwait(false);
                    }
                    else
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
                    }
                }
                catch
                {
                    return false;
                }
            }

            // verify
            foreach (var lang in languages)
            {
                if (!File.Exists(Path.Combine(destFolder, lang + ".traineddata"))) return false;
            }

            return true;
        }
    }
}
