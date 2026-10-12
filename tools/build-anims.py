"""Import approved generated component art, assemble editable SCML and compile KAnim.

Source sheets are kept untouched. Cropping, resampling, nine-slice layout and
alpha baking are mechanical game-asset operations, not artwork repainting.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import argparse, json, math, os, subprocess, xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'art/source'
SCALE = 100 / 256


def imported(sheet, rect, size=None):
    im = sheet.crop(rect)
    bounds = im.getchannel('A').point(lambda a: 255 if a > 32 else 0).getbbox()
    if not bounds:
        raise ValueError('Empty source slot: ' + str(rect))
    im = im.crop(bounds)
    return im.resize(size, Image.Resampling.LANCZOS) if size else im


class Animation:
    def __init__(self, name):
        self.name = name
        self.folder = SOURCE / name
        self.folder.mkdir(parents=True, exist_ok=True)
        self.dest = ROOT / 'anim/assets' / name
        self.dest.mkdir(parents=True, exist_ok=True)
        self.sprites = {}
        self.clips = {}

    def sprite(self, name, im, pivot=(.5, .5)):
        im.save(self.folder / (name + '.png'))
        self.sprites[name] = (im, pivot)
        return name

    def clip(self, name, frames, loop=False):
        self.clips[name] = (frames, loop)

    def compile(self, kanimal):
        root = E.Element('spriter_data', scml_version='1.0', generator='BaiyeMembrane', generator_version='0.1.1')
        folder = E.SubElement(root, 'folder', id='0')
        ids = {}
        for i, (name, (im, pivot)) in enumerate(self.sprites.items()):
            ids[name] = i
            E.SubElement(folder, 'file', id=str(i), name=name+'.png', width=str(im.width), height=str(im.height), pivot_x=str(pivot[0]), pivot_y=str(pivot[1]))
        entity = E.SubElement(root, 'entity', id='0', name=self.name)
        for index, (name, (frames, loop)) in enumerate(self.clips.items()):
            anim = E.SubElement(entity, 'animation', id=str(index), name=name, length=str(len(frames)*50), interval='50', looping=str(loop).lower())
            main = E.SubElement(anim, 'mainline')
            timelines = {}
            for i, parts in enumerate(frames):
                key = E.SubElement(main, 'key', id=str(i), time=str(i*50))
                for z, (timeline, sprite, attrs) in enumerate(parts):
                    if timeline not in timelines:
                        timelines[timeline] = E.SubElement(anim, 'timeline', id=str(len(timelines)), name=timeline)
                    tl = timelines[timeline]
                    kid = str(len(tl.findall('key')))
                    tk = E.SubElement(tl, 'key', id=kid, time=str(i*50), curve_type='instant')
                    E.SubElement(tk, 'object', folder='0', file=str(ids[sprite]), **{k: str(round(v, 6)) for k, v in attrs.items()})
                    E.SubElement(key, 'object_ref', id=str(z), timeline=tl.attrib['id'], key=kid, z_index=str(z))
        E.indent(root)
        scml = self.folder / (self.name + '.scml')
        E.ElementTree(root).write(scml, encoding='utf-8', xml_declaration=True)
        env = os.environ.copy()
        env['DOTNET_ROLL_FORWARD'] = 'Major'
        subprocess.run([kanimal, 'kanim', str(scml), '-o', str(self.dest)], env=env, check=True)

    def render(self, parts, width, height, pixels=3):
        # Preview the same part layout exported to SCML, never pretend Unity ran.
        canvas = Image.new('RGBA', (round(width*pixels), round(height*pixels)))
        for _, name, attrs in parts:
            im, pivot = self.sprites[name]
            sx, sy = attrs.get('scale_x', 1), attrs.get('scale_y', 1)
            if sx <= 0 or sy <= 0:
                continue
            im = im.resize((max(1, round(im.width*sx*pixels)), max(1, round(im.height*sy*pixels))), Image.Resampling.LANCZOS)
            angle = attrs.get('angle', 0)
            if angle:
                im = im.rotate(angle, expand=True, resample=Image.Resampling.BICUBIC)
            x = round((attrs.get('x', 0)+width/2)*pixels - im.width*pivot[0])
            y = round((height-attrs.get('y', 0))*pixels - im.height*(1-pivot[1]))
            canvas.alpha_composite(im, (x, y))
        return canvas


def layer(name, sprite=None, **attrs):
    return name, sprite or name+'_0', attrs


def film_variants(anim, name, im, fades=False):
    # Exporter ignores SCML object opacity; bake transition strength into alpha.
    for i in range(5 if fades else 1):
        frame = im.copy()
        strength = (i+1)/5 if fades else 1
        frame.putalpha(frame.getchannel('A').point(lambda a: round(a*.68*strength)))
        anim.sprite(name+'_'+str(i), frame)


def build(kanimal):
    hardware = Image.open(SOURCE/'hardware-sheet.png').convert('RGBA')
    material = Image.open(SOURCE/'film-sheet.png').convert('RGBA')
    tileframe = imported(hardware, (175, 20, 750, 560), (256, 256))
    doorframe = imported(hardware, (885, 15, 1465, 955), (256, 512))
    left = imported(hardware, (148, 642, 437, 704), (256, 40))
    right = imported(hardware, (478, 642, 771, 704), (256, 40))
    bus = imported(hardware, (475, 780, 710, 960), (64, 64))
    status = imported(hardware, (205, 760, 315, 960), (24, 64))
    quiet = imported(material, (30, 380, 655, 990), (256, 256))
    passage = imported(material, (690, 105, 1235, 1140), (256, 512))
    # Tiny functional direction glyph, a code-native icon, not painted artwork.
    marker = Image.new('RGBA', (16, 16))
    ImageDraw.Draw(marker).polygon([(3, 3), (13, 8), (3, 13)], fill=(159, 199, 210, 215))
    tile = Animation('baiye_membrane_tile')
    film_variants(tile, 'film_quiet', quiet)
    for row, ys in enumerate([(0,64), (64,192), (192,256)]):
        for col, xs in enumerate([(0,64), (64,192), (192,256)]):
            if (row, col) != (1, 1):
                tile.sprite(f'frame_{row}{col}_0', tileframe.crop((xs[0], ys[0], xs[1], ys[1])), (0, 1))
    tile.sprite('bus_east_0', bus.crop((0,0,32,64)))
    tile.sprite('bus_west_0', bus.crop((32,0,64,64)))
    vertical=bus.rotate(90)
    tile.sprite('bus_north_0',vertical.crop((0,32,64,64)))
    tile.sprite('bus_south_0',vertical.crop((0,0,64,32)))
    tile.sprite('status_0', status)
    tile.sprite('flow_forward_0', marker)
    tile.sprite('flow_reverse_0', marker.transpose(Image.Transpose.FLIP_LEFT_RIGHT))
    def tile_parts(mask):
        l,r,b,t = [bool(mask&bit) for bit in (1,2,4,8)]
        widths = [12.5 if l else 25, 50 + 12.5*(l+r), 12.5 if r else 25]
        heights = [12.5 if t else 25, 50 + 12.5*(t+b), 12.5 if b else 25]
        xs=[-50, -50+widths[0], 50-widths[2]]
        ys=[100, 100-heights[0], heights[2]]
        # Film extends under the seal; shrinking connected rails reveals more film.
        parts=[layer('film_quiet', x=0, y=50, scale_x=(80+8*(l+r))/256, scale_y=(80+8*(t+b))/256)]
        for row in range(3):
            for col in range(3):
                if (row,col)==(1,1):continue
                pw = 128 if col==1 else 64; ph = 128 if row==1 else 64
                parts.append(layer(f'frame_{row}{col}', x=xs[col], y=ys[row], scale_x=widths[col]/pw, scale_y=heights[row]/ph))
        # Each tile owns only its in-cell half. Together they form one complete
        # node, without overhang, double rendering or neighbour draw-order holes.
        if r:parts.append(layer('bus_east', x=46.48, y=50, scale_x=.22, scale_y=.22))
        if l:parts.append(layer('bus_west', x=-46.48, y=50, scale_x=.22, scale_y=.22))
        if t:parts.append(layer('bus_north', x=0, y=96.48, scale_x=.22, scale_y=.22))
        if b:parts.append(layer('bus_south', x=0, y=3.52, scale_x=.22, scale_y=.22))
        parts += [layer('status', x=42 if not r else 46, y=52, scale_x=.33 if not r else .16, scale_y=.33),
                  layer('flow_forward', x=36, y=28, scale_x=.22, scale_y=.22),
                  layer('flow_reverse', x=36, y=28, scale_x=.22, scale_y=.22)]
        return parts
    for mask in range(16):tile.clip('tile_'+str(mask), [tile_parts(mask)])
    for name in ('idle','place','off','on'):tile.clip(name,[tile_parts(0)])
    tileicon=tile.render(tile_parts(0),100,100,pixels=1.28)
    tile.sprite('ui_0',tileicon);tile.clip('ui',[[layer('ui',x=0,y=0)]])

    door=Animation('baiye_membrane_door')
    door.sprite('frame_0',doorframe)
    film_variants(door,'film_quiet',quiet,fades=True)
    film_variants(door,'film_passage',passage,fades=True)
    door.sprite('clamp_left_0',left,(0,.5));door.sprite('clamp_right_0',right,(1,.5))
    for symbol,im in [('join_west_lower',bus.crop((32,0,64,64))),('join_west_upper',bus.crop((32,0,64,64))),
                      ('join_east_lower',bus.crop((0,0,32,64))),('join_east_upper',bus.crop((0,0,32,64))),
                      ('join_south',vertical.crop((0,0,64,32))),('join_north',vertical.crop((0,32,64,64)))]:
        door.sprite(symbol+'_0',im)
    door.sprite('status_0',status);door.sprite('flow_forward_0',marker);door.sprite('flow_reverse_0',marker.transpose(Image.Transpose.FLIP_LEFT_RIGHT))
    def door_parts(openness):
        parts=[]
        q=round((1-openness)*5);p=round(openness*5)
        if q:parts.append(layer('film_quiet','film_quiet_'+str(q-1),x=0,y=100,scale_x=80/256,scale_y=178/256))
        if p:parts.append(layer('film_passage','film_passage_'+str(p-1),x=0,y=100,scale_x=80/256,scale_y=178/512))
        parts.append(layer('frame',x=0,y=100,scale_x=SCALE,scale_y=SCALE))
        length=36*(1-openness)
        if length>.1:
            parts += [layer('clamp_left',x=-36,y=100,scale_x=length/256,scale_y=.09),
                      layer('clamp_right',x=36,y=100,scale_x=length/256,scale_y=.09)]
        for name,x,y in [('join_west_lower',-46.48,50),('join_west_upper',-46.48,150),
                         ('join_east_lower',46.48,50),('join_east_upper',46.48,150),
                         ('join_south',0,3.52),('join_north',0,196.48)]:
            parts.append(layer(name,x=x,y=y,scale_x=.22,scale_y=.22))
        parts += [layer('status',x=41,y=138,scale_x=.29,scale_y=.42),
                  layer('flow_forward',x=36,y=134,scale_x=.22,scale_y=.22),
                  layer('flow_reverse',x=36,y=134,scale_x=.22,scale_y=.22)]
        return parts
    door.clip('closed',[door_parts(0)]);door.clip('open',[door_parts(1)])
    door.clip('opening',[door_parts(i/5) for i in range(6)])
    door.clip('closing',[door_parts(1-i/5) for i in range(6)])
    def standalone(parts):return [p for p in parts if not p[0].startswith('join_')]
    for name in ('idle','place','off','on'):door.clip(name,[standalone(door_parts(0))])
    icon=door.render(standalone(door_parts(0)),100,200,pixels=.6)
    ui=Image.new('RGBA',(128,128));ui.alpha_composite(icon,((128-icon.width)//2,4))
    door.sprite('ui_0',ui);door.clip('ui',[[layer('ui',x=0,y=0)]])
    for anim in (tile,door):anim.compile(kanimal)
    preview(tile,door,tile_parts,door_parts)
    (ROOT/'art/import-manifest.json').write_text(json.dumps({'tile_size':[100,100],'door_size':[100,200], 'film_alpha_multiplier':.68, 'tile_masks':16, 'door_transition_frames':6, 'sources':['hardware-sheet.png','film-sheet.png'], 'anims':{a.name:list(a.clips) for a in (tile,door)}},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def preview(tile,door,tile_parts,door_parts):
    dest=ROOT/'dist/verification';dest.mkdir(parents=True,exist_ok=True)
    # Hide both runtime-direction alternatives for the static source preview.
    def visible(parts):return [p for p in parts if p[0] not in ('flow_forward','flow_reverse') and not p[0].startswith('join_')]
    canvas=Image.new('RGBA',(1080,850),(34,40,48,255));d=ImageDraw.Draw(canvas)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
    d.text((25,15),'实际导出图层预览 · 非游戏截图',font=font,fill='#e2eff5')
    for x,parts,a,w,h,label in [(70,tile_parts(0),tile,100,100,'膜砖'),(420,door_parts(0),door,100,200,'关闭'),(745,door_parts(1),door,100,200,'通行')]:
        im=a.render(visible(parts),w,h,pixels=2.4);canvas.alpha_composite(im,(x,65+(200-h)*2))
        d.text((x,560),label,font=font,fill='#e2eff5')
    wall=Image.new('RGBA',(900,210))
    for i,mask in enumerate((2,3,1)):
        im=tile.render(visible(tile_parts(mask)),100,100,pixels=2)
        wall.alpha_composite(im,(200*i+100,0))
    canvas.alpha_composite(wall,(40,615));canvas.save(dest/'art-preview.png')
    frames=[]
    for openness in [0,0,0,.2,.4,.6,.8,1,1,1,1,.8,.6,.4,.2,0]:
        frame=Image.new('RGBA',(300,560),(34,40,48,255))
        frame.alpha_composite(door.render(visible(door_parts(openness)),100,200,pixels=2.5),(25,25));frames.append(frame.convert('RGB'))
    frames[0].save(dest/'door-transition.gif',save_all=True,append_images=frames[1:],duration=100,loop=0)


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--kanimal',required=True);build(p.parse_args().kanimal)
