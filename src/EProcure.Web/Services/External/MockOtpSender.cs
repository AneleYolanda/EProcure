using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EProcure.Web.Services.External;

/// <summary>
/// Mock OTP sender for development/demo mode.
/// Logs the code to ILogger and stores it in TempData["DemoOtp"] so it can be displayed on the VerifyPhone page.
/// </summary>
public class MockOtpSender : IOtpSender
{
    private readonly ILogger<MockOtpSender> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MockOtpSender(ILogger<MockOtpSender> logger, IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task SendAsync(string phoneNumber, string code)
    {
        // Log the code so developers can see it during testing.
        _logger.LogInformation("DEMO MODE: OTP code '{Code}' generated for phone {Phone}", code, phoneNumber);

        // Store in TempData so VerifyPhone can display it.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Session != null)
        {
            httpContext.Session.SetString("DemoOtp", code);
        }

        return Task.CompletedTask;
    }
}
