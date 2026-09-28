using EProcure.Web.Services.External;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EProcure.Web.Controllers;

/// <summary>
/// The pretend payment provider's own page (development and demos only). It stands in for PayFast / Ozow:
/// the supplier is sent here to pay, chooses "succeeded" or "failed", and is sent back to eProcure, which then
/// VERIFIES the outcome with the gateway. No card or bank details are ever asked for.
/// Returns 404 unless the configured payment provider is "Mock".
/// </summary>
[Authorize(Policy = "SupplierOnly")]
public class MockGatewayController : Controller
{
    private readonly MockPaymentGateway? _gateway;

    public MockGatewayController(IServiceProvider services)
    {
        _gateway = services.GetService<MockPaymentGateway>(); // NULL when a real provider is configured
    }

    [HttpGet("mock-gateway/{reference}")]
    public IActionResult Index(string reference)
    {
        var payment = _gateway?.Find(reference);
        return payment is null ? NotFound() : View(payment);
    }

    [HttpPost("mock-gateway/{reference}/complete")]
    public IActionResult Complete(string reference, bool succeeded)
    {
        var payment = _gateway?.Find(reference);
        if (payment is null) return NotFound();

        _gateway!.Complete(reference, succeeded);
        // Send the supplier back to eProcure (always a local address), like a real provider's return URL.
        return LocalRedirect($"{payment.ReturnUrl}?reference={Uri.EscapeDataString(reference)}");
    }
}
