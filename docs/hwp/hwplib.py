import olefile, zlib, struct
def recs(data):
    out=[];i=0
    while i < len(data):
        h = struct.unpack('<I', data[i:i+4])[0]; i+=4
        tag=h&0x3ff; lvl=(h>>10)&0x3ff; size=h>>20
        if size==0xfff: size=struct.unpack('<I',data[i:i+4])[0]; i+=4
        out.append([tag,lvl,data[i:i+size]]); i+=size
    return out
def pack(rs):
    b=bytearray()
    for tag,lvl,d in rs:
        n=len(d)
        if n>=0xfff:
            b+=struct.pack('<I',tag|(lvl<<10)|(0xfff<<20))+struct.pack('<I',n)
        else:
            b+=struct.pack('<I',tag|(lvl<<10)|(n<<20))
        b+=d
    return bytes(b)
def load(path):
    f=olefile.OleFileIO(path)
    streams={ '/'.join(s): f.openstream(s).read() for s in f.listdir()}
    f.close(); return streams
def dec(b): return zlib.decompress(b,-15)
def enc(b):
    c=zlib.compressobj(9,zlib.DEFLATED,-15); return c.compress(b)+c.flush()
