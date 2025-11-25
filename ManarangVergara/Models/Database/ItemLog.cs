using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

// MODEL: THE SECURITY LOG (AUDIT TRAIL)
// this file records every single time the stock numbers change.
// if a pill goes missing, this list tells you who took it and when.
public partial class ItemLog
{
    public int LogId { get; set; }

    // link to the product. tells us which item was changed.
    public int ProductId { get; set; }

    // what actually happened? e.g., "restock", "sold", "voided", or "expired".
    public string Action { get; set; } = null!;

    // how many items were added or removed?
    public int Quantity { get; set; }

    // the fingerprint: links to the specific employee who made this change.
    public int EmployeeId { get; set; }

    // the exact time stamp of the action.
    public DateTime LoggedAt { get; set; }

    // mandatory explanation. e.g., "delivery arrived" or "bottle broke on floor".
    // prevents staff from changing numbers without giving a reason.
    public string LogReason { get; set; } = null!;

    // CONNECTOR:
    // lets us pull the full name of the employee (e.g., "Juan Cruz") instead of just their ID number.
    public virtual Employee Employee { get; set; } = null!;

    // CONNECTOR:
    // lets us see the product name (e.g., "Amoxicillin") in the report.
    public virtual Product Product { get; set; } = null!;
}