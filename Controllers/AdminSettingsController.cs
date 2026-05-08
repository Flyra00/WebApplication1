using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.ViewModels.Admin;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class AdminSettingsController : Controller
    {
        private readonly AppDbContext _context;

        public AdminSettingsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Pengaturan Persentase";
            return View(await LoadViewModelAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AdminChargesSettingsViewModel model)
        {
            ViewData["Title"] = "Pengaturan Persentase";

            if (!ModelState.IsValid)
                return View(model);

            await UpsertSettingAsync(AppSettingKeys.OrderPpnPercentage, model.OrderPpnPercentage, "Persentase PPN pesanan");
            await UpsertSettingAsync(AppSettingKeys.OrderServicePercentage, model.OrderServicePercentage, "Persentase service pesanan");
            await UpsertSettingAsync(AppSettingKeys.ReservationDpPercentage, model.ReservationDpPercentage, "Persentase DP reservasi");

            TempData["Success"] = "Pengaturan persentase berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<AdminChargesSettingsViewModel> LoadViewModelAsync()
        {
            return new AdminChargesSettingsViewModel
            {
                OrderPpnPercentage = await GetSettingDecimalAsync(AppSettingKeys.OrderPpnPercentage),
                OrderServicePercentage = await GetSettingDecimalAsync(AppSettingKeys.OrderServicePercentage),
                ReservationDpPercentage = await GetSettingDecimalAsync(AppSettingKeys.ReservationDpPercentage)
            };
        }

        private async Task<decimal> GetSettingDecimalAsync(string key)
        {
            var rawValue = await _context.AppSettings
                .AsNoTracking()
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private async Task UpsertSettingAsync(string key, decimal value, string description)
        {
            var normalized = NormalizePercentage(value);
            var rawValue = normalized.ToString(CultureInfo.InvariantCulture);
            var now = DateTime.UtcNow;

            var setting = await _context.AppSettings.FirstOrDefaultAsync(item => item.Key == key);
            if (setting == null)
            {
                setting = new AppSetting
                {
                    Key = key,
                    Value = rawValue,
                    Description = description,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _context.AppSettings.Add(setting);
            }
            else
            {
                setting.Value = rawValue;
                setting.Description = description;
                setting.UpdatedAtUtc = now;
            }

            await _context.SaveChangesAsync();
        }

        private static decimal NormalizePercentage(decimal value)
        {
            if (value < 0)
                return 0;
            if (value > 100)
                return 100;
            return Math.Round(value, 2);
        }
    }
}
