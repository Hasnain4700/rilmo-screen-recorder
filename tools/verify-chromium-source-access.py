# SPDX-License-Identifier: GPL-3.0-or-later
"""Verify pinned Windows Chromium Git source access without executing DEPS.

This validates equivalent VCS access. It does not claim an offline dependency
checkout, a native rebuild, or execution of Chromium's third-party hooks.
"""
import ast
import base64
import concurrent.futures
import hashlib
import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'release/browser-source-inputs'
PIN = 'bd35b66a22eb81109589ee5093b7c492fd7ed32485574446a107e3f274affc55'
raw = (OUT / 'Chromium-DEPS').read_bytes()
if hashlib.sha256(raw).hexdigest() != PIN:
    raise ValueError('Pinned DEPS changed')
tree = ast.parse(raw.decode('utf-8'))
var_node = next(a.value for a in tree.body if isinstance(a, ast.Assign) and a.targets[0].id == 'vars')
variables = {}
for key, value in zip(var_node.keys, var_node.values):
    if isinstance(value, ast.Constant):
        variables[ast.literal_eval(key)] = value.value

context = {'host_os': 'win', 'host_cpu': 'x64', 'mac': False}
for platform in ('android', 'ios', 'linux', 'mac', 'chromeos', 'arm', 'arm64', 'x86', 'mips', 'mips64'):
    context['checkout_' + platform] = False
context.update(checkout_win=True, checkout_x64=True)

def condition(source):
    def read(node):
        if isinstance(node, ast.Constant):
            return node.value
        if isinstance(node, ast.Name):
            if node.id in context:
                return context[node.id]
            value = variables[node.id]
            # DEPS represents expressions as strings; a plain string used in a
            # comparison (e.g. checkout_configuration) remains a literal.
            if isinstance(value, str) and (value in ('True', 'False') or value.startswith('checkout_')):
                return condition(value)
            return value
        if isinstance(node, ast.BoolOp):
            vals = [bool(read(n)) for n in node.values]
            return all(vals) if isinstance(node.op, ast.And) else any(vals)
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.Not):
            return not read(node.operand)
        if isinstance(node, ast.Compare) and len(node.ops) == 1:
            left, right = read(node.left), read(node.comparators[0])
            if isinstance(node.ops[0], ast.Eq): return left == right
            if isinstance(node.ops[0], ast.NotEq): return left != right
        raise ValueError('Unsupported condition: ' + ast.dump(node))
    return read(ast.parse(source, mode='eval').body)

def request(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'Rilmo-source-release'}), timeout=35) as response:
        return response.read()

def verify(row):
    result = {k: row[k] for k in ('path', 'repository', 'revision')}
    repo = row['repository'].removesuffix('.git')
    url = repo + '/+/' + row['revision'] + '?format=JSON'
    result['commitUrl'] = url
    try:
        data = request(url)
        commit = json.loads(data.decode('utf-8').removeprefix(")]}'\n"))
        if commit.get('commit') != row['revision']:
            raise ValueError('Commit response did not match pinned revision')
        result['available'] = True
        if row['path'] in index['recursiveDependencies']:
            nested_url = repo + '/+/' + row['revision'] + '/DEPS?format=TEXT'
            nested = base64.b64decode(request(nested_url))
            target = OUT / 'recursive-deps' / (row['path'].replace('/', '__') + '.DEPS')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(nested)
            result['recursiveDeps'] = {'url': nested_url, 'path': target.relative_to(ROOT).as_posix(), 'sha256':hashlib.sha256(nested).hexdigest()}
    except Exception as exc:
        result['available'] = False
        result['error'] = str(exc)
    return result

index = json.loads((OUT / 'Chromium-DEPS-index.json').read_text(encoding='utf-8-sig'))
active, excluded = [], []
for row in index['dependencies']:
    enabled = not row['condition'] or bool(condition(row['condition']))
    if row['kind'] == 'git' and enabled:
        if not row['commitPinned']: raise ValueError('Unpinned source')
        active.append(row)
    elif row['kind'] == 'git':
        excluded.append({'path':row['path'], 'condition':row['condition']})
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as pool:
    results = list(pool.map(verify, active))
report = {'schemaVersion':1, 'chromiumCommit':index['chromiumCommit'], 'sourceAccessMethod':'pinned public Git repositories; recursive checkout controlled by pinned DEPS and gclient', 'inputExecuted':False, 'offlineCheckoutComplete':False, 'platform':'Windows x64', 'verified':sum(r['available'] for r in results), 'failed':sum(not r['available'] for r in results), 'dependencies':results, 'excludedByWindowsConditions':excluded, 'nonGitInputs':'Compiler, SDK and package hooks are retained in the pinned DEPS; use the pinned depot_tools/CEF automation instructions. No hooks executed by this verifier.'}
(OUT / 'source-access-verification.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'verified':report['verified'], 'failed':report['failed'], 'failures':[r for r in results if not r['available']]}))
