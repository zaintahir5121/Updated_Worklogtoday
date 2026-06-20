using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorklogToday.Models.Domain;

namespace WorklogToday.Controllers.Api;

[Authorize]
[ApiController]
[Route("api/user")]
public class UserApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;

    public UserApiController(UserManager<ApplicationUser> users) => _users = users;

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] UserSettingsDto dto)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();

        user.EmailDigestEnabled = dto.EmailDigestEnabled;
        if (dto.HourlyRate >= 0) user.HourlyRate = dto.HourlyRate;
        if (!string.IsNullOrWhiteSpace(dto.JobTitle)) user.JobTitle = dto.JobTitle.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Company)) user.Company = dto.Company.Trim();

        await _users.UpdateAsync(user);
        return Ok(new { ok = true });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Unauthorized();
        return Ok(new
        {
            emailDigestEnabled = user.EmailDigestEnabled,
            hourlyRate = user.HourlyRate,
            jobTitle = user.JobTitle ?? "",
            company = user.Company ?? ""
        });
    }
}

public record UserSettingsDto(bool EmailDigestEnabled, decimal HourlyRate, string? JobTitle, string? Company);
