using EProcure.Tests.Support;
using EProcure.Web.Data;
using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EProcure.Tests.Services;

/// <summary>Four-eyes publication: when the organisation requires it, a SECOND SCM Officer approves before publishing.</summary>
public sealed class TenderApprovalTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly MockEmailSender _mail = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly string _requester;
    private readonly string _approver;

    public TenderApprovalTests()
    {
        _requester = _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        _approver = _db.AddStaff(TestDb.Rbidz, TestDb.OrgAdminRoleId);
        using var arrange = _db.Marketplace();
        arrange.Organisations.Single(o => o.Id == TestDb.Rbidz).RequireTenderApproval = true;
        arrange.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private (TenderService Service, EProcureDbContext Context) As(int organisationId)
    {
        var tenant = TestTenant.Org(organisationId);
        var context = _db.Context(tenant);
        return (new TenderService(context, tenant, new AuditService(context, new HttpContextAccessor()), Notifications.Into(_mail, context)), context);
    }

    private int Draft(double closesInDays = 14) => _db.AddTender(TestDb.Rbidz, TestDb.InDays(closesInDays), status: TenderStatus.Draft);

    [Fact]
    public async Task An_scm_officer_cannot_publish_alone_when_approval_is_required()
    {
        var draft = Draft();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        var result = await service.PublishAsync(draft, _ct);

        Assert.Contains("second SCM Officer", result.Errors.Single().Message);
        Assert.Equal(TenderStatus.Draft, (await context.Tenders.SingleAsync(t => t.Id == draft)).Status);
    }

    [Fact]
    public async Task A_second_scm_officer_approves_and_that_publishes_the_tender()
    {
        var draft = Draft();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        Assert.True((await service.RequestApprovalAsync(draft, _requester, _ct)).Succeeded);
        Assert.False((await service.ApproveAsync(draft, _requester, _ct)).Succeeded);
        Assert.True((await service.ApproveAsync(draft, _approver, _ct)).Succeeded);

        var tender = await context.Tenders.SingleAsync(t => t.Id == draft);
        Assert.Equal(TenderStatus.Published, tender.Status);
        Assert.Equal(_approver, tender.ApprovedByUserId);
        Assert.Null(tender.ApprovalRequestedAtUtc);
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Tender.Approved"));
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Tender.Published"));
    }

    [Fact]
    public async Task Nobody_approves_their_own_request()
    {
        var draft = Draft();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.RequestApprovalAsync(draft, _requester, _ct);

        var result = await service.ApproveAsync(draft, _requester, _ct);

        Assert.Contains("another SCM Officer", result.Errors.Single().Message);
        Assert.Equal(TenderStatus.Draft, (await context.Tenders.SingleAsync(t => t.Id == draft)).Status);
    }

    [Fact]
    public async Task The_request_emails_the_other_scm_officers_and_the_decision_emails_the_requester()
    {
        var draft = Draft();
        var bec = _db.AddStaff(TestDb.Rbidz, TestDb.EvaluatorRoleId);
        var otherOrg = _db.AddStaff(TestDb.Mvlm, TestDb.OrgAdminRoleId);
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        await service.RequestApprovalAsync(draft, _requester, _ct);
        var asked = _mail.Sent.Where(m => m.Message.Subject.StartsWith("Approval needed")).Select(m => m.Message.To).ToList();
        Assert.Equal(new[] { $"{_approver}@example.test" }, asked); // not the requester, not the BEC, not another organisation

        await service.ApproveAsync(draft, _approver, _ct);
        Assert.Contains(_mail.Sent, m => m.Message.To == $"{_requester}@example.test" && m.Message.Subject.StartsWith("Approved and published"));
        Assert.DoesNotContain(_mail.Sent, m => m.Message.To == $"{bec}@example.test" || m.Message.To == $"{otherOrg}@example.test");
    }

    [Fact]
    public async Task A_waiting_draft_is_locked_and_can_be_withdrawn_only_by_the_requester()
    {
        var draft = Draft();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.RequestApprovalAsync(draft, _requester, _ct);

        var edit = await service.UpdateDraftAsync(draft, new TenderFormViewModel { Title = "Changed" }, _ct);
        Assert.Contains("waiting for approval", edit.Errors.Single().Message);

        Assert.Contains("Only the SCM Officer who asked", (await service.WithdrawApprovalRequestAsync(draft, _approver, _ct)).Errors.Single().Message);
        Assert.True((await service.WithdrawApprovalRequestAsync(draft, _requester, _ct)).Succeeded);
        Assert.Null((await context.Tenders.SingleAsync(t => t.Id == draft)).ApprovalRequestedAtUtc);
    }

    [Fact]
    public async Task Sending_back_needs_a_note_which_the_requester_sees()
    {
        var draft = Draft();
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;
        await service.RequestApprovalAsync(draft, _requester, _ct);

        Assert.Equal("ReturnNote", (await service.ReturnForChangesAsync(draft, _approver, "no", _ct)).Errors.Single().Field);
        Assert.True((await service.ReturnForChangesAsync(draft, _approver, "Add the CIDB grading to the requirements.", _ct)).Succeeded);

        var tender = await context.Tenders.SingleAsync(t => t.Id == draft);
        Assert.Null(tender.ApprovalRequestedAtUtc);
        Assert.Equal("Add the CIDB grading to the requirements.", tender.ApprovalReturnNote);
        Assert.Contains(_mail.Sent, m => m.Message.Subject.StartsWith("Sent back") && m.Message.Body.Contains("CIDB grading"));
    }

    [Fact]
    public async Task A_draft_that_fails_the_publish_checks_cannot_be_submitted_for_approval()
    {
        var expired = Draft(closesInDays: -1);
        var (service, context) = As(TestDb.Rbidz);
        await using var _ = context;

        Assert.False((await service.RequestApprovalAsync(expired, _requester, _ct)).Succeeded);
        Assert.Empty(await service.ApprovalQueueAsync(_ct));
    }

    [Fact]
    public async Task The_queue_shows_the_organisations_waiting_tenders_only()
    {
        var draft = Draft();
        var (rbidz, rbidzContext) = As(TestDb.Rbidz);
        await using var _ = rbidzContext;
        await rbidz.RequestApprovalAsync(draft, _requester, _ct);
        var (mvlm, mvlmContext) = As(TestDb.Mvlm);
        await using var __ = mvlmContext;

        Assert.Equal(draft, Assert.Single(await rbidz.ApprovalQueueAsync(_ct)).TenderId);
        Assert.Empty(await mvlm.ApprovalQueueAsync(_ct));
        Assert.True((await mvlm.ApproveAsync(draft, _approver, _ct)).NotFound);
    }

    [Fact]
    public async Task Without_the_rule_scm_officers_publish_directly()
    {
        var draft = _db.AddTender(TestDb.Mvlm, TestDb.InDays(14), status: TenderStatus.Draft);
        var (service, context) = As(TestDb.Mvlm);
        await using var _ = context;

        Assert.False(await service.RequiresApprovalAsync(_ct));
        Assert.False((await service.RequestApprovalAsync(draft, _requester, _ct)).Succeeded);
        Assert.True((await service.PublishAsync(draft, _ct)).Succeeded);
    }

    [Fact]
    public async Task The_rule_is_switched_per_organisation_and_audited()
    {
        var (service, context) = As(TestDb.Mvlm);
        await using var _ = context;

        Assert.True((await service.SetApprovalRuleAsync(true, _ct)).Succeeded);

        Assert.True(await service.RequiresApprovalAsync(_ct));
        Assert.True(await context.AuditEntries.AnyAsync(a => a.Action == "Organisation.ApprovalRuleChanged" && a.OrganisationId == TestDb.Mvlm));
        await using var check = _db.Marketplace();
        Assert.True((await check.Organisations.SingleAsync(o => o.Id == TestDb.Rbidz)).RequireTenderApproval); // RBIDZ untouched
    }
}
