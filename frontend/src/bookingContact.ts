export function normalizeBookingPhone(value: string): string | null {
  let phone = value.trim().replace(/[\s()-]/g, "");
  if (/^05\d{9}$/.test(phone)) phone = `+9${phone}`;
  else if (/^5\d{9}$/.test(phone)) phone = `+90${phone}`;
  else if (/^905\d{9}$/.test(phone)) phone = `+${phone}`;
  else if (phone.startsWith("00")) phone = `+${phone.slice(2)}`;
  return /^\+[1-9]\d{7,14}$/.test(phone) ? phone : null;
}
