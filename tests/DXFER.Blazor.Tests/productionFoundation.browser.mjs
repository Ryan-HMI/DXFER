import assert from "node:assert/strict";
import { mkdir, readFile } from "node:fs/promises";

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? "playwright");
const baseUrl = process.env.DXFER_QA_URL ?? "http://127.0.0.1:5271";
assert.ok(["127.0.0.1", "localhost"].includes(new URL(baseUrl).hostname));
const output = process.env.DXFER_QA_OUTPUT ?? "/tmp/dxfer-production-foundation-20261104";
const fixture = new URL("../fixtures/cut-length/rectangle-hole-inches.dxf", import.meta.url);
const dxf = await readFile(fixture);

assert.equal((await fetch(`${baseUrl}/api/dxfer/capabilities`)).status, 200);
assert.equal((await fetch(`${baseUrl}/auth/status`)).status, 404);
assert.equal((await fetch(`${baseUrl}/api/dxfer/normalize`)).status, 405);
const form = new FormData();
form.append("dxf", new Blob([dxf], { type: "application/dxf" }), "rectangle-hole-inches.dxf");
const response = await fetch(`${baseUrl}/api/dxfer/normalize`, { method: "POST", body: form });
assert.equal(response.status, 200, "normal production normalization must not acquire a new auth requirement");
const normalized = await response.json();
assert.ok(Math.abs(normalized.cutLengthInches - (30 + Math.PI * 2)) < 0.000001);
assert.equal(normalized.cutLengthReviewReason, null);
assert.equal(normalized.closedContourCount, 2);
assert.equal(normalized.contourReviewReason, null);
assert.equal(normalized.units, "Inches");

await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto(baseUrl);
    await page.waitForFunction(() => document.querySelector("canvas.drawing-canvas")?.dataset.entityCount === "0");
    await page.locator("input[type=file]").setInputFiles({
        name: "rectangle-hole-inches.dxf", mimeType: "application/dxf", buffer: dxf
    });
    await page.waitForFunction(() => document.querySelector("canvas.drawing-canvas")?.dataset.entityCount === "2");
    await page.locator(".dxfer-live-readout-item").filter({ hasText: "36.283185 in" }).waitFor();
    const contours = page.locator(".dxfer-live-readout-item").filter({ hasText: "Contours" });
    assert.equal((await contours.locator(".dxfer-live-readout-value").innerText()).trim(), "2");
    assert.ok(await page.getByRole("button", { name: "Remove duplicates", exact: true }).first().isEnabled());

    for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 900 });
        const layout = await page.locator(".dxfer-command-bar").evaluate(bar => {
            const bounds = bar.getBoundingClientRect();
            const readouts = [...bar.querySelectorAll(".dxfer-live-readout-item")];
            const canvas = document.querySelector("canvas.drawing-canvas").getBoundingClientRect();
            return {
                bounded: bounds.left >= 0 && bounds.right <= innerWidth + 1,
                readoutsFit: readouts.every(item => {
                    const rect = item.getBoundingClientRect();
                    return rect.left >= bounds.left && rect.right <= bounds.right + 1
                        && rect.top >= bounds.top && rect.bottom <= bounds.bottom + 1
                        && item.scrollWidth <= item.clientWidth + 1;
                }),
                canvasSeparate: canvas.bottom <= bounds.top + 1,
                canvasVisible: canvas.width > 100 && canvas.height > 100
            };
        });
        assert.deepEqual(layout, { bounded: true, readoutsFit: true, canvasSeparate: true, canvasVisible: true });
        await page.screenshot({ path: `${output}/foundation-${width}.png` });
    }
    assert.deepEqual(errors, []);
    console.log("PASS unchanged anonymous API access, guarded normalization, local DXF open, duplicate control, and 1440/390/320px readouts.");
    console.log(`Screenshots: ${output}`);
} finally {
    await browser.close();
}
