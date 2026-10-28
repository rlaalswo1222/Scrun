import sys, struct, pythoncom
from win32com import storagecon
from hwplib import *
S = load('src.hwp')

def paras(rs):
    """list of (start,end) index ranges for each paragraph (header + its subtree)"""
    out=[]
    for i,(t,l,d) in enumerate(rs):
        if t==66:
            j=i+1
            while j<len(rs) and rs[j][1]>l: j+=1
            out.append((i,j))
    return out
def pchildren(rs, i):
    """direct non-control children of paragraph header i: TEXT/CHARSHAPE/LINESEG/RANGETAG"""
    l=rs[i][1]; j=i+1; got={}
    while j<len(rs) and rs[j][1]>l:
        if rs[j][1]==l+1 and rs[j][0] in (67,68,69,70): got.setdefault(rs[j][0],[]).append(j)
        j+=1
    return got
def text_of(rs,i):
    c=pchildren(rs,i)
    return rs[c[67][0]][2].decode('utf-16le','replace') if 67 in c else ''

s2=recs(dec(S['BodyText/Section2'])); s3=recs(dec(S['BodyText/Section3']))
p2=[a for a,b in paras(s2)]; p3=[a for a,b in paras(s3)]
def find(rs,plist,needle):
    r=[i for i in plist if needle in text_of(rs,i)]
    assert len(r)==1,(needle,r); return r[0]

# 1) tax $10 -> $20 (3-1)
i=[k for k in p2 if text_of(s2,k)=='$10\r']; assert len(i)==1
c=pchildren(s2,i[0]); s2[c[67][0]][2]='$20\r'.encode('utf-16le')

# 2) clear placeholder text in 3-1
tg=[]
for ph in ['EX: UNITY','(일회성 구매)','칸 추가 가능']:
    k=[x for x in p2 if text_of(s2,x)==ph+chr(13)]; assert len(k)==1,ph
    tg.append(k[0])
dead=set()
for k in tg:
    c=pchildren(s2,k)
    h=bytearray(s2[k][2]); n=struct.unpack('<I',h[0:4])[0]
    h[0:4]=struct.pack('<I',(n&0x80000000)|1); s2[k][2]=bytes(h)
    dead.add(c[67][0])
s2=[r for i,r in enumerate(s2) if i not in dead]

# 3) remove black highlight range tags everywhere
def strip_tags(rs):
    dead=set()
    for a,_ in paras(rs):
        c=pchildren(rs,a)
        if 70 in c:
            h=bytearray(rs[a][2]); h[14:16]=bytes(2); rs[a][2]=bytes(h)
            dead.update(c[70])
    return [r for i,r in enumerate(rs) if i not in dead]
s2=strip_tags(s2); s3=strip_tags(s3)
p2=[a for a,b in paras(s2)]; p3=[a for a,b in paras(s3)]

# 4) 3-2 성명: copy the 3-1 성명 paragraph (same cell layout)
src=find(s2,p2,'김민재, 김지환, 고상준'); dst=[k for k in p3 if text_of(s3,k)=='김민재\r']
# the 성명 cell is the first '김민재' (team-leader line later is also 김민재) -> pick the one right after '성명'
dst=[k for k in dst if text_of(s3,p3[p3.index(k)-1])=='성명\r']; assert len(dst)==1
dst=dst[0]; sa,sb=[(a,b) for a,b in paras(s2) if a==src][0]; da,db=[(a,b) for a,b in paras(s3) if a==dst][0]
assert s2[sa][1]==s3[da][1]
s3[da:db]=[list(r) for r in s2[sa:sb]]
p3=[a for a,b in paras(s3)]

# 5) 3-2 heading label: copy 3-1's first paragraph text/header, keep 3-2's own section controls
c2=pchildren(s2,0); c3=pchildren(s3,0)
t2=rs_t=s2[c2[67][0]][2].decode('utf-16le'); assert '[서식3-1]' in t2
s3[0][2]=s2[0][2]
s3[c3[67][0]][2]=t2.replace('[서식3-1]','[서식3-2]').encode('utf-16le')
s3[c3[68][0]][2]=s2[c2[68][0]][2]; s3[c3[69][0]][2]=s2[c2[69][0]][2]

# 6) page: drop header/footer reserve so the 3-2 table fits on one page (both forms for consistency)
for rs in (s2,s3):
    for r in rs:
        if r[0]==73:
            v=list(struct.unpack('<9I',r[2][:36])); v[6]=0; v[7]=0
            r[2]=struct.pack('<9I',*v)+r[2][36:]

# 7) DocInfo: 2 sections, caret reset, remove dark shade from pasted char shapes
di=recs(dec(S['DocInfo']))
d=bytearray(di[0][2]); d[0:2]=struct.pack('<H',2); d[14:26]=b'\0'*12; di[0][2]=bytes(d)
n=0
for r in di:
    if r[0]==21:
        sh=struct.unpack('<I',r[2][60:64])[0]
        if sh!=0xFFFFFFFF:
            b=bytearray(r[2]); b[60:64]=struct.pack('<I',0xFFFFFFFF); r[2]=bytes(b); n+=1
print('cleared shades',n)

out={'\x05HwpSummaryInformation':S['\x05HwpSummaryInformation'],'FileHeader':S['FileHeader'],
     'DocInfo':enc(pack(di)),'BodyText/Section0':enc(pack(s2)),'BodyText/Section1':enc(pack(s3))}
for k in S:
    if k.startswith('BinData/'): out[k]=S[k]

path=sys.argv[1]
st=pythoncom.StgCreateDocfile(path, storagecon.STGM_CREATE|storagecon.STGM_READWRITE|storagecon.STGM_SHARE_EXCLUSIVE, 0)
subs={}
for k,v in out.items():
    parts=k.split('/')
    parent=st
    if len(parts)==2:
        if parts[0] not in subs:
            subs[parts[0]]=st.CreateStorage(parts[0], storagecon.STGM_CREATE|storagecon.STGM_READWRITE|storagecon.STGM_SHARE_EXCLUSIVE,0,0)
        parent=subs[parts[0]]
    s=parent.CreateStream(parts[-1], storagecon.STGM_CREATE|storagecon.STGM_WRITE|storagecon.STGM_SHARE_EXCLUSIVE,0,0)
    s.Write(v); s=None
for x in subs.values(): x.Commit(0)
st.Commit(0); subs=None; st=None
print('written',path)
