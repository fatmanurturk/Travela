import { expect } from "@playwright/test";

const installed = new WeakSet();
// Existing isolated UI fixtures also need the new server selection contract.
export async function installSeatSelectionMock(page) {
  if (installed.has(page)) return;
  installed.add(page); const sessions = new Map();
  await page.route("**/api/seat-selections**", route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    let state;
    if (method === "POST") {
      const body = request.postDataJSON(); state = sessions.get(body.id);
      if (!state) { state = { id: body.id, token: body.token, phase: "Selection", expiresAt: null, purchaseGroupId: null, seats: [] }; sessions.set(body.id, state); }
    } else {
      state = sessions.get(path.split("/")[3]);
      if (!state || request.headers()["x-seat-token"] !== state.token) return route.fulfill({ status: 404, json: { code: "selection_not_found", message: "Oturum bulunamadı." } });
      if (method === "PUT") {
        const body = request.postDataJSON(); state.expiresAt ??= new Date(Date.now() + 300000).toISOString();
        state.seats = [...state.seats.filter(s => s.seatNumber !== body.seatNumber), { seatNumber: body.seatNumber, gender: body.gender }];
      } else if (method === "DELETE") state.seats = state.seats.filter(s => s.seatNumber !== Number(path.split("/").at(-1)));
    }
    const { token: _token, ...result } = state;
    return route.fulfill({ json: { ...result, serverTime: new Date().toISOString() } });
  });
}

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
