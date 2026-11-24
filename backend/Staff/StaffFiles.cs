using System.Text;

namespace Travela.Api.Staff;

public static class StaffFiles
{
    public const int PhotoLimit = 5 * 1024 * 1024;
    public const int DocumentLimit = 10 * 1024 * 1024;

    public static async Task<(byte[] Data, string ContentType, string FileName)> Read(IFormFile file, bool photo, CancellationToken ct)
    {
        var limit = photo ? PhotoLimit : DocumentLimit;
        if (file.Length <= 0 || file.Length > limit)
            throw new ArgumentException($"{(photo ? "Fotoğraf" : "Belge")} boş olamaz ve {(photo ? 5 : 10)} MB sınırını aşamaz.");
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > limit) throw new ArgumentException("Dosya izin verilen boyutu aşıyor.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var data = output.ToArray();
        var type = Detect(data);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var valid = type switch {
            "image/jpeg" => ext is ".jpg" or ".jpeg",
            "image/png" => ext == ".png",
            "image/webp" => ext == ".webp",
            "application/pdf" => !photo && ext == ".pdf",
            _ => false
        };
        if (!valid || !string.Equals(file.ContentType, type, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(photo ? "JPEG, PNG veya WebP biçiminde geçerli bir fotoğraf seçin. Dosya içeriği ve türü eşleşmelidir."
                : "PDF, JPEG, PNG veya WebP biçiminde geçerli bir belge seçin. Dosya içeriği ve türü eşleşmelidir.");
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c != '"').ToArray());
        if (name.Length > 200) name = name[..(190 - ext.Length)] + ext;
        return (data, type!, name);
    }

    private static string? Detect(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return "image/jpeg";
        if (data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            Encoding.ASCII.GetString(data, 12, 4) == "IHDR") return "image/png";
        if (data.Length >= 16 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WEBP" &&
            Encoding.ASCII.GetString(data, 12, 4) is "VP8 " or "VP8L" or "VP8X") return "image/webp";
        if (data.Length >= 8 && Encoding.ASCII.GetString(data, 0, 5) == "%PDF-") return "application/pdf";
        return null;
    }
}
