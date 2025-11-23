using ManarangVergara.Models.Database;
using ManarangVergara.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class CategoriesController : Controller
    {
        private readonly PharmacyDbContext _context;

        public CategoriesController(PharmacyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string sortOrder, string searchString, bool showArchived = false, int? pageNumber = 1)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            var query = _context.ProductCategories.AsQueryable();

            if (!showArchived) query = query.Where(c => c.IsActive == true);

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.CategoryName.Contains(searchString));
            }

            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(c => c.CategoryName),
                "" => query.OrderBy(c => c.CategoryName),
                "Count" => query.OrderBy(c => c.Products.Count),
                "count_desc" => query.OrderByDescending(c => c.Products.Count),
                _ => query.OrderByDescending(c => c.LastUpdated)
            };

            var data = await query
                .Select(c => new CategoryListViewModel
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    ProductCount = c.Products.Count()
                })
                .ToListAsync();

            // PAGINATION: 10 Items
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // 1. NEW: Archives Page (Paginated & Sorted)
        public async Task<IActionResult> Archives(string sortOrder, string searchString, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";

            // QUERY: Fetch only INACTIVE (Archived) Categories
            var query = _context.ProductCategories
                .Where(c => c.IsActive == false)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(c => c.CategoryName.Contains(searchString));
            }

            // Sort Logic
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(c => c.CategoryName),
                "" => query.OrderBy(c => c.CategoryName),
                "Count" => query.OrderBy(c => c.Products.Count),
                "count_desc" => query.OrderByDescending(c => c.Products.Count),
                _ => query.OrderByDescending(c => c.LastUpdated)
            };

            var data = await query
                .Select(c => new CategoryListViewModel
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    ProductCount = c.Products.Count()
                })
                .ToListAsync();

            // PAGINATION: 10 Items per page
            int pageSize = 10;
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCategory category)
        {
            if (ModelState.IsValid)
            {
                category.IsActive = true;
                category.LastUpdated = DateTime.Now; // Timestamp

                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var category = await _context.ProductCategories.FindAsync(id);
            if (category == null) return NotFound();
            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductCategory category)
        {
            if (id != category.CategoryId) return NotFound();

            if (ModelState.IsValid)
            {
                // Preserve IsActive status
                var existing = await _context.ProductCategories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == id);
                if (existing != null) category.IsActive = existing.IsActive;

                category.LastUpdated = DateTime.Now; // Timestamp update

                _context.Update(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // SOFT DELETE (Archive)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                category.IsActive = false; // Soft Delete
                category.LastUpdated = DateTime.Now;
                _context.Update(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category archived.";
            }
            return RedirectToAction(nameof(Index));
        }

        // RESTORE (Unarchive)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                category.IsActive = true; // Restore
                category.LastUpdated = DateTime.Now; // Moves to top
                _context.Update(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category restored.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}