namespace EProcure.Web.ViewModels.Account;

public class VerifyPhoneViewModel
{
    /// <summary>
    /// One digit per box. The six boxes are all named "Digits", so ASP.NET binds them to this
    /// array; that is why the page works even with JavaScript switched off.
    /// </summary>
    public string[] Digits { get; set; } = Array.Empty<string>();

    /// <summary>The code as one string, built from <see cref="Digits"/>.</summary>
    public string Code => string.Concat(Digits.Select(d => (d ?? string.Empty).Trim()));

    /// <summary>The number the code was sent to, e.g. "082 000 0004" (display only).</summary>
    public string PhoneDisplay { get; set; } = string.Empty;

    /// <summary>Only set by the mock OTP sender, so testers can see the code on screen.</summary>
    public string? DemoCode { get; set; }
}
