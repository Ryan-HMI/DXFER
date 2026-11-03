# Linux Runtime Deployment

DXFER service publishes must contain their own .NET and ASP.NET runtime. Do not
use a framework-dependent publish for long-running services: removing a shared
runtime during OS servicing leaves a running process unable to lazy-load
assemblies. On 2026-11-03 this broke Sync downloads with a missing
`System.Net.NameResolution` from the removed 8.0.31 runtime.

Publish a clean, tested commit to a fresh directory:

```sh
bash scripts/publish-linux.sh /home/ryan/dxfer-runtime/releases/NEW_RELEASE linux-x64
node --test scripts/verify-linux-publish.test.cjs
```

The Ubuntu-packaged SDK uses its locally installed runtime packs. On the existing
Ubuntu 24.04 x64 host, pass `ubuntu.24.04-x64` instead of `linux-x64`. The script
enables the distro RID graph for this case. Do not downgrade the runtime to make
publishing succeed. The validation command prints bundled runtime versions.

Smoke-test the new directory on a free loopback port, including a signed Sync
download and canvas rendering. Do not call Save to Sync against live records
during this read-only check. Stop only the target service, retain its previous
directory for rollback, replace its publish path, then start and verify it.
Never publish over a running directory or delete previous releases used by live
processes. Preserve environment files, authentication, state and storage paths.

Bundled runtimes do not automatically receive OS runtime updates. Rebuild and
redeploy all three DXFER environments when servicing .NET security updates; keep
each environment on its own approved application revision. Check the deployed
runtime config, networking assembly and process `libcoreclr` path, not merely an
HTTP 200 from the root page. Dev features must not be promoted as part of a
runtime repair.
