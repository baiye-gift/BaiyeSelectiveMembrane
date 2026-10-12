"""Verify actual SCML, PNG and exported native KAnim; does not run Unity."""
from pathlib import Path
from PIL import Image
import re, struct, xml.etree.ElementTree as E

ROOT=Path(__file__).resolve().parents[1]
passed=0
def check(condition,message):
    global passed
    assert condition,message
    passed+=1

class Reader:
    def __init__(self,path):self.data=path.read_bytes();self.offset=4
    def read(self,fmt):
        result=struct.unpack_from('<'+fmt,self.data,self.offset);self.offset+=struct.calcsize('<'+fmt);return result
    def string(self):
        n=self.read('I')[0];s=self.data[self.offset:self.offset+n].decode('utf-8');self.offset+=n;return s

def exported_animation(path):
    r=Reader(path);check(r.data[:4]==b'ANIM','animation magic')
    version,elements,frames,count=r.read('4I');check(version==5,'animation version')
    clips={};actual_elements=0;actual_frames=0
    for _ in range(count):
        name=r.string();_,rate,n=r.read('IfI');parts=[]
        for _ in range(n):
            bounds=r.read('4f');ne=r.read('I')[0];actual_frames+=1;actual_elements+=ne
            parts.append([(*r.read('4I'),r.read('11f')) for _ in range(ne)])
        clips[name]=(rate,parts)
    maximum=r.read('I')[0];hashes={}
    for _ in range(r.read('I')[0]):
        h=r.read('I')[0];hashes[h]=r.string()
    check(r.offset==len(r.data),'unconsumed animation data')
    # Native KGlobalAnimParser ignores the two legacy header counts; kanimal
    # leaves them zero. The trailing visible-element maximum is authoritative.
    check(actual_frames>0 and maximum==max(len(f) for _,fs in clips.values() for f in fs),'native visible element maximum')
    return clips,hashes

def exported_build(path,atlas,source_count):
    r=Reader(path);check(r.data[:4]==b'BILD','build magic')
    version,ns,nf=r.read('3I');check(version==10,'build version');r.string();actual=0;symbols={}
    for _ in range(ns):
        header=r.read('5I');frames=[]
        for _ in range(header[4]):
            vals=r.read('3I8f');actual+=1;frames.append(vals)
            check(vals[1]>0 and vals[2]==0 and all(0<=v<=1 for v in vals[-4:]),'atlas UV/index/duration out of range')
        check(header[0] not in symbols,'duplicate native symbol');symbols[header[0]]=frames
    for _ in range(r.read('I')[0]):r.read('I');r.string()
    # Native parser ignores the legacy build frame-total header.
    check(r.offset==len(r.data) and actual==source_count,'native build/source frame totals')
    return symbols

def native_geometry(element,build):
    symbol,frame,_,_,f=element
    check(symbol in build,'native animation references missing symbol')
    matches=[b for b in build[symbol] if b[0]<=frame<b[0]+b[1]]
    check(len(matches)==1,'native animation references missing/overlapping symbol frame')
    b=matches[0];cx,cy,w,h=b[3:7]
    corners=[(.005*(f[4]*x+f[6]*y+f[8]),-.005*(f[5]*x+f[7]*y+f[9]))
             for x in (cx-w/2,cx+w/2) for y in (cy-h/2,cy+h/2)]
    return b,(min(x for x,y in corners),min(y for x,y in corners),max(x for x,y in corners),max(y for x,y in corners))

def verify(name,door):
    src=ROOT/'art/source'/name;dest=ROOT/'anim/assets'/name
    tree=E.parse(src/(name+'.scml')).getroot()
    files={f.attrib['id']:f.attrib for f in tree.findall('folder/file')}
    clips={a.attrib['name']:a for a in tree.findall('entity/animation')}
    native,hashes=exported_animation(dest/(name+'_anim.bytes'))
    atlas=Image.open(dest/(name+'.png'));check(atlas.mode=='RGBA' and max(atlas.size)<=2048,'unbounded/non-alpha atlas')
    build=exported_build(dest/(name+'_build.bytes'),atlas,len(files))
    check(clips.keys()==native.keys(),'source/native clip mismatch')
    for f in files.values():
        im=Image.open(src/f['name'])
        check(im.mode=='RGBA' and im.size==(int(f['width']),int(f['height'])),'sprite size/mode '+f['name'])
    check('place' in clips and 'ui' in clips and 'idle' in clips,'missing native preview/icon/idle')
    for key in (src/'ui_0.png',):check(Image.open(key).size==(128,128),'icon dimensions')
    for anim in clips.values():
        for tl in anim.findall('timeline'):
            for key in tl.findall('key'):
                obj=key.find('object');f=files[obj.attrib['file']]
                check(re.sub(r'_\d+$','',Path(f['name']).stem)==tl.attrib['name'],'symbol file binding')
        if anim.attrib['name']=='ui':continue
        timelines={t.attrib['id']:t for t in anim.findall('timeline')}
        for mainkey in anim.findall('mainline/key'):
            used=[]
            for ref in mainkey.findall('object_ref'):
                tl=timelines[ref.attrib['timeline']];key=next(k for k in tl.findall('key') if k.attrib['id']==ref.attrib['key'])
                used.append((tl.attrib['name'],key.find('object')))
            films=[o for n,o in used if n.startswith('film_')]
            check(bool(films),'film absent in '+anim.attrib['name'])
            for obj in films:
                im=Image.open(src/files[obj.attrib['file']]['name']);a=im.getchannel('A')
                check(0<a.getpixel((im.width//2,im.height//2))<210,'film alpha erased/opaque')
        # Inspect real symbol frame references, BILD bboxes and ANIM matrices.
        # Kanimal already doubles source coordinates; native scale is .005.
        for frame in native[anim.attrib['name']][1]:
            check(any(hashes.get(p[0],'').startswith('film_') for p in frame),'exported film layer absent')
            frame_bounds=[]
            for element in frame:
                b,bounds=native_geometry(element,build);symbol=hashes.get(element[0],'')
                x0,y0,x1,y1=bounds
                check(-.5001<=x0<=x1<=.5001 and -.0001<=y0<=y1<=(2 if door else 1)+.0001,'native part outside footprint: '+symbol)
                if symbol.startswith('frame'):frame_bounds.append(bounds)
                if symbol.startswith('film_'):
                    crop=atlas.crop(tuple(round(v*n) for v,n in zip(b[-4:],(atlas.width,atlas.height,atlas.width,atlas.height))))
                    check(crop.width>0 and crop.height>0,'empty native film UV')
                    alpha=crop.getchannel('A').getpixel((crop.width//2,crop.height//2))*element[4][0]*element[4][3]
                    check(0<alpha<210,'native film opaque/erased')
                if symbol.startswith(('bus_','join_')):
                    side=next(s for s in ('west','east','south','north') if s in symbol)
                    edge={'west':x0,'east':x1,'south':y0,'north':y1}[side]
                    target={'west':-.5,'east':.5,'south':0,'north':2 if door else 1}[side]
                    check(abs(edge-target)<.0001,'native half-coupler fails to meet seam: '+symbol)
            check(bool(frame_bounds),'native frame missing')
            footprint=(min(b[0] for b in frame_bounds),min(b[1] for b in frame_bounds),max(b[2] for b in frame_bounds),max(b[3] for b in frame_bounds))
            check(all(abs(a-b)<.0001 for a,b in zip(footprint,(-.5,0,.5,2 if door else 1))),'native frame footprint differs from logical grid')
    if door:
        for clip in ('opening','closing'):check(len(native[clip][1])==6,'transition frame count')
        check(all(not any(hashes.get(p[0],'').startswith('clamp_') for p in frame) for frame in native['open'][1]),'open still has center clamp')
        check(any(hashes.get(p[0],'')=='clamp_left' for p in native['closed'][1][0]),'closed clamp absent')
        first=clips['closed'].find("timeline[@name='frame']/key/object")
        file=files[first.attrib['file']]
        check(abs(float(first.attrib['scale_x'])*int(file['width'])-100)<.001 and abs(float(first.attrib['scale_y'])*int(file['height'])-200)<.001,'door logical footprint')
    else:
        check(all('tile_'+str(i) in clips for i in range(16)),'incomplete adjacency set')
        for mask in range(16):
            names={t.attrib['name'] for t in clips['tile_'+str(mask)].findall('timeline')}
            for bit,s in [(1,'west'),(2,'east'),(4,'south'),(8,'north')]:check(('bus_'+s in names)==bool(mask&bit),'coupler side mismatch')

for name,door in [('baiye_membrane_tile',False),('baiye_membrane_door',True)]:verify(name,door)
hardware=Image.open(ROOT/'art/source/hardware-sheet.png')
check(hardware.getpixel((450,250))[3]==0 and hardware.getpixel((1100,500))[3]==0,'hardware aperture filled')
print(f'PASS {passed} asset assertions: native frame references/transformed geometry/film atlas alpha, 16 tile joins, 6-frame transitions, continuous membrane, icons and bounded atlases (no Unity rendering)')
