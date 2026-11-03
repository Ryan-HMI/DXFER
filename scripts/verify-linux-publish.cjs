const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

function verifyPublish(directory) {
    const config = JSON.parse(fs.readFileSync(path.join(directory, 'DXFER.Web.runtimeconfig.json'), 'utf8'));
    const runtime = config.runtimeOptions;
    assert(!runtime.framework && !runtime.frameworks, 'Shared-framework deployment is not allowed');
    for (const name of ['Microsoft.NETCore.App', 'Microsoft.AspNetCore.App']) {
        assert(runtime.includedFrameworks?.some(x => x.name === name), `Bundled ${name} is missing`);
    }
    for (const file of ['DXFER.Web', 'DXFER.Web.dll', 'DXFER.Blazor.dll',
        'libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so', 'System.Private.CoreLib.dll',
        'System.Net.NameResolution.dll', 'System.Net.Http.dll',
        'Microsoft.AspNetCore.Components.Server.dll']) {
        assert(fs.statSync(path.join(directory, file)).size > 0, `Missing or empty runtime file: ${file}`);
    }
    fs.accessSync(path.join(directory, 'DXFER.Web'), fs.constants.X_OK);
    return runtime.includedFrameworks;
}

module.exports = { verifyPublish };
if (require.main === module) {
    const directory = process.argv[2];
    assert(directory, 'Usage: node verify-linux-publish.cjs PUBLISH_DIRECTORY');
    console.log('Verified self-contained DXFER runtime:', JSON.stringify(verifyPublish(directory)));
}
