using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace ManarangVergara.Services
{
    // SERVICE: THE DIGITAL POSTMAN
    // this handles the technical job of actually sending an email out to the internet.
    // it uses a library called "MailKit" which is much better than the built-in tools.
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        // reads your "appsettings.json" file to find your gmail/outlook username and password
        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        // FUNCTION: SEND THE EMAIL
        // this is the only function you need to call from other files. just give it an address, subject, and message.
        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            // 1. GET SETTINGS
            // pull the secrets (host, port, password) from your configuration file
            var emailSettings = _config.GetSection("EmailSettings");

            // 2. CREATE THE ENVELOPE
            // create a new blank email object
            var email = new MimeMessage();

            // set the sender (e.g., "MedTory System <admin@medtory.com>")
            email.From.Add(new MailboxAddress(emailSettings["FromName"], emailSettings["SmtpUser"]));

            // set the recipient
            email.To.Add(MailboxAddress.Parse(toEmail));

            // set the subject line
            email.Subject = subject;

            // set the body content. "Html" means we can use bold, colors, and links inside the email.
            email.Body = new TextPart(TextFormat.Html) { Text = body };

            // 3. START THE DELIVERY TRUCK (SMTP CLIENT)
            using var smtp = new SmtpClient();

            // get the door number (port) for the server. usually 587 or 465.
            int port = int.Parse(emailSettings["SmtpPort"]);

            // 4. CONNECT TO SERVER
            // "SecureSocketOptions.Auto" is smart—it automatically figures out if the server needs SSL or TLS security.
            await smtp.ConnectAsync(emailSettings["SmtpHost"], port, SecureSocketOptions.Auto);

            // 5. LOGIN
            // give the server your username and app password so it lets you send mail
            await smtp.AuthenticateAsync(emailSettings["SmtpUser"], emailSettings["SmtpPass"]);

            // 6. SEND & GOODBYE
            // fly away, little email! then hang up the phone connection.
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}