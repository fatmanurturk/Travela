import { test } from "node:test";
import assert from "node:assert/strict";
import { buildSchedule, turkeyInput } from "../src/tripScheduling.ts";
const now = Date.parse("2034-01-01T00:00:00Z");
test("Haftalık tekrar Türkiye günlerine göre seçilir ve gece varışını korur", () => {
 const result = buildSchedule("2035-01-01T23:30", "2035-01-02T05:00", "weekly", "2035-01-08", [1, 3], [], now);
 assert.deepEqual(result.map(t => t.departureTime), ["2035-01-01T20:30:00.000Z", "2035-01-03T20:30:00.000Z", "2035-01-08T20:30:00.000Z"]);
 assert.equal(result[0].arrivalTime, "2035-01-02T02:00:00.000Z");
 assert.equal(turkeyInput(result[0].departureTime), "2035-01-01T23:30");
});
test("Tek tek seçilen tarihler sıralanır, aynı tarih çoğaltılmaz", () => {
 const result = buildSchedule("2035-01-01T10:00", "2035-01-01T16:00", "dates", "", [], ["2035-01-04", "2035-01-02", "2035-01-04"], now);
 assert.equal(result.length, 2); assert.equal(result[0].departureTime, "2035-01-02T07:00:00.000Z");
});
test("Boş seçim, geçersiz tarih, geçmiş kalkış ve aşırı tekrar reddedilir", () => {
 const run = (mode, until, days, dates, at = now) => buildSchedule("2035-01-01T10:00", "2035-01-01T16:00", mode, until, days, dates, at);
 assert.throws(() => run("weekly", "2035-01-10", [], []), /günü/);
 assert.throws(() => run("weekly", "2034-12-31", [1], []), /aralığı/);
 assert.throws(() => run("weekly", "2036-02-01", [1], []), /366/);
 assert.throws(() => run("weekly", "2035-12-31", [0,1,2,3,4,5,6], []), /100/);
 assert.throws(() => run("dates", "", [], []), /100/);
 assert.throws(() => run("dates", "", [], ["2035-02-30"]), /Geçerli/);
 assert.throws(() => run("single", "", [], [], Date.parse("2035-01-01T08:00:00Z")), /gelecekte/);
});
test("Seferlerin kendi aralarında çakışması engellenir; sınırda bitiş kabul edilir", () => {
 assert.throws(() => buildSchedule("2035-01-01T10:00", "2035-01-02T11:00", "dates", "", [], ["2035-01-01", "2035-01-02"], now), /çakışıyor/);
 assert.equal(buildSchedule("2035-01-01T10:00", "2035-01-02T10:00", "dates", "", [], ["2035-01-01", "2035-01-02"], now).length, 2);
});
