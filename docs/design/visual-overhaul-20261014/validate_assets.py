from pathlib import Path
import xml.etree.ElementTree as E
import json
R=Path(__file__).parent; data=json.loads((R/'icons.json').read_text()); files=sorted((R/'icons').glob('*.svg'))
assert len(files)==len(data)==81
for p in files:
 s=p.read_text(); e=E.fromstring(s)
 assert e.attrib['viewBox']=='0 0 24 24',p
 assert 'currentColor' in s and 'stroke-width="1.6"' in s,p
 assert not any(v in s for v in ['<script','http://','https://','href=','<image','<foreignObject'] if v!='http://'),p
 assert s.count('http://')==1 and 'http://www.w3.org/2000/svg' in s,p
sprite=E.parse(R/'dxfer-icons.svg').getroot(); ids=[e.attrib['id'] for e in sprite]
assert len(ids)==81 and len(set(ids))==81
for p in ['icon-contact-sheet.svg','workspace-target.svg']:E.parse(R/p)
print('PASS: 81 valid standalone masters; unique sprite IDs; both visual SVGs parse; no external assets or active content in masters.')
