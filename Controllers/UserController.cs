using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class UserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        public UserController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }
        //ambil data
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            foreach (var user in users)
            {
                user.Roles = await _userManager.GetRolesAsync(user);
            }
            return View(users);
        }
        //CREATE
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateRolesAsync();
            return View(new ApplicationUser());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string fullname, string userName, string password, string role = AppRoles.Customer)
        {
            var user = new ApplicationUser
            {
                FullName = fullname,
                UserName = userName,
                EmailConfirmed = true
            };

            if (string.IsNullOrWhiteSpace(role) || !await _roleManager.RoleExistsAsync(role))
            {
                ModelState.AddModelError(string.Empty, "Role yang dipilih tidak valid.");
            }

            if (ModelState.IsValid)
            {
                var createResult = await _userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    foreach (var error in createResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
                else
                {
                    await _userManager.AddToRoleAsync(user, role);
                    return RedirectToAction("Index");
                }
            }

            await PopulateRolesAsync(role);
            return View(user);
        }
        //Update
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(p => p.Id == id);
            if (user == null)
                return NotFound();

            user.Roles = await _userManager.GetRolesAsync(user);
            await PopulateRolesAsync(user.Roles.FirstOrDefault());
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string fullName, string username, string role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            user.Roles = await _userManager.GetRolesAsync(user);

            if (string.IsNullOrWhiteSpace(role) || !await _roleManager.RoleExistsAsync(role))
            {
                ModelState.AddModelError(string.Empty, "Role yang dipilih tidak valid.");
            }

            if (ModelState.IsValid)
            {
                user.FullName = fullName;
                user.UserName = username;

                var currentRoles = await _userManager.GetRolesAsync(user);
                var removeRoles = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeRoles.Succeeded)
                {
                    foreach (var error in removeRoles.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    await PopulateRolesAsync(role);
                    user.Roles = currentRoles;
                    return View(user);
                }

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    foreach (var error in updateResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    await PopulateRolesAsync(role);
                    user.Roles = currentRoles;
                    return View(user);
                }

                var addResult = await _userManager.AddToRoleAsync(user, role);
                if (!addResult.Succeeded)
                {
                    foreach (var error in addResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    await PopulateRolesAsync(role);
                    user.Roles = currentRoles;
                    return View(user);
                }

                return RedirectToAction(nameof(Index));
            }

            await PopulateRolesAsync(role);
            return View(user);
        }
        //Delete

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(String id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var deleteResult = await _userManager.DeleteAsync(user); 
            if (!deleteResult.Succeeded)
                return BadRequest(string.Join(", ", deleteResult.Errors.Select(e => e.Description)));

            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateRolesAsync(string? selectedRole = null)
        {
            var existingRoles = await _roleManager.Roles
                .Select(r => r.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToListAsync();

            var orderedRoles = AppRoles.All
                .Where(existingRoles.Contains)
                .ToList();

            foreach (var role in existingRoles)
            {
                if (!orderedRoles.Contains(role))
                {
                    orderedRoles.Add(role);
                }
            }

            ViewBag.Roles = orderedRoles;
            ViewBag.SelectedRole = selectedRole;
        }
    }
}
