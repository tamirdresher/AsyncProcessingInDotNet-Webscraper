using System;

var builder = DistributedApplication.CreateBuilder(args);

var isBenchmark = false;

var infinteWebSite = builder.AddProject<Projects.InfiniteDepthWebSite>("infinitedepthwebsite");

if (isBenchmark)
{
    builder.AddProject<Projects.WebScraper_Benchmark>("webscraper-benchmark")
         .WaitFor(infinteWebSite);
}
else
{
    var url = "https://books.toscrape.com/";//"https://dotnet.microsoft.com/en-us/";//https://localhost:7060/home?links=3
    var scrapers = "TplDataflow";//new string[] { "Naive", "AllTasks", "BlockingCollection", "ChannelsBased", "TplDataflow" };
    int maxDepth = 1;
    string translateToLanguage = "he";
    bool stayInDomain = true;

    builder.AddProject<Projects.WebScraper_Concurrency>("webscraper-concurrency")
        .WaitFor(infinteWebSite)
        .WithArgs(url, scrapers, maxDepth.ToString(), translateToLanguage, stayInDomain.ToString());
}

builder.Build().Run();
