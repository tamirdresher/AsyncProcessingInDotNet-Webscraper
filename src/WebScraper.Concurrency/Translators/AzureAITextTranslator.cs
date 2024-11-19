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
    class AzureAITextTranslator : ITranslator
    {
        private readonly TextTranslationClient _textTranslationClient;
        private readonly IConfiguration _configuration;

        public AzureAITextTranslator(IConfiguration configuration)
        {
            _configuration = configuration;
            var subscriptionKey = _configuration["AzureTranslationService:SubscriptionKey"];
            var region = _configuration["AzureTranslationService:Region"];
            _textTranslationClient = new(new Azure.AzureKeyCredential(subscriptionKey), region: region); ;
        }
        public async Task<string> TranslateHtmlAsync(string html, string toLanguage)
        {
            return await TranslateHtmlContentAsync(html, toLanguage);

        }

        protected async Task<string> TranslateHtmlContentAsync(string html, string toLanguage)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var textNodes = new List<HtmlTextNode>();
            CollectTextNodes(htmlDoc.DocumentNode, textNodes);
            var textsToTranslate = textNodes.Select(node => node.Text).ToList();
            var translatedTexts = await TranslateTextsAsync(textsToTranslate, toLanguage);

            for (int i = 0; i < textNodes.Count; i++)
            {
                textNodes[i].Text = translatedTexts[i];
            }

            return htmlDoc.DocumentNode.OuterHtml;
        }

        protected static void CollectTextNodes(HtmlNode node, List<HtmlTextNode> textNodes)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is HtmlTextNode textNode && !string.IsNullOrWhiteSpace(textNode.Text))
                {
                    textNodes.Add(textNode);
                }
                else
                {
                    CollectTextNodes(child, textNodes);
                }
            }
        }

        protected async Task<List<string>> TranslateTextsAsync(List<string> textsToTranslate, string toLanguage)
        {
            var response = await _textTranslationClient.TranslateAsync(targetLanguage: toLanguage, content: textsToTranslate);
            return response.Value.Select(translation => translation.Translations.First().Text).ToList();
        }

    }

    
}
