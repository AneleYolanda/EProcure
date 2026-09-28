using System.ComponentModel.DataAnnotations;

namespace EProcure.Web.ViewModels.Account;

public class VerifyPhoneViewModel
{
    [Required(ErrorMessage = "Please enter the verification code")]
    [Display(Name = "Verification code")]
    public string Code { get; set; } = string.Empty;
}
