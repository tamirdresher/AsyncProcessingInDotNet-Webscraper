using Azure.AI.Translation.Document;
using Azure;
using Azure.AI.Translation.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using Azure.Core;

namespace WebScraper.Concurrency.Translators
{
    class AzureAIDocumentTranslator : ITranslator
    {
        private readonly IConfiguration _configuration;
        private SingleDocumentTranslationClient _documentTranslationClient;

        public AzureAIDocumentTranslator(IConfiguration configuration)
        {
            _configuration = configuration;
            var subscriptionKey = _configuration["AzureTranslationService:SubscriptionKey"];
            var endpoint = _configuration["AzureTranslationService:documentendpoint"];
            _documentTranslationClient = new SingleDocumentTranslationClient(new Uri(endpoint), new AzureKeyCredential(subscriptionKey));
        }
        public async Task<string> TranslateHtmlAsync(string html, string toLanguage)
        {
            var translateContent = new DocumentTranslateContent(new MultipartFormFileData("html", new MemoryStream(Encoding.UTF8.GetBytes(html)), "text/html"));
            var result = await _documentTranslationClient.TranslateAsync(toLanguage, translateContent);
            var translated = result.Value.ToString();

            return translated;
        }
    }
}
