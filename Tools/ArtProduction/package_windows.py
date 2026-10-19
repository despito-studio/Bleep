"""Package an existing Windows build without launching or attesting game tests."""

import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--notes', type=Path, required=True)
    parser.add_argument('--expected-version', required=True)
    args = parser.parse_args()
    directory = args.build.resolve()
    manifest_path = directory / 'build-info.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    if manifest.get('buildResult') != 'Succeeded' or manifest.get('version') != args.expected_version:
        raise SystemExit('A successful build of the requested version is required.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    start = (
        f'DEAD AIR v{args.expected_version} — Windows x64\n\n'
        'Розпакуйте весь архів і запустіть DeadAir.exe.\n'
        'Залиште DeadAir_Data, MonoBleedingEdge та DLL поруч із програмою.\n'
        'Для кооперативу всі учасники мають використовувати цю версію.\n\n'
        'Після оптимізації текстур повторні тести не запускалися за вказівкою користувача.\n'
    )
    # Consume the build's explicit file list so editor backups and stale outputs cannot enter the package.
    with zipfile.ZipFile(args.output, 'x', zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
        for item in manifest['files']:
            source = (directory / item['path']).resolve()
            if not source.is_relative_to(directory) or not source.is_file():
                raise SystemExit(f'Invalid packaged path: {item["path"]}')
            archive.write(source, item['path'])
        archive.write(manifest_path, 'build-info.json')
        archive.writestr('START-HERE.txt', start)
        archive.write(args.notes, 'RELEASE-NOTES.md')
        archive.writestr('ART-SOURCES.txt',
            'Environment scans and PBR maps: Poly Haven, CC0-1.0.\n'
            'License: https://polyhaven.com/license\n'
            'Source URLs, original checksums and budgeted texture derivatives:\n'
            'https://github.com/despito-studio/Dear-air/tree/v2.0.0/Art/ProductionSources\n'
            'The network diagram and window-grime image are generated graphics,\n'
            'documented separately in generated-graphics.json; they are not Poly Haven scans.\n'
            'Runtime library notices included in the Unity build retain their original terms.\n')
    with args.output.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    hash_path = Path(str(args.output) + '.sha256')
    hash_path.write_text(f'{digest}  {args.output.name}\n', encoding='utf-8')
    record = {
        'version': manifest['version'],
        'archive': str(args.output.resolve()),
        'bytes': args.output.stat().st_size,
        'sha256': digest,
        'packagedRuntimeFiles': len(manifest['files']),
        'runtimeVerification': manifest['runtimeVerification'],
        'operation': 'Packaging only; no tests, player launch or runtime attestation performed.',
    }
    args.output.with_suffix('.package.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(record, ensure_ascii=False))


if __name__ == '__main__':
    main()
