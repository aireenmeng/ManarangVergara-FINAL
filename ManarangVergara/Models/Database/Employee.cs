using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // <--- ADD THIS NAMESPACE

namespace ManarangVergara.Models.Database;

public partial class Employee
{
    public int EmployeeId { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string EmployeeName { get; set; } = null!;
    public string Position { get; set; } = null!;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid Email Format.")]
    public string ContactInfo { get; set; } = null!;

    // --- ADD THIS ---
    public bool IsActive { get; set; } = true;
    // ----------------

    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiry { get; set; }

    public virtual ICollection<ItemLog> ItemLogs { get; set; } = new List<ItemLog>();
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public virtual ICollection<Void> Voids { get; set; } = new List<Void>();

    // --- add these new properties for tracking ---
    public string? TokenPurpose { get; set; } // "Activation" or "Reset"
    public string? TokenSentBy { get; set; }  // "Admin Juan"
    public DateTime? TokenSentDate { get; set; }
}