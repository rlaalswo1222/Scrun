import sys, json, urllib.request, os
UA={'User-Agent':'ScrunDev/1.0'}
def j(u): return json.load(urllib.request.urlopen(urllib.request.Request(u,headers=UA)))
out=sys.argv[1]; res=sys.argv[2]; maps=sys.argv[3].split(',')
for a in sys.argv[4:]:
    d=j('https://api.polyhaven.com/files/'+a)
    for m in maps:
        if m not in d: print('missing',a,m); continue
        r=d[m].get(res) or d[m]['1k']
        u=r['jpg']['url'] if 'jpg' in r else r['png']['url']
        fn=os.path.join(out,f'{a}__{m}'+os.path.splitext(u)[1])
        if not os.path.exists(fn):
            open(fn,'wb').write(urllib.request.urlopen(urllib.request.Request(u,headers=UA)).read())
        print(fn, os.path.getsize(fn))
