using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
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
            var roles = await _roleManager.Roles
                .Select(r => r.Name!)
                .ToListAsync();
            ViewBag.Roles = roles;
            return View(new ApplicationUser());
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string fullname, string userName, string password, string role = "Customer")
        {
            var user = new ApplicationUser
            {
                FullName = fullname,
                UserName = userName,
                EmailConfirmed = true
            };

            if (ModelState.IsValid)
            {
                var createResult = await _userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                    return BadRequest(string.Join(", ", createResult.Errors.Select(e => e.Description)));
                if (await _roleManager.RoleExistsAsync(role))
                    await _userManager.AddToRoleAsync(user, role);
                return RedirectToAction("Index");
            }
            return View(user);
        }
        //Update
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var roles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
            ViewBag.Roles = roles;
            var user = await _userManager.Users.FirstOrDefaultAsync(p => p.Id == id);
            return View(user);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string fullName, string username, string role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (ModelState.IsValid)
            {
            
                if (user == null)
                    return NotFound();
                user.FullName = fullName;
                user.UserName = username;
                var currentRoles = await _userManager.GetRolesAsync(user);
                var removeRoles = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                var updateResult = await _userManager.UpdateAsync(user);
                var addResult = await _userManager.AddToRoleAsync(user, role);
                if (!addResult.Succeeded)
                    return BadRequest(string.Join(", ", addResult.Errors.Select(e => e.Description)));
                if (!updateResult.Succeeded)
                    return BadRequest(string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                return RedirectToAction(nameof(Index));
            }
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
    }
}
