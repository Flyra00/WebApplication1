using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Supervisor}")]
    public class ProductsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        public ProductsController(AppDbContext context, IWebHostEnvironment env)
        {  
            _context = context;
            _env = env;
        }
        //ambil data
        public async Task<IActionResult> Index()
        {
            var products = await _context.Products
                .AsNoTracking()
                .ToListAsync();
            return View(products);
        }
        //create
        [Authorize(Roles = AppRoles.Admin)]
        public IActionResult Create()
        {
            return View(new Product { IsAvailable = true });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AppRoles.Admin)]
        public async Task<IActionResult> Create([Bind("Name,Category,Price,MemberDiscountPercentage,Stock,IsAvailable")] Product product, IFormFile? ImageFile)
        {
            if (ModelState.IsValid)
            {
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    product.ImageFileName = await SaveProductImageAsync(ImageFile);
                }

                _context.Products.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }

//update
        [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Supervisor}")]
        public async Task<IActionResult> Edit (int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            return View(product);
        }

[HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Supervisor}")]
        public async Task<IActionResult> Edit (int id, [Bind("Id,Name,Price,MemberDiscountPercentage,Category,ImageFileName,Stock,IsAvailable")]Product product, IFormFile? ImageFile)
        {
            if (id != product.Id) return NotFound();
            if (ModelState.IsValid)
            {
                var existingProduct = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
                if (existingProduct == null) return NotFound();

existingProduct.Name = product.Name;
                existingProduct.Category = product.Category;
                existingProduct.Price = product.Price;
                existingProduct.MemberDiscountPercentage = product.MemberDiscountPercentage;
                existingProduct.Stock = product.Stock;
                existingProduct.IsAvailable = product.IsAvailable;

                if (ImageFile != null && ImageFile.Length > 0)
                {
                    var fileName = await SaveProductImageAsync(ImageFile);
                    if (!string.IsNullOrEmpty(existingProduct.ImageFileName))
                    {
                        DeleteProductImage(existingProduct.ImageFileName);
                    }

                    existingProduct.ImageFileName = fileName;
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            foreach (var error in errors)
            {
                Console.WriteLine("ERROR NYA NIH : " + error.ErrorMessage);
            }
            return View(product);
        }
        //delate
        [Authorize(Roles = AppRoles.Admin)]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>p.Id == id);
            if (product == null) return NotFound();
            return View(product);
        }
        [HttpPost, ActionName (nameof(Delete))]
        [Authorize(Roles = AppRoles.Admin)]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product != null)
            {
                var isReferenced = await _context.OrderItems.AnyAsync(item => item.ProductId == id);
                if (isReferenced)
                {
                    TempData["Error"] = "Menu tidak bisa dihapus karena sudah dipakai pada transaksi.";
                    return RedirectToAction(nameof(Index));
                }

                if (!string.IsNullOrEmpty(product.ImageFileName))
                {
                    DeleteProductImage(product.ImageFileName);
                }
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> SaveProductImageAsync(IFormFile imageFile)
        {
            var productsDirectory = GetProductsDirectory();
            Directory.CreateDirectory(productsDirectory);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
            var filePath = Path.Combine(productsDirectory, fileName);

            await using var stream = new FileStream(filePath, FileMode.CreateNew);
            await imageFile.CopyToAsync(stream);

            return fileName;
        }

        private void DeleteProductImage(string imageFileName)
        {
            var safeFileName = Path.GetFileName(imageFileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return;

            var filePath = Path.Combine(GetProductsDirectory(), safeFileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        private string GetProductsDirectory()
        {
            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            return Path.Combine(webRootPath, "products");
        }

    }
}
