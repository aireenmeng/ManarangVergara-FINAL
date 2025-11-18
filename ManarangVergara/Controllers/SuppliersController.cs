using ManarangVergara.Models.Database;
using ManarangVergara.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ManarangVergara.Controllers
{
    // SECURE: Only Admin/Owner/Manager can access suppliers
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class SuppliersController : Controller
    {
        private readonly PharmacyDbContext _context;

        public SuppliersController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Suppliers
        public async Task<IActionResult> Index(string sortOrder, string searchString)
        {
            // 1. Set up Sorting Parameters
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";
            ViewData["CountSortParm"] = sortOrder == "Count" ? "count_desc" : "Count";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder; // Keep track of current sort for pagination/search links

            // 2. Start Query & Project to ViewModel
            var suppliers = _context.Suppliers
                .Select(s => new SupplierListViewModel
                {
                    SupplierId = s.SupplierId,
                    Name = s.Name,
                    ContactInfo = s.ContactInfo,
                    ProductCount = s.Products.Count() // Counts linked products efficiently
                });

            // 3. Apply Search
            if (!string.IsNullOrEmpty(searchString))
            {
                suppliers = suppliers.Where(s => s.Name.Contains(searchString) || s.ContactInfo.Contains(searchString));
            }

            // 4. Apply Sort
            suppliers = sortOrder switch
            {
                "name_desc" => suppliers.OrderByDescending(s => s.Name),
                "Contact" => suppliers.OrderBy(s => s.ContactInfo),
                "contact_desc" => suppliers.OrderByDescending(s => s.ContactInfo),
                "Count" => suppliers.OrderBy(s => s.ProductCount),
                "count_desc" => suppliers.OrderByDescending(s => s.ProductCount),
                _ => suppliers.OrderBy(s => s.Name), // Default
            };

            return View(await suppliers.ToListAsync());
        }

        // GET: Suppliers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Suppliers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Supplier supplier)
        {
            if (ModelState.IsValid)
            {
                _context.Add(supplier);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // GET: Suppliers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // POST: Suppliers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Supplier supplier)
        {
            if (id != supplier.SupplierId) return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(supplier);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // POST: Suppliers/Delete/5 (With Error Handling)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier != null)
            {
                try
                {
                    _context.Suppliers.Remove(supplier);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    TempData["ErrorMessage"] = "Cannot delete this supplier because they are linked to existing products.";
                    return RedirectToAction(nameof(Index));
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}