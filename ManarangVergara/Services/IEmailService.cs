namespace ManarangVergara.Services
{
    // INTERFACE: THE "JOB DESCRIPTION" OR CONTRACT
    // this file doesn't actually send the email. it just creates a rule that says:
    // "any email service we build MUST have a function called SendEmailAsync".
    // this allows the rest of the app to trust that email capabilities exist without knowing the technical details.
    public interface IEmailService
    {
        // the specific task required by this contract.
        // it demands 3 inputs: who to send to, the subject line, and the message body.
        Task SendEmailAsync(string toEmail, string subject, string body);
    }
}