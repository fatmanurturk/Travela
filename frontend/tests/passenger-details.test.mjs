import { test } from "node:test";
import assert from "node:assert/strict";
import { initialPassengerDetails, passengerDetailsReducer as reduce } from "../src/passengerDetails.ts";

const profile = { fullName: "Hesap Sahibi", identityNumber: "10000000146", email: "buyer@example.test", phone: "+905321234567" };
const select = (state, seats) => reduce(state, { type: "seats", seats });
const load = (state, value = profile) => reduce(state, { type: "profile", profile: value });

test("Profil önce veya sonra gelse de yalnız ilk seçilen koltuk otomatik dolar", () => {
  for (const state of [select(load(initialPassengerDetails()), [8]), load(select(initialPassengerDetails(), [8]))]) {
    const next = select(state, [2, 8]);
    assert.equal(next.passengers[8].fullName, profile.fullName);
    assert.equal(next.passengers[8].identityNumber, profile.identityNumber);
    assert.equal(next.passengers[2].fullName, "");
    assert.equal(next.passengers[2].identityNumber, "");
    assert.deepEqual(next.contact, { email: profile.email, phone: profile.phone });
  }
});
test("Geç profil, değiştirilen veya bilerek temizlenen alanları ezmez; dokunulmayanları tamamlar", () => {
  let state = select(initialPassengerDetails(), [8, 2]);
  state = reduce(state, { type: "passenger", seat: 8, field: "fullName", value: "Başka Yolcu" });
  state = reduce(state, { type: "contact", field: "email", value: "" });
  state = load(state);
  assert.equal(state.passengers[8].fullName, "Başka Yolcu");
  assert.equal(state.passengers[8].identityNumber, profile.identityNumber);
  assert.equal(state.contact.email, "");
  assert.equal(state.contact.phone, profile.phone);
  state = reduce(state, { type: "clear", seat: 8 });
  state = load(state);
  assert.equal(state.passengers[8].fullName, "");
  assert.equal(state.passengers[8].identityNumber, "");
});
test("Sıralama ve başka koltuğu kaldırma yolcu bilgisini taşımaz", () => {
  let state = select(load(initialPassengerDetails()), [8]);
  state = select(state, [8, 2, 5]);
  state = reduce(state, { type: "passenger", seat: 2, field: "fullName", value: "İkinci Yolcu" });
  state = select(state, [2, 5, 8]);
  state = select(state, [2, 8]);
  assert.equal(state.passengers[8].fullName, profile.fullName);
  assert.equal(state.passengers[2].fullName, "İkinci Yolcu");
});
test("İlk koltuk kaldırıldığında, boş sepet ve yeniden seçimde otomatik aktarım yapılmaz", () => {
  let state = select(load(initialPassengerDetails()), [8]);
  state = select(state, [8, 2]);
  state = select(state, [2]);
  assert.equal(state.passengers[2].fullName, "");
  state = select(state, []);
  state = select(state, [8]);
  assert.equal(state.passengers[8].fullName, "");
  state = reduce(state, { type: "use-profile", seat: 8 });
  assert.equal(state.passengers[8].identityNumber, profile.identityNumber);
});
test("İlk koltuk profil gelmeden kaldırılsa da hesap bilgileri başka koltuğa kaymaz", () => {
  let state = select(initialPassengerDetails(), [8]);
  state = select(state, [8, 2]);
  state = select(state, [2]);
  state = load(state);
  assert.equal(state.passengers[2].fullName, "");
  assert.equal(state.passengers[2].identityNumber, "");
  assert.equal(state.contact.email, profile.email);
});
test("Eksik profil boş bırakılır; açık aktarımda profilde olmayan kimlik elle girilmiş değeri silmez", () => {
  let state = load(select(initialPassengerDetails(), [8]), { ...profile, fullName: "", identityNumber: "", phone: "" });
  assert.equal(state.passengers[8].fullName, "");
  assert.equal(state.passengers[8].identityNumber, "");
  assert.equal(state.contact.phone, "");
  state = reduce(state, { type: "passenger", seat: 8, field: "identityNumber", value: "12345678950" });
  state = reduce(state, { type: "use-profile", seat: 8 });
  assert.equal(state.passengers[8].identityNumber, "12345678950");
});
