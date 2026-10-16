// Isolated artwork QA; no app server, account, document or deployment is touched.
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = process.env.DXFER_ICON_EVIDENCE ?? '/tmp/dxfer-join-trim-evidence';
const source = await readFile(resolve(root, 'src/DXFER.Blazor/Components/CadIcon.razor'), 'utf8');
const names = [['Coincident', 'Coincident'], ['PowerTrim', 'Power trim/extend']];
const artwork = Object.fromEntries(names.map(([name]) => {
    const body = source.match(new RegExp(`case CadIconName\\.${name}:\\s*([\\s\\S]*?)\\s*break;`))?.[1];
    assert.ok(body, name);
    return [name, body];
}));
const svg = name => `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${artwork[name]}</svg>`;
const treatments = ['dark', 'light', 'selected', 'disabled'];
const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>DXFer join / trim glyph review</title>
<style>
*{box-sizing:border-box}body{margin:0;padding:24px;background:#10141b;color:#f4f7fb;font:14px system-ui;letter-spacing:0}
h1{font-size:18px;font-weight:600;margin:0 0 8px}p{font-size:12px;color:#bbc5d1;margin:0 0 22px}
table{border-collapse:collapse}th,td{text-align:left;padding:12px 18px;border-bottom:1px solid #343e49}th{font-weight:500;font-size:12px}
button{display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;padding:0;border:1px solid transparent;border-radius:4px;background:transparent;color:#bbc5d1}
svg{display:block;flex:none;width:20px;height:20px}.light{background:#eef2f5}.light button{color:#17202a}
.selected button{color:#66d9ff;background:rgba(102,217,255,.14);border-color:#66d9ff;box-shadow:inset 2px 0 #66d9ff}
.disabled button{color:#56616f}button:focus-visible{outline:2px solid #66d9ff;outline-offset:2px}
.detail svg{width:80px;height:80px}.detail{color:#bbc5d1}
</style></head><body><h1>Coincident / Power Trim</h1><p>Actual 20 px glyphs in 32 px targets; detail at right is 4x. Static artwork review, not installed-app acceptance.</p>
<table><thead><tr><th>Command</th>${treatments.map(t => `<th>${t}</th>`).join('')}<th>Detail</th></tr></thead><tbody>
${names.map(([name, label]) => `<tr><th>${label}</th>${treatments.map(t => `<td class="${t}"><button data-icon="${name}" aria-label="${label}" title="${label}" ${t === 'disabled' ? 'disabled' : ''} ${t === 'selected' ? 'aria-pressed="true"' : ''}>${svg(name)}</button></td>`).join('')}<td class="detail">${svg(name)}</td></tr>`).join('')}
</tbody></table></body></html>`;
await mkdir(output, { recursive: true });
await writeFile(resolve(output, 'review.html'), html);
const browser = await chromium.launch({ headless: true });
try {
    for (const deviceScaleFactor of [1, 2]) {
        const page = await browser.newPage({ viewport: { width: 820, height: 390 }, deviceScaleFactor });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.setContent(html);
        const measurements = await page.locator('button').evaluateAll(buttons => buttons.map(button => {
            const b = button.getBoundingClientRect(), s = button.querySelector('svg').getBoundingClientRect();
            return { name: button.getAttribute('aria-label'), title: button.title, width: b.width, height: b.height,
                glyphWidth: s.width, glyphHeight: s.height, offsetX: s.x - b.x, offsetY: s.y - b.y };
        }));
        assert.equal(measurements.length, 8);
        for (const m of measurements) {
            assert.deepEqual([m.width, m.height, m.glyphWidth, m.glyphHeight, m.offsetX, m.offsetY], [32, 32, 20, 20, 6, 6]);
            assert.equal(m.name, m.title);
        }
        // Rasterize the exact DOM SVG at its real size to guard against blank/clipped glyphs.
        const pixels = await page.locator('td.dark svg').evaluateAll(async icons => Promise.all(icons.map(async icon => {
            const xml = new XMLSerializer().serializeToString(icon).replaceAll('currentColor', '#ffffff');
            const image = new Image();
            image.src = `data:image/svg+xml,${encodeURIComponent(xml)}`;
            await image.decode();
            const canvas = document.createElement('canvas'); canvas.width = canvas.height = 20;
            const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0, 20, 20);
            const data = ctx.getImageData(0, 0, 20, 20).data;
            let ink = 0, edge = 0;
            for (let y = 0; y < 20; y++) for (let x = 0; x < 20; x++) {
                if (data[(y * 20 + x) * 4 + 3] > 32) {
                    ink++;
                    if (x === 0 || y === 0 || x === 19 || y === 19) edge++;
                }
            }
            return { ink, edge };
        })));
        for (const pixel of pixels) { assert.ok(pixel.ink > 25); assert.equal(pixel.edge, 0); }
        assert.deepEqual(errors, []);
        await page.screenshot({ path: resolve(output, `icons-${deviceScaleFactor}x.png`) });
        await writeFile(resolve(output, `metrics-${deviceScaleFactor}x.json`), JSON.stringify({ measurements, pixels }, null, 2));
        await page.close();
    }
    console.log(`PASS: 20px glyphs, 32px targets, four treatments, nonblank/unclipped at 1x/2x; ${output}`);
} finally { await browser.close(); }
