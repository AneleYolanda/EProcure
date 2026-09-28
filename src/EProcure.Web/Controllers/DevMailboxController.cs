using EProcure.Web.Services.External;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Controllers;

/// <summary>
/// Development-only "inbox" for the mock email sender, so a demo can follow password-reset and invitation links
/// without a real mail server. It shows every email sent since the app started, to anyone, so it exists ONLY when
/// the app runs in Development AND the mock sender is configured; otherwise it is "not found".
/// </summary>
[AllowAnonymous]
[Route("dev/mailbox")]
public class DevMailboxController : Controller
{
    private readonly IEmailSender _email;
    private readonly IWebHostEnvironment _environment;

    public DevMailboxController(IEmailSender email, IWebHostEnvironment environment)
    {
        _email = email;
        _environment = environment;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_environment.IsDevelopment() || _email is not MockEmailSender mock) return NotFound();
        return View(mock.Sent);
    }

    /// <summary>Runs the scheduled emails now instead of waiting for the timer (Development only, for demos).</summary>
    [HttpPost("run-scheduled")]
    public async Task<IActionResult> RunScheduled([FromServices] Services.ReminderService reminders, CancellationToken ct)
    {
        if (!_environment.IsDevelopment() || _email is not MockEmailSender) return NotFound();
        var result = await reminders.RunAsync(DateTime.UtcNow, ct);
        TempData["Flash"] = $"Scheduled emails run: {result.ClosingReminders} closing reminder(s) and {result.ClosedNotices} tender-closed notice(s) sent. " +
                            "Each is sent only once.";
        return Redirect("/dev/mailbox");
    }
}
