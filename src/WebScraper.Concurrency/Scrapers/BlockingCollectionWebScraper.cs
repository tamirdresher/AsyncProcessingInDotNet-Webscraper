using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency.Scrapers
{
    public class BlockingCollectionWebScraper : BaseWebScraper
    {
        private readonly List<Task> _workerTasks = new();
        private readonly ConcurrentDictionary<string, bool> _visitedUrls = new();
        private readonly BlockingCollection<(string url, string basePath, int depth, int maxDepth, string translateToLanguage)> _htmlJobs = new();
        private readonly BlockingCollection<(string imageUrl, string basePath, Uri uri)> _imageJobs = new();
        private readonly BlockingCollection<(string htmlContent, string basePath, Uri uri, string language)> _translationJobs = new();
        private int _htmlJobsCount = 0;

        public BlockingCollectionWebScraper(ILogger<BlockingCollectionWebScraper> logger, IConfiguration configuration, ITranslator translator) : base(logger, configuration, translator)
        {
            StartWorkers();
        }

        public void StartWorkers(int htmlWorkerCount = 20, int imageWorkerCount = 20, int translationWorkerCount = 20)
        {
            // Start HTML scraping workers
            for (int i = 0; i < htmlWorkerCount; i++)
            {
                _workerTasks.Add(Task.Run(() => ProcessHtmlJobs()));
            }

            // Start image download workers
            for (int i = 0; i < imageWorkerCount; i++)
            {
                _workerTasks.Add(Task.Run(() => ProcessImageJobs()));
            }

            // Start translation workers
            for (int i = 0; i < translationWorkerCount; i++)
            {
                _workerTasks.Add(Task.Run(() => ProcessTranslationJobs()));
            }
        }

        public override async Task ScrapeAsync(string startUrl, string basePath = "", int maxDepth = 5, string translateToLanguage = "he", bool stayInDomain = true)
        {
            basePath = Path.Combine(basePath, ConvertUrlToFileName(startUrl));
            _htmlJobsCount = 1;
            _htmlJobs.Add((startUrl, basePath, 0, maxDepth, translateToLanguage));  // Add the initial URL to the HTML jobs

            // Wait for all tasks to complete processing
            await Task.WhenAll(_workerTasks);
        }

        private async Task ProcessHtmlJobs()
        {
            foreach (var (url, basePath, depth, maxDepth, translateToLanguage) in _htmlJobs.GetConsumingEnumerable())
            {
                try
                {
                    if (depth > maxDepth) continue;  // Depth limit
                    if (!_visitedUrls.TryAdd(url, true)) //already processed this url
                    {
                        continue;
                    }

                    _logger.LogInformation("Processing {url}", url);
                    var uri = new Uri(url);

                    // Download HTML
                    var html = await DownloadHtmlAsync(uri);
                    if (string.IsNullOrEmpty(html)) continue;

                    // Extract links and queue them if within depth limit
                    var links = RetrieveLinksFromHtml(html)
                                .Select(link => GetAbsoluteUrl(link, url))
                                .Where(link => !string.IsNullOrEmpty(link))
                                .ToList();
                    Interlocked.Add(ref _htmlJobsCount, links.Count);
                    foreach (var link in links)
                    {
                        _htmlJobs.Add((link!, basePath, depth + 1, maxDepth, translateToLanguage));
                    }

                    // Extract images and queue them for download
                    var imageLinks = RetrieveImageLinks(html);
                    foreach (var imageUrl in imageLinks)
                    {
                        _imageJobs.Add((imageUrl, basePath, uri));
                    }


                    _translationJobs.Add((html, basePath, uri, translateToLanguage));

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing {url}", url);
                }
                finally
                {
                    var remainingJobsCount = Interlocked.Decrement(ref _htmlJobsCount);
                    Debug.Assert(remainingJobsCount >= 0);
                    if (remainingJobsCount < 1)
                    {
                        _htmlJobs.CompleteAdding();
                        _imageJobs.CompleteAdding();
                        _translationJobs.CompleteAdding();
                    }
                }
            }
        }

        private async Task ProcessImageJobs()
        {
            foreach (var (imageUrl, basePath, uri) in _imageJobs.GetConsumingEnumerable())
            {
                if (!_visitedUrls.TryAdd(imageUrl, true)) //already processed this image
                {
                    continue;
                }
                await DownloadImageAsync(basePath, uri, imageUrl);
            }
        }

        private async Task ProcessTranslationJobs()
        {
            foreach (var (htmlContent, basePath, uri, language) in _translationJobs.GetConsumingEnumerable())
            {
                try
                {
                    var translatedHtml = string.IsNullOrEmpty(language) ? htmlContent : await TranslateHtmlAsync(htmlContent, language);
                    var localizedHtml = ReplaceToLocalLinks(basePath, uri, translatedHtml);
                    await SaveHtmlAsync(basePath, uri, localizedHtml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error translating HTML for {uri}", uri);
                }
            }
        }


    }
}
