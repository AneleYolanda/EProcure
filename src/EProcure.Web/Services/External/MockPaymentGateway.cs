using System.Collections.Concurrent;

namespace EProcure.Web.Services.External;

/// <summary>
/// A pretend payment provider for development and demos. No money moves and no card details are asked for.
/// It keeps its payments in memory (they vanish when the app restarts, which is fine for a mock) and shows
/// its own clearly labelled "DEMO gateway" page (MockGatewayController) where the tester chooses
/// "payment succeeded" or "payment failed". eProcure then verifies the outcome through VerifyAsync,
/// exactly as it would with a real provider.
/// Registered as a singleton so the in-memory list is shared by every request.
/// </summary>
public class MockPaymentGateway : IPaymentGateway
{
    public record MockPayment(string Reference, int SubmissionId, decimal Amount, string Description, string Method, string ReturnUrl)
    {
        public PaymentOutcome Outcome { get; set; } = PaymentOutcome.Pending;
    }

    private readonly ConcurrentDictionary<string, MockPayment> _payments = new();

    public Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken ct)
    {
        var reference = "MOCK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _payments[reference] = new MockPayment(reference, request.SubmissionId, request.Amount, request.Description, request.Method, request.ReturnUrl);
        return Task.FromResult(new PaymentStart(reference, $"/mock-gateway/{reference}"));
    }

    public Task<PaymentVerification> VerifyAsync(string reference, CancellationToken ct)
    {
        var result = _payments.TryGetValue(reference, out var payment)
            ? new PaymentVerification(reference, payment.Outcome, payment.Outcome == PaymentOutcome.Paid ? payment.Amount : 0m)
            : new PaymentVerification(reference, PaymentOutcome.Failed, 0m);
        return Task.FromResult(result);
    }

    /// <summary>Used only by the mock gateway's own page.</summary>
    public MockPayment? Find(string reference) => _payments.TryGetValue(reference, out var p) ? p : null;

    /// <summary>Used only by the mock gateway's own page: records the outcome the tester chose.</summary>
    public void Complete(string reference, bool succeeded)
    {
        if (_payments.TryGetValue(reference, out var payment) && payment.Outcome == PaymentOutcome.Pending)
            payment.Outcome = succeeded ? PaymentOutcome.Paid : PaymentOutcome.Failed;
    }
}
