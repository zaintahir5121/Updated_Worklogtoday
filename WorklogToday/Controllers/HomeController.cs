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
  <url><loc>{baseUrl}/privacy</loc><lastmod>{today}</lastmod><changefreq>yearly</changefreq><priority>0.3</priority></url>
  <url><loc>{baseUrl}/terms</loc><lastmod>{today}</lastmod><changefreq>yearly</changefreq><priority>0.3</priority></url>
</urlset>";
        return Content(xml, "application/xml");
    }

    [Route("privacy")]
    public IActionResult Privacy() => View();

    [Route("terms")]
    public IActionResult Terms() => View();

    [Route("badge.svg")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Badge()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="176" height="20">
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
                <stop offset="1" stop-opacity=".1"/>
              </linearGradient>
              <clipPath id="r"><rect width="176" height="20" rx="3" fill="#fff"/></clipPath>
              <g clip-path="url(#r)">
                <rect width="100" height="20" fill="#555"/>
                <rect x="100" width="76" height="20" fill="#f59e0b"/>
                <rect width="176" height="20" fill="url(#s)"/>
              </g>
              <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
                <text x="51" y="15" fill="#010101" fill-opacity=".3">tracked with</text>
                <text x="51" y="14">tracked with</text>
                <text x="139" y="15" fill="#010101" fill-opacity=".3">worklog.today</text>
                <text x="139" y="14">worklog.today</text>
              </g>
            </svg>
            """;
        return Content(svg, "image/svg+xml");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
