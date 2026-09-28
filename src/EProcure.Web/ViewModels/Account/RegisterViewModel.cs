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
    [Display(Name = "Cellphone number")]
    public string Cellphone { get; set; } = string.Empty;

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
}
