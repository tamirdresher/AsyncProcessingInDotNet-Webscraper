using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency.Scrapers
{
    public class NaiveWebScraper : BaseWebScraper
    {
        private readonly ConcurrentDictionary<string, bool> _visitedUrls = new();

        public NaiveWebScraper(ILogger<NaiveWebScraper> logger, IConfiguration configuration, ITranslator translator) : base(logger, configuration, translator)
        {
        }
        public override async Task ScrapeAsync(string startUrl, string basePath = "", int maxDepth = 5, string translateToLanguage = "he", bool stayInDomain = true)
        {
            Queue<(int depth, string url)> urlQueue = new();
            urlQueue.Enqueue((depth: 0, startUrl));

            basePath = Path.Combine(basePath, ConvertUrlToFileName(startUrl));
            while (urlQueue.TryDequeue(out var qItem))
            {
                var (depth, url) = qItem;
                if (string.IsNullOrEmpty(url)) continue;

                try
                {
                    if (!_visitedUrls.TryAdd(url, true)) //already processed this url
                    {
                        continue;
                    }

                    _logger.LogInformation("Processing {url}", url);
                    var uri = new Uri(url);

                    string html = await DownloadHtmlAsync(uri);
                    if (html == null)
                    {
                        continue;
                    }


                    if (depth < maxDepth)
                    {
                        List<string> links = RetrieveLinksFromHtml(html);
                        links = links
                            .Select(link => GetAbsoluteUrl(link, url))
                            .Where(link => !string.IsNullOrEmpty(link))
                            .Where(link => !stayInDomain || new Uri(link!).Host == uri.Host)
                            .Cast<string>()
                            .ToList();

                        _logger.LogInformation("Found {count} links under {url}", links.Count, url);
                        foreach (var link in links)
                        {
                            urlQueue.Enqueue((depth + 1, GetAbsoluteUrl(link, url) ?? ""));
                        }
                    }
                    List<string> imageLinks = RetrieveImageLinks(html);
                    _logger.LogInformation("Found {count} images under {url}", imageLinks.Count, url);

                    await DownloadImagesAsync(basePath, uri, imageLinks);
                    var translated = string.IsNullOrEmpty(translateToLanguage) ? html : await TranslateHtmlAsync(html, translateToLanguage);
                    var localizedLinksHtml = ReplaceToLocalLinks(basePath, uri, translated);

                    await SaveHtmlAsync(basePath, uri, localizedLinksHtml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing {url}", url);
                }
            }


        }



        private async Task DownloadImagesAsync(string basePath, Uri uri, List<string> imageLinks)
        {

            foreach (var imageUrl in imageLinks)
            {
                await DownloadImageAsync(basePath, uri, imageUrl);

            }
        }






    }
}


