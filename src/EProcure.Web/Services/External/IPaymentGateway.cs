namespace EProcure.Web.Services.External;

public record PaymentRequest(int SubmissionId, decimal Amount, string Description, string Method, string ReturnUrl);

public record PaymentStart(string Reference, string RedirectUrl);

public enum PaymentOutcome { Pending, Paid, Failed }

public record PaymentVerification(string Reference, PaymentOutcome Outcome, decimal AmountPaid);

/// <summary>
/// The tender-fee payment provider. The MVP uses MockPaymentGateway; PayFast or Ozow (sandbox first)
/// can replace it with no change to the application flow.
///
/// Two rules every implementation must follow:
///  1. eProcure never sees or stores card numbers: the supplier pays on the provider's own page.
///  2. The browser coming back to eProcure proves nothing. The app always asks the provider
///     (VerifyAsync) whether the payment really went through, and for how much.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Creates a payment with the provider and returns where to send the supplier.</summary>
    Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken ct);

    /// <summary>Asks the provider for the real outcome of a payment.</summary>
    Task<PaymentVerification> VerifyAsync(string reference, CancellationToken ct);
}
