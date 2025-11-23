using ManarangVergara.Models.Database;
using ManarangVergara.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class SuppliersController : Controller
    {
        private readonly PharmacyDbContext _context;

        public SuppliersController(PharmacyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string sortOrder, string searchString, bool showArchived = false, int? pageNumber = 1)
        {
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder; // Save sort state
            ViewData["ShowArchived"] = showArchived;

            var query = _context.Suppliers.AsQueryable();

            // 1. Soft Delete Logic
            if (!showArchived)
            {
                query = query.Where(s => s.IsActive == true);
            }

            // 2. Search
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.Name.Contains(searchString) || s.ContactInfo.Contains(searchString));
            }

            // 3. Sort
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

            var data = await query
                .Select(s => new SupplierListViewModel
                {
                    SupplierId = s.SupplierId,
                    Name = s.Name,
                    ContactInfo = s.ContactInfo,
                    ProductCount = s.Products.Count()
                })
                .ToListAsync();

            // PAGINATION: 10 Items
            int pageSize = 10;
            return View(PaginatedList<SupplierListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        public async Task<IActionResult> Archives(string searchString, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;

            var query = _context.Suppliers.Where(s => s.IsActive == false); // Inactive only

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(s => s.Name.Contains(searchString));
            }

            query = query.OrderByDescending(s => s.LastUpdated); // Newest archives first

            var list = await query.Select(s => new SupplierListViewModel
            {
                SupplierId = s.SupplierId,
                Name = s.Name,
                ContactInfo = s.ContactInfo,
                ProductCount = s.Products.Count()
            }).ToListAsync();

            return View(PaginatedList<SupplierListViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, 10));
        }

        public IActionResult Create()
        {
            return View();
        }

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

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Supplier supplier)
        {
            if (id != supplier.SupplierId) return NotFound();

            if (ModelState.IsValid)
            {
                var existing = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == id);
                if (existing != null) supplier.IsActive = existing.IsActive;

                supplier.LastUpdated = DateTime.Now;

                _context.Update(supplier);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // Soft Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                supplier.IsActive = false;
                supplier.LastUpdated = DateTime.Now;
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Supplier archived.";
            }
            return RedirectToAction(nameof(Index));
        }

        // Restore
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                supplier.IsActive = true;
                supplier.LastUpdated = DateTime.Now;
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Supplier restored.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}