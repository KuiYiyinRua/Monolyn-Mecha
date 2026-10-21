"""Check the delivered payload against the actual 1.7 runtime evidence."""
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8')
root = Path(__file__).resolve().parents[2]
evidence = Path(sys.argv[1]).resolve()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()

def errors(run):
    text = (evidence / run / 'stdout.log').read_text(encoding='utf-8', errors='replace')
    return sorted(set(re.findall(r'^\S+\s+ERROR (.+)$', text, re.M)))

baseline = set(errors('Baseline-R1')) | set(errors('WorldBaseline-R1'))
results = {}
for run in ('Startup-Final', 'Functional-R3', 'Cold-R1', 'WorldBaseline-R1'):
    folder = evidence / run
    assert (folder / 'complete').is_file(), f'{run}: missing completion'
    assert not (folder / 'failed').exists(), f'{run}: failure marker'
    new_errors = set(errors(run)) - baseline
    assert not new_errors, f'{run}: new runtime errors: {new_errors}'
    checks = (folder / 'assertions.txt').read_text(encoding='utf-8').splitlines() if (folder / 'assertions.txt').exists() else []
    assert not any(line.startswith('FAIL') for line in checks)
    results[run] = {'completion': (folder / 'complete').read_text(), 'passed_assertions': sum(x.startswith('PASS ') for x in checks), 'new_errors': list(new_errors)}

runtime = evidence / 'Runtime-R1/Game/Mods/MonolynMecha'
core = root / '1.7/Assemblies/Monolyn.Mecha.dll'
expected = (evidence / 'Functional-R3/loaded-core.sha256').read_text().strip()
assert sha(core) == expected == sha(runtime / '1.7/Assemblies/Monolyn.Mecha.dll')
assert (evidence / 'Cold-R1/actual-state.txt').read_bytes() == (evidence / 'Functional-R3/expected-state.txt').read_bytes()

folders = ET.parse(root / 'LoadFolders.xml').getroot()
assert [x.text for x in folders.find('v1.7')] == ['/', '1.7']
assert [x.text for x in folders.find('v1.6')] == ['/', '1.6', 'Multiplayer']
assert folders.find('v1.6/li[@IfModActive]').attrib['IfModActive'] == 'rwmt.Multiplayer'
about = ET.parse(root / 'About/About.xml').getroot()
assert [x.text for x in about.find('supportedVersions')] == ['1.6', '1.7']
assert about.findtext('modVersion') == '0.9.4'
assert sha(root / 'About/About.xml') == sha(root / 'Source/RuntimeOverrides/About/About.xml')

for part in ('About', 'Languages', 'Textures', '1.7'):
    for file in (root / part).rglob('*'):
        if not file.is_file() or file.suffix == '.pdb':
            continue
        rel = file.relative_to(root)
        assert sha(file) == sha(runtime / rel), f'tested payload differs: {rel}'
assert sha(root / 'LoadFolders.xml') == sha(runtime / 'LoadFolders.xml')

xml_count = 0
for part in ('About', '1.6', '1.7', 'Languages', 'Source/RuntimeOverrides'):
    for file in (root / part).rglob('*.xml'):
        ET.parse(file)
        xml_count += 1
for file in (root / '1.7').rglob('*.xml'):
    assert sha(file) == sha(root / '1.6' / file.relative_to(root / '1.7')), f'balance/resource drift: {file}'

initial = json.loads((evidence / 'initial-hashes.json').read_text(encoding='utf-8-sig'))
for record in initial:
    assert sha(Path(record['Path'])) == record['Hash'], f'original assembly changed: {record["Path"]}'

result = {'status': 'PASS', 'game': '1.7.4984 rev1242', 'core_sha256': expected, 'xml_files_parsed': xml_count,
          'runtime': results, 'baseline_errors': sorted(baseline), 'multiplayer17_tested': False,
          'rimworld16_binary_preserved': True}
(evidence / 'final-validation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
