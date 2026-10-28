import olefile, zlib, struct, sys
f = olefile.OleFileIO(sys.argv[1])
print(f.listdir())
hdr = f.openstream('FileHeader').read()
flags = struct.unpack('<I', hdr[36:40])[0]
print('compressed', flags & 1, 'ver', hdr[32:36][::-1].hex())
def recs(data):
    i=0
    while i < len(data):
        h = struct.unpack('<I', data[i:i+4])[0]; i+=4
        tag=h&0x3ff; lvl=(h>>10)&0x3ff; size=h>>20
        if size==0xfff: size=struct.unpack('<I',data[i:i+4])[0]; i+=4
        yield tag,lvl,data[i:i+size]; i+=size
EXT=set([1,2,3,11,12,14,15,16,17,18,21,22,23,4,5,6,7,8,9,19,20])
def txt(d):
    out=[];i=0
    w=[struct.unpack('<H',d[k:k+2])[0] for k in range(0,len(d),2)]
    while i<len(w):
        c=w[i]
        if c<32:
            if c in EXT:
                out.append('<%d:%s>'%(c,struct.pack('<H',w[i+1]).decode('latin1')+struct.pack('<H',w[i+2]).decode('latin1'))); i+=8; continue
            out.append('<%d>'%c); i+=1; continue
        out.append(chr(c)); i+=1
    return ''.join(out)
for s in f.listdir():
    if s[0]=='BodyText':
        raw=zlib.decompress(f.openstream(s).read(),-15)
        print('==',s,len(raw))
        n=0
        for tag,lvl,d in recs(raw):
            if tag==66: # PARA_HEADER
                nch=struct.unpack('<I',d[0:4])[0]; ctrl=struct.unpack('<I',d[4:8])[0]; ps=struct.unpack('<H',d[8:10])[0]; brk=d[11]
                print(f'P{n} lvl{lvl} nch={nch&0x7fffffff} ctrlmask={ctrl:x} ps={ps} brk={brk:x}'); n+=1
            elif tag==67:
                print('   TEXT', lvl, txt(d)[:200])
            elif tag==71:
                print('   CTRL', lvl, d[:4][::-1])
