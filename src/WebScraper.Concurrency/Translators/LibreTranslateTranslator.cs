using System.Net.Http.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;

namespace WebScraper.Concurrency.Translators
{
    class LibreTranslateTranslator : ITranslator
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly IConfiguration _configuration;

        public LibreTranslateTranslator(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<string> TranslateHtmlAsync(string html, string toLanguage)
        {
            return await TranslateHtmlContentAsync(html, toLanguage, async (texts, toLanguage) => await TranslateTextsLibreTranslateAsync(texts, toLanguage));
        }


        protected async Task<string> TranslateHtmlContentAsync(string html, string toLanguage, Func<List<string>, string, Task<List<string>>> translateTextsFunc)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var textNodes = new List<HtmlTextNode>();
            CollectTextNodes(htmlDoc.DocumentNode, textNodes);
            var textsToTranslate = textNodes.Select(node => node.Text).ToList();
            var translatedTexts = await translateTextsFunc(textsToTranslate, toLanguage);

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

        protected async Task<List<string>> TranslateTextsLibreTranslateAsync(List<string> textsToTranslate, string toLanguage)
        {
            var libraTranslateBaseUrl = _configuration["LibreTranslate:BaseUrl"];
            var requestBody = new { q = textsToTranslate, source = "auto", target = toLanguage, format = "text" };
            var result = await _httpClient.PostAsync($"{libraTranslateBaseUrl}/translate", JsonContent.Create(requestBody));
            //var resultAsString = await result.Content.ReadAsStringAsync();
            var libreTranslateResult = await result.Content.ReadFromJsonAsync<LibreTranslateResult>();
            return libreTranslateResult.translatedText.ToList();
        }

    }

    class LibreTranslateResult
    {
        public string[] translatedText { get; set; }
    }


}