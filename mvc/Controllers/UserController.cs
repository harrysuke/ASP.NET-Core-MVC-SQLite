using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using mvc.Data;
using mvc.Models;

namespace mvc.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class UserController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserController(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index(string? q, string? role)
        {
            List<IdentityUser> users = await _userManager.Users
                .OrderBy(u => u.Email)
                .ToListAsync();

            List<UserListViewModel> all = new List<UserListViewModel>();
            foreach (IdentityUser user in users)
            {
                IList<string> roles = await _userManager.GetRolesAsync(user);
                all.Add(new UserListViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    EmailConfirmed = user.EmailConfirmed,
                    LockoutEnd = user.LockoutEnd?.LocalDateTime,
                    IsAdmin = roles.Contains(SeedData.AdminRole),
                    IsMember = roles.Contains(SeedData.MemberRole)
                });
            }

            // Totals describe the whole directory, not the filtered page.
            ViewData["TotalUsers"] = all.Count;
            ViewData["TotalAdmins"] = all.Count(u => u.IsAdmin);
            ViewData["TotalMembers"] = all.Count(u => u.IsMember);
            ViewData["TotalNoAccess"] = all.Count(u => !u.IsAdmin && !u.IsMember);

            IEnumerable<UserListViewModel> filtered = all;

            string? term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            if (term != null)
            {
                filtered = filtered.Where(u =>
                    u.Email.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || u.UserName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                filtered = role switch
                {
                    SeedData.AdminRole => filtered.Where(u => u.IsAdmin),
                    SeedData.MemberRole => filtered.Where(u => u.IsMember),
                    "None" => filtered.Where(u => !u.IsAdmin && !u.IsMember),
                    _ => filtered
                };
            }

            ViewData["SearchTerm"] = term;
            ViewData["RoleFilter"] = role;

            return View(filtered.ToList());
        }

        public async Task<IActionResult> Details(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            UserDetailsViewModel model = new UserDetailsViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber ?? string.Empty,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd,
                Roles = (await _userManager.GetRolesAsync(user)).ToList()
            };
            return View(model);
        }

        public IActionResult Create()
        {
            return View(new UserCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityUser? existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
                ModelState.AddModelError(string.Empty, "A user with this email already exists.");
                return View(model);
            }

            IdentityUser user = new IdentityUser { UserName = model.Email, Email = model.Email };
            IdentityResult result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return View(model);
            }

            // Grant the selected access level; anything unexpected falls back to Member.
            string role = model.Role == SeedData.AdminRole ? SeedData.AdminRole : SeedData.MemberRole;
            IdentityResult roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                TempData["UserError"] = $"User '{user.Email}' was created but the '{role}' role could not be assigned.";
                return RedirectToAction(nameof(Index));
            }

            HttpContext.Items["AuditDetails"] = $"Created user '{user.Email}' with role '{role}'.";
            TempData["UserSuccess"] = $"User '{user.Email}' created with '{role}' access.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            UserEditViewModel model = new UserEditViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityUser? user = await _userManager.FindByIdAsync(model.Id);
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
                    return View(model);
                }

                IdentityResult emailResult = await _userManager.SetEmailAsync(user, newEmail);
                if (!emailResult.Succeeded)
                {
                    AddIdentityErrors(emailResult);
                    return View(model);
                }

                IdentityResult userNameResult = await _userManager.SetUserNameAsync(user, newEmail);
                if (!userNameResult.Succeeded)
                {
                    AddIdentityErrors(userNameResult);
                    return View(model);
                }
            }

            IdentityResult phoneResult = await _userManager.SetPhoneNumberAsync(user, model.PhoneNumber);
            if (!phoneResult.Succeeded)
            {
                AddIdentityErrors(phoneResult);
                return View(model);
            }

            HttpContext.Items["AuditDetails"] = $"Admin edited user '{user.Email}' (email {user.Email}).";
            TempData["UserSuccess"] = $"User '{user.Email}' updated.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return View(new UserListViewModel { Id = user.Id, UserName = user.UserName ?? string.Empty, Email = user.Email ?? string.Empty });
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            string currentUserId = _userManager.GetUserId(User) ?? string.Empty;
            if (user.Id == currentUserId)
            {
                TempData["UserError"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Index));
            }

            if (await IsLastAdmin(user.Id))
            {
                TempData["UserError"] = "You cannot delete the last remaining admin.";
                return RedirectToAction(nameof(Index));
            }

            IdentityResult result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return RedirectToAction(nameof(Index));
            }

            HttpContext.Items["AuditDetails"] = $"Deleted user '{user.Email}'.";
            TempData["UserSuccess"] = $"User '{user.Email}' deleted.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRole(string id, string role)
        {
            if (role != SeedData.AdminRole && role != SeedData.MemberRole)
            {
                return BadRequest();
            }

            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            bool hasRole = await _userManager.IsInRoleAsync(user, role);

            if (role == SeedData.AdminRole && hasRole)
            {
                string currentUserId = _userManager.GetUserId(User) ?? string.Empty;
                if (user.Id == currentUserId)
                {
                    TempData["UserError"] = "You cannot remove the Admin role from your own account.";
                    return RedirectToAction(nameof(Index));
                }
                if (await IsLastAdmin(user.Id))
                {
                    TempData["UserError"] = "You cannot remove the Admin role from the last remaining admin.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if (hasRole)
            {
                IdentityResult result = await _userManager.RemoveFromRoleAsync(user, role);
                if (result.Succeeded)
                {
                    HttpContext.Items["AuditDetails"] = $"Removed '{role}' role from '{user.Email}'.";
                    TempData["UserSuccess"] = $"Removed the '{role}' role from '{user.Email}'.";
                }
                else
                {
                    AddIdentityErrors(result);
                }
            }
            else
            {
                IdentityResult result = await _userManager.AddToRoleAsync(user, role);
                if (result.Succeeded)
                {
                    HttpContext.Items["AuditDetails"] = $"Assigned '{role}' role to '{user.Email}'.";
                    TempData["UserSuccess"] = $"{user.Email} is now in the '{role}' role.";
                }
                else
                {
                    AddIdentityErrors(result);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLockout(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                HttpContext.Items["AuditDetails"] = $"Unlocked user '{user.Email}'.";
                TempData["UserSuccess"] = $"'{user.Email}' unlocked.";
            }
            else
            {
                // Locking is the admin-only kill switch, so guard the two ways it could
                // remove the last person able to unlock anyone.
                string currentUserId = _userManager.GetUserId(User) ?? string.Empty;
                if (user.Id == currentUserId)
                {
                    TempData["UserError"] = "You cannot lock your own account.";
                    return RedirectToAction(nameof(Index));
                }
                if (await IsLastAdmin(user.Id))
                {
                    TempData["UserError"] = "You cannot lock the last remaining admin.";
                    return RedirectToAction(nameof(Index));
                }

                // Lockout is only honoured when LockoutEnabled is on, so set both.
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                HttpContext.Items["AuditDetails"] = $"Locked user '{user.Email}'.";
                TempData["UserSuccess"] = $"'{user.Email}' locked.";
            }
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> ResetPassword(string id)
        {
            IdentityUser? user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(new UserResetPasswordViewModel { Id = user.Id, Email = user.Email ?? string.Empty });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(UserResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            IdentityUser? user = await _userManager.FindByIdAsync(model.Id);
            if (user == null)
            {
                return NotFound();
            }

            if (await _userManager.HasPasswordAsync(user))
            {
                IdentityResult removeResult = await _userManager.RemovePasswordAsync(user);
                if (!removeResult.Succeeded)
                {
                    AddIdentityErrors(removeResult);
                    return View(model);
                }
            }

            IdentityResult addResult = await _userManager.AddPasswordAsync(user, model.Password);
            if (!addResult.Succeeded)
            {
                AddIdentityErrors(addResult);
                return View(model);
            }

            HttpContext.Items["AuditDetails"] = $"Admin reset password for '{user.Email}'.";
            TempData["UserSuccess"] = $"Password for '{user.Email}' has been reset.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> IsLastAdmin(string userId)
        {
            IList<IdentityUser> admins = await _userManager.GetUsersInRoleAsync(SeedData.AdminRole);
            return admins.Count == 1 && admins[0].Id == userId;
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