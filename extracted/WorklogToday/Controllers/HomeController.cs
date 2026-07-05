using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorklogToday.Models;

namespace WorklogToday.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    private readonly IConfiguration _config;

    public HomeController(IConfiguration config) => _config = config;

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "App");
        return View();
    }

    [Route("robots.txt")]
    public IActionResult Robots()
    {
        var baseUrl = _config["Site:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var sb = new StringBuilder();
        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /$");
        sb.AppendLine("Disallow: /app");
        sb.AppendLine("Disallow: /Account");
        sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");
        return Content(sb.ToString(), "text/plain");
    }

    [Route("sitemap.xml")]
    public IActionResult Sitemap()
    {
        var baseUrl = _config["Site:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url><loc>{baseUrl}/</loc><lastmod>{today}</lastmod><changefreq>weekly</changefreq><priority>1.0</priority></url>
  <url><loc>{baseUrl}/Account/Register</loc><lastmod>{today}</lastmod><changefreq>monthly</changefreq><priority>0.8</priority></url>
  <url><loc>{baseUrl}/Account/Login</loc><lastmod>{today}</lastmod><changefreq>monthly</changefreq><priority>0.5</priority></url>
</urlset>";
        return Content(xml, "application/xml");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
