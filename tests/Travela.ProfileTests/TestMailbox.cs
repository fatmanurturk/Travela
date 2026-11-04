using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class TestMailbox
{
    public static string DirectoryPath { get; set; } = "";
    public static async Task<string> Token(TravelaDbContext db, int userId)
    {
        var message = await db.AccountEmailMessages.AsNoTracking().Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstAsync();
        var file = Path.Combine(DirectoryPath, message.Id + ".html");
        for (var i = 0; i < 150; i++)
        {
            if (File.Exists(file))
            {
                var html = await File.ReadAllTextAsync(file);
                var match = Regex.Match(html, "#token=([^\"<\\s]+)");
                if (match.Success) return Uri.UnescapeDataString(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
            }
            await Task.Delay(100);
        }
        throw new Exception("Test e-postası yerel önizlemeye teslim edilemedi.");
    }
}
