using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class TableSessionController : Controller
    {
        private readonly AppDbContext _context;

        public TableSessionController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("/qr/{token}")]
        public async Task<IActionResult> StartFromQr(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return NotFound();

            var table = await _context.Tables
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.QrCodeToken == token && t.IsActive);

            if (table == null)
                return NotFound();

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddHours(8),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            };

            Response.Cookies.Append("nr_tableNumber", table.Number.ToString(), cookieOptions);
            Response.Cookies.Append("nr_tableToken", table.QrCodeToken, cookieOptions);

            if (!Request.Cookies.ContainsKey("nr_membershipStatus"))
                Response.Cookies.Append("nr_membershipStatus", "Guest", cookieOptions);

            return Redirect("/#menu");
        }
    }
}
