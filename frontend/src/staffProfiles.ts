import { apiUrl } from "./api";

export type Validity = { status: "Valid" | "Expiring" | "Expired" | "Indefinite"; label: string; days: number | null; message: string };
export type StaffDocument = { id: number; name: string; documentType: string; issuedOn: string; expiresOn: string | null; isIndefinite: boolean; fileName: string; contentType: string; size: number; validity: Validity };
export type StaffProfile = {
  id: number; fullName: string; duty: string; phone: string | null; isActive: boolean; hasPhoto: boolean;
  careerStartYear: number | null; experienceYears: number | null; employmentStartDate: string | null;
  emergencyContactName: string | null; emergencyContactRelationship: string | null; emergencyContactPhone: string | null;
  languages: string[]; licenseClasses: string[]; vehicleTypes: string[];
  serviceExperience: boolean; serviceExperienceNotes: string | null; baggageExperience: boolean; baggageExperienceNotes: string | null;
  passengerCommunicationExperience: boolean; passengerCommunicationExperienceNotes: string | null; documents: StaffDocument[];
};
export const documentTypes: Record<string, string> = { DrivingLicense: "Sürücü belgesi", Professional: "Mesleki belge", FirstAid: "İlk yardım sertifikası", Training: "Eğitim sertifikası", Other: "Diğer" };
export const staffHeaders = () => ({ Authorization: `Bearer ${sessionStorage.getItem("travela_token")}` });
export async function staffRequest(path: string, options: RequestInit = {}) {
  const response = await fetch(apiUrl(`/api/company/staff${path}`), { ...options, headers: { ...staffHeaders(), ...options.headers }, cache: "no-store" });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.message ?? (body?.errors ? Object.values(body.errors).flat().join(" ") : response.status === 413 ? "Dosya boyutu sınırı aşıldı." : response.status === 401 || response.status === 403 ? "Bu işlem için oturumunuzu ve yetkinizi kontrol edin." : "İşlem tamamlanamadı. Lütfen yeniden deneyin."));
  }
  return response;
}
export function checkStaffFile(file: File, photo: boolean) {
  const types = photo ? ["image/jpeg", "image/png", "image/webp"] : ["application/pdf", "image/jpeg", "image/png", "image/webp"];
  if (!types.includes(file.type)) throw new Error(photo ? "JPEG, PNG veya WebP fotoğraf seçin." : "PDF, JPEG, PNG veya WebP belge seçin.");
  if (!file.size || file.size > (photo ? 5 : 10) * 1024 * 1024) throw new Error(`Dosya boş olamaz ve ${photo ? 5 : 10} MB sınırını aşamaz.`);
}
export const staffDate = (value?: string | null) => value ? value.split("-").reverse().join(".") : "Belirtilmedi";
