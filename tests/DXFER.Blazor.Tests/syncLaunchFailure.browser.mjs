import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import http from "node:http";

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? "playwright");
const baseUrl = process.env.DXFER_QA_URL ?? "http://127.0.0.1:5271";
assert.ok(["127.0.0.1", "localhost"].includes(new URL(baseUrl).hostname));
const output = process.env.DXFER_QA_OUTPUT ?? "/tmp/dxfer-sync-launch-20261103";
const dxf = "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n4\n70\n1\n10\n0\n20\n0\n10\n7.25\n20\n0\n10\n7.25\n20\n4.1\n10\n0\n20\n4.1\n0\nENDSEC\n0\nEOF\n";
let attempts = 0;
let releaseDownload;
const requests = [];
const source = http.createServer((request, response) => {
    requests.push(request.url);
    if (request.url.startsWith("/retry")) {
        if (++attempts === 1) {
            response.writeHead(503).end("synthetic failure");
        } else {
            releaseDownload = () => response.writeHead(200, { "Content-Type": "application/dxf" }).end(dxf);
        }
    } else {
        response.writeHead(200, { "Content-Type": "text/html" }).end("<html>not a DXF</html>");
    }
});
await new Promise(resolve => source.listen(0, "127.0.0.1", resolve));
const sourceUrl = `http://127.0.0.1:${source.address().port}`;
const launch = path => {
    const url = new URL(baseUrl);
    url.search = new URLSearchParams({
        syncBaseUrl: sourceUrl,
        artifactId: "1",
        jobId: "qa-job",
        editToken: "qa-placeholder",
        downloadUrl: `${sourceUrl}${path}?editToken=qa-placeholder`
    });
    return url.toString();
};
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    const status = page.locator(".dxfer-sync-control-bar [role=status]");
    const save = page.locator(".dxfer-sync-send-button");
    const retry = page.getByRole("button", { name: "Retry Sync download", exact: true });
    const canvas = page.locator("canvas.drawing-canvas");
    await page.goto(launch("/retry"));
    await status.filter({ hasText: "HTTP 503" }).waitFor();
    assert.ok(await save.isDisabled());
    assert.ok(await retry.isEnabled());
    assert.equal(await canvas.getAttribute("data-entity-count"), "0");
    await page.screenshot({ path: `${output}/desktop-failure.png` });

    await retry.click();
    await status.filter({ hasText: "Loading Sync DXF" }).waitFor();
    assert.ok(await save.isDisabled());
    assert.ok(await retry.isDisabled());
    await page.screenshot({ path: `${output}/desktop-loading.png` });
    assert.ok(releaseDownload);
    releaseDownload();
    await status.filter({ hasText: "Sync DXF loaded" }).waitFor();
    await page.waitForFunction(() => document.querySelector("canvas.drawing-canvas")?.dataset.entityCount === "1");
    assert.ok(await save.isEnabled());
    assert.equal(await retry.count(), 0);
    assert.equal(attempts, 2);
    assert.equal(requests[0], requests[1], "retry must reuse the exact launch download URL");
    await page.screenshot({ path: `${output}/desktop-recovered.png` });

    for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(launch("/empty"));
        await status.filter({ hasText: "no supported DXF geometry" }).waitFor();
        assert.ok(await save.isDisabled());
        const metrics = await page.locator(".dxfer-sync-control-bar").evaluate(bar => {
            const status = bar.querySelector("[role=status]");
            const rect = bar.getBoundingClientRect();
            const label = status.getBoundingClientRect();
            const actions = bar.querySelector(".dxfer-sync-actions").getBoundingClientRect();
            const overlaps = (a, b) => a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
            return {
                bounded: rect.left >= 0 && rect.right <= innerWidth,
                clipped: status.scrollWidth > status.clientWidth + 1 || status.scrollHeight > status.clientHeight + 1,
                overlapping: overlaps(label, actions),
                coversPalette: [...document.querySelectorAll(".dxfer-tool-group")].some(group => overlaps(rect, group.getBoundingClientRect()))
            };
        });
        assert.deepEqual(metrics, { bounded: true, clipped: false, overlapping: false, coversPalette: false });
        await page.screenshot({ path: `${output}/empty-response-${width}.png` });
    }
    assert.deepEqual(errors, []);
    console.log("PASS visible failures, disabled save, loading state, exact-URL retry, canvas recovery, and 1440/390/320px status layout.");
    console.log(`Screenshots: ${output}`);
} finally {
    await browser.close();
    source.closeAllConnections();
    await new Promise(resolve => source.close(resolve));
}
