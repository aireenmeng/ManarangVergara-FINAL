using ManarangVergara.Models.Database;
using ManarangVergara.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // SUPPLIERS CONTROLLER (master data management)
    // ============================================================
    // manages the list of suppliers who provide products to the pharmacy.
    // allows adding, editing, and archiving (soft deleting) supplier records.
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class SuppliersController : Controller
    {
        private readonly PharmacyDbContext _context;

        public SuppliersController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. LIST ACTIVE SUPPLIERS (index)
        // ============================================================

        // get: displays a paginated list of active suppliers.
        // supports sorting, searching, and filtering by active/archived status.
        public async Task<IActionResult> Index(string sortOrder, string searchString, bool showArchived = false, int? pageNumber = 1)
        {
            // save current state for pagination links
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            var query = _context.Suppliers.AsQueryable();

            // 1. soft delete logic: by default, only show active suppliers
            if (!showArchived)
            {
                query = query.Where(s => s.IsActive == true);
            }

            // 2. search logic: filter by name or contact info
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.Name.Contains(searchString) || s.ContactInfo.Contains(searchString));
            }

            // 3. sort logic
            // default sort is by 'LastUpdated' to show recent activity first
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(s => s.Name),
                "" => query.OrderBy(s => s.Name),
                "Contact" => query.OrderBy(s => s.ContactInfo),
                "contact_desc" => query.OrderByDescending(s => s.ContactInfo),
                "Count" => query.OrderBy(s => s.Products.Count),
                "count_desc" => query.OrderByDescending(s => s.Products.Count),
                _ => query.OrderByDescending(s => s.LastUpdated)
            };

            // project to viewmodel to count linked products
            var data = await query
                .Select(s => new SupplierListViewModel
                {
                    SupplierId = s.SupplierId,
                    Name = s.Name,
                    ContactInfo = s.ContactInfo,
                    ProductCount = s.Products.Count()
                })
                .ToListAsync();

            // pagination: 10 items per page
            int pageSize = 10;
            return View(PaginatedList<SupplierListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 2. LIST ARCHIVED SUPPLIERS (recycle bin)
        // ============================================================

        // get: dedicated view for soft-deleted suppliers.
        public async Task<IActionResult> Archives(string searchString, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;

            // query: fetch only inactive (archived) suppliers
            var query = _context.Suppliers.Where(s => s.IsActive == false);

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.Name.Contains(searchString));
            }

            // sort: newest archives first
            query = query.OrderByDescending(s => s.LastUpdated);

            var list = await query.Select(s => new SupplierListViewModel
            {
                SupplierId = s.SupplierId,
                Name = s.Name,
                ContactInfo = s.ContactInfo,
                ProductCount = s.Products.Count()
            }).ToListAsync();

            // pagination: 10 items per page
            return View(PaginatedList<SupplierListViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 3. CREATE SUPPLIER
        // ============================================================

        // get: show empty form
        public IActionResult Create()
        {
            return View();
        }

        // post: save new supplier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Supplier supplier)
        {
            if (ModelState.IsValid)
            {
                supplier.IsActive = true;
                supplier.LastUpdated = DateTime.Now;

                _context.Add(supplier);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // ============================================================
        // 4. EDIT SUPPLIER
        // ============================================================

        // get: load supplier details
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // post: update supplier details
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Supplier supplier)
        {
            if (id != supplier.SupplierId) return NotFound();

            if (ModelState.IsValid)
            {
                // preserve existing isactive status
                var existing = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == id);
                if (existing != null) supplier.IsActive = existing.IsActive;

                supplier.LastUpdated = DateTime.Now;

                _context.Update(supplier);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // ============================================================
        // 5. ARCHIVE (soft delete)
        // ============================================================

        // post: marks supplier as inactive.
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                supplier.IsActive = false; // soft delete
                supplier.LastUpdated = DateTime.Now;
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Supplier archived.";
            }
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 6. RESTORE (unarchive)
        // ============================================================

        // post: reactivates a previously archived supplier.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                supplier.IsActive = true; // restore
                supplier.LastUpdated = DateTime.Now;
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Supplier restored.";
            }
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 7. PERMANENT DELETE (For empty suppliers)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> DeletePermanent(int id)
        {
            // 1. Find the supplier
            var supplier = await _context.Suppliers
                .Include(s => s.Products)
                .Include(s => s.PurchaseOrders)
                .FirstOrDefaultAsync(s => s.SupplierId == id);

            if (supplier == null) return NotFound();

            // 2. Safety Check: Do not delete if they have products or orders linked
            if (supplier.Products.Any() || supplier.PurchaseOrders.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete: This supplier has linked products or orders.";
                return RedirectToAction(nameof(Archives));
            }

            // 3. Hard Delete
            _context.Suppliers.Remove(supplier);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Supplier permanently deleted.";
            return RedirectToAction(nameof(Archives));
        }
    }
}