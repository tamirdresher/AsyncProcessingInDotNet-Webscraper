using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WebScraper.Concurrency.Translators
{
    public interface ITranslator
    {
        Task<string> TranslateHtmlAsync(string html, string toLanguage);
    }
}