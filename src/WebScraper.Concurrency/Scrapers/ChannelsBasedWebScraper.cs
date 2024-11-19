using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency.Scrapers
{
    public class ChannelsBasedWebScraper : BaseWebScraper
    {
        private readonly List<Task> _workerTasks = new();
        private readonly ConcurrentDictionary<string, bool> _visitedUrls = new();
        private readonly Channel<HtmlScrapeJob> _htmlChannel = Channel.CreateUnbounded<HtmlScrapeJob>();
        private readonly Channel<ImageDownloadJob> _imageChannel = Channel.CreateUnbounded<ImageDownloadJob>();
        private readonly Channel<HtmlTranslationJob> _translationChannel = Channel.CreateUnbounded<HtmlTranslationJob>();
        private int _htmlJobsCount = 0;

        public ChannelsBasedWebScraper(ILogger<ChannelsBasedWebScraper> logger, IConfiguration configuration, ITranslator translator) : base(logger, configuration, translator)
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
            await _htmlChannel.Writer.WriteAsync(new HtmlScrapeJob(startUrl, basePath, 0, maxDepth, translateToLanguage, stayInDomain));  // Add the initial URL to the HTML channel

            // Wait for all tasks to complete processing
            await Task.WhenAll(_workerTasks);
        }

        private async Task ProcessHtmlJobs()
        {
            await foreach (var htmlScrapeJob in _htmlChannel.Reader.ReadAllAsync())
            {
                try
                {
                    if (htmlScrapeJob.depth > htmlScrapeJob.maxDepth) continue;  // Depth limit
                    if (!_visitedUrls.TryAdd(htmlScrapeJob.url, true)) //already processed this url
                    {
                        continue;
                    }
                    _logger.LogInformation("Processing {url}", htmlScrapeJob.url);
                    var uri = new Uri(htmlScrapeJob.url);

                    // Download HTML
                    var html = await DownloadHtmlAsync(uri);
                    if (string.IsNullOrEmpty(html)) continue;

                    // Extract links and queue them if within depth limit
                    var links = RetrieveLinksFromHtml(html)
                                    .Select(link => GetAbsoluteUrl(link, htmlScrapeJob.url))
                                    .Where(link => !string.IsNullOrEmpty(link))
                                    .Where(link => !htmlScrapeJob.stayInDomain || new Uri(link!).Host == uri.Host)
                                    .ToList();
                    Interlocked.Add(ref _htmlJobsCount, links.Count);
                    foreach (var link in links)
                    {
                        await _htmlChannel.Writer.WriteAsync(new HtmlScrapeJob(link!, htmlScrapeJob.basePath, htmlScrapeJob.depth + 1, htmlScrapeJob.maxDepth, htmlScrapeJob.translateToLanguage, htmlScrapeJob.stayInDomain));
                    }

                    // Extract images and queue them for download
                    var imageLinks = RetrieveImageLinks(html);
                    foreach (var imageUrl in imageLinks)
                    {
                        await _imageChannel.Writer.WriteAsync(new ImageDownloadJob(imageUrl, htmlScrapeJob.basePath, uri));
                    }


                    await _translationChannel.Writer.WriteAsync(new HtmlTranslationJob(html, htmlScrapeJob.basePath, uri, htmlScrapeJob.translateToLanguage));

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing {url}", htmlScrapeJob.url);
                }
                finally
                {
                    var remainingJobsCount = Interlocked.Decrement(ref _htmlJobsCount);
                    Debug.Assert(remainingJobsCount >= 0);
                    if (remainingJobsCount < 1)
                    {
                        _htmlChannel.Writer.Complete();
                        _imageChannel.Writer.Complete();
                        _translationChannel.Writer.Complete();
                    }
                }
            }
        }

        private async Task ProcessImageJobs()
        {
            await foreach (var imageDownloadJob in _imageChannel.Reader.ReadAllAsync())
            {
                if (!_visitedUrls.TryAdd(imageDownloadJob.imageUrl, true)) //already processed this image
                {
                    continue;
                }
                await DownloadImageAsync(imageDownloadJob.basePath, imageDownloadJob.pageUri, imageDownloadJob.imageUrl);
            }
        }

        private async Task ProcessTranslationJobs()
        {
            await foreach (var translationJob in _translationChannel.Reader.ReadAllAsync())
            {
                try
                {
                    var translatedHtml = string.IsNullOrEmpty(translationJob.language) ? translationJob.htmlContent : await TranslateHtmlAsync(translationJob.htmlContent, translationJob.language);
                    var localizedHtml = ReplaceToLocalLinks(translationJob.basePath, translationJob.pageUri, translatedHtml);
                    await SaveHtmlAsync(translationJob.basePath, translationJob.pageUri, localizedHtml);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error translating HTML for {uri}", translationJob.pageUri);
                }
            }
        }
    }

    public record HtmlScrapeJob(string url, string basePath, int depth, int maxDepth, string translateToLanguage, bool stayInDomain);

    internal record struct HtmlTranslationJob(string htmlContent, string basePath, Uri pageUri, string language);
    public record ImageDownloadJob(string imageUrl, string basePath, Uri pageUri);
}