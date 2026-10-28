"""Read-only: list exported C++ class/namespace names (first _ZN component) from an ELF64 .so, filtered by keywords."""
import struct, sys, re, collections

path = sys.argv[1]
kw = re.compile(sys.argv[2], re.I) if len(sys.argv) > 2 else None

data = open(path, 'rb').read()
# reuse section parsing by re-reading dynsym exports
e_shoff, = struct.unpack_from('<Q', data, 0x28)
e_shentsize, e_shnum, e_shstrndx = struct.unpack_from('<HHH', data, 0x3A)
secs = [struct.unpack_from('<IIQQQQIIQQ', data, e_shoff + i * e_shentsize) for i in range(e_shnum)]
shstr_off = secs[e_shstrndx][4]
def nm(s):
    o = shstr_off + s[0]
    return data[o:data.index(b'\0', o)].decode()
by = {nm(s): s for s in secs}
dynstr_off = by['.dynstr'][4]
ds = by['.dynsym']
names = []
for i in range(ds[5] // 24):
    st_name, st_info, st_other, st_shndx, _, _ = struct.unpack_from('<IBBHQQ', data, ds[4] + i * 24)
    if st_name and st_shndx != 0:
        o = dynstr_off + st_name
        names.append(data[o:data.index(b'\0', o)].decode(errors='replace'))

classes = collections.Counter()
plain = collections.Counter()
for n in names:
    m = re.match(r'_ZN(?:K)?(\d+)', n)
    if m:
        L = int(m.group(1)); s = m.end()
        cls = n[s:s + L]
        # include second component for nested (namespace::Class)
        rest = n[s + L:]
        m2 = re.match(r'(\d+)', rest)
        if m2 and int(m2.group(1)) < 60:
            cls2 = rest[m2.end():m2.end() + int(m2.group(1))]
            classes[cls + '::' + cls2] += 1
        else:
            classes[cls] += 1
    elif not n.startswith('_Z'):
        plain[n.split('_')[0]] += 1

print('exports', len(names), 'mangled-class-groups', len(classes))
items = [(c, k) for c, k in classes.items() if (kw is None or kw.search(c))]
for c, k in sorted(items, key=lambda x: -x[1])[:150]:
    print(f'{k:6d}  {c}')
if kw:
    print('--- plain C symbols matching ---')
    hits = sorted(n for n in names if not n.startswith('_Z') and kw.search(n))
    print(len(hits)); print(hits[:150])
