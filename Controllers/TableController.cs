using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;


namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TableController : Controller
    {
        private readonly AppDbContext _context;
        public TableController(AppDbContext context)
        {
            _context = context;
        }
        //Read
        public async Task<IActionResult> Index()
        {
            var Table = await _context.Tables.ToListAsync();
            return View(Table);
        }
        //Create
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Number,Capacity")]Table table)
        {
            if(table == null)
            {
                return NotFound();
            }
            if (ModelState.IsValid)
            {
                await _context.Tables.AddAsync(table);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }
        //update
        public async Task<IActionResult> Edit(int id)
        {
            var chair = await _context.Tables.FirstOrDefaultAsync(t => t.Id == id);
            return View(chair);
        }
        [HttpPost, ValidateAntiForgeryToken]

        public async Task<IActionResult> Edit([Bind("Id,Number,Capacity")]Table table)
        {
            if (ModelState.IsValid)
            {
                _context.Tables.Update(table);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }

        //Delete
        public async Task<IActionResult> Delete(int Id)
        {
            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == Id);
            return View(table);
        }
        [HttpPost, ActionName(nameof(Delete))]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int Id)
        {
            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == Id);
            if (table == null)
            {
                return NotFound();
            }
            _context.Tables.Remove(table);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
