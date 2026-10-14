from pathlib import Path
import json,html
R=Path(__file__).parent; icons=json.loads((R/'icons.json').read_text()); s=['<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="900" viewBox="0 0 1440 900">','<defs><radialGradient id="depth"><stop stop-color="#172832"/><stop offset="1" stop-color="#0B1016"/></radialGradient><pattern id="grid" width="28" height="28" patternUnits="userSpaceOnUse"><path d="M28 0H0V28" fill="none" stroke="#8A9EB2" stroke-opacity=".055"/></pattern></defs>']
def rect(x,y,w,h,fill,stroke=None,r=0):s.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}"'+(f' stroke="{stroke}"' if stroke else '')+'/>')
def text(x,y,t,size=13,col='#BBC5D1',weight=400):s.append(f'<text x="{x}" y="{y}" fill="{col}" font-family="sans-serif" font-size="{size}" font-weight="{weight}">{html.escape(t)}</text>')
def icon(n,x,y,col='#BBC5D1',z=20):s.append(f'<g transform="translate({x} {y}) scale({z/24})" color="{col}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">{icons[n]}</g>')
rect(0,0,1440,900,'#07090D');rect(0,0,1440,48,'#0A0D12');rect(0,48,200,820,'#10141B');rect(200,48,944,820,'url(#depth)');rect(200,48,944,820,'url(#grid)');rect(1144,48,296,820,'#10141B');rect(0,868,1440,32,'#0A0D12')
text(20,30,'DXFer',17,'#F4F7FB',700);text(106,30,'Bracket profile.dxf');text(237,29,'•',14,'#66D9FF');rect(260,12,56,25,'#151B24','#242D39',4);text(270,29,'Inches',11)
for x,n,l in [(820,'search','Find a command'),(1005,'undo','Undo'),(1096,'redo','Redo'),(1228,'download','Export DXF')]:icon(n,x,14);text(x+28,29,l)
y=76
for title,items in [('Create geometry',[('line','Line'),('rectangle','Rectangle'),('circle','Circle'),('arc','Arc'),('spline','Spline'),('ellipse','Ellipse'),('slot','Slot')]),('Modify geometry',[('trim','Trim'),('extend','Extend'),('fillet','Fillet'),('chamfer','Chamfer'),('offset','Offset'),('dimension','Dimension')]),('Transform & pattern',[('move','Transform'),('linear-pattern','Pattern')]),('Relations & preparation',[('constraints','Constraints'),('layers','Cleanup & grain')])]:
 text(20,y,title,12,'#F4F7FB',600);y+=12
 for n,l in items:
  if n=='fillet':rect(12,y,176,34,'#1C303B','#687B90',6);rect(12,y+5,2,24,'#66D9FF')
  icon(n,21,y+7,'#66D9FF' if n=='fillet' else '#BBC5D1');text(51,y+23,l,13,'#66D9FF' if n=='fillet' else '#BBC5D1');y+=35
 if title=='Create geometry':text(22,y+17,'More create tools  ›',12,'#97A5B6');y+=30
 y+=22
text(222,80,'Top · orthographic',13,'#F4F7FB',600);text(222,99,'Sketch editing',12,'#97A5B6');text(854,131,'DESIGN TARGET · illustrative geometry',10,'#97A5B6')
rect(849,62,278,40,'#0A0D12','#242D39',8)
for x,n,l in [(861,'fit','Fit'),(929,'grid','Grid'),(1010,'constraints','Relations')]:icon(n,x,72);text(x+25,87,l,12)
s.append('<g transform="translate(200 48)" fill="none" stroke-linecap="round" stroke-linejoin="round"><path d="M180 590V300a70 70 0 0 1 70-70h370V590Z" stroke="#D3E1ED" stroke-width="2"/><path d="M180 300V230h70" stroke="#66D9FF" stroke-dasharray="5 5"/><path d="M180 300a70 70 0 0 1 70-70" stroke="#66D9FF" stroke-width="3"/><circle cx="292" cy="360" r="40" stroke="#D3E1ED" stroke-width="2"/><circle cx="506" cy="360" r="40" stroke="#D3E1ED" stroke-width="2"/><path d="M292 476h214a30 30 0 0 1 0 60H292a30 30 0 0 1 0-60Z" stroke="#D3E1ED" stroke-width="2"/><path d="M250 300 212 261 134 261" stroke="#66D9FF"/><path d="M180 633h440m-434-4-6 4 6 4m428-8 6 4-6 4M180 600v44m440-44v44" stroke="#97A5B6"/></g>')
text(319,295,'R 0.750 in',13,'#66D9FF');text(563,672,'4.714 in')
text(1160,79,'Active tool',12,'#97A5B6');text(1160,103,'Fillet',17,'#F4F7FB',600);text(1160,127,'Round a corner between two edges.',12)
for yy,a,b in [(150,'First edge','Line 04'),(211,'Second edge','Line 05')]:
 rect(1160,yy,264,52,'#0A0D12','#687B90',6);icon('check',1170,yy+16,'#79E1AB');text(1200,yy+20,a,11,'#97A5B6');text(1200,yy+39,b,13,'#F4F7FB')
text(1160,291,'Radius',12);text(1300,291,'Corner mode',12)
for xx,val,unit in [(1160,'0.750','in'),(1300,'Trim','⌄')]:rect(xx,301,124,36,'#0A0D12','#687B90',6);text(xx+10,324,val,13,'#F4F7FB');text(xx+100,324,unit,12)
text(1160,367,'Keep original edges');rect(1378,349,46,25,'#151B24','#687B90',6);text(1390,366,'Off',12)
rect(1144,392,296,1,'#242D39');text(1160,421,'Selection',13,'#F4F7FB',600)
for yy,l,v in [(450,'Entities','2 lines'),(479,'Document units','Inches'),(508,'Layer','Cut profile')]:text(1160,yy,l,12,'#97A5B6');text(1345,yy,v,12)
rect(1144,528,296,1,'#242D39');text(1160,557,'Relations',13,'#F4F7FB',600)
for yy,n,l,v in [(579,'horizontal','Horizontal','Line 04'),(614,'vertical','Vertical','Line 05')]:icon(n,1160,yy);text(1188,yy+15,l,12);text(1380,yy+15,v,11,'#97A5B6')
text(1160,661,'Showing relations for this selection.',11,'#97A5B6')
rect(1144,761,296,107,'#0A0D12','#242D39');icon('check',1160,779,'#79E1AB',16);text(1183,792,'Preview valid · 1 corner affected',12,'#79E1AB')
rect(1170,813,93,36,'#151B24','#687B90',6);icon('close',1178,821,z=17);text(1202,836,'Cancel',12);rect(1273,813,151,36,'#F4F7FB',None,6);icon('check',1286,821,'#0A0D12',17);text(1311,836,'Apply fillet',12,'#0A0D12',600)
rect(220,804,904,44,'#10141B','#687B90',8);icon('fillet',234,816);text(265,831,'Fillet preview ready. Edit radius or apply.');text(905,831,'Enter  Apply     Esc  Cancel',12,'#97A5B6')
text(16,889,'●',11,'#79E1AB');text(34,889,'Preview · changes not committed',12);text(821,889,'2 selected     Cut length 18.42 in     Contours: 1 closed     Snap: endpoint',12)
(R/'workspace-target.svg').write_text(''.join(s)+'</svg>')
