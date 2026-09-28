using EProcure.Tests.Support;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EProcure.Tests.Services;

/// <summary>
/// The supplier's application journey, through the real ApplicationService on a real database:
/// hard stop, one application per company, closing date, PDF-only uploads, declaration, payment,
/// and "the organisation only sees it once submitted".
/// </summary>
public sealed class ApplicationServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly EProcureDbContext _context;
    private readonly InMemoryFileStorage _files = new();
    private readonly MockPaymentGateway _gateway = new();
    private readonly MockEmailSender _mail = new();
    private readonly ApplicationService _service;
    private readonly CancellationToken _ct = CancellationToken.None;

    public ApplicationServiceTests()
    {
        _context = _db.Marketplace(); // suppliers are not organisation-scoped
        _service = new ApplicationService(_context, new AuditService(_context, new HttpContextAccessor()),
            _files, _gateway, Options.Create(new UploadOptions()), Notifications.Into(_mail, _context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _db.Dispose();
    }

    // ------------------------------------------------------------------ starting

    [Fact]
    public async Task Company_below_the_minimum_level_is_stopped_and_nothing_is_created()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level5);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), minimum: BbbeeLevel.Level4);

        var result = await _service.StartAsync(tender, user, _ct);

        Assert.Equal(StartOutcome.NotEligible, result.Outcome);
        Assert.Contains("B-BBEE Level 4 or better", result.Reason);
        Assert.Equal(0, await _context.Submissions.CountAsync());
        Assert.True(await _context.AuditEntries.AnyAsync(a => a.Action == "Eligibility.Blocked"));
    }

    [Fact]
    public async Task Company_at_the_minimum_level_can_start()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level4);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), minimum: BbbeeLevel.Level4);

        Assert.Equal(StartOutcome.Started, (await _service.StartAsync(tender, user, _ct)).Outcome);
    }

    [Fact]
    public async Task Tender_past_its_closing_date_cannot_be_applied_for()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(-1));

        Assert.Equal(StartOutcome.Closed, (await _service.StartAsync(tender, user, _ct)).Outcome);
    }

    [Theory]
    [InlineData(TenderStatus.Draft)]
    [InlineData(TenderStatus.Cancelled)]
    public async Task Unpublished_or_cancelled_tender_is_not_found(TenderStatus status)
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), status: status);

        Assert.Equal(StartOutcome.TenderNotFound, (await _service.StartAsync(tender, user, _ct)).Outcome);
    }

    [Fact]
    public async Task Supplier_without_a_company_is_sent_to_create_one()
    {
        var user = _db.AddUser();
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7));

        Assert.Equal(StartOutcome.NoCompany, (await _service.StartAsync(tender, user, _ct)).Outcome);
    }

    // ------------------------------------------------------------------ duplicates

    [Fact]
    public async Task Applying_again_reopens_the_same_application_instead_of_a_duplicate()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7));

        var first = await _service.StartAsync(tender, user, _ct);
        var second = await _service.StartAsync(tender, user, _ct);

        Assert.Equal(StartOutcome.Existing, second.Outcome);
        Assert.Equal(first.SubmissionId, second.SubmissionId);
        Assert.Equal(1, await _context.Submissions.CountAsync());
    }

    [Fact]
    public async Task Database_refuses_a_second_application_from_the_same_company()
    {
        var (user, company) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7));
        _db.AddSubmission(tender, company, user, SubmissionStatus.Draft);

        // Bypasses the service on purpose: the unique index is the last line of defence (two clicks at once).
        await using var direct = _db.Marketplace();
        direct.Submissions.Add(new Submission
        {
            TenderId = tender, CompanyId = company, SubmittedByUserId = user, CreatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ ownership and locking

    [Fact]
    public async Task Another_supplier_cannot_load_the_application()
    {
        var (owner, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var (other, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7));
        var id = (await _service.StartAsync(tender, owner, _ct)).SubmissionId;

        Assert.NotNull(await _service.LoadAsync(id, owner, _ct));
        Assert.Null(await _service.LoadAsync(id, other, _ct));
    }

    [Fact]
    public async Task Nothing_can_be_changed_after_the_closing_date()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7));
        var submission = await StartAndLoad(tender, user);

        // The deadline passes while the supplier is still busy.
        submission.Tender.ClosingDateUtc = DateTime.UtcNow.AddMinutes(-1);

        Assert.Contains("closed", _service.CheckCanChange(submission, DateTime.UtcNow));
        var upload = await _service.UploadAsync(submission, submission.Tender.Requirements.First().Id, Upload.Pdf(), user, _ct);
        Assert.False(upload.Succeeded);
        Assert.Empty(_files.Files);
        Assert.False((await _service.SubmitAsync(submission, declared: true, user, _ct)).Succeeded);
    }

    [Fact]
    public async Task Company_that_drops_below_the_minimum_after_starting_is_stopped_at_every_step()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level2);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), minimum: BbbeeLevel.Level3);
        var submission = await StartAndLoad(tender, user);

        submission.Company.BbbeeLevel = BbbeeLevel.Level6; // new certificate captured on the profile

        Assert.Contains("or better", _service.CheckCanChange(submission, DateTime.UtcNow));
        Assert.False((await _service.SaveComplianceAsync(submission, true, true, _ct)).Succeeded);
    }

    // ------------------------------------------------------------------ uploads

    [Fact]
    public async Task A_text_file_renamed_to_pdf_is_rejected_and_not_stored()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);
        var fake = Upload.File("tax.pdf", "application/pdf", "just some text");

        var result = await _service.UploadAsync(submission, submission.Tender.Requirements.First().Id, fake, user, _ct);

        Assert.False(result.Succeeded);
        Assert.Contains("not a valid PDF", result.Errors.Single().Message);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task A_word_document_is_rejected()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);
        var word = Upload.File("quote.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK");

        var result = await _service.UploadAsync(submission, submission.Tender.Requirements.First().Id, word, user, _ct);

        Assert.Contains("Only PDF files", result.Errors.Single().Message);
    }

    [Fact]
    public async Task A_valid_pdf_is_stored_with_its_fingerprint()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);

        var result = await _service.UploadAsync(submission, submission.Tender.Requirements.First().Id, Upload.Pdf("CIPC.pdf"), user, _ct);

        Assert.True(result.Succeeded);
        var document = Assert.Single(submission.Documents);
        Assert.Equal("CIPC.pdf", document.OriginalFileName);
        Assert.Equal(64, document.Sha256.Length);
        Assert.True(_files.Files.ContainsKey(document.StorageKey));
    }

    [Fact]
    public async Task Uploading_again_for_the_same_item_replaces_the_earlier_file()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);
        var item = submission.Tender.Requirements.First().Id;

        await _service.UploadAsync(submission, item, Upload.Pdf("old.pdf"), user, _ct);
        await _service.UploadAsync(submission, item, Upload.Pdf("new.pdf"), user, _ct);

        Assert.Equal("new.pdf", Assert.Single(await _context.UploadedDocuments.ToListAsync()).OriginalFileName);
        Assert.Single(_files.Files);
    }

    [Fact]
    public async Task A_checklist_item_from_another_tender_is_refused()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);
        var otherTender = _db.AddTender(TestDb.Mvlm, TestDb.InDays(7));
        var foreignItem = await _context.TenderRequirements.Where(r => r.TenderId == otherTender).Select(r => r.Id).FirstAsync();

        var result = await _service.UploadAsync(submission, foreignItem, Upload.Pdf(), user, _ct);

        Assert.Contains("not on this tender's checklist", result.Errors.Single().Message);
    }

    // ------------------------------------------------------------------ declarations and submitting

    [Fact]
    public async Task Declaring_an_interest_requires_the_details()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);

        var result = await _service.SaveDeclarationsAsync(submission, hasInterest: true, interestDetails: " ",
            onRestrictedList: false, restrictionDetails: null, independentBid: true, _ct);

        Assert.Equal("InterestDetails", result.Errors.Single().Field);
        Assert.Null(submission.HasDeclaredInterest);
    }

    [Fact]
    public async Task Cannot_submit_with_a_required_document_missing()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await StartAndLoad(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), requirements: 2), user);
        await AnswerQuestions(submission);
        await _service.UploadAsync(submission, submission.Tender.Requirements.First().Id, Upload.Pdf(), user, _ct);

        var result = await _service.SubmitAsync(submission, declared: true, user, _ct);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Message == "Upload: Document 2 (step 4).");
        Assert.Equal(SubmissionStatus.Draft, submission.Status);
    }

    [Fact]
    public async Task Cannot_submit_without_ticking_the_declaration()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7)), user);

        var result = await _service.SubmitAsync(submission, declared: false, user, _ct);

        Assert.Equal("Declared", result.Errors.Single().Field);
    }

    [Fact]
    public async Task Free_tender_is_submitted_straight_away_and_the_organisation_can_see_it()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m), user);

        Assert.True((await _service.SubmitAsync(submission, declared: true, user, _ct)).Succeeded);

        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal(PaymentStatus.NotRequired, submission.PaymentStatus);
        Assert.StartsWith("EP-", submission.ReferenceNumber);
        Assert.True(await VisibleToOrganisation(submission.Id, TestDb.Rbidz));
    }

    // ------------------------------------------------------------------ payment

    [Fact]
    public async Task Paid_tender_stays_hidden_from_the_organisation_until_the_payment_is_verified()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m), user);

        await _service.SubmitAsync(submission, declared: true, user, _ct);
        Assert.Equal(SubmissionStatus.AwaitingPayment, submission.Status);
        Assert.False(await VisibleToOrganisation(submission.Id, TestDb.Rbidz));

        var (_, redirect) = await _service.StartPaymentAsync(submission, "card", "/return", _ct);
        Assert.StartsWith("/mock-gateway/", redirect);
        _gateway.Complete(submission.PaymentReference!, succeeded: true);

        var paid = await _service.CompletePaymentAsync(submission, submission.PaymentReference!, user, _ct);

        Assert.True(paid.Succeeded);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal(500m, submission.AmountPaid);
        Assert.True(await VisibleToOrganisation(submission.Id, TestDb.Rbidz));
        Assert.False(await VisibleToOrganisation(submission.Id, TestDb.Mvlm));
    }

    [Fact]
    public async Task A_made_up_payment_reference_is_refused()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m), user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        await _service.StartPaymentAsync(submission, "card", "/return", _ct);

        var result = await _service.CompletePaymentAsync(submission, "MOCK-FORGED00000", user, _ct);

        Assert.Contains("does not belong", result.Errors.Single().Message);
        Assert.Equal(SubmissionStatus.AwaitingPayment, submission.Status);
    }

    [Fact]
    public async Task Returning_from_the_gateway_is_not_enough_the_provider_must_confirm()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m), user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        await _service.StartPaymentAsync(submission, "card", "/return", _ct);

        // The browser comes back with the right reference, but the provider has not confirmed payment.
        var result = await _service.CompletePaymentAsync(submission, submission.PaymentReference!, user, _ct);

        Assert.False(result.Succeeded);
        Assert.Equal(SubmissionStatus.AwaitingPayment, submission.Status);
        Assert.False(await VisibleToOrganisation(submission.Id, TestDb.Rbidz));
    }

    [Fact]
    public async Task Failed_payment_keeps_the_application_unsubmitted()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m), user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        await _service.StartPaymentAsync(submission, "card", "/return", _ct);
        _gateway.Complete(submission.PaymentReference!, succeeded: false);

        var result = await _service.CompletePaymentAsync(submission, submission.PaymentReference!, user, _ct);

        Assert.Contains("did not go through", result.Errors.Single().Message);
        Assert.Equal(PaymentStatus.Failed, submission.PaymentStatus);
        Assert.Equal(SubmissionStatus.AwaitingPayment, submission.Status);
    }

    [Fact]
    public async Task A_submitted_application_is_locked()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m), user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);

        Assert.Contains("can no longer be changed", _service.CheckCanChange(submission, DateTime.UtcNow));
        Assert.False((await _service.RemoveDocumentAsync(submission, submission.Documents.First().Id, _ct)).Succeeded);
    }

    // ------------------------------------------------------------------ emails

    [Fact]
    public async Task Submitting_emails_the_bidder_a_confirmation_with_the_reference()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m), user);

        await _service.SubmitAsync(submission, declared: true, user, _ct);

        var email = Assert.Single(_mail.Sent).Message;
        Assert.Equal($"{user}@example.test", email.To);
        Assert.Contains(submission.ReferenceNumber!, email.Subject);
        Assert.Equal($"/Supplier/Applications/Details/{submission.Id}", email.LinkUrl);
    }

    [Fact]
    public async Task No_email_is_sent_while_the_fee_is_unpaid()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m), user);

        await _service.SubmitAsync(submission, declared: true, user, _ct);

        Assert.Empty(_mail.Sent);
    }

    // ------------------------------------------------------------------ withdrawal

    [Fact]
    public async Task A_submitted_bid_can_be_withdrawn_before_the_closing_date()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m), user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);

        var result = await _service.WithdrawAsync(submission, "Pricing error; will resubmit", user, _ct);

        Assert.True(result.Succeeded);
        Assert.Equal(SubmissionStatus.Withdrawn, submission.Status);
        Assert.Contains(submission.StatusHistory, h => h.Note == "Withdrawn by the bidder: Pricing error; will resubmit");
        Assert.Contains(_mail.Sent, m => m.Message.Subject.StartsWith("Bid withdrawn"));
        Assert.True(await _context.AuditEntries.AnyAsync(a => a.Action == "Submission.Withdrawn" && a.OrganisationId == TestDb.Rbidz));
    }

    [Fact]
    public async Task A_draft_or_a_closed_tender_cannot_be_withdrawn()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var submission = await ReadyToSubmit(_db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m), user);

        Assert.Contains("Only a submitted bid", _service.CheckCanWithdraw(submission, DateTime.UtcNow));

        await _service.SubmitAsync(submission, declared: true, user, _ct);
        submission.Tender.ClosingDateUtc = DateTime.UtcNow.AddMinutes(-1);

        Assert.False((await _service.WithdrawAsync(submission, null, user, _ct)).Succeeded);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
    }

    [Fact]
    public async Task A_withdrawn_bid_can_be_reopened_and_resubmitted_with_the_same_reference()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m);
        var submission = await ReadyToSubmit(tender, user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        var reference = submission.ReferenceNumber;
        await _service.WithdrawAsync(submission, null, user, _ct);

        var reopened = await _service.StartAsync(tender, user, _ct);

        Assert.Equal(StartOutcome.Existing, reopened.Outcome);
        Assert.Equal(submission.Id, reopened.SubmissionId);
        Assert.Equal(SubmissionStatus.Draft, submission.Status);
        Assert.Null(submission.DeclaredAtUtc); // must declare again
        Assert.Empty(_service.MissingItems(submission)); // answers and documents were kept

        Assert.True((await _service.SubmitAsync(submission, declared: true, user, _ct)).Succeeded);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Equal(reference, submission.ReferenceNumber);
    }

    [Fact]
    public async Task A_resubmitted_paid_bid_is_not_charged_the_tender_fee_again()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 500m);
        var submission = await ReadyToSubmit(tender, user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        await _service.StartPaymentAsync(submission, "card", "/return", _ct);
        _gateway.Complete(submission.PaymentReference!, succeeded: true);
        await _service.CompletePaymentAsync(submission, submission.PaymentReference!, user, _ct);
        await _service.WithdrawAsync(submission, null, user, _ct);
        await _service.StartAsync(tender, user, _ct);

        await _service.SubmitAsync(submission, declared: true, user, _ct);

        Assert.Equal(SubmissionStatus.Submitted, submission.Status); // straight to submitted, no second payment
        Assert.Equal(PaymentStatus.Paid, submission.PaymentStatus);
        Assert.Contains(submission.StatusHistory, h => h.Note == "Resubmitted (tender fee already paid)");
    }

    [Fact]
    public async Task A_withdrawn_bid_cannot_be_reopened_after_the_closing_date()
    {
        var (user, _) = _db.AddSupplier(BbbeeLevel.Level1);
        var tender = _db.AddTender(TestDb.Rbidz, TestDb.InDays(7), fee: 0m);
        var submission = await ReadyToSubmit(tender, user);
        await _service.SubmitAsync(submission, declared: true, user, _ct);
        await _service.WithdrawAsync(submission, null, user, _ct);
        await using (var arrange = _db.Marketplace())
        {
            (await arrange.Tenders.SingleAsync(t => t.Id == tender)).ClosingDateUtc = DateTime.UtcNow.AddMinutes(-1);
            await arrange.SaveChangesAsync();
        }

        await using var fresh = _db.Marketplace();
        var service = new ApplicationService(fresh, new AuditService(fresh, new HttpContextAccessor()), _files, _gateway,
            Options.Create(new UploadOptions()), Notifications.Into(_mail, fresh));
        Assert.Equal(StartOutcome.Closed, (await service.StartAsync(tender, user, _ct)).Outcome);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Submission> StartAndLoad(int tenderId, string userId)
    {
        var started = await _service.StartAsync(tenderId, userId, _ct);
        Assert.Equal(StartOutcome.Started, started.Outcome);
        return (await _service.LoadAsync(started.SubmissionId, userId, _ct))!;
    }

    private async Task AnswerQuestions(Submission submission)
    {
        Assert.True((await _service.SaveComplianceAsync(submission, true, true, _ct)).Succeeded);
        Assert.True((await _service.SaveDeclarationsAsync(submission, false, null, false, null, true, _ct)).Succeeded);
    }

    private async Task<Submission> ReadyToSubmit(int tenderId, string userId)
    {
        var submission = await StartAndLoad(tenderId, userId);
        await AnswerQuestions(submission);
        foreach (var requirement in submission.Tender.Requirements.ToList())
            Assert.True((await _service.UploadAsync(submission, requirement.Id, Upload.Pdf(), userId, _ct)).Succeeded);
        Assert.Empty(_service.MissingItems(submission));
        return submission;
    }

    private async Task<bool> VisibleToOrganisation(int submissionId, int organisationId)
    {
        await using var staff = _db.Context(TestTenant.Org(organisationId));
        return await staff.Submissions.AnyAsync(s => s.Id == submissionId);
    }
}
