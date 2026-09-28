using EProcure.Web.Domain;
using EProcure.Web.Services;

namespace EProcure.Web.ViewModels.Admin;

public class StaffPageViewModel
{
    public IReadOnlyList<StaffMember> People { get; set; } = Array.Empty<StaffMember>();
    public InviteStaffForm Invite { get; set; } = new();
    public bool ShowDemoMailbox { get; set; }
}

public class InviteStaffForm
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; } = AppRoles.Evaluator;
}
