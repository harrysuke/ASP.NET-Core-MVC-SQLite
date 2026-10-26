using System.ComponentModel.DataAnnotations;

namespace mvc.Models
{
    public class UserListViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public DateTime? LockoutEnd { get; set; }
        public bool IsLockedOut => LockoutEnd.HasValue && LockoutEnd.Value > DateTimeOffset.UtcNow;
        public bool IsAdmin { get; set; }
        public bool IsMember { get; set; }
    }

    public class UserDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public bool PhoneNumberConfirmed { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public bool LockoutEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public List<string> Roles { get; set; } = new();
    }

    public class UserCreateViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>
        /// Access level granted on creation: <c>Admin</c> or <c>Member</c>.
        /// Defaults to <c>Member</c> so a new account can at least use the catalog.
        /// </summary>
        [Required]
        [Display(Name = "Access level")]
        public string Role { get; set; } = "Member";
    }

    public class UserEditViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }

    public class UserResetPasswordViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}