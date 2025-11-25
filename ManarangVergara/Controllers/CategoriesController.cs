using ManarangVergara.Models.Database;
using ManarangVergara.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // CATEGORIES CONTROLLER (master data management)
    // ============================================================
    // manages product categories. allows creating, editing, and soft deleting (archiving).
    // restricted to authorized staff to maintain data integrity.
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class CategoriesController : Controller
    {
        private readonly PharmacyDbContext _context;

        public CategoriesController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. LIST ACTIVE CATEGORIES (index)
        // ============================================================

        // get: displays a paginated list of active categories.
        // supports filtering by name and sorting by various columns.
        public async Task<IActionResult> Index(string sortOrder, string searchString, bool showArchived = false, int? pageNumber = 1)
        {
            // store current state for pagination links
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            var query = _context.ProductCategories.AsQueryable();

            // filter logic: only show active categories unless requested otherwise
            if (!showArchived) query = query.Where(c => c.IsActive == true);

            // search logic: filter by category name
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.CategoryName.Contains(searchString));
            }

            // sort logic
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(c => c.CategoryName),
                "" => query.OrderBy(c => c.CategoryName),
                "Count" => query.OrderBy(c => c.Products.Count),
                "count_desc" => query.OrderByDescending(c => c.Products.Count),
                _ => query.OrderByDescending(c => c.LastUpdated) // default: recent updates first
            };

            // project data to viewmodel, including product count
            var data = await query
                .Select(c => new CategoryListViewModel
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    ProductCount = c.Products.Count()
                })
                .ToListAsync();

            // paginate: 10 items per page
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 2. LIST ARCHIVED CATEGORIES (recycle bin)
        // ============================================================

        // get: displays categories that have been "soft deleted".
        public async Task<IActionResult> Archives(string sortOrder, string searchString, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";

            // query: fetch only inactive (archived) categories
            var query = _context.ProductCategories
                .Where(c => c.IsActive == false)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(c => c.CategoryName.Contains(searchString));
            }

            // sort logic
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
                    ProductCount = c.Products.Count(),

                    // THIS WAS MISSING: Check if 0 products exist
                    CanDeletePermanently = !c.Products.Any()
                })
                .ToListAsync();

            // paginate: 10 items per page
            int pageSize = 10;
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 3. CREATE CATEGORY
        // ============================================================

        // get: show empty form
        public IActionResult Create()
        {
            return View();
        }

        // post: save new category
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCategory category)
        {
            if (ModelState.IsValid)
            {
                category.IsActive = true;
                category.LastUpdated = DateTime.Now;

                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // ============================================================
        // 4. EDIT CATEGORY
        // ============================================================

        // get: load category for editing
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var category = await _context.ProductCategories.FindAsync(id);
            if (category == null) return NotFound();
            return View(category);
        }

        // post: update existing category
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductCategory category)
        {
            if (id != category.CategoryId) return NotFound();

            if (ModelState.IsValid)
            {
                // preserve isactive status (prevent accidental archiving during edit)
                var existing = await _context.ProductCategories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == id);
                if (existing != null) category.IsActive = existing.IsActive;

                category.LastUpdated = DateTime.Now;

                _context.Update(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // ============================================================
        // 5. ARCHIVE (soft delete)
        // ============================================================

        // post: marks category as inactive instead of deleting it.
        // safer than hard delete because products might still be linked to it.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                category.IsActive = false; // soft delete
                category.LastUpdated = DateTime.Now;
                _context.Update(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category archived.";
            }
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 6. RESTORE (unarchive)
        // ============================================================

        // post: reactivates a previously archived category.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                category.IsActive = true; // restore
                category.LastUpdated = DateTime.Now; // bumps to top of list
                _context.Update(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category restored.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}