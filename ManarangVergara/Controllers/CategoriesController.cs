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
    // restricts access so only authorized staff can manage product categories.
    // uses standard crud operations but implements "soft delete" (archiving)
    // instead of permanent deletion to preserve data integrity for historical reports.
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
        // displays the main list of active product categories.
        // supports sorting, searching, and pagination.
        public async Task<IActionResult> Index(string sortOrder, string searchString, bool showArchived = false, int? pageNumber = 1)
        {
            // save current sort/filter state to viewdata so pagination links keep them.
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            var query = _context.ProductCategories.AsQueryable();

            // 1. filter logic: by default, show only active items.
            if (!showArchived) query = query.Where(c => c.IsActive == true);

            // 2. search logic: filter by category name.
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.CategoryName.Contains(searchString));
            }

            // 3. sort logic: allows sorting by name or product count.
            // default sort is by 'LastUpdated' so recently edited items appear first.
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(c => c.CategoryName),
                "" => query.OrderBy(c => c.CategoryName),
                "Count" => query.OrderBy(c => c.Products.Count),
                "count_desc" => query.OrderByDescending(c => c.Products.Count),
                _ => query.OrderByDescending(c => c.LastUpdated)
            };

            // project to viewmodel to include the count of linked products.
            var data = await query
                .Select(c => new CategoryListViewModel
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName,
                    ProductCount = c.Products.Count()
                })
                .ToListAsync();

            // pagination: limit to 10 items per page for cleaner ui.
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 2. LIST ARCHIVED CATEGORIES (recycle bin)
        // ============================================================
        // a dedicated page for viewing "deleted" categories.
        // allows users to restore them if needed.
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
                    ProductCount = c.Products.Count()
                })
                .ToListAsync();

            // pagination: 10 items per page
            int pageSize = 10;
            return View(PaginatedList<CategoryListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 3. CREATE NEW CATEGORY
        // ============================================================

        // get: show the empty form
        public IActionResult Create()
        {
            return View();
        }

        // post: save the new category to database
        [HttpPost]
        [ValidateAntiForgeryToken] // prevents csrf attacks
        public async Task<IActionResult> Create(ProductCategory category)
        {
            if (ModelState.IsValid)
            {
                // automatically set as active and timestamp it
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

        // get: fetch category by id and show form
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
                // preserve existing isactive status (don't accidentally archive/unarchive on edit)
                var existing = await _context.ProductCategories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == id);
                if (existing != null) category.IsActive = existing.IsActive;

                category.LastUpdated = DateTime.Now; // update timestamp

                _context.Update(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // ============================================================
        // 5. SOFT DELETE (archive)
        // ============================================================
        // instead of deleting the row, we set IsActive = false.
        // this ensures historical sales data linked to this category remains valid.
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
        // reactivates a previously archived category.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var category = await _context.ProductCategories.FindAsync(id);
            if (category != null)
            {
                category.IsActive = true; // restore
                category.LastUpdated = DateTime.Now; // updating moves it to the top of the list
                _context.Update(category);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Category restored.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}