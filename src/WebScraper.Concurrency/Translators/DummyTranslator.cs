namespace WebScraper.Concurrency.Translators
{
    public class DummyTranslator : ITranslator
    {
        public async Task<string> TranslateHtmlAsync(string html, string toLanguage)
        {
            await Task.Delay(100);
            return html;
        }
    }
}