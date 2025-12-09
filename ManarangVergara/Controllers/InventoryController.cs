using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Helpers;

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

        // ... [Index and Archives methods remain the same as before] ...
        // (Omitting Index/Archives to save space, paste previous versions here if needed)
        // Only showing the CHANGED methods below:

        // GET: Inventory/Create
        // [InventoryController.cs]

        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> Create()
        {
            // -----------------------------------------------------------------
            // LOGIC: GENERATE SEQUENTIAL BATCH (B-yyyyMMdd-XXX)
            // -----------------------------------------------------------------

            // 1. Get today's date prefix
            string datePart = DateTime.Now.ToString("yyyyMMdd");
            string prefix = $"B-{datePart}-";

            // 2. Find the HIGHEST existing batch for today
            // We sort by InventoryId descending to get the absolute latest entry.
            var lastBatchItem = await _context.Inventories
                .Where(i => i.BatchNumber.StartsWith(prefix))
                .OrderByDescending(i => i.InventoryId)
                .FirstOrDefaultAsync();

            string nextSequence = "001"; // Default start for a new day

            if (lastBatchItem != null)
            {
                // 3. Extract the last 3 digits (e.g., "012" from "B-20251202-012")
                string currentSeqStr = lastBatchItem.BatchNumber.Substring(lastBatchItem.BatchNumber.Length - 3);

                // 4. Do the math: 12 + 1 = 13
                if (int.TryParse(currentSeqStr, out int currentSeqInt))
                {
                    nextSequence = (currentSeqInt + 1).ToString("D3"); // Format as "013"
                }
            }

            string generatedBatch = $"{prefix}{nextSequence}";

            // -----------------------------------------------------------------
            // PREPARE THE VIEW
            // -----------------------------------------------------------------
            var viewModel = new ProductFormViewModel
            {
                CategoryList = await _context.ProductCategories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CategoryName)
                    .Select(c => new SelectListItem
                    {
                        Value = c.CategoryId.ToString(),
                        Text = c.CategoryName
                    })
                    .ToListAsync(),

                // Uses your helper method for the dropdown
                SupplierList = await GetSupplierListAsync(),

                BatchNumber = generatedBatch, // <--- The Sequential Number
                ExpiryDate = DateTime.Today.AddDays(1)
            };

            return View(viewModel);
        }

        // ============================================================
        // 2. CREATE (POST) - Handles New Supplier Logic
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> Create(ProductFormViewModel model)
        {
            // Custom Validation: If SupplierId is -1 (Other), NewSupplierName is REQUIRED
            if (model.SupplierId == -1 && string.IsNullOrWhiteSpace(model.NewSupplierName))
            {
                ModelState.AddModelError("NewSupplierName", "Please enter the new supplier name.");
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    int finalSupplierId = model.SupplierId ?? 0;

                    // 1. HANDLE NEW SUPPLIER
                    if (model.SupplierId == -1)
                    {
                        var newSupplier = new Supplier
                        {
                            Name = model.NewSupplierName!,
                            ContactInfo = "Pending", // Default placeholder
                            IsActive = true,
                            LastUpdated = DateTime.Now
                        };
                        _context.Suppliers.Add(newSupplier);
                        await _context.SaveChangesAsync(); // Save to get the ID
                        finalSupplierId = newSupplier.SupplierId;
                    }

                    // 2. CREATE PRODUCT
                    var newProduct = new Product
                    {
                        Name = model.Name,
                        Description = model.Description ?? "",
                        Manufacturer = model.Manufacturer,
                        CategoryId = model.CategoryId,
                        SupplierId = finalSupplierId,
                        CreatedAt = DateTime.Now
                    };
                    _context.Products.Add(newProduct);
                    await _context.SaveChangesAsync();

                    // 3. CREATE INVENTORY BATCH
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

                    // 4. LOG THE ADDITION (Initial Stock)
                    var log = new ItemLog
                    {
                        ProductId = newProduct.ProductId,
                        Action = "Added",
                        Quantity = model.Quantity,
                        EmployeeId = int.Parse(User.FindFirst("EmployeeId")!.Value),
                        LoggedAt = DateTime.Now,
                        LogReason = "Initial Stock (New Product)"
                    };
                    _context.ItemLogs.Add(log);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = $"{model.Name} added successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "Error: " + ex.Message);
                }
            }

            // Reload Lists on Error
            model.CategoryList = await _context.ProductCategories.Where(c => c.IsActive).Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.CategoryName }).ToListAsync();
            model.SupplierList = await GetSupplierListAsync();
            return View(model);
        }

        // Helper to build the list with "Add New" option
        private async Task<List<SelectListItem>> GetSupplierListAsync()
        {
            var list = await _context.Suppliers.Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem { Value = s.SupplierId.ToString(), Text = s.Name })
                .ToListAsync();

            // Append the "Add New" option at the bottom
            list.Add(new SelectListItem { Value = "-1", Text = "+ Add New Supplier..." });
            return list;
        }

        // ============================================================
        // 3. ADJUST STOCK - Records Losses for Reports
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> AdjustStock(int id, int adjustmentQty, string reason)
        {
            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();

            var inventory = await _context.Inventories
                .Where(i => i.ProductId == id)
                .OrderByDescending(i => i.LastUpdated)
                .FirstOrDefaultAsync();

            if (inventory == null) return RedirectToAction(nameof(Index));

            if (inventory.Quantity + adjustmentQty < 0)
            {
                TempData["ErrorMessage"] = "Cannot remove more stock than available.";
                return RedirectToAction(nameof(Index));
            }

            inventory.Quantity += adjustmentQty;
            inventory.LastUpdated = DateTime.Now;

            // ACTION LOGIC:
            // If negative (adjustmentQty < 0), it is "Removed".
            // The ReportsController looks for "Removed" to calculate Profit Loss.
            var log = new ItemLog
            {
                ProductId = id,
                Action = adjustmentQty > 0 ? "Added" : "Removed",
                Quantity = Math.Abs(adjustmentQty),
                EmployeeId = int.Parse(employeeIdStr),
                LoggedAt = DateTime.Now,
                LogReason = "Manual Adjustment: " + reason // e.g. "Damaged", "Expired"
            };
            _context.ItemLogs.Add(log);

            _context.Update(inventory);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock updated.";
            return RedirectToAction(nameof(Index));
        }

        // ... [Include Index, Archives, Edit, Delete methods from previous uploads if needed] ...
        // (Just ensure the whole class structure is maintained)

        // --- RE-INCLUDING INDEX FOR COMPLETENESS ---
        public async Task<IActionResult> Index(string searchString, string sortOrder, bool showArchived = false, int? pageNumber = 1)
        {
            // (Standard Index Code - same as previous turn)
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["ShowArchived"] = showArchived;
            ViewData["NameSortParm"] = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewData["CatSortParm"] = sortOrder == "Category" ? "cat_desc" : "Category";
            ViewData["StockSortParm"] = sortOrder == "Stock" ? "stock_desc" : "Stock";
            ViewData["PriceSortParm"] = sortOrder == "Price" ? "price_desc" : "Price";
            ViewData["ExpirySortParm"] = sortOrder == "Expiry" ? "expiry_desc" : "Expiry";

            var query = _context.Products.Include(p => p.Category).Include(p => p.Inventories).AsQueryable();

            if (!showArchived) query = query.Where(p => p.IsActive == 1);

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p => p.Name.Contains(searchString) || p.Category.CategoryName.Contains(searchString) || p.Manufacturer.Contains(searchString));
            }

            query = sortOrder switch
            {
                "name_desc" => query.OrderByDescending(p => p.Name),
                "Category" => query.OrderBy(p => p.Category.CategoryName),
                "cat_desc" => query.OrderByDescending(p => p.Category.CategoryName),
                _ => query.OrderBy(p => p.Name),
            };

            var rawData = await query.ToListAsync();
            var mappedList = rawData.Select(p => {
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

            if (sortOrder == "Stock") mappedList = mappedList.OrderBy(x => x.Quantity).ToList();
            else if (sortOrder == "stock_desc") mappedList = mappedList.OrderByDescending(x => x.Quantity).ToList();
            else if (sortOrder == "Price") mappedList = mappedList.OrderBy(x => x.SellingPrice).ToList();
            else if (sortOrder == "price_desc") mappedList = mappedList.OrderByDescending(x => x.SellingPrice).ToList();
            else if (sortOrder == "Expiry") mappedList = mappedList.OrderBy(x => x.ExpiryDate).ToList();
            else if (sortOrder == "expiry_desc") mappedList = mappedList.OrderByDescending(x => x.ExpiryDate).ToList();

            return View(PaginatedList<InventoryListViewModel>.Create(mappedList.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ... (Keep Edit, Archives, Delete methods)

        // Re-include History, Archives, Edit, Delete, Unarchive logic as standard.
        // For brevity, assuming you have them from previous uploads. If you need the FULL 500 lines, tell me.
        public async Task<IActionResult> Archives(string searchString, string sortOrder, int? pageNumber = 1)
        {
            // ... same as before
            var query = _context.Products.Include(p => p.Category).Include(p => p.Inventories).Include(p => p.SalesItems).Include(p => p.PurchaseOrders).Where(p => p.IsActive == 0).AsQueryable();
            if (!string.IsNullOrEmpty(searchString)) query = query.Where(p => p.Name.Contains(searchString));
            var list = await query.ToListAsync();
            var mapped = list.Select(p => new InventoryListViewModel { ProductId = p.ProductId, ProductName = p.Name, CategoryName = p.Category?.CategoryName ?? "N/A", Quantity = p.Inventories.Sum(i => i.Quantity), Status = "Archived", CanDeletePermanently = !p.SalesItems.Any() && !p.PurchaseOrders.Any() });
            return View(PaginatedList<InventoryListViewModel>.Create(mapped.AsQueryable(), pageNumber ?? 1, 10));
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> ArchiveConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();
            product.IsActive = 0;
            _context.Update(product);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> Unarchive(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();
            product.IsActive = 1;
            _context.Update(product);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Archives));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var product = await _context.Products.Include(p => p.Inventories).FirstOrDefaultAsync(p => p.ProductId == id);
            if (product == null) return NotFound();
            var latestInv = product.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault();
            var vm = new ProductFormViewModel
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
                SupplierList = await GetSupplierListAsync()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager")]
        public async Task<IActionResult> Edit(int id, ProductFormViewModel model)
        {
            if (id != model.ProductId) return NotFound();
            ModelState.Remove("BatchNumber"); ModelState.Remove("ExpiryDate"); ModelState.Remove("Quantity");
            if (ModelState.IsValid)
            {
                if (model.SupplierId == -1 && !string.IsNullOrEmpty(model.NewSupplierName))
                {
                    var newSup = new Supplier { Name = model.NewSupplierName, IsActive = true, ContactInfo = "Pending" };
                    _context.Suppliers.Add(newSup); await _context.SaveChangesAsync(); model.SupplierId = newSup.SupplierId;
                }
                var product = await _context.Products.FindAsync(id);
                product.Name = model.Name; product.Description = model.Description; product.Manufacturer = model.Manufacturer; product.CategoryId = model.CategoryId; product.SupplierId = model.SupplierId ?? product.SupplierId;
                _context.Update(product);
                var invs = await _context.Inventories.Where(i => i.ProductId == id).ToListAsync();
                foreach (var i in invs) { i.CostPrice = model.CostPrice; i.SellingPrice = model.SellingPrice; _context.Update(i); }
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        public async Task<IActionResult> History(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();
            ViewData["ProductName"] = product.Name;
            var logs = await _context.ItemLogs.Include(l => l.Employee).Where(l => l.ProductId == id).OrderByDescending(l => l.LoggedAt).ToListAsync();
            return View(logs);
        }
    }
}