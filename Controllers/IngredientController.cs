using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class IngredientController : Controller
    {
        private readonly AppDbContext _context;

        public IngredientController(AppDbContext context)
        {
            _context = context;
        }
        //reading
        public async Task<IActionResult> Index()
        {
            var ingredients = await _context.Ingredients.ToListAsync();
            return View(ingredients);
        }
        //create
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ItemName,Unit,Qty,MinimumStock")] Ingredient ingredient)
        {

            if (ModelState.IsValid)
            {
                _context.Ingredients.Add(ingredient);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            return View(ingredient);
        }
        //Update
        public async Task<IActionResult> Edit(int Id)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == Id);
            if (ingredient == null) return NotFound();
            return View(ingredient);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind("Id,ItemName,Unit,Qty,MinimumStock")]Ingredient ingredient)
        {
            if (ModelState.IsValid)
            {
                _context.Ingredients.Update(ingredient);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(ingredient);
        }
        //Delete
        public async Task<IActionResult>Delete (int Id)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == Id);
            return View(ingredient);
        }
        [HttpPost, ActionName(nameof(Delete))]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int Id)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == Id);
            if (ingredient == null)
                return NotFound();
            _context.Ingredients.Remove(ingredient);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(Index));
        }

    }
}
