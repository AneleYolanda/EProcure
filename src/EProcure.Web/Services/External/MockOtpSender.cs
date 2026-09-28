using Microsoft.Extensions.Logging;

namespace EProcure.Web.Services.External
{
    public class MockOtpSender : IOtpSender
    {
        private readonly ILogger<MockOtpSender> _logger;
        public MockOtpSender(ILogger<MockOtpSender> logger)
        {
            _logger = logger;
        }

        public System.Threading.Tasks.Task SendOtpAsync(string phoneNumber, string code)
        {
            _logger.LogInformation("DEMO MODE: OTP for {phone} is {code}", phoneNumber, code);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
