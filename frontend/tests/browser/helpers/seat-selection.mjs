import { expect } from "@playwright/test";

export async function chooseSeat(page, number, gender = "Kadın") {
  const seat = page.getByRole("button", { name: new RegExp(`^Koltuk ${number},`) });
  await seat.click();
  const dialog = page.getByRole("dialog", { name: `Koltuk ${number}`, exact: true });
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: gender, exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(seat).toHaveAttribute("aria-pressed", "true");
  return seat;
}

export async function releaseSeat(page, number) {
  const seat = page.getByRole("button", { name: new RegExp(`^Koltuk ${number},`) });
  await page.getByRole("button", { name: `Koltuğu çıkar · ${number}`, exact: true }).click();
  await expect(seat).toHaveAttribute("aria-pressed", "false");
}
