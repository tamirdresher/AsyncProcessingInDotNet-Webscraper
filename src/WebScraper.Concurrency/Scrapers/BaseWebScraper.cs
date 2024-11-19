using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency.Scrapers
{
    public abstract class BaseWebScraper : IDisposable
    {
        protected readonly ILogger _logger;
        protected readonly IConfiguration _configuration;
        private readonly ITranslator _translator;
        protected readonly HttpClient _httpClient = new HttpClient();

        protected BaseWebScraper(ILogger logger, IConfiguration configuration, ITranslator translator)
        {
            _logger = logger;
            _configuration = configuration;
            _translator = translator;
        }

        abstract public Task ScrapeAsync(string startUrl, string basePath = "", int maxDepth = 5, string translateToLanguage = "he", bool stayInDomain = true);
        protected static string ConvertUrlToFileName(string url)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                url = url.Replace(c, '_');
            }
            return url;
        }

        protected string? GetAbsoluteUrl(string url, string baseUrl)
        {
            return Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url :
                Uri.TryCreate(new Uri(baseUrl), url, out var absoluteUri) ? absoluteUri.ToString() : null;
        }
        protected string CreatePathForImage(string basePath, Uri uri)
        {
            var fileName = uri.ToString();
            if (!string.IsNullOrEmpty(Path.GetExtension(uri.AbsolutePath)))
            {
                // Return the file name including the extension
                fileName = Path.GetFileName(uri.AbsolutePath);
            }

            string path = Path.Combine(basePath, uri.Host, "images", ConvertUrlToFileName(fileName));

            string directory = Path.GetDirectoryName(path);

            // Create the directory if it doesn't exist
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return path;
        }

        protected string CreatePathForHtml(string basePath, Uri uri)
        {
            string path = Path.Combine(basePath, uri.Host, ConvertUrlToFileName(uri.ToString()));

            string directory = Path.GetDirectoryName(path);

            // Create the directory if it doesn't exist
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            path = Path.HasExtension(uri.ToString()) ? path : path + ".html";
            return path;
        }

        bool IsHtmlFileExists(string basePath, Uri uri)
        {
            string path = CreatePathForHtml(basePath, uri);
            return File.Exists(path);
        }
        protected async Task<string> DownloadHtmlAsync(Uri uri)
        {
            try
            {
                var response = await _httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error while processing {uri}", uri);
                return string.Empty;
            }
        }

        protected async Task DownloadImageAsync(string basePath, Uri uri, string imageUrl)
        {
            try
            {
                var absoluteImageUrl = GetAbsoluteUrl(imageUrl, uri.ToString());
                if (absoluteImageUrl == null) return;

                var imageUri = new Uri(absoluteImageUrl);
                var imagePath = CreatePathForImage(basePath, imageUri);

                if (File.Exists(imagePath)) return;

                var imageBytes = await _httpClient.GetByteArrayAsync(imageUri);

                await File.WriteAllBytesAsync(imagePath, imageBytes);

                _logger.LogInformation("Downloaded image {imageUri} to {imagePath}", imageUri, imagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading image {imageUrl}", imageUrl);
            }
        }

        protected async Task SaveHtmlAsync(string basePath, Uri uri, string htmlContent)
        {
            try
            {
                string path = CreatePathForHtml(basePath, uri);
                if (!File.Exists(path))
                {
                    await File.WriteAllTextAsync(path, htmlContent);
                }
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Error while saving {basePath} {uri}", basePath, uri);
            }
        }

        protected async Task<string> TranslateHtmlAsync(string html, string toLanguage)
        {
            return await _translator.TranslateHtmlAsync(html, toLanguage);
        }

        protected string ReplaceToLocalLinks(string basePath, Uri uri, string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            ReplaceAttributeLinks(htmlDoc, "a", "href", basePath, uri);
            ReplaceAttributeLinks(htmlDoc, "img", "src", basePath, uri);

            return htmlDoc.DocumentNode.OuterHtml;
        }

        void ReplaceAttributeLinks(HtmlDocument doc, string tagName, string attribute, string basePath, Uri uri)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tagName}[@{attribute}]");
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                var urlValue = node.GetAttributeValue(attribute, string.Empty);
                var absoluteUrl = GetAbsoluteUrl(urlValue, uri.ToString());
                if (absoluteUrl != null)
                {
                    var localPath = tagName == "img"
                        ? CreatePathForImage(basePath, new Uri(absoluteUrl))
                        : CreatePathForHtml(basePath, new Uri(absoluteUrl));
                    node.SetAttributeValue(attribute, localPath);
                }
            }
        }


        protected List<string> RetrieveImageLinks(string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            return htmlDoc.DocumentNode.SelectNodes("//img[@src]")?
                .Select(node => node.GetAttributeValue("src", string.Empty))
                .Where(src => !string.IsNullOrEmpty(src))
                .ToList() ?? new List<string>();
        }

        protected List<string> RetrieveLinksFromHtml(string html)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            return htmlDoc.DocumentNode.SelectNodes("//a[@href]")?
                .Select(node => node.GetAttributeValue("href", string.Empty))
                .Where(href => !string.IsNullOrEmpty(href))
                .ToList() ?? new List<string>();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}


