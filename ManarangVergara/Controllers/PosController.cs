using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Helpers; // Needed for our new Session helper
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
    [Authorize] // Anyone logged in can access POS
    public class PosController : Controller
    {
        private readonly PharmacyDbContext _context;
        // Key used to store cart in session
        private const string CART_SESSION_KEY = "CurrentCart";

        public PosController(PharmacyDbContext context)
        {
            _context = context;
        }

        // =========================================
        // GET: POS Screen (Main UI)
        // =========================================
        public async Task<IActionResult> Index()
        {
            // 1. Get current cart from session (or create empty if none)
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();

            // 2. Prepare ViewModel
            var viewModel = new TransactionViewModel
            {
                Cart = cart
            };

            // 3. Load Product Dropdown for SEARCH (Only active products with stock > 0)
            // We join with Inventory to get current stock and price
            var products = await _context.Products
                .Include(p => p.Inventories)
                .Where(p => p.IsActive == 1 && p.Inventories.Sum(i => i.Quantity) > 0)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.ProductId,
                    DisplayText = $"{p.Name} - ₱{p.Inventories.FirstOrDefault().SellingPrice:N2} (Stock: {p.Inventories.Sum(i => i.Quantity)})"
                })
                .ToListAsync();

            ViewData["ProductList"] = new SelectList(products, "ProductId", "DisplayText");

            return View(viewModel);
        }

        // POST: Add Item to Cart (Now with Discount!)
        [HttpPost]
        public async Task<IActionResult> AddToCart(int selectedProductId, int selectedQuantity, decimal selectedDiscount)
        {
            if (selectedQuantity < 1) selectedQuantity = 1;

            var product = await _context.Products
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.ProductId == selectedProductId);

            if (product == null) return NotFound();

            int currentStock = product.Inventories.Sum(i => i.Quantity);
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();

            var existingItem = cart.FirstOrDefault(x => x.ProductId == selectedProductId);
            int quantityInCart = existingItem?.Quantity ?? 0;

            if (selectedQuantity + quantityInCart > currentStock)
            {
                TempData["ErrorMessage"] = $"Not enough stock! Available: {currentStock}";
                return RedirectToAction(nameof(Index));
            }

            if (existingItem != null)
            {
                existingItem.Quantity += selectedQuantity;
                // Note: We don't update discount on existing items to avoid confusion, 
                // or you could overwrite it here if you prefer.
            }
            else
            {
                cart.Add(new CartItemViewModel
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    Price = product.Inventories.First().SellingPrice,
                    Quantity = selectedQuantity,
                    DiscountRate = selectedDiscount // <-- SAVE THE DISCOUNT
                });
            }

            HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);
            return RedirectToAction(nameof(Index));
        }

        // =========================================
        // POST: Remove Item from Cart
        // =========================================
        [HttpPost]
        public IActionResult RemoveFromCart(int productId)
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();
            var itemToRemove = cart.FirstOrDefault(x => x.ProductId == productId);
            if (itemToRemove != null)
            {
                cart.Remove(itemToRemove);
                HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);
            }
            return RedirectToAction(nameof(Index));
        }

        // =========================================
        // POST: Complete Sale (Checkout)
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteSale(TransactionViewModel model)
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY);
            if (cart == null || !cart.Any())
            {
                TempData["ErrorMessage"] = "Cart is empty!";
                return RedirectToAction(nameof(Index));
            }

            // Get current logged-in cashier ID
            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();
            int cashierId = int.Parse(employeeIdStr);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Create Transaction Record
                var newSale = new Transaction
                {
                    SalesDate = DateTime.Now,
                    TotalAmount = cart.Sum(x => x.Total),
                    PaymentMethod = model.PaymentMethod,
                    Status = "Completed",
                    EmployeeId = cashierId
                };
                _context.Transactions.Add(newSale);
                await _context.SaveChangesAsync();

                // 2. Process Each Cart Item
                foreach (var item in cart)
                {
                    // A. Add to SalesItems table
                    var salesItem = new SalesItem
                    {
                        SalesId = newSale.SalesId,
                        ProductId = item.ProductId,
                        QuantitySold = item.Quantity,
                        Price = item.Price,
                        Discount = item.DiscountAmount // Saved calculated discount to DB
                    };
                    _context.SalesItems.Add(salesItem);

                    // B. DEDUCT STOCK FROM INVENTORY (FIFO - First In, First Out)
                    int qtyToDeduct = item.Quantity;
                    // Get batches ordered by expiry date (sell oldest first)
                    var batches = await _context.Inventories
                        .Where(i => i.ProductId == item.ProductId && i.Quantity > 0)
                        .OrderBy(i => i.ExpiryDate)
                        .ToListAsync();

                    foreach (var batch in batches)
                    {
                        if (qtyToDeduct <= 0) break;

                        if (batch.Quantity >= qtyToDeduct)
                        {
                            // This batch has enough to cover the remaining need
                            batch.Quantity -= qtyToDeduct;
                            qtyToDeduct = 0;
                        }
                        else
                        {
                            // Take everything from this batch and move to next
                            qtyToDeduct -= batch.Quantity;
                            batch.Quantity = 0;
                        }
                        _context.Inventories.Update(batch);
                    }

                    if (qtyToDeduct > 0)
                    {
                        throw new Exception($"Critical Error: Stock mismatch for {item.ProductName} during checkout.");
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 3. Clear Cart & Redirect to Receipt
                HttpContext.Session.Remove(CART_SESSION_KEY);
                TempData["SuccessMessage"] = "Transaction completed successfully!";
                return RedirectToAction("Details", "Transactions", new { id = newSale.SalesId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Dig down to find the REAL error from the database
                string detailedError = ex.InnerException?.Message ?? ex.Message;
                TempData["ErrorMessage"] = $"Transaction Failed: {detailedError}";
                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================
        // POST: Hold Transaction (Save as Pending)
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HoldTransaction()
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY);
            if (cart == null || !cart.Any())
            {
                TempData["ErrorMessage"] = "Cannot hold an empty cart.";
                return RedirectToAction(nameof(Index));
            }

            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();
            int cashierId = int.Parse(employeeIdStr);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Create 'Pending' Transaction
                var newSale = new Transaction
                {
                    SalesDate = DateTime.Now,
                    TotalAmount = cart.Sum(x => x.Total),
                    PaymentMethod = "Pending", // Placeholder
                    Status = "Pending",
                    EmployeeId = cashierId
                };
                _context.Transactions.Add(newSale);
                await _context.SaveChangesAsync();

                // 2. Save Items (But DO NOT deduct stock yet)
                foreach (var item in cart)
                {
                    _context.SalesItems.Add(new SalesItem
                    {
                        SalesId = newSale.SalesId,
                        ProductId = item.ProductId,
                        QuantitySold = item.Quantity,
                        Price = item.Price,
                        Discount = 0
                    });
                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                HttpContext.Session.Remove(CART_SESSION_KEY);
                TempData["SuccessMessage"] = $"Transaction #{newSale.SalesId} put on hold.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Failed to hold: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================
        // POST: Resume Transaction (Load back to Cart)
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeTransaction(int id)
        {
            // 1. Find the Pending Transaction
            var pendingSale = await _context.Transactions
                .Include(t => t.SalesItems)
                .ThenInclude(si => si.Product) // Need product details for names/prices
                .FirstOrDefaultAsync(t => t.SalesId == id && t.Status == "Pending");

            if (pendingSale == null)
            {
                TempData["ErrorMessage"] = "Cannot resume: Transaction not found or not pending.";
                return RedirectToAction("Index", "Transactions");
            }

            // 2. Convert DB SalesItems back to Cart ViewModels
            var cart = new List<CartItemViewModel>();
            foreach (var item in pendingSale.SalesItems)
            {
                cart.Add(new CartItemViewModel
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    Price = item.Price,
                    Quantity = item.QuantitySold
                });
            }

            // 3. Load into Session (overwriting any current cart)
            HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);

            // 4. Delete the old 'Pending' record so it doesn't get duplicated when we finish it later
            _context.SalesItems.RemoveRange(pendingSale.SalesItems);
            _context.Transactions.Remove(pendingSale);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Transaction #{id} resumed. Ready to checkout.";
            return RedirectToAction(nameof(Index));
        }
    }
}