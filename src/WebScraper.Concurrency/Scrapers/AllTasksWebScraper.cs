using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency.Scrapers
{
    public class AllTasksWebScraper : BaseWebScraper
    {
        private readonly ConcurrentDictionary<string, bool> _visitedUrls = new();

        public AllTasksWebScraper(ILogger<AllTasksWebScraper> logger, IConfiguration configuration, ITranslator translator) : base(logger, configuration, translator)
        {
        }

        public override async Task ScrapeAsync(string startUrl, string basePath = "", int maxDepth = 5, string translateToLanguage = "he", bool stayInDomain = true)
        {
            basePath = Path.Combine(basePath, ConvertUrlToFileName(startUrl));
            await ScrapeUrlAsync(startUrl, basePath, maxDepth, translateToLanguage, 0, stayInDomain);
        }

        private async Task ScrapeUrlAsync(string url, string basePath, int maxDepth, string translateToLanguage, int currentDepth, bool stayInDomain)
        {
            if (currentDepth > maxDepth || !_visitedUrls.TryAdd(url, true))
                return;

            // Register the task to track it in the ConcurrentDictionary
            await Task.Run(async () =>
            {
                try
                {
                    var uri = new Uri(url);

                    var html = await DownloadHtmlAsync(uri);
                    if (string.IsNullOrEmpty(html)) return;


                    var translationTask = string.IsNullOrEmpty(translateToLanguage) ?
                        Task.FromResult(html) :
                        TranslateHtmlAsync(html, translateToLanguage);

                    var replaceLinksTask = translationTask
                            .ContinueWith(async translated =>
                            {
                                var translatedHtml = html;
                                if (!translated.IsCompletedSuccessfully)
                                {
                                    _logger.LogError(translated.Exception, "Error translating {url}", url);
                                }
                                else
                                {
                                    translatedHtml = translated.Result;
                                }
                                var localizedLinksHtml = ReplaceToLocalLinks(basePath, uri, translatedHtml);
                                await SaveHtmlAsync(basePath, uri, localizedLinksHtml);
                            });


                    IEnumerable<Task> scrapingTasks = [Task.CompletedTask];

                    // Start a new task for each link if within depth
                    if (currentDepth < maxDepth)
                    {
                        var links = RetrieveLinksFromHtml(html);
                        _logger.LogInformation("Found {count} links under {url}", links.Count, url);

                        scrapingTasks = links.Select(link => GetAbsoluteUrl(link, url))
                            .Where(link => !string.IsNullOrEmpty(link))
                            .Where(link => !stayInDomain || new Uri(link!).Host == uri.Host)
                            .Select(link => ScrapeUrlAsync(link!, basePath, maxDepth, translateToLanguage, currentDepth + 1, stayInDomain))
                            .ToList();

                    }

                    var imageLinks = RetrieveImageLinks(html);
                    _logger.LogInformation("Found {count} images under {url}", imageLinks.Count, url);

                    var tasks = new List<Task>
                    {
                        Task.WhenAll(scrapingTasks),
                        DownloadImagesAsync(basePath, uri, imageLinks),
                        translationTask,
                        replaceLinksTask
                    };

                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing {url}", url);
                }
            });

        }

        private async Task DownloadImagesAsync(string basePath, Uri uri, List<string> imageLinks)
        {
            var tasks = new List<Task>();
            foreach (var imageUrl in imageLinks)
            {
                if (!_visitedUrls.TryAdd(imageUrl, true)) //already processed this image
                {
                    continue;
                }

                tasks.Add(DownloadImageAsync(basePath, uri, imageUrl));
            }

            await Task.WhenAll(tasks);
        }





    }
}
