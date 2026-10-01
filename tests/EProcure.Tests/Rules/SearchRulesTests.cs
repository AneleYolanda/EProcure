using EProcure.Web.Domain.Enums;
using EProcure.Web.Services;
using EProcure.Web.ViewModels.Admin;

namespace EProcure.Tests.Rules;

/// <summary>
/// Search on received applications. A sealed bid's company is hidden before closing, so a search for a company
/// name must not find it either: otherwise staff could learn who has bid by searching.
/// </summary>
public class SearchRulesTests
{
    private static ReceivedApplicationRow Row(int id, string company, string tenderRef, string title, DateTime? sealedUntil = null) =>
        new(id, $"APP-{id}", company, BbbeeLevel.Level1, tenderRef, title, SubmissionStatus.Submitted, DateTime.UtcNow, 0, 3, sealedUntil);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_search_returns_everything(string? q)
    {
        var rows = new[] { Row(1, "Siyakha Builders", "T-01", "Clinic road"), Row(2, "Khanya Electrical", "T-02", "Street lights") };
        Assert.Equal(2, SearchRules.FilterApplications(rows, q).Count);
        Assert.Null(SearchRules.Term(q));
    }

    [Fact]
    public void Search_is_case_insensitive_and_trimmed()
    {
        var rows = new[] { Row(1, "Siyakha Builders", "T-01", "Clinic road"), Row(2, "Khanya Electrical", "T-02", "Street lights") };
        var found = SearchRules.FilterApplications(rows, "  street LIGHTS ");
        Assert.Equal(2, Assert.Single(found).Id);
    }

    [Fact]
    public void Open_bids_are_found_by_company_name()
    {
        var rows = new[] { Row(1, "Siyakha Builders", "T-01", "Clinic road") };
        Assert.Single(SearchRules.FilterApplications(rows, "siyakha"));
    }

    [Fact]
    public void Sealed_bids_are_never_found_by_company_name()
    {
        var closing = DateTime.UtcNow.AddDays(5);
        var rows = new[] { Row(1, "Siyakha Builders", "T-01", "Clinic road", closing) };
        Assert.Empty(SearchRules.FilterApplications(rows, "Siyakha"));
        // ...but staff can still find it by its reference or its tender.
        Assert.Single(SearchRules.FilterApplications(rows, "APP-1"));
        Assert.Single(SearchRules.FilterApplications(rows, "clinic"));
    }
}
