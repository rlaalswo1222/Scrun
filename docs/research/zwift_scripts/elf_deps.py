"""Read-only ELF64 parser: DT_NEEDED libs and imported (undefined) dynamic symbols, grouped by prefix."""
import struct, sys, collections, re

def parse(path):
    data = open(path, 'rb').read()
    assert data[:4] == b'\x7fELF' and data[4] == 2, 'not ELF64'
    e_shoff, = struct.unpack_from('<Q', data, 0x28)
    e_shentsize, e_shnum, e_shstrndx = struct.unpack_from('<HHH', data, 0x3A)
    secs = []
    for i in range(e_shnum):
        off = e_shoff + i * e_shentsize
        name, typ, flags, addr, offset, size, link, info, align, entsize = struct.unpack_from('<IIQQQQIIQQ', data, off)
        secs.append(dict(name=name, type=typ, offset=offset, size=size, link=link, entsize=entsize))
    shstr = secs[e_shstrndx]
    def sname(s):
        o = shstr['offset'] + s['name']
        return data[o:data.index(b'\0', o)].decode()
    for s in secs:
        s['nm'] = sname(s)
    by = {s['nm']: s for s in secs}
    dynstr = by['.dynstr']
    def dstr(o):
        o += dynstr['offset']
        return data[o:data.index(b'\0', o)].decode(errors='replace')
    needed = []
    dyn = by['.dynamic']
    for i in range(dyn['size'] // 16):
        tag, val = struct.unpack_from('<qQ', data, dyn['offset'] + i * 16)
        if tag == 0:
            break
        if tag == 1:
            needed.append(dstr(val))
    imports, exports = [], 0
    ds = by['.dynsym']
    for i in range(ds['size'] // 24):
        st_name, st_info, st_other, st_shndx, st_value, st_size = struct.unpack_from('<IBBHQQ', data, ds['offset'] + i * 24)
        if not st_name:
            continue
        n = dstr(st_name)
        if st_shndx == 0:
            imports.append(n)
        else:
            exports += 1
    return needed, imports, exports, [s['nm'] for s in secs]

for path in sys.argv[1:]:
    needed, imports, exports, secnames = parse(path)
    print('=' * 70)
    print(path.split('\\')[-1])
    print('NEEDED:', needed)
    print(f'imports={len(imports)} exports={exports}')
    groups = collections.Counter()
    for n in imports:
        m = re.match(r'(gl|egl|vk|AAudio|ANativeWindow|AAsset|ASensor|AChoreographer|pthread|Noesis|_ZN6Noesis)', n)
        groups[m.group(1) if m else 'other'] += 1
    print('import groups:', dict(groups))
    for pref in ('egl', 'vk', 'ASensor', 'AChoreographer', 'ANativeWindow'):
        hits = sorted(n for n in imports if n.startswith(pref))
        if hits:
            print(f'  {pref}*:', hits[:40])
    gl = sorted(n for n in imports if n.startswith('gl'))
    if gl:
        print(f'  gl* ({len(gl)}):', gl[:80])
