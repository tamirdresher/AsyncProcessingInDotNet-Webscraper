using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebScraper.Concurrency.Scrapers;
using WebScraper.Concurrency.Translators;

public record UrlProcessingData(string Url, ScrapeContext Context, int depth, bool Complete);
public record HtmlProcessingData(string Html, string Url, int depth, ScrapeContext Context);
public record ImageProcessingData(string HtmlUrl, string ImageUrl, ScrapeContext Context);
public record TranslationData(string Html, Uri Uri, ScrapeContext Context);

public class ScrapeContext
{
    private TaskCompletionSource<bool> CompletionSource { get; }

    public string StartUrl { get; set; }
    public string BasePath { get; set; }
    public int MaxDepth { get; set; }
    public string TranslateToLanguage { get; set; }
    public bool StayInDomain { get; set; }
    public int JobsCounter;
    public Task Completion => CompletionSource.Task;

    public ScrapeContext(string startUrl, string basePath, int maxDepth, string translateToLanguage, bool stayInDomain)
    {
        StartUrl = startUrl;
        BasePath = basePath;
        MaxDepth = maxDepth;
        TranslateToLanguage = translateToLanguage;
        StayInDomain = stayInDomain;
        JobsCounter = 1;
        CompletionSource = new TaskCompletionSource<bool>();
    }



    public void NotifyCompletion()
    {
        CompletionSource.TrySetResult(true);
    }

    internal void NotifyFault(AggregateException exception)
    {
        CompletionSource.TrySetException(exception);
    }
}



public class TplDataflowWebScraper : BaseWebScraper
{
    private readonly ConcurrentDictionary<string, bool> _processedUrls = new();

    public TplDataflowWebScraper(ILogger<TplDataflowWebScraper> logger, IConfiguration configuration, ITranslator translator) : base(logger, configuration, translator) 
    { 
    }

    public override async Task ScrapeAsync(string startUrl, string basePath = "", int maxDepth = 5, string translateToLanguage = "he", bool stayInDomain = true)
    {
        var context = new ScrapeContext(startUrl, Path.Combine(basePath, ConvertUrlToFileName(startUrl)), maxDepth, translateToLanguage, stayInDomain);

        var dataflowOptions = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 20 };

        // Set up dataflow blocks
        var fetchHtmlBlock = CreateFetchHtmlBlock(dataflowOptions, context);
        var htmlBroadcaster = new BroadcastBlock<HtmlProcessingData>(x => x);
        var retrieveHtmlLinksBlock = CreateRetrieveHtmlLinksBlock(dataflowOptions, context);
        var retrieveHtmlImageLinksBlock = CreateRetrieveHtmlImageLinksBlock(dataflowOptions);
        var downloadImageBlock = CreateDownloadImageBlock(dataflowOptions);
        var translateHtmlBlock = CreateTranslateHtmlBlock(dataflowOptions, context);
        var replaceToLocalLinksBlock = CreateReplaceToLocalLinksBlock(dataflowOptions);
        var saveHtmlBlock = CreateSaveHtmlBlock(dataflowOptions);

        // Link blocks in the pipeline
        fetchHtmlBlock.LinkTo(htmlBroadcaster, new DataflowLinkOptions { PropagateCompletion = true });
        htmlBroadcaster.LinkTo(retrieveHtmlLinksBlock, new DataflowLinkOptions { PropagateCompletion = true });
        htmlBroadcaster.LinkTo(retrieveHtmlImageLinksBlock, new DataflowLinkOptions { PropagateCompletion = true }, x => !string.IsNullOrEmpty(x.Html));
        retrieveHtmlImageLinksBlock.LinkTo(downloadImageBlock, new DataflowLinkOptions { PropagateCompletion = true });
        retrieveHtmlLinksBlock.LinkTo(fetchHtmlBlock, new DataflowLinkOptions { PropagateCompletion = true });
        htmlBroadcaster.LinkTo(translateHtmlBlock, new DataflowLinkOptions { PropagateCompletion = true }, x => !string.IsNullOrEmpty(x.Html));
        translateHtmlBlock.LinkTo(replaceToLocalLinksBlock, new DataflowLinkOptions { PropagateCompletion = true });
        replaceToLocalLinksBlock.LinkTo(saveHtmlBlock, new DataflowLinkOptions { PropagateCompletion = true });

       
        // Seed the pipeline
        fetchHtmlBlock.Post(new UrlProcessingData(context.StartUrl, context, depth:0, false));
        
        await context.Completion.ConfigureAwait(false);
        fetchHtmlBlock.Complete();

        _logger.LogInformation("Links sending completed");
        await Task.WhenAll(fetchHtmlBlock.Completion, downloadImageBlock.Completion, saveHtmlBlock.Completion ).ConfigureAwait(false);
    }

    private TransformBlock<UrlProcessingData, HtmlProcessingData> CreateFetchHtmlBlock(ExecutionDataflowBlockOptions options, ScrapeContext context)
    {
        return new TransformBlock<UrlProcessingData, HtmlProcessingData>(async item =>
        {
            try
            {

                if (item.Complete)
                {
                    item.Context.NotifyCompletion();
                }
                else
                {
                    _logger.LogInformation("Fetching HTML for {url}", item.Url);

                    if (item.depth <= context.MaxDepth && _processedUrls.TryAdd(item.Url, true))
                    {
                        var html = await DownloadHtmlAsync(new Uri(item.Url));
                        return new HtmlProcessingData(html, item.Url, item.depth, context);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching html {url}", item?.Url);               
            }

            return new HtmlProcessingData("", "", -1, context);
        }, options);
    }

    private TransformManyBlock<HtmlProcessingData, UrlProcessingData> CreateRetrieveHtmlLinksBlock(ExecutionDataflowBlockOptions options, ScrapeContext context)
    {
        return new TransformManyBlock<HtmlProcessingData, UrlProcessingData>(item =>
        {
            var links = new List<UrlProcessingData>();

            try
            {
                _logger.LogInformation("Retrieving links for {url}", item?.Url);
                if (item?.depth < item?.Context?.MaxDepth && !string.IsNullOrEmpty(item.Html))
                {
                    var uri = new Uri(item.Url);
                    links = RetrieveLinksFromHtml(item.Html)
                        .Select(link => GetAbsoluteUrl(link, item.Url))
                        .Where(link => link != null)
                        .Where(link => !item.Context.StayInDomain || new Uri(link!).Host == uri.Host)
                        .Select(link => new UrlProcessingData(link!, context, item.depth + 1, false))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting links from {Url}", item?.Url);
            }

            try
            {
                _logger.LogInformation("Found {count} links under {url}", links.Count, item?.Url);
                Interlocked.Add(ref context.JobsCounter, links.Count);
                int jobCounter = Interlocked.Decrement(ref context.JobsCounter);
                _logger.LogInformation("Jobs counter {count}", jobCounter);
                if (jobCounter == 0)
                {
                    _logger.LogInformation("Reached the final url to process {url}", item?.Url);
                    links.Add(new UrlProcessingData(default, context, -1, true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Couldnt decrement the job counter");
                throw;
            }

            return links;
        }, options);
    }

    private TransformManyBlock<HtmlProcessingData, ImageProcessingData> CreateRetrieveHtmlImageLinksBlock(ExecutionDataflowBlockOptions options)
    {
        return new TransformManyBlock<HtmlProcessingData, ImageProcessingData>(item =>
        {
            return string.IsNullOrEmpty(item.Html)
                ? Enumerable.Empty<ImageProcessingData>()
                : RetrieveImageLinks(item.Html).Select(link => GetAbsoluteUrl(link, item.Url)).Select(imageUrl => new ImageProcessingData(item.Url, imageUrl, item.Context));
        }, options);
    }

    private ActionBlock<ImageProcessingData> CreateDownloadImageBlock(ExecutionDataflowBlockOptions options)
    {
        return new ActionBlock<ImageProcessingData>(async item =>
        {
            if (_processedUrls.TryAdd(item.ImageUrl, true))
            {
                await DownloadImageAsync(item.Context.BasePath, new Uri(item.HtmlUrl), item.ImageUrl);
            }
        }, options);
    }

    private TransformBlock<HtmlProcessingData, TranslationData> CreateTranslateHtmlBlock(ExecutionDataflowBlockOptions options, ScrapeContext context)
    {
        return new TransformBlock<HtmlProcessingData, TranslationData>(async item =>
        {
            var uri = new Uri(item.Url);
            var translatedHtml = string.IsNullOrEmpty(context.TranslateToLanguage) ? item.Html : await TranslateHtmlAsync(item.Html, context.TranslateToLanguage);
            return new TranslationData(translatedHtml, uri, context);
        }, options);
    }

    private TransformBlock<TranslationData, TranslationData> CreateReplaceToLocalLinksBlock(ExecutionDataflowBlockOptions options)
    {
        return new TransformBlock<TranslationData, TranslationData>(item =>
        {
            var localizedHtml = ReplaceToLocalLinks(item.Context.BasePath, item.Uri, item.Html);
            return item with { Html = localizedHtml };
        }, options);
    }

    private ActionBlock<TranslationData> CreateSaveHtmlBlock(ExecutionDataflowBlockOptions options)
    {
        return new ActionBlock<TranslationData>(async item =>
        {
            _logger.LogInformation("Saving HTML file for {uri}", item.Uri);
            await SaveHtmlAsync(item.Context.BasePath, item.Uri, item.Html);
        }, options);
    }

}

