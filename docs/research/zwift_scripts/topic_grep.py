"""Grep dumped symbols/strings/dex by regex; print counts and unique samples per source."""
import re, sys, os

dump = sys.argv[1]
pattern = re.compile(sys.argv[2], re.I)
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 60
exclude = re.compile(sys.argv[4], re.I) if len(sys.argv) > 4 else None

def load(name, strip):
    with open(os.path.join(dump, name), encoding='utf-8-sig', errors='replace') as f:
        for line in f:
            yield strip(line.rstrip('\n'))

sources = {
    'syms': load('syms.txt', lambda l: l.split(' ', 2)[-1] if len(l) > 19 else l),
    'strings': load('strings.txt', lambda l: l),
    'dex': load('dex.txt', lambda l: l.split('\t', 1)[-1]),
}
for src, it in sources.items():
    seen = []
    s = set()
    total = 0
    for line in it:
        if pattern.search(line) and not (exclude and exclude.search(line)):
            total += 1
            key = line[:200]
            if key not in s:
                s.add(key)
                if len(seen) < limit:
                    seen.append(key)
    print(f'##### {src}: {total} hits, {len(s)} unique')
    for x in seen:
        print('  ', x)
