using System.ComponentModel.DataAnnotations;

public class UserViewModel
{
    private string _email;

    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string UserName { get; set; }

    [Required]
    [EmailAddress]
    public string Email
    {
        get => _email;
        set => _email = value?.ToLower().Trim();
    }

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string Password { get; set; }

    [Required]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; }

    [Required]
    public bool AgreeToTerms { get; set; }

    // Replace SelectedPlan with PlanId
    [Required]
    public int PlanId { get; set; } // PlanId to store selected plan's ID

}