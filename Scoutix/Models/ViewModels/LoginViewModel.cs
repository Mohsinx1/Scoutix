using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System.ComponentModel.DataAnnotations;

namespace Scoutix.Models.ViewModels
{
    public class LoginViewModel
    {
        private string _email;

        [Required]
        [EmailAddress]
        public string Email
        {
            get => _email;
            set => _email = value?.ToLower().Trim();
        }

        [Required]
        public string Password { get; set; }

        public bool RememberMe { get; set; }
    }
}
