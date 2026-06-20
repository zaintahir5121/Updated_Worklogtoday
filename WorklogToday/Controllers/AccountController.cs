using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorklogToday.Models.Domain;
using WorklogToday.Models.ViewModels;

namespace WorklogToday.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IAuthenticationSchemeProvider _schemes;

    public AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IAuthenticationSchemeProvider schemes)
    {
        _users = users;
        _signIn = signIn;
        _schemes = schemes;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (_signIn.IsSignedIn(User)) return RedirectToAction("Index", "App");
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
        if (!ModelState.IsValid) return View(model);

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return SafeRedirect(returnUrl);
        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Account temporarily locked. Try again shortly.");
        else
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Register(string? returnUrl = null)
    {
        if (_signIn.IsSignedIn(User)) return RedirectToAction("Index", "App");
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
        return View(new RegisterViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName,
            JobTitle = model.JobTitle,
            Company = model.Company,
            AvatarColor = PickColor(model.FullName)
        };
        var result = await _users.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signIn.SignInAsync(user, isPersistent: true);
            return SafeRedirect(returnUrl);
        }
        foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
        return View(model);
    }

    // ── Google OAuth ──────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
        var properties = _signIn.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Sign-in error: {remoteError}");
            ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
            return View("Login", new LoginViewModel());
        }

        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info == null) return RedirectToAction(nameof(Login));

        // Sign in if an existing external login link exists
        var result = await _signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (result.Succeeded) return SafeRedirect(returnUrl);

        // No link yet — resolve by email
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (email == null)
        {
            ModelState.AddModelError(string.Empty, "Could not retrieve email from Google.");
            ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
            return View("Login", new LoginViewModel());
        }

        var existingUser = await _users.FindByEmailAsync(email);
        if (existingUser != null)
        {
            await _users.AddLoginAsync(existingUser, info);
            await _signIn.SignInAsync(existingUser, isPersistent: true);
            return SafeRedirect(returnUrl);
        }

        // Auto-create account from Google profile
        var fullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0];
        var newUser = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            AvatarColor = PickColor(fullName)
        };
        var createResult = await _users.CreateAsync(newUser);
        if (createResult.Succeeded)
        {
            await _users.AddLoginAsync(newUser, info);
            await _signIn.SignInAsync(newUser, isPersistent: true);
            return SafeRedirect(returnUrl);
        }

        foreach (var e in createResult.Errors) ModelState.AddModelError(string.Empty, e.Description);
        ViewData["GoogleEnabled"] = await GoogleEnabledAsync();
        return View("Login", new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private IActionResult SafeRedirect(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "App");

    private async Task<bool> GoogleEnabledAsync() =>
        (await _schemes.GetAllSchemesAsync()).Any(s => s.Name == "Google");

    private static string PickColor(string key)
    {
        string[] palette = { "#f59e0b", "#ef4444", "#10b981", "#3b82f6", "#8b5cf6", "#ec4899", "#14b8a6" };
        return palette[Math.Abs(key.GetHashCode()) % palette.Length];
    }
}
