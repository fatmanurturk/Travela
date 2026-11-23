import { apiUrl } from "./api";

export async function downloadPrivatePdf(path: string, token?: string | null) {
  const response = await fetch(apiUrl(path), { credentials: "include", cache: "no-store", headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new Error(data?.message ?? "PDF indirilemedi. Biletinizi yeniden sorgulayın.");
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a"); link.href = url; link.download = "Travela-bilet.pdf"; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
