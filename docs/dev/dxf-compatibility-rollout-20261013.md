# Narrow DXF Compatibility Rollout

Ryan delivery76eb7f2451d4fd538aa4475b requests all environments ASAP.
This test writer candidate starts from the running assembly's recorded source
fa7390de, not the newer branch head. Only DXF serialization and its regression
tests are changed. No full-editor, lifecycle, solver, authentication or other
Dev work is promoted. Test retains its existing measurement/cleanup behavior.

AC1015 framing, file-local hex handles, model/paper-space records and owners,
entity subclasses and explicit planar Z coordinates match the verified Dev
writer. Original geometry and layers are unchanged; no bulk storage rewrite.
570 .NET tests pass. All23 BRCAB outputs from this candidate pass native QCAD3.33.1
with exact entity counts including64 splines, and independent ezdxf audit with
zero errors/repairs. Numeric geometry matches the verified Dev writer outputs.
Evidence: /tmp/codex-writer-rollout/test; native verifier in /tmp/codex-dxf-qcad.

Deploy with the existing service/environment and retain a rollback publish.
Fresh downloads/saves use the fix; previous local files are not rewritten.
