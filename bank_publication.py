"""Validate and install the Windows and macOS banks with their Unity metadata."""
import os
from pathlib import Path
import shutil
import tempfile
import uuid

from hall_design import HALL_BANK


PLATFORMS = ('Windows', 'Mac')


def publish_banks(generated, destination, bank_names):
    # Validate both platforms before replacing any playable banks.
    minimum_sizes = {'Init': 1, HALL_BANK: 100}
    for platform in PLATFORMS:
        for bank in ['Init', *bank_names]:
            path = generated / platform / (bank + '.bnk')
            if path.stat().st_size < minimum_sizes.get(bank, 1000000):
                raise ValueError(f'Incomplete SoundBank: {path}')

    # Complete every copy before installing files, so a read error or full disk leaves
    # the existing playable set intact. Stage on the destination volume for os.replace.
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination.parent, prefix='.bank-publish-') as temp:
        stage = Path(temp)
        for platform in PLATFORMS:
            folder = stage / platform
            folder.mkdir()
            target = destination / platform
            template = target / 'UpmixStems.bnk.meta'
            for bank in ['Init', *bank_names]:
                name = bank + '.bnk'
                shutil.copyfile(generated / platform / name, folder / name)
                if (target / (name + '.meta')).exists():
                    continue
                lines = template.read_text().splitlines() if template.exists() else [
                    'fileFormatVersion: 2', '', 'DefaultImporter:', '  externalObjects: {}',
                    '  userData:', '  assetBundleName:', '  assetBundleVariant:',
                ]
                lines[1] = 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, platform + '/' + bank).hex
                (folder / (name + '.meta')).write_text('\n'.join(lines) + '\n')
        install_staged_banks(stage, destination)


def install_staged_banks(stage, destination):
    installed = []
    try:
        for platform in PLATFORMS:
            target = destination / platform
            target.mkdir(parents=True, exist_ok=True)
            backup = stage / 'previous' / platform
            backup.mkdir(parents=True)
            # Install importer metadata before Unity discovers new bank files.
            for file in sorted((stage / platform).iterdir(), key=lambda p: p.suffix != '.meta'):
                live = target / file.name
                previous = None
                if live.exists():
                    if file.suffix == '.meta':
                        continue
                    previous = backup / file.name
                    try:
                        os.link(live, previous)
                    except OSError:
                        shutil.copyfile(live, previous)
                file.replace(live)
                installed.append((live, previous))
    except BaseException:
        rollback_errors = []
        for live, previous in reversed(installed):
            try:
                if previous is None:
                    live.unlink(missing_ok=True)
                else:
                    previous.replace(live)
            except OSError as error:
                rollback_errors.append(f'{live}: {error}')
        if rollback_errors:
            # Keep original files outside the temporary directory for manual recovery.
            recovery = destination.parent / ('.bank-recovery-' + uuid.uuid4().hex)
            (stage / 'previous').rename(recovery)
            raise RuntimeError(f'Incomplete rollback. Backups: {recovery}. ' + ', '.join(rollback_errors))
        raise
