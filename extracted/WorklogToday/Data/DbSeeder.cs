using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorklogToday.Models.Domain;

namespace WorklogToday.Data;

public static class DbSeeder
{
    public const string DemoEmail = "demo@worklog.today";
    public const string DemoPassword = "Demo#2026";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync();

        var demo = await users.FindByEmailAsync(DemoEmail);
        if (demo is null)
        {
            demo = new ApplicationUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                EmailConfirmed = true,
                FullName = "Alex Rivera",
                JobTitle = "Senior Software Engineer",
                Company = "Acme Corp",
                AvatarColor = "#f59e0b"
            };
            await users.CreateAsync(demo, DemoPassword);
        }

        if (!await db.Notes.AnyAsync(n => n.UserId == demo.Id))
        {
            var now = DateTime.UtcNow;
            db.Notes.AddRange(
                new Note { UserId = demo.Id, Title = "Weekly goals", Content = "• Finish auth refactor\n• Review 3 PRs\n• Prep sprint demo", ColorHex = "#fff8c5", Labels = "todo, planning", IsPinned = true, CreatedAt = now.AddDays(-2), UpdatedAt = now.AddDays(-2) },
                new Note { UserId = demo.Id, Title = "Deploy checklist", Content = "1. Run migrations\n2. Smoke test\n3. Tag release\n4. Notify #releases", ColorHex = "#d3f9d8", Labels = "release", IsPinned = true, CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1) },
                new Note { UserId = demo.Id, Title = "Meeting notes — sync", Content = "Discussed Q3 roadmap, agreed to spike on caching layer. Follow up with infra team.", ColorHex = "#dbeafe", Labels = "meeting", CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1) },
                new Note { UserId = demo.Id, Title = "Idea", Content = "Auto-generate timesheets from notes using AI. Tag detection + hour estimation.", ColorHex = "#fbe4ff", Labels = "idea, research", CreatedAt = now.AddHours(-5), UpdatedAt = now.AddHours(-5) },
                new Note { UserId = demo.Id, Title = null, Content = "Remember to update the README before the release.", ColorHex = "#ffe8cc", Labels = "docs", CreatedAt = now.AddHours(-3), UpdatedAt = now.AddHours(-3) },
                new Note { UserId = demo.Id, Title = "Bug to investigate", Content = "Timesheet export off-by-one on week boundary. Repro on Sundays.", ColorHex = "#ffd6d6", Labels = "bug", CreatedAt = now.AddHours(-2), UpdatedAt = now.AddHours(-2) }
            );

            var today = DateTime.UtcNow.Date;
            DateTime D(int back) => today.AddDays(-back);
            db.WorkEntries.AddRange(
                new WorkEntry { UserId = demo.Id, Date = D(4), Task = "Auth refactor — token service", Project = "Platform", Category = WorkCategory.Development, Status = WorkStatus.Done, Hours = 4.5, Billable = true },
                new WorkEntry { UserId = demo.Id, Date = D(4), Task = "Team standup", Project = "Platform", Category = WorkCategory.Meeting, Status = WorkStatus.Done, Hours = 0.5 },
                new WorkEntry { UserId = demo.Id, Date = D(3), Task = "Code review (3 PRs)", Project = "Platform", Category = WorkCategory.Review, Status = WorkStatus.Done, Hours = 2 },
                new WorkEntry { UserId = demo.Id, Date = D(3), Task = "Caching spike research", Project = "Performance", Category = WorkCategory.Research, Status = WorkStatus.InProgress, Hours = 3 },
                new WorkEntry { UserId = demo.Id, Date = D(2), Task = "Fix timesheet export bug", Project = "Reporting", Category = WorkCategory.Development, Status = WorkStatus.Done, Hours = 3.5, Billable = true },
                new WorkEntry { UserId = demo.Id, Date = D(2), Task = "Sprint planning", Project = "Platform", Category = WorkCategory.Planning, Status = WorkStatus.Done, Hours = 1.5 },
                new WorkEntry { UserId = demo.Id, Date = D(1), Task = "Build AI summary feature", Project = "Reporting", Category = WorkCategory.Development, Status = WorkStatus.InProgress, Hours = 5, Billable = true },
                new WorkEntry { UserId = demo.Id, Date = D(1), Task = "Customer support escalation", Project = "Support", Category = WorkCategory.Support, Status = WorkStatus.Blocked, Hours = 1 },
                new WorkEntry { UserId = demo.Id, Date = D(0), Task = "Write release notes", Project = "Reporting", Category = WorkCategory.Documentation, Status = WorkStatus.Done, Hours = 1 },
                new WorkEntry { UserId = demo.Id, Date = D(0), Task = "Demo prep", Project = "Platform", Category = WorkCategory.Planning, Status = WorkStatus.InProgress, Hours = 2 }
            );

            await db.SaveChangesAsync();
        }
    }
}
