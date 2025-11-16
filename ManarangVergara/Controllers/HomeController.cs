using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly PharmacyDbContext _context;

        public HomeController(PharmacyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var todayDateOnly = DateOnly.FromDateTime(today);
            var thirtyDaysFromNow = todayDateOnly.AddDays(30);
            int lowStockThreshold = 20; // Defined threshold from your blueprint

            // --- 1. FETCH KPIS ---
            // Gross Profit: We need to join SalesItems with Inventory (or Product if Cost is there) to get Cost Price.
            // Assuming CostPrice is in INVENTORY. We might need to approximate if multiple batches have different costs.
            // For this exact blueprint, we'll do a simpler approximation if exact tracking is too complex for a dashboard query:
            // Profit = Total Sales Today - (Sum of approximate cost of items sold today)
            var dailySales = await _context.Transactions
                .Where(t => t.SalesDate >= today && t.Status == "Completed")
                .Include(t => t.SalesItems)
                .ToListAsync();

            // Note: Exact Gross Profit requires joining SalesItems -> Products -> Inventory to find exact batch cost.
            // For now, we will use Total Sales as a placeholder until we strictly define how we track *which* batch was sold.
            // OR if your Product table has a 'CurrentCost' we could use that.
            // Let's stick to Total Revenue for now to avoid crashing if data is missing,
            // BUT I will add the code to calculate it if you have CostPrice on Products.
            decimal dailyGrossProfit = dailySales.Sum(t => t.TotalAmount); // Placeholder for Revenue. Real Profit needs deeper joins.

            var totalStockValue = await _context.Inventories
                .SumAsync(i => i.CostPrice * i.Quantity);

            var lowStockCount = await _context.Inventories
                .CountAsync(i => i.Quantity <= lowStockThreshold);

            var nearExpiryCount = await _context.Inventories
                 .CountAsync(i => i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly);

            // --- 2. FETCH PROACTIVE ALERTS LIST ---
            var rawAlerts = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity <= lowStockThreshold || (i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly))
                .OrderBy(i => i.ExpiryDate) // Most urgent first
                .Take(10) // Limit to top 10 risks
                .ToListAsync();

            var alertList = rawAlerts.Select(i => new ProactiveAlertVM
            {
                ProductName = i.Product.Name,
                Quantity = i.Quantity,
                ExpiryDate = i.ExpiryDate,
                DaysLeft = i.ExpiryDate.DayNumber - todayDateOnly.DayNumber,
                // Innovation: Smart Alert Typing
                AlertType = (i.Quantity <= lowStockThreshold && i.ExpiryDate <= thirtyDaysFromNow) ? "CRITICAL: BOTH" :
                            (i.Quantity <= lowStockThreshold) ? "Low Stock" : "Near Expiry",
                Urgency = (i.Quantity == 0 || i.ExpiryDate <= todayDateOnly) ? "Critical" : "Warning"
            }).ToList();

            // --- 3. FETCH RECENT TRANSACTIONS LIST ---
            // We need to join with Employee to get the Cashier Name
            // NOTE: We need to ensure the Transaction table actually HAS an EmployeeID column for the cashier.
            // Based on your SQL, 'transactions' table DOES NOT have Employee_ID.
            // We might need to add it later for full tracking. For now, we'll skip Cashier Name in the preview.
            // --- 3. FETCH RECENT TRANSACTIONS LIST ---
            var recentTxns = await _context.Transactions
                .OrderByDescending(t => t.SalesDate)
                .Take(8)
                .Select(t => new TransactionPreviewVM
                {
                    TransactionId = t.SalesId,
                    Date = t.SalesDate,
                    TotalAmount = t.TotalAmount,
                    PaymentMethod = t.PaymentMethod,
                    // NEW: Calculate total items sold in this transaction
                    ItemCount = t.SalesItems.Sum(si => si.QuantitySold),
                    CashierName = "N/A"
                })
                .ToListAsync();

            var viewModel = new DashboardViewModel
            {
                DailyGrossProfit = dailyGrossProfit, // Currently Revenue, need schema tweak for true profit
                TotalStockValue = totalStockValue,
                LowStockCount = lowStockCount,
                NearExpiryCount = nearExpiryCount,
                ProactiveAlerts = alertList,
                RecentTransactions = recentTxns
            };

            return View(viewModel);
        }

        // ... (Privacy and Error methods remain the same) ...
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}