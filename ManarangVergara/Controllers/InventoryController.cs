using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Helpers; // <--- Add this namespace

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

        public async Task<IActionResult> Index(string searchString, string sortOrder, bool showArchived = false, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["StockSortParm"] = sortOrder == "Stock" ? "stock_desc" : "Stock";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .AsQueryable();

            // 1. Filter (Active/Archived)
            if (!showArchived) query = query.Where(p => p.IsActive == 1);

            // 2. Search
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString) || p.Category.CategoryName.Contains(searchString));
            }

            // 3. Sort Logic (Done BEFORE materializing the list to optimize SQL)
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Name),
                "Category" => query.OrderBy(p => p.Category.CategoryName),
                "cat_desc" => query.OrderByDescending(p => p.Category.CategoryName),
                // Note: Stock sorting is complex in SQL, keeping default for now to avoid errors
                _ => query.OrderBy(p => p.Name),
            };

            // 4. EXECUTE QUERY & MAP TO VIEWMODEL
            // We fetch the raw data first, then map it. 
            // (In complex apps, we map before fetching, but this is safer for your calculated "Status" fields)

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

            // Re-apply sorting for calculated fields (Stock/Price) if needed
            if (sortOrder == "Stock") mappedList = mappedList.OrderBy(x => x.Quantity).ToList();
            else if (sortOrder == "stock_desc") mappedList = mappedList.OrderByDescending(x => x.Quantity).ToList();
            else if (sortOrder == "Price") mappedList = mappedList.OrderBy(x => x.SellingPrice).ToList();
            else if (sortOrder == "price_desc") mappedList = mappedList.OrderByDescending(x => x.SellingPrice).ToList();

            // 5. PAGINATE (15 items per page)
            int pageSize = 10;
            return View(PaginatedList<InventoryListViewModel>.Create(mappedList.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // 1. NEW: Archives Page (Paginated & Sorted)
        public async Task<IActionResult> Archives(string searchString, string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            // Filter only INACTIVE items
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Inventories)
                .Where(p => p.IsActive == 0) // <--- Key difference
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString) || p.Category.CategoryName.Contains(searchString));
            }

            // Default sort: Most recently archived/updated first
            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Name),
                "Category" => query.OrderBy(p => p.Category.CategoryName),
                "cat_desc" => query.OrderByDescending(p => p.Category.CategoryName),
                "Expiry" => query.OrderBy(p => p.Inventories.Min(i => i.ExpiryDate)),
                "expiry_desc" => query.OrderByDescending(p => p.Inventories.Min(i => i.ExpiryDate)),
                _ => query.OrderByDescending(p => p.CreatedAt) // Or LastUpdated if you have it
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
                Status = "Archived"
            });

            int pageSize = 10;
            return View(PaginatedList<InventoryListViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // --- NEW: ADJUST STOCK ACTION ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> AdjustStock(int id, int adjustmentQty, string reason)
        {
            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();

            // Find the best batch to adjust (Simplification: Adjust the one with largest qty, or last updated)
            // For negative adjustment (removing stock), we normally take from oldest. 
            // For positive, we add to latest or create new.
            // To keep it simple for this project: We adjust the LATEST updated batch.
            var inventory = await _context.Inventories
                .Where(i => i.ProductId == id)
                .OrderByDescending(i => i.LastUpdated)
                .FirstOrDefaultAsync();

            if (inventory == null)
            {
                TempData["ErrorMessage"] = "No inventory batch found to adjust. Please use 'Add New Product' or DB Tools.";
                return RedirectToAction(nameof(Index));
            }

            // Check if removing more than we have
            if (inventory.Quantity + adjustmentQty < 0)
            {
                TempData["ErrorMessage"] = "Cannot remove more stock than available in the active batch.";
                return RedirectToAction(nameof(Index));
            }

            // 1. Update Quantity
            inventory.Quantity += adjustmentQty;
            inventory.LastUpdated = DateTime.Now;

            // 2. Log the Change
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

            // 3. Save
            _context.Update(inventory);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock adjusted successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Create()
        {
            var viewModel = new ProductFormViewModel
            {
                CategoryList = await _context.ProductCategories.OrderBy(c => c.CategoryName).Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync(),
                SupplierList = await _context.Suppliers.OrderBy(s => s.Name).Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync(),
                BatchNumber = $"B-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100, 999)}"
            };
            return View(viewModel);
        }

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

            model.CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync();
            model.SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync();
            return View(model);
        }

        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            // Get latest prices from inventory
            var latestInv = product.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault();

            var viewModel = new ProductFormViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Manufacturer = product.Manufacturer,
                CategoryId = product.CategoryId,
                SupplierId = product.SupplierId,
                // Pass current prices/qty for display
                CostPrice = latestInv?.CostPrice ?? 0,
                SellingPrice = latestInv?.SellingPrice ?? 0,
                Quantity = product.Inventories.Sum(i => i.Quantity),

                CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync(),
                SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync()
            };

            return View(viewModel);
        }

        // --- UPDATE: Handles Price Updates ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Edit(int id, ProductFormViewModel model)
        {
            if (id != model.ProductId) return NotFound();

            // Ignore Batch/Expiry/Quantity validation for simple edits
            ModelState.Remove("BatchNumber");
            ModelState.Remove("ExpiryDate");
            ModelState.Remove("Quantity");

            if (ModelState.IsValid)
            {
                // 1. Update Product Info
                var product = await _context.Products.FindAsync(id);
                product.Name = model.Name;
                product.Description = model.Description;
                product.Manufacturer = model.Manufacturer;
                product.CategoryId = model.CategoryId;
                product.SupplierId = model.SupplierId;
                _context.Update(product);

                // 2. Update Pricing on ALL batches (for simplicity)
                var inventories = await _context.Inventories.Where(i => i.ProductId == id).ToListAsync();
                foreach (var inv in inventories)
                {
                    inv.CostPrice = model.CostPrice;
                    inv.SellingPrice = model.SellingPrice;
                    _context.Update(inv);
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Product details and pricing updated.";
                return RedirectToAction(nameof(Index));
            }

            model.CategoryList = await _context.ProductCategories.Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync();
            model.SupplierList = await _context.Suppliers.Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name }).ToListAsync();
            return View(model);
        }

        // POST: Archive (Soft Delete)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            // Simply Archive it. Safer than checking for hard delete eligibility.
            product.IsActive = 0;
            _context.Update(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product successfully archived.";
            return RedirectToAction(nameof(Index));
        }

        // 2. UPDATE: Unarchive (Restore)
        // Ensure this updates the timestamp so it appears at the TOP of the Active list
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Unarchive(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = 1;
            // product.LastUpdated = DateTime.Now; // <--- Ensure your Product model has this field!
            // If Product doesn't have LastUpdated, rely on sorting logic or add the column.

            _context.Update(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product restored successfully.";
            return RedirectToAction(nameof(Archives)); // Stay on Archive page or go to Index
        }

        // GET: Inventory/History/5
        public async Task<IActionResult> History(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            ViewData["ProductName"] = product.Name;

            var logs = await _context.ItemLogs
                .Include(l => l.Employee) // Include Employee to see WHO did it
                .Where(l => l.ProductId == id)
                .OrderByDescending(l => l.LoggedAt)
                .ToListAsync();

            return View(logs);
        }
    }
}