using EProcure.Web.ViewModels.Admin;

namespace EProcure.Web.Services;

/// <summary>
/// Plain-text search for the lists that are filtered in memory: case-insensitive "contains" on the visible fields.
/// Sealed bids: a bid's company name is not shown before closing, so it is not searchable before closing either.
/// Otherwise a search for a company name would reveal who has bid.
/// </summary>
public static class SearchRules
{
    /// <summary>The trimmed search term, or null when there is nothing to search for.</summary>
    public static string? Term(string? q) => string.IsNullOrWhiteSpace(q) ? null : q.Trim();

    public static bool Matches(string term, params string?[] fields) =>
        fields.Any(f => f is not null && f.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>Received applications matching the search; sealed bids match on their reference and tender only.</summary>
    public static IReadOnlyList<ReceivedApplicationRow> FilterApplications(IReadOnlyList<ReceivedApplicationRow> rows, string? q)
    {
        if (Term(q) is not string term) return rows;
        return rows.Where(r => Matches(term, r.ReferenceNumber, r.TenderReference, r.TenderTitle)
                               || (r.SealedUntilUtc is null && Matches(term, r.CompanyName))).ToList();
    }
}
