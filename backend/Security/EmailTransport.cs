using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace Travela.Api.Security;

public sealed class EmailTransport(IOptions<AccountEmailOptions> options, IHostEnvironment env)
{
    public async Task<string> Send(Guid id, string recipient, string subject, string html, CancellationToken ct)
    {
        using var mail = new MailMessage(new MailAddress(options.Value.FromAddress, options.Value.FromName), new MailAddress(recipient))
            { Subject = subject, Body = html, IsBodyHtml = true, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        mail.Headers.Add("X-Travela-Message-Id", id.ToString());
        using var smtp = new SmtpClient { Timeout = 45000 };
        if (options.Value.DeliveryMode == "Pickup")
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing")) throw new InvalidOperationException("Yerel posta üretimde kullanılamaz.");
            var directory = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.PickupDirectory));
            Directory.CreateDirectory(directory);
            smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory; smtp.PickupDirectoryLocation = directory;
            await smtp.SendMailAsync(mail, ct);
            await File.WriteAllTextAsync(Path.Combine(directory, id + ".html"), html, Encoding.UTF8, ct);
            return "Preview";
        }
        smtp.Host = options.Value.SmtpHost!; smtp.Port = options.Value.SmtpPort; smtp.EnableSsl = true; smtp.UseDefaultCredentials = false;
        if (!string.IsNullOrEmpty(options.Value.SmtpUserName)) smtp.Credentials = new NetworkCredential(options.Value.SmtpUserName, options.Value.SmtpPassword);
        await smtp.SendMailAsync(mail, ct);
        return "Accepted"; // SMTP acceptance is not an inbox delivery receipt.
    }
}
