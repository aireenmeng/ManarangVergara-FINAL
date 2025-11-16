using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ManarangVergara.Controllers
{
    // SECURE: Only allow these roles to access ANY part of this controller
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class CategoriesController : Controller
    {
        private readonly PharmacyDbContext _context;

        public CategoriesController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Categories (With Search AND Sorting)
        public async Task<IActionResult> Index(string sortOrder, string searchString)
        {
            // 1. Set up Sorting Parameters for the View to use in links
            // If sortOrder is empty, clicking Name header will set it to "name_desc".
            // If it's already "name_desc", clicking it sets it back to "" (default ascending).
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;

            // 2. Start the Query projecting into our new ViewModel
            // Notice: We use .Select() to calculate ProductCount right in the database query!
            var categories = _context.ProductCategories
                .Select(c => new CategoryListViewModel
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    ProductCount = c.Products.Count() // Efficiently counts related products
                });

            // 3. Apply Search
            if (!string.IsNullOrEmpty(searchString))
            {
                categories = categories.Where(s => s.CategoryName.Contains(searchString));
            }

            // 4. Apply Sorting based on the parameter passed
            categories = sortOrder switch
            {
                "name_desc" => categories.OrderByDescending(c => c.CategoryName),
                "Count" => categories.OrderBy(c => c.ProductCount),
                "count_desc" => categories.OrderByDescending(c => c.ProductCount),
                _ => categories.OrderBy(c => c.CategoryName), // Default: Name Ascending
            };

            return View(await categories.ToListAsync());
        }

        // GET: Categories/Create (The Add Form)
        public IActionResult Create()
        {
            return View();
        }

        // POST: Categories/Create (Processing the Add)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCategory category)
        {
            if (ModelState.IsValid)
            {
                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // GET: Categories/Edit/5 (The Edit Form)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var category = await _context.ProductCategories.FindAsync(id);
            if (category == null) return NotFound();

            return View(category);
        }

        // POST: Categories/Edit/5 (Processing the Edit)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductCategory category)
        {
            if (id != category.CategoryId) return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // POST: Categories/Delete/5 (Now with Error Handling!)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                try
                {
                    _context.ProductCategories.Remove(category);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // This catches the foreign key error!
                    TempData["ErrorMessage"] = "Cannot delete this category because it is currently assigned to one or more products.";
                    return RedirectToAction(nameof(Index));
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}