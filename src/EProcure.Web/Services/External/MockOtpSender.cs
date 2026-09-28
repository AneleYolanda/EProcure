using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EProcure.Web.Services.External;

/// <summary>
/// Development/demo stand-in for an SMS provider (Twilio, SMSPortal, ... later).
/// Nothing is sent: the code is written to the log and put in TempData["DemoOtp"] so the
/// Verify phone page can show it in a clearly marked DEMO MODE box.
///
/// TempData (a short-lived, encrypted cookie) is used instead of Session because Session is
/// not switched on in this app; reading HttpContext.Session would throw.
/// </summary>
public class MockOtpSender : IOtpSender
{
    public const string TempDataKey = "DemoOtp";

    private readonly ILogger<MockOtpSender> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public MockOtpSender(
        ILogger<MockOtpSender> logger,
        IHttpContextAccessor httpContextAccessor,
        ITempDataDictionaryFactory tempDataFactory)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tempDataFactory = tempDataFactory;
    }

    public Task SendAsync(string phoneNumber, string code)
    {
        _logger.LogInformation("DEMO MODE: OTP code {Code} generated for phone {Phone}", code, phoneNumber);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var tempData = _tempDataFactory.GetTempData(httpContext);
            tempData[TempDataKey] = code;
            tempData.Save(); // make sure it survives the redirect to the Verify phone page
        }

        return Task.CompletedTask;
    }
}
