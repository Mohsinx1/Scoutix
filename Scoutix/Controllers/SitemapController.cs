using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Xml;

namespace Scoutix.Controllers
{
    [AllowAnonymous]
    public class SitemapController : Controller
    {
        [Route("sitemap.xml")]
        [ResponseCache(Duration = 86400)]
        public IActionResult Index()
        {
            var baseUrl = "https://scoutix.io";

            var urls = new[]
            {
                new { Loc = baseUrl + "/",                         Priority = "1.0", ChangeFreq = "weekly"  },
                new { Loc = baseUrl + "/Account/PrivacyPolicy",    Priority = "0.3", ChangeFreq = "yearly"  },
                new { Loc = baseUrl + "/Account/TermsOfService",   Priority = "0.3", ChangeFreq = "yearly"  },
            };

            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            using (var writer = XmlWriter.Create(ms, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

                foreach (var url in urls)
                {
                    writer.WriteStartElement("url");
                    writer.WriteElementString("loc", url.Loc);
                    writer.WriteElementString("changefreq", url.ChangeFreq);
                    writer.WriteElementString("priority", url.Priority);
                    writer.WriteElementString("lastmod", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            return File(ms.ToArray(), "application/xml; charset=utf-8");
        }
    }
}
