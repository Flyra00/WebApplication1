using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser> _users;

        public AuthController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users)    
        {
            _signIn = signIn;
            _users = users;
        }

        [HttpGet("/Auth")]
        [AllowAnonymous]
        public IActionResult Index()
        {
            // Auth UI lives on the public home page modal.
            return RedirectToAction("Index", "Home");
        }

        public sealed class AjaxLoginRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        public sealed class AjaxRegisterRequest
        {
            public string? FullName { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AjaxLogin([FromBody] AjaxLoginRequest req)
        {
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, error = "Username & password wajib." });

            var result = await _signIn.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: false);
            if (!result.Succeeded)
                return Json(new { success = false, error = "Username atau password salah." });

            var user = await _users.FindByNameAsync(username);
            var redirectUrl = "/";
            if (user != null && await _users.IsInRoleAsync(user, AppRoles.Admin))
                redirectUrl = "/Admin";

            return Json(new { success = true, redirectUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AjaxRegister([FromBody] AjaxRegisterRequest req)
        {
            var fullName = (req.FullName ?? string.Empty).Trim();
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, error = "Full name, username, password wajib." });

            var existing = await _users.FindByNameAsync(username);
            if (existing != null)
                return Json(new { success = false, error = "Username sudah dipakai." });

            var user = new ApplicationUser
            {
                FullName = fullName,
                UserName = username,
                EmailConfirmed = true
            };

            var create = await _users.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                var msg = string.Join(", ", create.Errors.Select(e => e.Description));
                return Json(new { success = false, error = msg });
            }

            await _users.AddToRoleAsync(user, AppRoles.Customer);
            await _signIn.SignInAsync(user, isPersistent: false);

            return Json(new { success = true, redirectUrl = "/" });
        }

        [HttpPost("/Auth/Logout")]
        public async Task<IActionResult> Logout()
        {
            // Reset table & membership cookies on logout
            Response.Cookies.Delete("nr_tableNumber");
            Response.Cookies.Delete("nr_membershipStatus");

            await _signIn.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
    }
}
