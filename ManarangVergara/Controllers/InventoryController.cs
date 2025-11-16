using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    [Authorize]
    public class InventoryController : Controller
    {
        private readonly PharmacyDbContext _context;

        public InventoryController(PharmacyDbContext context)
        {
            _context = context;
        }

        // =========================================
        // GET: Inventory List (Index) with Search, Sort, AND Archive Toggle
        // =========================================
        public async Task<IActionResult> Index(string searchString, string sortOrder, bool showArchived = false)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived; // Pass this to the view so the toggle button works

            // Sort Toggles
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["StockSortParm"] = sortOrder == "Stock" ? "stock_desc" : "Stock";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .AsQueryable();

            // ARCHIVE FILTER: Only show active products unless specifically asked for archived ones
            if (!showArchived)
            {
                query = query.Where(p => p.IsActive == 1);
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString) || p.Category.CategoryName.Contains(searchString));
            }

            var rawData = await query.ToListAsync();

            // Transform data for the view
            var inventoryList = rawData.Select(p =>
            {
                var totalQuantity = p.Inventories.Sum(i => i.Quantity);
                var earliestExpiry = p.Inventories.Any() ? p.Inventories.Min(i => i.ExpiryDate) : DateOnly.MaxValue;

                string status = "Active";
                // Override status if it's archived
                if (p.IsActive == 0) status = "Archived";
                else if (totalQuantity == 0) status = "Out of Stock";
                else if (totalQuantity < 20) status = "Low Stock";
                else if (earliestExpiry <= DateOnly.FromDateTime(DateTime.Today)) status = "Expired";

                return new InventoryListViewModel
                {
                    ProductId = p.ProductId,
                    ProductName = p.Name,
                    CategoryName = p.Category?.CategoryName ?? "N/A",
                    Manufacturer = p.Manufacturer,
                    Quantity = totalQuantity,
                    SellingPrice = p.Inventories.FirstOrDefault()?.SellingPrice ?? 0,
                    ExpiryDate = earliestExpiry,
                    Status = status
                };
            });

            // Apply Sorting
            inventoryList = sortOrder switch
            {
                "name_desc" => inventoryList.OrderByDescending(i => i.ProductName),
                "Category" => inventoryList.OrderBy(i => i.CategoryName),
                "cat_desc" => inventoryList.OrderByDescending(i => i.CategoryName),
                "Stock" => inventoryList.OrderBy(i => i.Quantity),
                "stock_desc" => inventoryList.OrderByDescending(i => i.Quantity),
                "Price" => inventoryList.OrderBy(i => i.SellingPrice),
                "price_desc" => inventoryList.OrderByDescending(i => i.SellingPrice),
                "Expiry" => inventoryList.OrderBy(i => i.ExpiryDate),
                "expiry_desc" => inventoryList.OrderByDescending(i => i.ExpiryDate),
                _ => inventoryList.OrderBy(i => i.ProductName),
            };

            return View(inventoryList.ToList());
        }

        // =========================================
        // GET: Show "Add Product" Form
        // =========================================
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Create()
        {
            var viewModel = new ProductFormViewModel
            {
                CategoryList = await _context.ProductCategories
                    .OrderBy(c => c.CategoryName)
                    .Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName })
                    .ToListAsync(),
                SupplierList = await _context.Suppliers
                    .OrderBy(s => s.Name)
                    .Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name })
                    .ToListAsync(),
                BatchNumber = $"B-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100, 999)}"
            };
            return View(viewModel);
        }

        // =========================================
        // POST: Process "Add Product" Form
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Create(ProductFormViewModel model)
        {
            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var newProduct = new Product
                    {
                        Name = model.Name,
                        Description = model.Description ?? "",
                        Manufacturer = model.Manufacturer,
                        CategoryId = model.CategoryId,
                        SupplierId = model.SupplierId,
                        CreatedAt = DateTime.Now
                    };
                    _context.Products.Add(newProduct);
                    await _context.SaveChangesAsync();

                    var newInventory = new Inventory
                    {
                        ProductId = newProduct.ProductId,
                        Quantity = model.Quantity,
                        CostPrice = model.CostPrice,
                        SellingPrice = model.SellingPrice,
                        ExpiryDate = DateOnly.FromDateTime(model.ExpiryDate),
                        BatchNumber = model.BatchNumber,
                        IsExpired = (model.ExpiryDate <= DateTime.Today) ? (sbyte)1 : (sbyte)0,
                        LastUpdated = DateTime.Now
                    };
                    _context.Inventories.Add(newInventory);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();
                    TempData["SuccessMessage"] = $"{model.Name} added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "Database Error: " + ex.Message);
                }
            }

            model.CategoryList = await _context.ProductCategories
                .Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName })
                .ToListAsync();
            model.SupplierList = await _context.Suppliers
                .Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name })
                .ToListAsync();
            return View(model);
        }

        // =========================================
        // GET: Show "Edit Product" Form
        // =========================================
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            var viewModel = new ProductFormViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Manufacturer = product.Manufacturer,
                CategoryId = product.CategoryId,
                SupplierId = product.SupplierId,
                CategoryList = await _context.ProductCategories
                    .Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName })
                    .ToListAsync(),
                SupplierList = await _context.Suppliers
                    .Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name })
                    .ToListAsync()
            };

            return View(viewModel);
        }

        // POST: Inventory/Archive/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var product = await _context.Products
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            try
            {
                // 1. Try Hard Delete (SAFE WAY)
                // We use .ToList() to create a copy of the list, so we don't modify the collection while iterating over it.
                foreach (var inventory in product.Inventories.ToList())
                {
                    _context.Inventories.Remove(inventory);
                }

                _context.Products.Remove(product);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Product permanently deleted.";
            }
            catch (Exception) // Catch ALL errors (including the one you just got)
            {
                // 2. Fallback to Archive if ANYTHING goes wrong with hard delete
                // Reload the product from DB to get a clean state
                _context.Entry(product).State = EntityState.Unchanged;

                product.IsActive = 0;
                _context.Update(product);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Product successfully archived (could not be hard-deleted due to existing history).";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================
        // POST: Inventory/Unarchive/5 (RESTORE)
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Unarchive(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = 1;
            _context.Update(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product successfully restored (unarchived).";
            return RedirectToAction(nameof(Index));
        }
    }
}