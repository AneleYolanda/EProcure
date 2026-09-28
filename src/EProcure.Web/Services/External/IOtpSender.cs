namespace EProcure.Web.Services.External;

/// <summary>
/// Service for sending OTP codes to phone numbers.
/// </summary>
public interface IOtpSender
{
    /// <summary>
    /// Sends an OTP code to the specified phone number.
    /// </summary>
    /// <param name="phoneNumber">The phone number in +27 format.</param>
    /// <param name="code">The OTP code to send.</param>
    Task SendAsync(string phoneNumber, string code);
}
