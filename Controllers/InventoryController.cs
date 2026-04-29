using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
    public class InventoryController : Controller
    {
        private readonly AppDbContext _context;
        public InventoryController(AppDbContext context)
        {
            _context = context;
        }
        //read
        public async Task<IActionResult> Index()
        {
            var inven = await _context.Inventory.ToListAsync();
            return View(inven);
        }
        //create
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ItemName,Category,Qty,Condition")]Inventory inven)
        {
            if(inven == null)
            {
                return NotFound();
            }
            if(ModelState.IsValid)
            {
                await _context.Inventory.AddAsync(inven);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(inven);
        }
        //update
        public async Task<IActionResult> Edit(int Id)
        {
            var inven = await _context.Inventory.FirstOrDefaultAsync(i =>i.Id == Id);
            if(inven == null)
            {
                return NotFound();
            }
            return View(inven);
        }
        [HttpPost,ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind("Id,ItemName,Category,Qty,Condition")]Inventory inven)
        {
            if (ModelState.IsValid)
            {
                _context.Inventory.Update(inven);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(inven);
        }
        //delete
        public async Task<IActionResult> Delete(int Id)
        {
            var inven = await _context.Inventory.FirstOrDefaultAsync(i =>i.Id == Id);
            if(inven == null)
            {
                return NotFound();
            }
            return View(inven);
        }
        [HttpPost, ActionName(nameof(Delete))]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int Id)
        {
            var inven = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == Id);
            if (inven == null)
            {
                return NotFound();
            }
            _context.Inventory.Remove(inven);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
