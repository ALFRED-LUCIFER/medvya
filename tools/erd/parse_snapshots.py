"""Reads the EF Core model snapshots (src/Modules/*/.../*ModelSnapshot.cs) into plain dicts. No dependencies."""
import re
from pathlib import Path

ENTITY = re.compile(r'^ {12}modelBuilder\.Entity\("([^"]+)", b =>', re.M)
PROP = re.compile(r'b\.Property<([^>]+)>\("(\w+)"\)((?:\s*\.[^;]*?)?);', re.S)
OWNS = re.compile(r'b\.OwnsMany\("[^"]+", "(\w+)", b1 =>(.*?)\n {20}\}\);', re.S)
FK = re.compile(r'b\.HasOne\("([^"]+)",[^)]*\)\s*\.With(?:Many|One)\([^)]*\)\s*\.HasForeignKey\((?:"[^"]+", )?"(\w+)"\)', re.S)
KEY = re.compile(r'b\.HasKey\(([^)]*)\)')
TABLE = re.compile(r'b\.ToTable\("(\w+)", "(\w+)"')
SQLTYPE = {'uniqueidentifier': 'guid', 'datetimeoffset': 'datetimeoffset', 'bit': 'bool', 'int': 'int', 'bigint': 'bigint',
           'date': 'date', 'time': 'time', 'varbinary(max)': 'bytes'}


def short(fqn: str) -> str:
    return fqn.rsplit('.', 1)[-1]


def parse(path: Path) -> dict:
    text = path.read_text(encoding='utf-8-sig')
    schema = re.search(r'HasDefaultSchema\("(\w+)"\)', text).group(1)
    starts = [(m.start(), m.group(1)) for m in ENTITY.finditer(text)]
    entities: dict[str, dict] = {}
    for i, (pos, fqn) in enumerate(starts):
        body = text[pos:(starts[i + 1][0] if i + 1 < len(starts) else len(text))]
        e = entities.setdefault(fqn, {'name': short(fqn), 'schema': schema, 'table': None, 'cols': {}, 'key': [], 'fks': [], 'json': []})
        for m in OWNS.finditer(body):
            e['json'].append(m.group(1))
        owned_free = OWNS.sub('', body)
        for m in PROP.finditer(owned_free):
            clr, name, chain = m.group(1), m.group(2), m.group(3)
            ctype = re.search(r'HasColumnType\("([^"]+)"\)', chain)
            sql = ctype.group(1) if ctype else clr
            e['cols'][name] = {'type': SQLTYPE.get(sql, sql), 'required': '.IsRequired()' in chain or not clr.endswith('?')}
        if (k := KEY.search(owned_free)):
            e['key'] = re.findall(r'"(\w+)"', k.group(1))
        if (t := TABLE.search(owned_free)):
            e['table'] = t.group(1)
        for m in FK.finditer(owned_free):
            e['fks'].append((short(m.group(1)), m.group(2)))
    return {'schema': schema, 'entities': {f: e for f, e in entities.items() if e['table']}}


def parse_all(root: Path) -> dict:
    out = {}
    for p in sorted(root.glob('src/Modules/*/*/Persistence/Migrations/*ModelSnapshot.cs')):
        module = p.name.replace('DbContextModelSnapshot.cs', '')
        out[module] = parse(p)
    return out


if __name__ == '__main__':
    import sys
    data = parse_all(Path(sys.argv[1] if len(sys.argv) > 1 else '.'))
    for module, m in data.items():
        print(f"\n## {module} (schema {m['schema']}): {len(m['entities'])} tables")
        for e in m['entities'].values():
            ids = [c for c in e['cols'] if c.endswith('Id') and c != 'Id']
            print(f"  {e['table']:<28} cols={len(e['cols']):<3} pk={e['key']} fks={e['fks']} json={e['json']} idcols={ids}")
