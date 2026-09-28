using EProcure.Web.Domain.Enums;

namespace EProcure.Web.Services;

public enum EligibilityOutcome
{
    Qualifies,
    NotEligible,
    Unknown // the supplier has no company profile yet, so we cannot check
}

public record EligibilityResult(EligibilityOutcome Outcome, string Reason);

/// <summary>
/// The B-BBEE pre-qualification rule in ONE place, as a pure function (no database, easy to test).
/// A LOWER level number is BETTER (Level 1 best, NonCompliant = 9 worst), so a company qualifies
/// when its level number is less than or equal to the tender's minimum.
/// The same rule is applied to every bidder; there is no override.
/// Used now to label tender cards; Step 6 enforces it as a hard stop on every application POST.
/// </summary>
public static class EligibilityRules
{
    public static EligibilityResult CheckBbbee(BbbeeLevel? companyLevel, BbbeeLevel? tenderMinimum)
    {
        if (tenderMinimum is null)
            return new(EligibilityOutcome.Qualifies, "This tender has no minimum B-BBEE level.");

        if (companyLevel is null)
            return new(EligibilityOutcome.Unknown, "Add your company profile to check eligibility.");

        return (int)companyLevel.Value <= (int)tenderMinimum.Value
            ? new(EligibilityOutcome.Qualifies, $"Your company meets the minimum of {Describe(tenderMinimum.Value)}.")
            : new(EligibilityOutcome.NotEligible,
                $"This tender requires {Describe(tenderMinimum.Value)} or better. Your company is {Describe(companyLevel.Value)}.");
    }

    public static string Describe(BbbeeLevel level) =>
        level == BbbeeLevel.NonCompliant ? "Non-compliant" : $"B-BBEE Level {(int)level}";
}
