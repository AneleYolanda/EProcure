using System.ComponentModel.DataAnnotations;

namespace EProcure.Web.ViewModels.Account;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter the email address of your eProcure account.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>True after the form is sent: the same message is shown whether or not the address has an account.</summary>
    public bool Sent { get; set; }

    /// <summary>Development only: a link to the demo mailbox, because no real email is sent.</summary>
    public bool ShowDemoMailbox { get; set; }
}

/// <summary>Choosing a password from an emailed link: a password reset, or a staff invitation.</summary>
public class SetPasswordViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a password.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Use at least 10 characters.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type the password again.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // Display only.
    public bool IsInvitation { get; set; }
    public string? Greeting { get; set; }
}
