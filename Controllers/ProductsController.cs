using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
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
            var products = await _context.Products.ToListAsync();
            return View(products);
        }
        //create
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Category,Price")] Product product, IFormFile? ImageFile)
        {
            if (ModelState.IsValid)
            {
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(ImageFile.FileName);
                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/products", fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await ImageFile.CopyToAsync(stream);
                    }

                    product.ImageFileName = fileName;
                }

                _context.Products.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }

        //update
        public async Task<IActionResult> Edit (int id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit (int id, [Bind("Id,Name,Price,Category,ImageFileName")]Product product, IFormFile? ImageFile)
        {
            if (id != product.Id) return NotFound();
            if (ModelState.IsValid)
            {
                

                if (ImageFile != null && ImageFile.Length > 0)
                {
                    // 1. Upload Gambar Baru
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(ImageFile.FileName);
                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/products", fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await ImageFile.CopyToAsync(stream);
                    }

                    // 2. Hapus Gambar Lama biar folder wwwroot lu kaga penuh sampah
                    // Kita ambil data lama dari DB buat tau nama file lamanya
                    var oldProduct = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
                    if (oldProduct != null && !string.IsNullOrEmpty(oldProduct.ImageFileName))
                    {
                        string oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/products", oldProduct.ImageFileName);
                        if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                    }

                    product.ImageFileName = fileName;
                }
                _context.Products.Update(product);
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
                                                        
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p =>p.Id == id);
            return View(product);
        }
        [HttpPost, ActionName (nameof(Delete))]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product != null)
            {
                if (!string.IsNullOrEmpty(product.ImageFileName))
                {
                    string filePath = Path.Combine(_env.WebRootPath, "products", product.ImageFileName);

                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("index");
        }

    }
}
