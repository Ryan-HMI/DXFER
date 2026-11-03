const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const assert = require('node:assert/strict');
const { verifyPublish } = require('./verify-linux-publish.cjs');

function fixture(t) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'dxfer-publish-test-'));
    t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const runtimeOptions = { includedFrameworks: [
        { name: 'Microsoft.NETCore.App', version: '8.0.33' },
        { name: 'Microsoft.AspNetCore.App', version: '8.0.33' },
    ] };
    fs.writeFileSync(path.join(directory, 'DXFER.Web.runtimeconfig.json'), JSON.stringify({ runtimeOptions }));
    for (const file of ['DXFER.Web', 'DXFER.Web.dll', 'DXFER.Blazor.dll', 'libcoreclr.so',
        'libhostfxr.so', 'libhostpolicy.so', 'System.Private.CoreLib.dll',
        'System.Net.NameResolution.dll', 'System.Net.Http.dll', 'Microsoft.AspNetCore.Components.Server.dll']) {
        fs.writeFileSync(path.join(directory, file), 'fixture');
    }
    fs.chmodSync(path.join(directory, 'DXFER.Web'), 0o700);
    return directory;
}

test('accepts a complete bundled runtime', t => {
    assert.equal(verifyPublish(fixture(t)).length, 2);
});

test('rejects the framework-dependent deployment behind the blank Sync launch', t => {
    const directory = fixture(t);
    fs.writeFileSync(path.join(directory, 'DXFER.Web.runtimeconfig.json'), JSON.stringify({
        runtimeOptions: { frameworks: [{ name: 'Microsoft.NETCore.App', version: '8.0.0' }] },
    }));
    assert.throws(() => verifyPublish(directory), /Shared-framework/);
});

test('rejects missing networking runtime assemblies', t => {
    const directory = fixture(t);
    fs.unlinkSync(path.join(directory, 'System.Net.NameResolution.dll'));
    assert.throws(() => verifyPublish(directory), /System.Net.NameResolution/);
});

test('rejects missing ASP.NET runtime', t => {
    const directory = fixture(t);
    fs.writeFileSync(path.join(directory, 'DXFER.Web.runtimeconfig.json'), JSON.stringify({
        runtimeOptions: { includedFrameworks: [{ name: 'Microsoft.NETCore.App', version: '8.0.33' }] },
    }));
    assert.throws(() => verifyPublish(directory), /Microsoft.AspNetCore.App/);
});
