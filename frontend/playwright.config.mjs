import { defineConfig } from "@playwright/test";
import path from "node:path";

export default defineConfig({
  testDir: "./tests/browser",
  outputDir: path.resolve("../tmp/ticket-qa/browser-results"),
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: "http://127.0.0.1:5178",
    channel: process.env.TRAVELA_BROWSER_CHANNEL ?? "chrome",
    headless: true,
    trace: "retain-on-failure",
  },
  webServer: {
    command: `"${process.execPath}" node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5178 --strictPort`,
    url: "http://127.0.0.1:5178",
    reuseExistingServer: false,
  },
});
