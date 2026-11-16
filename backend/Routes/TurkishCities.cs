using System.Globalization;

namespace Travela.Api.Routes;

public static class TurkishCities
{
    // Stable catalog IDs. Keep the existing catalog order when adding new metadata.
    public static int? Id(string? name) { var city = Normalize(name); return city == null ? null : All.ToList().IndexOf(city) + 1; }
    public static string? Name(int? id) => id is >= 1 and <= 81 ? All[id.Value - 1] : null;
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Aksaray", "Amasya", "Ankara", "Antalya",
        "Ardahan", "Artvin", "Aydın", "Balıkesir", "Bartın", "Batman", "Bayburt", "Bilecik",
        "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum",
        "Denizli", "Diyarbakır", "Düzce", "Edirne", "Elazığ", "Erzincan", "Erzurum", "Eskişehir",
        "Gaziantep", "Giresun", "Gümüşhane", "Hakkari", "Hatay", "Iğdır", "Isparta", "İstanbul",
        "İzmir", "Kahramanmaraş", "Karabük", "Karaman", "Kars", "Kastamonu", "Kayseri", "Kırıkkale",
        "Kırklareli", "Kırşehir", "Kilis", "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa",
        "Mardin", "Mersin", "Muğla", "Muş", "Nevşehir", "Niğde", "Ordu", "Osmaniye", "Rize",
        "Sakarya", "Samsun", "Siirt", "Sinop", "Sivas", "Şanlıurfa", "Şırnak", "Tekirdağ",
        "Tokat", "Trabzon", "Tunceli", "Uşak", "Van", "Yalova", "Yozgat", "Zonguldak"
    });

    public static string? Normalize(string? value) => All.FirstOrDefault(city =>
        string.Equals(city.ToUpper(CultureInfo.GetCultureInfo("tr-TR")),
            value?.Trim().ToUpper(CultureInfo.GetCultureInfo("tr-TR")), StringComparison.Ordinal));
}
