"""Read-only DEX parser: dump the string table (includes class/method/field names and literals) from DEX files inside an APK."""
import struct, sys, zipfile

def uleb(data, off):
    result = shift = 0
    while True:
        b = data[off]; off += 1
        result |= (b & 0x7F) << shift
        if b < 0x80:
            return result, off
        shift += 7

def strings(data):
    n, off = struct.unpack_from('<II', data, 0x38)
    for i in range(n):
        so, = struct.unpack_from('<I', data, off + i * 4)
        _, p = uleb(data, so)
        end = data.index(b'\0', p)
        yield data[p:end].decode('utf-8', errors='replace')

apk, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(apk) as z, open(out, 'w', encoding='utf-8') as f:
    for name in z.namelist():
        if name.endswith('.dex'):
            for s in strings(z.read(name)):
                f.write(f'{name}\t{s}\n'.replace('\r', ' ') if '\n' not in s else f'{name}\t{s!r}\n')
