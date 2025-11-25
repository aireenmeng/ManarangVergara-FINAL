using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Helpers;
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // POS CONTROLLER (point of sale)
    // ============================================================
    // handles the selling process. manages the shopping cart, 
    // calculates totals, deducts stock, and saves transactions.
    // accessible to all logged-in staff (cashiers, managers, etc.).
    [Authorize]
    public class PosController : Controller
    {
        private readonly PharmacyDbContext _context;

        // key used to identify the user's shopping cart in the server's memory (session).
        private const string CART_SESSION_KEY = "CurrentCart";

        public PosController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. POS MAIN SCREEN
        // ============================================================

        // get: loads the main selling interface.
        // retrieves the current cart from session and loads the product list for the search bar.
        public async Task<IActionResult> Index()
        {
            // 1. get current cart from session
            // if no cart exists, create a new empty list so the page doesn't crash.
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();

            // 2. prepare viewmodel to send data to the page
            var viewModel = new TransactionViewModel
            {
                Cart = cart
            };

            // 3. load product dropdown for search
            // we only load products that are active AND have stock > 0 to prevent selling out-of-stock items.
            // eager load 'inventories' to get the current selling price.
            var products = await _context.Products
                .Include(p => p.Inventories)
                .Where(p => p.IsActive == 1 && p.Inventories.Sum(i => i.Quantity) > 0)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.ProductId,
                    // format the text to show name, price, and current stock level
                    DisplayText = $"{p.Name} - ₱{p.Inventories.FirstOrDefault().SellingPrice:N2} (Stock: {p.Inventories.Sum(i => i.Quantity)})"
                })
                .ToListAsync();

            // create a select list for the dropdown menu
            ViewData["ProductList"] = new SelectList(products, "ProductId", "DisplayText");

            return View(viewModel);
        }

        // ============================================================
        // 2. ADD ITEM TO CART
        // ============================================================

        // post: adds a selected product to the session cart.
        // checks if there is enough stock before adding.
        [HttpPost]
        public async Task<IActionResult> AddToCart(int selectedProductId, int selectedQuantity)
        {
            // basic validation: quantity must be at least 1
            if (selectedQuantity < 1) selectedQuantity = 1;

            // find the product in the database
            var product = await _context.Products
                .Include(p => p.Inventories)
                .FirstOrDefaultAsync(p => p.ProductId == selectedProductId);

            if (product == null) return NotFound();

            // check total stock available across all batches
            int currentStock = product.Inventories.Sum(i => i.Quantity);

            // retrieve the current cart
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();

            // check if item is already in cart
            var existingItem = cart.FirstOrDefault(x => x.ProductId == selectedProductId);
            int quantityInCart = existingItem?.Quantity ?? 0;

            // prevent adding more than available stock
            if (selectedQuantity + quantityInCart > currentStock)
            {
                TempData["ErrorMessage"] = $"Not enough stock! Available: {currentStock}";
                return RedirectToAction(nameof(Index));
            }

            // update cart: if exists, increase qty. if new, add to list.
            if (existingItem != null)
            {
                existingItem.Quantity += selectedQuantity;
            }
            else
            {
                cart.Add(new CartItemViewModel
                {
                    ProductId = product.ProductId,
                    ProductName = product.Name,
                    // use the price from the first available batch (fifo logic approximation)
                    Price = product.Inventories.First().SellingPrice,
                    Quantity = selectedQuantity,
                    DiscountRate = 0 // discounts are applied at checkout, not per item
                });
            }

            // save the updated cart back to session memory
            HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 3. REMOVE ITEM FROM CART
        // ============================================================

        // post: removes a specific product from the shopping cart.
        [HttpPost]
        public IActionResult RemoveFromCart(int productId)
        {
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY) ?? new List<CartItemViewModel>();

            var itemToRemove = cart.FirstOrDefault(x => x.ProductId == productId);

            if (itemToRemove != null)
            {
                cart.Remove(itemToRemove);
                // update session after removal
                HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);
            }
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // 4. CHECKOUT (COMPLETE SALE)
        // ============================================================

        // post: finalizes the transaction.
        // calculates totals with discounts, saves to database, and updates inventory stock.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteSale(TransactionViewModel model, string referenceNo, decimal globalDiscountRate)
        {
            // retrieve cart to ensure it's not empty
            var cart = HttpContext.Session.GetObjectFromJson<List<CartItemViewModel>>(CART_SESSION_KEY);
            if (cart == null || !cart.Any())
            {
                TempData["ErrorMessage"] = "Cart is empty!";
                return RedirectToAction(nameof(Index));
            }

            // validate e-wallet reference number (required for gcash/paymaya)
            if ((model.PaymentMethod == "Gcash" || model.PaymentMethod == "PayMaya") && string.IsNullOrWhiteSpace(referenceNo))
            {
                TempData["ErrorMessage"] = "Reference Number is required for GCash/PayMaya.";
                return RedirectToAction(nameof(Index));
            }

            // identify the cashier processing the sale
            var employeeIdStr = User.FindFirst("EmployeeId")?.Value;
            if (string.IsNullOrEmpty(employeeIdStr)) return Forbid();
            int cashierId = int.Parse(employeeIdStr);

            // start a database transaction to ensure data integrity
            // (either everything saves, or nothing saves)
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // calculate financial totals
                decimal rawTotal = cart.Sum(x => x.Price * x.Quantity);
                decimal discountAmount = rawTotal * globalDiscountRate;
                decimal finalTotal = rawTotal - discountAmount;

                // 1. create the main transaction record
                var newSale = new Transaction
                {
                    SalesDate = DateTime.Now,
                    TotalAmount = finalTotal, // save net total
                    PaymentMethod = model.PaymentMethod,
                    Status = "Completed",
                    EmployeeId = cashierId,
                    ReferenceNo = referenceNo
                };
                _context.Transactions.Add(newSale);
                await _context.SaveChangesAsync(); // save to get the generated SalesId

                // 2. process each item in the cart
                foreach (var item in cart)
                {
                    // calculate pro-rated discount for this specific item (for accurate profit reporting)
                    decimal itemTotal = item.Price * item.Quantity;
                    decimal itemDiscount = itemTotal * globalDiscountRate;

                    // save line item to sales_items table
                    var salesItem = new SalesItem
                    {
                        SalesId = newSale.SalesId,
                        ProductId = item.ProductId,
                        QuantitySold = item.Quantity,
                        Price = item.Price,
                        Discount = itemDiscount
                    };
                    _context.SalesItems.Add(salesItem);

                    // --- fifo inventory logic (first-in, first-out) ---
                    // we must deduct stock from the oldest batches first to reduce spoilage.

                    int qtyToDeduct = item.Quantity;

                    // get all batches for this product, ordered by expiry date (oldest first)
                    var batches = await _context.Inventories
                        .Where(i => i.ProductId == item.ProductId && i.Quantity > 0)
                        .OrderBy(i => i.ExpiryDate)
                        .ToListAsync();

                    foreach (var batch in batches)
                    {
                        if (qtyToDeduct <= 0) break; // stop if we have deducted enough

                        if (batch.Quantity >= qtyToDeduct)
                        {
                            // batch has enough stock to cover the remainder
                            batch.Quantity -= qtyToDeduct;
                            qtyToDeduct = 0;
                        }
                        else
                        {
                            // batch is too small, take everything and move to the next batch
                            qtyToDeduct -= batch.Quantity;
                            batch.Quantity = 0;
                        }
                        _context.Inventories.Update(batch);
                    }
                }

                // commit all changes to the database
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // clear the shopping cart
                HttpContext.Session.Remove(CART_SESSION_KEY);

                TempData["SuccessMessage"] = "Transaction completed successfully!";
                // redirect to the receipt page
                return RedirectToAction("Details", "Transactions", new { id = newSale.SalesId });
            }
            catch (Exception ex)
            {
                // rollback changes if anything failed
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Transaction Failed: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ============================================================
        // 5. HOLD TRANSACTION
        // ============================================================

        // post: saves the current cart as "pending" in the database.
        // allows the cashier to serve another customer and resume this later.
        // does NOT deduct stock yet (stock is only deducted on completion).
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
                // create transaction with 'pending' status
                var newSale = new Transaction
                {
                    SalesDate = DateTime.Now,
                    TotalAmount = cart.Sum(x => x.Total),
                    PaymentMethod = "Pending",
                    Status = "Pending",
                    EmployeeId = cashierId
                };
                _context.Transactions.Add(newSale);
                await _context.SaveChangesAsync();

                // save items so we can retrieve them later
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

                // clear cart for next customer
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

        // ============================================================
        // 6. RESUME TRANSACTION
        // ============================================================

        // post: loads a "pending" transaction back into the session cart.
        // deletes the pending database record so it can be re-processed as new.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeTransaction(int id)
        {
            // 1. find the pending transaction and its items
            var pendingSale = await _context.Transactions
                .Include(t => t.SalesItems)
                .ThenInclude(si => si.Product) // need product names for display
                .FirstOrDefaultAsync(t => t.SalesId == id && t.Status == "Pending");

            if (pendingSale == null)
            {
                TempData["ErrorMessage"] = "Cannot resume: Transaction not found or not pending.";
                return RedirectToAction("Index", "Transactions");
            }

            // 2. convert database items back into cart items
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

            // 3. load into session (overwriting any current cart)
            HttpContext.Session.SetObjectAsJson(CART_SESSION_KEY, cart);

            // 4. delete the old 'pending' record
            // we delete it because when they click 'complete', a brand new 'completed' record will be created.
            _context.SalesItems.RemoveRange(pendingSale.SalesItems);
            _context.Transactions.Remove(pendingSale);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Transaction #{id} resumed. Ready to checkout.";
            return RedirectToAction(nameof(Index));
        }
    }
}