using System.ComponentModel.DataAnnotations;

namespace EProcure.Web.ViewModels.Account;

public class RegisterViewModel
{
    [Required]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [MinLength(10, ErrorMessage = "Password must be at least 10 characters long")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "The password and confirmation do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "You must accept the POPIA processing notice to register.")]
    [Display(Name = "I agree to the POPIA processing notice")]
    public bool PopiaConsent { get; set; }

    [Required(ErrorMessage = "Phone number is required for verification")]
    [Display(Name = "South African cellphone number")]
    // Accepts how people actually type numbers: 0821234567, 082 123 4567 or +27 82 123 4567.
    // Spaces are removed before saving (see AccountController.Register).
    [RegularExpression(@"^\s*(0\d{2}\s?\d{3}\s?\d{4}|\+27\s?\d{2}\s?\d{3}\s?\d{4})\s*$", ErrorMessage = "Enter a valid South African cellphone number, e.g. 082 123 4567.")]
    public string PhoneNumber { get; set; } = string.Empty;
}
