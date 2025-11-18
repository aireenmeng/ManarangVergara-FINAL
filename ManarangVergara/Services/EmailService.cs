using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace ManarangVergara.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var emailSettings = _config.GetSection("EmailSettings");

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(emailSettings["FromName"], emailSettings["SmtpUser"]));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;
            email.Body = new TextPart(TextFormat.Html) { Text = body };

            using var smtp = new SmtpClient();

            // Get port from config
            int port = int.Parse(emailSettings["SmtpPort"]);

            // USE "Auto" - This allows it to work with both Port 465 (SSL) and 587 (TLS) automatically
            await smtp.ConnectAsync(emailSettings["SmtpHost"], port, SecureSocketOptions.Auto);

            await smtp.AuthenticateAsync(emailSettings["SmtpUser"], emailSettings["SmtpPass"]);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}