using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ManarangVergara.Models.Database;

// MODEL: THE EMPLOYEE BLUEPRINT
// this file defines exactly what information we save for every staff member in the database.
public partial class Employee
{
    // unique id number for the database (like a student id)
    public int EmployeeId { get; set; }

    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!; // stores the encrypted (scrambled) password, not plain text
    public string EmployeeName { get; set; } = null!;
    public string Position { get; set; } = null!; // stores if they are "admin", "cashier", or "manager"

    // DATA VALIDATION:
    // these tags automatically check if the user typed a real email address before saving
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid Email Format.")]
    public string ContactInfo { get; set; } = null!; // used for sending password reset links



    // THE "SOFT DELETE" SWITCH
    // if true = they can log in. 
    // if false = they are fired/deactivated. we use this instead of deleting them so we keep their sales history.
    public bool IsActive { get; set; } = true;



    // SECURITY TOKENS:
    // these store the long, random code that goes into the "click here to reset password" email link.
    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiry { get; set; } // ensures the link dies after 24-48 hours



    // RELATIONSHIPS (LINKS TO OTHER TABLES):
    // these are lists connecting this employee to other parts of the database.
    // e.g., "ItemLogs" lets us see every time this specific person changed stock quantities.
    public virtual ICollection<ItemLog> ItemLogs { get; set; } = new List<ItemLog>();
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public virtual ICollection<Void> Voids { get; set; } = new List<Void>();



    // extra details so we know why an email was sent to them
    public string? TokenPurpose { get; set; } // was it for "Activation" (new hire) or "Reset" (forgot password)?
    public string? TokenSentBy { get; set; }  // remembers which admin sent the invite
    public DateTime? TokenSentDate { get; set; }
}