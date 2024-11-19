using Azure.AI.Translation.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using WebScraper.Concurrency.Scrapers;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Concurrency
{
    public enum ScraperType
    {
        Naive,
        AllTasks,
        BlockingCollection,
        ChannelsBased,
        TplDataflow
    }

    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Set up a service collection to use the built-in logging system
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            // Build the service provider
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var scrapersDictionary = new Dictionary<ScraperType, Type>()
            {
                { ScraperType.Naive, typeof(NaiveWebScraper) },
                { ScraperType.AllTasks, typeof(AllTasksWebScraper) },
                { ScraperType.BlockingCollection, typeof(BlockingCollectionWebScraper) },
                { ScraperType.ChannelsBased, typeof(ChannelsBasedWebScraper) },
                { ScraperType.TplDataflow, typeof(TplDataflowWebScraper) },
            };

            
            if (args.Length < 5)
            {
                Console.WriteLine("Usage: <url> <scrapers> <maxDepth> <translateToLanguage> <stayInDomain>\");");
                Console.WriteLine("Example:  https://dotnet.microsoft.com/en-us/ naive,alltasks,channelsbased 1 he true");
                Console.WriteLine($"Available Scrapers: {string.Join(',',Enum.GetNames<ScraperType>())}");
                return;
            }

            var url = args[0];
            var scrapersToRun = args[1].Split(',').Select(s => Enum.TryParse<ScraperType>(s.Trim(), true, out var scraper) ? scraper : (ScraperType?)null).Where(s => s.HasValue).Select(s => s.Value).ToList();
            int maxDepth = int.Parse(args[2]);
            string translateToLanguage = args[3];
            bool stayInDomain = bool.Parse(args[4]);

            // Run the specified scrapers
            foreach (var scraperType in scrapersToRun)
            {
                var scraper = serviceProvider.GetService(scrapersDictionary[scraperType]) as BaseWebScraper;
                if (scraper != null)
                {
                    await RunScraping(scraper, url, maxDepth: maxDepth, translateToLanguage: translateToLanguage, stayInDomain: stayInDomain);
                }
                else
                {
                    Console.WriteLine($"Unknown scraper: {scraperType}");
                }
            }

            //await RunScraping<NaiveWebScraper>(serviceProvider, "https://dotnet.microsoft.com/en-us/",  maxDepth: 1, translateToLanguage: "", stayInDomain: true);
            //await RunScraping<AllTasksWebScraper>(serviceProvider, "https://dotnet.microsoft.com/en-us/", maxDepth: 1, translateToLanguage: "he", stayInDomain: true);
            //await RunScraping<BlockingCollectionWebScraper>(serviceProvider, "https://dotnet.microsoft.com/en-us/",  maxDepth: 1, translateToLanguage: "", stayInDomain: true);
            //await RunScraping<ChannelsBasedWebScraper>(serviceProvider, "https://dotnet.microsoft.com/en-us/",  maxDepth: 1, translateToLanguage: "", stayInDomain: true);
            //await RunScraping<TplDataflowWebScraper>(serviceProvider, "https://dotnet.microsoft.com/en-us/",  maxDepth: 2, translateToLanguage: "he", stayInDomain: true);


            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task RunScraping<TScraper>(ServiceProvider serviceProvider, string url, int maxDepth, string translateToLanguage, bool stayInDomain) where TScraper : BaseWebScraper
        {
            BaseWebScraper scraper = serviceProvider.GetService<TScraper>();
            await RunScraping(scraper, url, maxDepth, translateToLanguage, stayInDomain);
        }

        private static async Task RunScraping(BaseWebScraper scraper, string url, int maxDepth, string translateToLanguage, bool stayInDomain) 
        {            
            var scraperName = scraper.GetType().Name;
            string? basePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), scraperName);
            var sw = Stopwatch.StartNew();
            await scraper?.ScrapeAsync(url, basePath, maxDepth, translateToLanguage, stayInDomain);

            Console.WriteLine($"{scraperName} Done in {sw.ElapsedMilliseconds}");
            Console.WriteLine("-----------------------------------------------------------");
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddUserSecrets<Program>()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            //services.AddSingleton<ITranslator, LibreTranslateTranslator>();
            services.AddSingleton<ITranslator, DummyTranslator>();

            // Configure logging and add the App class to DI
            services.AddLogging(configure => configure.AddConsole())
                    .Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Information);


            // Register the App class for DI
            services.AddTransient<NaiveWebScraper>();
            services.AddTransient<AllTasksWebScraper>();
            services.AddTransient<ChannelsBasedWebScraper>();
            services.AddTransient<BlockingCollectionWebScraper>();
            services.AddTransient<TplDataflowWebScraper>();
        }
    }
}


