using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using mvc.Models;

namespace mvc.Controllers
{
    public class AccountController: Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IEmailSender<IdentityUser> _emailSender;
        public AccountController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, IEmailSender<IdentityUser> emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = model.Email, Email = model.Email };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, mvc.Data.SeedData.MemberRole);
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    HttpContext.Items["AuditDetails"] = $"Registered user {model.Email}.";
                    return RedirectToAction("Index", "Home");
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return View(model);
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    HttpContext.Items["AuditDetails"] = $"User {model.Email} logged in.";
                    return RedirectToAction("Index", "Home");
                }
                HttpContext.Items["AuditSuccess"] = false;
                HttpContext.Items["AuditDetails"] = $"Login failed for {model.Email}.";
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            }
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            HttpContext.Items["AuditDetails"] = "User logged out.";
            return RedirectToAction("Index", "Home");
        }
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            IdentityUser? user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            ProfilePageViewModel model = new ProfilePageViewModel
            {
                Profile = new ProfileViewModel
                {
                    Email = user.Email ?? string.Empty,
                    PhoneNumber = user.PhoneNumber,
                    UserName = user.UserName ?? string.Empty,
                    TwoFactorEnabled = user.TwoFactorEnabled,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                    LockoutEnabled = user.LockoutEnabled
                }
            };
            return View(model);
        }
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile([Bind(Prefix = "Profile")] ProfileViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(Profile), new ProfilePageViewModel { Profile = model, ShowEdit = true });
            }

            IdentityUser? user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            string newEmail = model.Email.Trim();
            if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                IdentityUser? existing = await _userManager.FindByEmailAsync(newEmail);
                if (existing != null && existing.Id != user.Id)
                {
                    ModelState.AddModelError(string.Empty, "Email is already in use by another account.");
                    return View(nameof(Profile), new ProfilePageViewModel { Profile = model, ShowEdit = true });
                }

                IdentityResult emailResult = await _userManager.SetEmailAsync(user, newEmail);
                if (!emailResult.Succeeded)
                {
                    AddIdentityErrors(emailResult);
                    return View(nameof(Profile), new ProfilePageViewModel { Profile = model, ShowEdit = true });
                }

                IdentityResult userNameResult = await _userManager.SetUserNameAsync(user, newEmail);
                if (!userNameResult.Succeeded)
                {
                    AddIdentityErrors(userNameResult);
                    return View(nameof(Profile), new ProfilePageViewModel { Profile = model, ShowEdit = true });
                }
            }

            IdentityResult phoneResult = await _userManager.SetPhoneNumberAsync(user, model.PhoneNumber);
            if (!phoneResult.Succeeded)
            {
                AddIdentityErrors(phoneResult);
                return View(nameof(Profile), new ProfilePageViewModel { Profile = model, ShowEdit = true });
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            HttpContext.Items["AuditDetails"] = $"Updated profile for {newEmail}.";
            TempData["ProfileSuccess"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Profile));
        }
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(Profile), new ProfilePageViewModel { ChangePassword = model });
            }

            IdentityUser? user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            IdentityResult result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View(nameof(Profile), new ProfilePageViewModel { ChangePassword = model });
            }

            await _signInManager.RefreshSignInAsync(user);
            HttpContext.Items["AuditDetails"] = "Password changed.";
            TempData["PasswordSuccess"] = "Password changed successfully.";
            return RedirectToAction(nameof(Profile));
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityUser? user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null && await _userManager.IsEmailConfirmedAsync(user))
            {
                string token = await _userManager.GeneratePasswordResetTokenAsync(user);
                string? callbackUrl = Url.Action(nameof(ResetPassword), "Account", new { userId = user.Id, token }, protocol: Request.Scheme);
                if (callbackUrl != null)
                {
                    await _emailSender.SendPasswordResetLinkAsync(user, model.Email, callbackUrl);
                }
            }

            // Always redirect to the confirmation page to avoid user enumeration.
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string userId, string token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return BadRequest();
            }

            ResetPasswordViewModel model = new ResetPasswordViewModel
            {
                UserId = userId,
                Token = token
            };
            return View(model);
        }
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityUser? user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            IdentityResult result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Succeeded)
            {
                HttpContext.Items["AuditDetails"] = $"Password reset via forgot-password flow for {model.UserId}.";
                return RedirectToAction(nameof(ResetPasswordConfirmation));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation()
        {
            return View();
        }
        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
    }
}
