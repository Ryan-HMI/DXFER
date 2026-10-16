from pathlib import Path
import json, html, math
R=Path(__file__).parent
P=lambda d:f'<path d="{d}"/>'
L=lambda x1,y1,x2,y2:f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}"/>'
C=lambda x,y,r:f'<circle cx="{x}" cy="{y}" r="{r}"/>'
Q=lambda x,y,w,h,r=0:f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}"/>'
N=lambda x,y:f'<circle cx="{x}" cy="{y}" r="1.4" fill="currentColor" stroke="none"/>'
icons={
'select':P('M5 3 19 12 12 14 9 21Z'),
'line':P('M5 19 19 5')+N(5,19)+N(19,5),
'midpoint-line':P('M4 18 20 6')+N(4,18)+N(20,6)+Q(10.4,10.4,3.2,3.2),
'point':P('M12 3v5m0 8v5M3 12h5m8 0h5')+C(12,12,2),
'rectangle':Q(4,6,16,12)+N(4,6)+N(20,18),
'center-rectangle':Q(4,6,16,12)+P('M9 12h6m-3-3v6'),
'aligned-rectangle':P('m3 12 12-8 6 8-12 8Z')+N(3,12)+N(15,4),
'circle':C(12,12,8)+N(12,12)+P('m13.5 10.5 4-4'),
'circle-three-point':C(12,12,8)+N(12,4)+N(5.1,16)+N(18.9,16),
'arc':P('M4 18a8 8 0 0 1 16 0')+N(4,18)+N(12,10)+N(20,18),
'center-arc':P('M4 18a12 12 0 0 1 12-12M4 18h12V6')+N(16,18),
'ellipse':'<ellipse cx="12" cy="12" rx="9" ry="6"/>'+N(12,12),
'elliptical-arc':P('M3 15a9 6 0 0 1 18 0')+N(3,15)+N(21,15),
'spline':P('M3 18C9 18 5 5 12 6S15 18 21 5')+N(3,18)+N(12,6)+N(21,5),
'conic':P('M4 19Q12-9 20 19')+N(4,19)+N(12,5)+N(20,19),
'slot':Q(3,7,18,10,5)+P('M7 12h10'),
'polygon':P('m12 3 8.6 6.2-3.3 10.1H6.7L3.4 9.2Z'),
'inscribed-polygon':C(12,12,9)+P('m12 3 8.6 6.2-3.3 10.1H6.7L3.4 9.2Z'),
'circumscribed-polygon':P('m12 2 9.5 6.9-3.6 11.2H6.1L2.5 8.9Z')+C(12,12,6.7),
'text':P('M4 5h16M12 5v15m-4 0h8'),
'spline-point':P('M3 18C9 18 5 5 12 6S15 18 21 5M17 17h6m-3-3v6')+N(12,6),
'construction':P('M4 20 7 17m3-3 4-4m3-3 3-3'),
'undo':P('m8 5-5 5 5 5M4 10h10a6 6 0 0 1 0 12'),
'redo':P('m16 5 5 5-5 5M20 10H10a6 6 0 0 0 0 12'),
'delete':P('M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7m4-7v7'),
'trim':L(7,4,7,20)+L(17,4,17,20)+L(2,12,7,12)+L(17,12,22,12)+L(10,9,14,15)+L(14,9,10,15),
'extend':P('M3 15h12m-4-4 4 4-4 4M20 3v18M16 15h4'),
'split':P('M3 19 9 13m6-6 5-5M10 5l9 9M10 11l4 4')+N(11,10),
'offset':P('M3 18V6h14M7 22V10h14M12 14v5m-2-2 2 2 2-2'),
'fillet':P('M4 20V12a8 8 0 0 1 8-8h8')+P('M4 7V4h3'),
'chamfer':P('M4 20V12l8-8h8')+P('M4 7V4h3'),
'dimension':P('M5 3v18m14-18v18M5 12h14m-11-3-3 3 3 3m8-6 3 3-3 3'),
'dimension-angle':P('M3 20h18L9 4M10 20a11 11 0 0 0 5-9'),
'dimension-radius':C(12,12,8)+P('M12 12 18 6m-4 0h4v4'),
'dimension-diameter':C(12,12,8)+P('M4 20 20 4m-4 0h4v4M4 16v4h4'),
'move':P('M12 3v18M3 12h18M9 6l3-3 3 3M9 18l3 3 3-3M6 9l-3 3 3 3m12-6 3 3-3 3'),
'rotate':P('M5 9a8 8 0 1 1 0 7M5 3v6h6')+N(12,12),
'scale':Q(3,14,7,7)+P('M3 10V3h18v18h-7M10 14 17 7m-5 0h5v5'),
'mirror':P('M12 3v3m0 3v6m0 3v3M8 6 3 18h5ZM16 6l5 12h-5Z'),
'linear-pattern':''.join(Q(x,y,5,5,0.5) for y in [4,15] for x in [4,15])+P('M11 6.5h2m-1-1 1 1-1 1'),
'circular-pattern':''.join(C(x,y,2.2) for x,y in [(12,4),(20,12),(12,20),(4,12)])+P('M16 4a9 9 0 0 1 4 4')+N(12,12),
'coincident':L(3,16,12,16)+L(20,4,12,16)+C(12,16,3)+N(12,16),
'concentric':C(12,12,8)+C(12,12,4)+N(12,12),
'parallel':P('M4 17 16 5M8 21 20 9'),
'perpendicular':P('M5 3v16h16M5 13h6v6'),
'horizontal':P('M3 12h18')+N(3,12)+N(21,12),
'vertical':P('M12 3v18')+N(12,3)+N(12,21),
'equal':P('M4 8h16M4 16h16'),
'tangent':C(12,9,6)+P('M3 15h18')+N(12,15),
'midpoint':P('M3 15h18m-9-9-4 9h8Z')+N(3,15)+N(21,15),
'symmetric':P('M12 3v3m0 3v6m0 3v3M3 6l5 6-5 6m18-12-5 6 5 6'),
'normal':P('M3 19Q12 8 21 19M12 14V3m-3 3 3-3 3 3M12 14h4v3'),
'fix':Q(5,10,14,11,2)+P('M8 10V7a4 4 0 0 1 8 0v3')+C(12,15,1),
'fit':P('M3 9V3h6m6 0h6v6M3 15v6h6m6 0h6v-6')+Q(8,8,8,8,1),
'pan':P('M8 13V7a1.5 1.5 0 0 1 3 0v5-8a1.5 1.5 0 0 1 3 0v8-6a1.5 1.5 0 0 1 3 0v7-3a1.5 1.5 0 0 1 3 0v6c0 4-2 6-6 6h-1c-3 0-4-1-6-4l-3-5a1.5 1.5 0 0 1 2-2Z'),
'zoom':C(10,10,6)+P('m15 15 6 6M7 10h6m-3-3v6'),
'axes':P('M5 3v16h16m-19-13 3-3 3 3m10 10 3 3-3 3'),
'constraints':C(7,7,3)+C(17,17,3)+P('m9 9 6 6M17 3v6m-3-3h6M3 17h6'),
'measure':P('m3 16 13-13 5 5-13 13Zm5-5 3 3m0-6 2 2m1-5 3 3'),
'keyboard':Q(2,5,20,14,2)+P('M6 9h1m4 0h1m4 0h1M6 13h1m4 0h1m4 0h1M7 16h10'),
'grid':Q(3,3,18,18,1)+P('M9 3v18m6-18v18M3 9h18M3 15h18'),
'layers':P('m12 3 9 5-9 5-9-5Zm-9 9 9 5 9-5M3 16l9 5 9-5'),
'grain':P('M4 4v16m4-16v16m4-16v16m4-16v16m4-16v16'),
'origin':C(12,12,5)+P('M12 2v6m0 8v6M2 12h6m8 0h6'),
'rotate-90':P('M4 18V6h14m-4-4 4 4-4 4M9 18h9v-5'),
'remove-duplicates':Q(3,3,12,12,1)+Q(9,9,12,12,1)+P('m12 12 6 6m0-6-6 6'),
'auto-orient':P('M4 20V8m0 12h12M8 15 15 8l5 5-7 7ZM17 2v4m-2-2h4'),
'align-x':P('M3 19h18m-3-3 3 3-3 3M5 13l12-8M8 8l-3 5 6-1'),
'align-y':P('M5 3v18M2 6l3-3 3 3M11 17l7-12m-5 3 5-3-1 6'),
'open':P('M3 19V5h6l2 3h10v3M3 19l3-8h16l-3 8Z'),
'new':P('M5 3h9l5 5v13H5ZM14 3v5h5M8 14h8m-4-4v8'),
'download':P('M12 3v12m-4-4 4 4 4-4M4 16v5h16v-5'),
'search':C(10,10,6)+P('m15 15 6 6'),
'help':C(12,12,9)+P('M9 9a3 3 0 1 1 5 2c-2 1-2 2-2 3')+N(12,17.5),
'check':P('m4 12 5 5L20 6'),
'close':P('m6 6 12 12M6 18 18 6'),
'warning':P('M12 3 22 21H2Zm0 6v5')+N(12,17.5),
'conflict':P('m8 3-5 5v8l5 5h8l5-5V8l-5-5Zm0 5 8 8M8 16l8-8'),
'return-to-tools':Q(2,3,9,18,1)+P('M2 8h9M21 7v6H7m4-4-4 4 4 4'),
'chevron-down':P('m6 9 6 6 6-6'),
'menu':P('M4 6h16M4 12h16M4 18h16')
}
head='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">'
for name,body in icons.items(): (R/'icons'/f'{name}.svg').write_text(head+body+'</svg>\n')
(R/'icons.json').write_text(json.dumps(icons,indent=2)+'\n')
(R/'dxfer-icons.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg">\n'+''.join(f'<symbol id="dxfer-{n}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">{b}</symbol>\n' for n,b in icons.items())+'</svg>\n')
# 24px actual-size masters plus 18px toolbar example, dark/light/active backgrounds.
rows=math.ceil(len(icons)/6); sheet=[f'<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="{100+rows*105}" viewBox="0 0 1200 {100+rows*105}">','<rect width="100%" height="100%" fill="#0A0D12"/>','<text x="28" y="38" fill="#F4F7FB" font-family="sans-serif" font-size="22">DXFer · Precision workspace icon masters</text>','<text x="28" y="66" fill="#BBC5D1" font-family="sans-serif" font-size="13">24 px master / 18 px toolbar / light surface / selected · labels remain visible for CAD commands</text>']
for i,(n,b) in enumerate(icons.items()):
 x=24+(i%6)*198; y=92+(i//6)*105
 sheet += [f'<rect x="{x}" y="{y}" width="186" height="93" rx="6" fill="#10141B" stroke="#242D39"/>']
 for j,(size,col,bg) in enumerate([(24,'#BBC5D1',None),(18,'#BBC5D1',None),(24,'#17202A','#EEF2F5'),(24,'#66D9FF','#1C303B')]):
  xx=x+10+j*42
  if bg: sheet += [f'<rect x="{xx-4}" y="{y+10}" width="32" height="32" rx="5" fill="{bg}"/>']
  sheet += [f'<g transform="translate({xx} {y+14+(24-size)/2}) scale({size/24})" fill="none" color="{col}" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">{b}</g>']
 sheet += [f'<text x="{x+10}" y="{y+70}" font-family="sans-serif" font-size="12" fill="#F4F7FB">{n}</text>']
(R/'icon-contact-sheet.svg').write_text(''.join(sheet)+'</svg>')
print(len(icons),'icons built')
try:
 import cairosvg
 cairosvg.svg2png(url=str(R/'icon-contact-sheet.svg'),write_to=str(R/'icon-contact-sheet.png'))
 print('Contact sheet rendered')
except ImportError: print('No CairoSVG')
