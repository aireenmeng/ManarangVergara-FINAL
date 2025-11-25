using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // INVENTORY CONTROLLER (stock management)
    // ============================================================
    // manages product catalog, stock levels, and pricing.
    // handles "soft delete" (archiving) and tracks audit logs for stock changes.
    [Authorize]
    public class InventoryController : Controller
    {
        private readonly PharmacyDbContext _context;

        public InventoryController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. ACTIVE INVENTORY LIST
        // ============================================================

        // get: lists all active products with filtering, sorting, and pagination.
        public async Task<IActionResult> Index(string searchString, string sortOrder, bool showArchived = false, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            // setup sorting parameters for table headers
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["StockSortParm"] = sortOrder == "Stock" ? "stock_desc" : "Stock";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";

            // fix sorting toggle logic for expiry date
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            // eager load related data (category and inventory details)
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .AsQueryable();

            // 1. filter (active/archived)
            if (!showArchived) query = query.Where(p => p.IsActive == 1);

            // 2. search (by product name, category, or manufacturer)
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString)
                                      || p.Category.CategoryName.Contains(searchString)
                                      || p.Manufacturer.Contains(searchString));
            }

            // 3. sort logic (basic fields sorted in database)
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Name),
                "Category" => query.OrderBy(p => p.Category.CategoryName),
                "cat_desc" => query.OrderByDescending(p => p.Category.CategoryName),
                _ => query.OrderBy(p => p.Name),
            };

            // 4. execute query & map to viewmodel
            // we fetch raw data first, then calculate statuses in memory
            var rawData = await query.ToListAsync();

            var mappedList = rawData.Select(p =>
            {
                var totalQuantity = p.Inventories.Sum(i => i.Quantity);
                var earliestExpiry = p.Inventories.Any() ? p.Inventories.Min(i => i.ExpiryDate) : DateOnly.MaxValue;

                string status = "Active";
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
            }).ToList();

            // re-apply sorting for calculated fields (stock/price/expiry)
            if (sortOrder == "Stock") mappedList = mappedList.OrderBy(x => x.Quantity).ToList();
            else if (sortOrder == "stock_desc") mappedList = mappedList.OrderByDescending(x => x.Quantity).ToList();
            else if (sortOrder == "Price") mappedList = mappedList.OrderBy(x => x.SellingPrice).ToList();
            else if (sortOrder == "price_desc") mappedList = mappedList.OrderByDescending(x => x.SellingPrice).ToList();
            else if (sortOrder == "Expiry") mappedList = mappedList.OrderBy(x => x.ExpiryDate).ToList();
            else if (sortOrder == "expiry_desc") mappedList = mappedList.OrderByDescending(x => x.ExpiryDate).ToList();

            // 5. paginate (10 items per page)
            int pageSize = 10;
            return View(PaginatedList<InventoryListViewModel>.Create(mappedList.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 2. ARCHIVED INVENTORY LIST
        // ============================================================

        // get: separate view for deleted items.
        public async Task<IActionResult> Archives(string searchString, string sortOrder, int? pageNumber = 1)
        {
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            // filter only inactive items
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .Include(p => p.SalesItems)      // <--- ADD THIS: needed to check for sales
                .Include(p => p.PurchaseOrders)  // <--- ADD THIS: needed to check for supplier orders
                .Where(p => p.IsActive == 0)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString)
                                      || p.Category.CategoryName.Contains(searchString)
                                      || p.Manufacturer.Contains(searchString));
            }

            // default sort: most recently created/archived first
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Name),
                "Category" => query.OrderBy(p => p.Category.CategoryName),
                "cat_desc" => query.OrderByDescending(p => p.Category.CategoryName),
                "Expiry" => query.OrderBy(p => p.Inventories.Min(i => i.ExpiryDate)),
                "expiry_desc" => query.OrderByDescending(p => p.Inventories.Min(i => i.ExpiryDate)),
                _ => query.OrderByDescending(p => p.CreatedAt)
            };

            var rawData = await query.ToListAsync();

            var list = rawData.Select(p => new InventoryListViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.Name,
                CategoryName = p.Category?.CategoryName ?? "N/A",
                Manufacturer = p.Manufacturer,
                Quantity = p.Inventories.Sum(i => i.Quantity),
                SellingPrice = p.Inventories.FirstOrDefault()?.SellingPrice ?? 0,
                Status = "Archived",

                // --- LOGIC: SAFE TO DELETE? ---
                // safe if: no sales history AND no supplier orders
                CanDeletePermanently = !p.SalesItems.Any() && !p.PurchaseOrders.Any()
            });

            int pageSize = 10;
            return View(PaginatedList<InventoryListViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 3. ADJUST STOCK (AUDIT LOG)
        // ============================================================

        // post: handles adding/removing stock securely.
        // requires a reason for the change, which is logged in 'item_logs'.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> AdjustStock(int id, int adjustmentQty, string reason)
        {
            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();

            // find the latest active inventory batch to adjust
            var inventory = await _context.Inventories
                .Where(i => i.ProductId == id)
                .OrderByDescending(i => i.LastUpdated)
                .FirstOrDefaultAsync();

            if (inventory == null)
            {
                TempData["ErrorMessage"] = "No inventory batch found. Please use 'Add New Product'.";
                return RedirectToAction(nameof(Index));
            }

            // prevent negative stock
            if (inventory.Quantity + adjustmentQty < 0)
            {
                TempData["ErrorMessage"] = "Cannot remove more stock than available.";
                return RedirectToAction(nameof(Index));
            }

            // 1. update quantity
            inventory.Quantity += adjustmentQty;
            inventory.LastUpdated = DateTime.Now;

            // 2. create audit log entry
            var log = new ItemLog
            {
                ProductId = id,
                Action = adjustmentQty > 0 ? "Added" : "Removed",
                Quantity = Math.Abs(adjustmentQty),
                EmployeeId = int.Parse(employeeIdStr),
                LoggedAt = DateTime.Now,
                LogReason = "Manual Adjustment: " + reason
            };
            _context.ItemLogs.Add(log);

            // 3. save changes
            _context.Update(inventory);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock adjusted successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 4. CREATE NEW PRODUCT
        // ============================================================

        // get: show the form
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Create()
        {
            var viewModel = new ProductFormViewModel
            {
                CategoryList = await _context.ProductCategories.OrderBy(c => c.CategoryName).Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync(),
                SupplierList = await _context.Suppliers.OrderBy(s => s.Name).Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync(),
                // generate a default batch number
                BatchNumber = $"B-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100, 999)}"
            };
            return View(viewModel);
        }

        // post: save new product and initial inventory
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
                    // 1. create product master record
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

                    // 2. create initial inventory batch
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

            // reload dropdowns if validation failed
            model.CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync();
            model.SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync();
            return View(model);
        }

        // ============================================================
        // 5. EDIT PRODUCT
        // ============================================================

        // get: load product details for editing
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            // get latest prices to show in form
            var latestInv = product.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault();

            var viewModel = new ProductFormViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Manufacturer = product.Manufacturer,
                CategoryId = product.CategoryId,
                SupplierId = product.SupplierId,
                CostPrice = latestInv?.CostPrice ?? 0,
                SellingPrice = latestInv?.SellingPrice ?? 0,
                Quantity = product.Inventories.Sum(i => i.Quantity),

                CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync(),
                SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync()
            };

            return View(viewModel);
        }

        // post: update product details and pricing
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Edit(int id, ProductFormViewModel model)
        {
            if (id != model.ProductId) return NotFound();

            // remove validation for fields that are not editable here (batch/qty)
            ModelState.Remove("BatchNumber");
            ModelState.Remove("ExpiryDate");
            ModelState.Remove("Quantity");

            if (ModelState.IsValid)
            {
                // 1. update product info
                var product = await _context.Products.FindAsync(id);
                product.Name = model.Name;
                product.Description = model.Description;
                product.Manufacturer = model.Manufacturer;
                product.CategoryId = model.CategoryId;
                product.SupplierId = model.SupplierId;
                _context.Update(product);

                // 2. update pricing on all active batches
                var inventories = await _context.Inventories.Where(i => i.ProductId == id).ToListAsync();
                foreach (var inv in inventories)
                {
                    inv.CostPrice = model.CostPrice;
                    inv.SellingPrice = model.SellingPrice;
                    _context.Update(inv);
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Product updated.";
                return RedirectToAction(nameof(Index));
            }

            model.CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync();
            model.SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync();
            return View(model);
        }

        // ============================================================
        // 6. ARCHIVE (SOFT DELETE)
        // ============================================================

        // post: moves product to archive by setting IsActive = 0
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = 0;
            _context.Update(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product successfully archived.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 7. RESTORE (UNARCHIVE)
        // ============================================================

        // post: restores product to active list
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Unarchive(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = 1;
            _context.Update(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product restored successfully.";
            return RedirectToAction(nameof(Archives));
        }

        // ============================================================
        // 8. VIEW HISTORY
        // ============================================================

        // get: shows the audit trail (item_logs) for a specific product
        public async Task<IActionResult> History(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            ViewData["ProductName"] = product.Name;

            var logs = await _context.ItemLogs
                .Include(l => l.Employee)
                .Where(l => l.ProductId == id)
                .OrderByDescending(l => l.LoggedAt)
                .ToListAsync();

            return View(logs);
        }

        // ============================================================
        // 9. PERMANENT DELETE (clean up mistakes)
        // ============================================================

        // post: permanently removes a product from the database.
        // only allowed if the product has NO sales history.
        // removes associated inventory and logs to prevent database constraint errors.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")] // Managers cannot permanently delete
        public async Task<IActionResult> DeletePermanent(int id)
        {
            var product = await _context.Products
                .Include(p => p.SalesItems)
                .Include(p => p.PurchaseOrders)
                .Include(p => p.Inventories)
                .Include(p => p.ItemLogs)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            // double security check
            if (product.SalesItems.Any() || product.PurchaseOrders.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete: This product has sales or order history.";
                return RedirectToAction(nameof(Archives));
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. remove dependency: inventory batches
                _context.Inventories.RemoveRange(product.Inventories);

                // 2. remove dependency: audit logs
                _context.ItemLogs.RemoveRange(product.ItemLogs);

                // 3. remove the product itself
                _context.Products.Remove(product);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = "Product permanently deleted.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Delete failed: " + ex.Message;
            }

            return RedirectToAction(nameof(Archives));
        }
    }
}