using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebScraper.Concurrency.Scrapers;
using WebScraper.Concurrency.Translators;

namespace WebScraper.Benchmark
{
    public class WebScraperBenchmark
    {
        private readonly IServiceProvider _serviceProvider;
        private string _basePath;

        [Params( "https://books.toscrape.com/")]//"https://localhost:7060/home?links=3" ,"https://dotnet.microsoft.com/en-us/", "https://books.toscrape.com/", "https://quotes.toscrape.com/")]
        public string Url { get; set; } = "";

        public WebScraperBenchmark()
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();
            _basePath = Path.Combine(@"c:\temp\", "BenchmarkResults");

        }

        private void ConfigureServices(ServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<ITranslator, DummyTranslator>();

            // Configure logging and add the App class to DI
            services.AddLogging(configure => configure.AddConsole())
                    .Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Error);

            // Register the web scrapers for DI
            services.AddTransient<NaiveWebScraper>();
            services.AddTransient<AllTasksWebScraper>();
            services.AddTransient<ChannelsBasedWebScraper>();
            services.AddTransient<BlockingCollectionWebScraper>();
            services.AddTransient<TplDataflowWebScraper>();
        }

        //[GlobalSetup]
        //public void Cleanup()
        //{
        //    if (Directory.Exists(_basePath))
        //    {
        //        Directory.Delete(_basePath, true);
        //    }
        //}

        [Benchmark]
        public async Task TplDataflowWebScraperBenchmark()
        {
            await RunScraping<TplDataflowWebScraper>();
        }

        [Benchmark]
        public async Task AllTasksWebScraperBenchmark()
        {
            await RunScraping<AllTasksWebScraper>();
        }

        [Benchmark]
        public async Task ChannelsBasedWebScraperBenchmark()
        {
            await RunScraping<ChannelsBasedWebScraper>();
        }

        [Benchmark]
        public async Task BlockingCollectionWebScraperBenchmark()
        {
            await RunScraping<BlockingCollectionWebScraper>();
        }       

        [Benchmark]
        public async Task NaiveWebScraperBenchmark()
        {
            await RunScraping<NaiveWebScraper>();
        }

        private async Task RunScraping<TScraper>() where TScraper : BaseWebScraper
        {
            var scraper = _serviceProvider.GetService<TScraper>();
            string basePath = Path.Combine(_basePath, new Uri(Url).Host, scraper!.GetType().Name, Path.GetRandomFileName());
            await scraper.ScrapeAsync(Url, basePath, maxDepth: 7, translateToLanguage: "he", stayInDomain: true);
        }
    }

    internal class Program
    {
        public class FastConfig : ManualConfig
        {
            public FastConfig()
            {
                AddColumnProvider(DefaultColumnProviders.Instance);
                AddColumn(new FullUrlColumn());
                AddColumn(RankColumn.Stars);
                AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
                AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.Default);
                AddExporter(BenchmarkDotNet.Exporters.Csv.CsvExporter.Default);
                AddJob(Job.ShortRun
                    .WithLaunchCount(1)
                    .WithWarmupCount(1)
                    .WithIterationCount(3));
                AddDiagnoser(BenchmarkDotNet.Diagnosers.ThreadingDiagnoser.Default);
                AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);

            }
        }
        static void Main(string[] args)
        {
            //For Debugging purposes, uncomment this line
            //BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new DebugInProcessConfig());

            var summary = BenchmarkRunner.Run<WebScraperBenchmark>(new FastConfig());
            Console.WriteLine(summary);
        }
    }



    public class FullUrlColumn : IColumn
    {
        public string Id => nameof(FullUrlColumn);
        public string ColumnName => "Full URL";

        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => false;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => "Displays the full URL";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            // Assuming the URL is stored in the Parameters of the benchmark case
            return benchmarkCase.Parameters["Url"]?.ToString() ?? string.Empty;
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            return GetValue(summary, benchmarkCase);
        }

        public bool IsAvailable(Summary summary)
        {
            return true;
        }

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase)
        {
            return false;
        }

        public override string ToString() => ColumnName;
    }

}
