"""Build the additional song banks using the existing spatial sounds as templates."""
import copy
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
from wwise_work_unit import guid, effect_slot
from spatial_hall_banks import configure_spatial_hall

ROOT = Path(__file__).resolve().parent
BUILD = ROOT / 'WwiseUpmix/Library/SongBankBuild'
DEST = ROOT / 'WwiseUpmix/Assets/StreamingAssets/Audio/GeneratedSoundBanks'
SONGS = {
    'SeeYouAgain': 'See You Again', 'GloriousYears': '光辉岁月',
    'SmokeOnTheWater': 'Smoke on the Water', 'FiveHundredMiles': 'Five Hundred Miles',
    'ShuoSanJiuSan': '说散就散', 'FurElise': '《致爱丽丝》', 'XiangShanSheGu': '香山射鼓',
}


def property_value(props, name, kind, value):
    ET.SubElement(props, 'Property', Name=name, Type=kind, Value=str(value))


def configure_effects(build, sounds, banks):
    param_path = build / 'Game Parameters/Default Work Unit.wwu'
    params = ET.parse(param_path)
    unit = params.find('.//WorkUnit')
    children = unit.find('ChildrenList')
    if children is None:
        children = ET.SubElement(unit, 'ChildrenList')
    bands = [('StemEQLow', 160, 4), ('StemEQMid', 1200, 6), ('StemEQHigh', 6000, 5)]
    for name, _, _ in bands:
        param = ET.SubElement(children, 'GameParameter', Name=name, ID=guid(name))
        props = ET.SubElement(param, 'PropertyList')
        property_value(props, 'Min', 'Real64', -12)
        property_value(props, 'Max', 'Real64', 12)
    params.write(param_path, encoding='utf-8', xml_declaration=True)
    for sound in sounds.findall('Sound'):
        key = sound.get('Name') + '/EQ'
        property_value(sound.find('PropertyList'), 'UseGameAuxSends', 'bool', 'True')
        eq = ET.Element('Effect', Name=sound.get('Name') + '_EQ', ID=guid(key), PluginName='Wwise Parametric EQ',
                        CompanyID='0', PluginID='105', PluginType='3')
        props = ET.SubElement(eq, 'PropertyList')
        links = ET.SubElement(ET.SubElement(eq, 'ObjectLists'), 'ObjectList', Name='RTPC')
        for i, (name, hz, filter_type) in enumerate(bands, 1):
            property_value(props, f'FilterTypeBand{i}', 'int32', filter_type)
            property_value(props, f'FrequencyBand{i}', 'Real32', hz)
            property_value(props, f'OnOffBand{i}', 'bool', 'True')
            link = ET.SubElement(links, 'RTPC', Name='', ID=guid(key + name))
            property_value(ET.SubElement(link, 'PropertyList'), 'PropertyName', 'string', f'GainBand{i}')
            refs = ET.SubElement(link, 'ReferenceList')
            control = ET.SubElement(refs, 'Reference', Name='ControlInput')
            ET.SubElement(control, 'ObjectRef', Name=name, ID=guid(name), WorkUnitID=unit.get('ID'))
            custom = ET.SubElement(ET.SubElement(refs, 'Reference', Name='Curve'), 'Custom')
            curve = ET.SubElement(custom, 'Curve', Name='', ID=guid(key + name + '/curve'))
            property_value(ET.SubElement(curve, 'PropertyList'), 'Flags', 'int32', 3)
            points = ET.SubElement(curve, 'PointList')
            for value, flags in [(-12, 5), (12, 37)]:
                point = ET.SubElement(points, 'Point')
                for tag, number in [('XPos', value), ('YPos', value), ('Flags', flags)]:
                    ET.SubElement(point, tag).text = str(number)
        effect_slot(sound, eq, guid(sound.get('Name') + '/slot'))

    bus_path = build / 'Busses/Default Work Unit.wwu'
    buses = ET.parse(bus_path)
    main = buses.find(".//Bus[@Name='Main Audio Bus']")
    children = main.find('ChildrenList')
    if children is None:
        children = ET.SubElement(main, 'ChildrenList')
    factory = ET.parse(build / 'Effects/Factory Effects.wwu')
    for name, preset in [('StemSmallRoom', 'Room_Small'), ('StemHall', 'Hall_Medium')]:
        bus = ET.SubElement(children, 'AuxBus', Name=name, ID=guid(name))
        verb = copy.deepcopy(factory.find(f".//Effect[@Name='{preset}'][@PluginName='Wwise RoomVerb']"))
        verb.set('ID', guid(name + '/verb'))
        verb.set('Name', name + '_RoomVerb')
        props = verb.find('PropertyList')
        dry = props.find("Property[@Name='DryLevel']")
        if dry is None:
            property_value(props, 'DryLevel', 'Real32', -96)
        else:
            dry.set('Value', '-96')
        effect_slot(bus, verb, guid(name + '/slot'))
        for bank in banks:
            ET.SubElement(bank.find('ObjectInclusionList'), 'ObjectRef', Name=name, ID=guid(name),
                          WorkUnitID=buses.getroot().get('ID'), Origin='Manual', Filter='7')
    buses.write(bus_path, encoding='utf-8', xml_declaration=True)


def role_of(path):
    name = path.stem
    if '（和声）' in name:
        return 'Harmony'
    if '（人声）' in name or name.endswith('_vocals'):
        return 'Vocal'
    for role in ('Bass', 'Drums', 'Guitar', 'Piano', 'Other', 'Instrumental'):
        if name.endswith('_' + role.lower()):
            return role
    raise ValueError(path)


def build_song_banks(source, console):
    BUILD.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, BUILD / source.name)
    shutil.copytree(source.parent / 'Originals', BUILD / 'Originals', dirs_exist_ok=True)
    for unit in source.parent.glob('*/*.wwu'):
        target = BUILD / unit.relative_to(source.parent)
        target.parent.mkdir(exist_ok=True)
        shutil.copy2(unit, target)

    units = [BUILD / category / 'UpmixStems.wwu' for category in ('Containers', 'Events', 'SoundBanks')]
    trees = [ET.parse(unit) for unit in units]
    sounds, events, banks = [tree.find('.//WorkUnit/ChildrenList') for tree in trees]
    bank_names = ['UpmixStems']
    folders = sorted({p.parent for p in (ROOT / '音源分离 (1).zip').rglob('*.wav')})
    for song, title in SONGS.items():
        folder, = [p for p in folders if title in p.name]
        bank = copy.deepcopy(banks[0])
        bank_name = 'Stems_' + song
        bank_names.append(bank_name)
        bank.attrib.update(Name=bank_name, ID=guid(bank_name))
        inclusions = bank.find('ObjectInclusionList')
        inclusions.clear()
        roles = set()
        for wav in sorted(folder.glob('*.wav')):
            if wav.stem.endswith('_instrumental_20261123_222211'):
                continue
            role = role_of(wav)
            if role in roles:
                raise ValueError(f'Duplicate {role} in {folder}')
            roles.add(role)
            template = 'Stem_' + role
            name = template + '_' + song
            sound = copy.deepcopy(sounds.find(f"Sound[@Name='{template}']"))
            sound.attrib.update(Name=name, ID=guid(name))
            src = sound.find('.//AudioFileSource')
            src.attrib.update(Name=name, ID=guid(name + '/source'))
            rel = Path(song) / (role + '.wav')
            target = BUILD / 'Originals/SFX' / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(wav, target)
            src.find('AudioFile').text = str(rel)
            src.find('.//MediaID').set('ID', str(uuid.UUID(guid(name)).int % 2147483647))
            sound.find('.//ActiveSource').attrib.update(src.attrib)
            sounds.append(sound)
            event = copy.deepcopy(events.find(f"Event[@Name='Play_{template}']"))
            event.attrib.update(Name='Play_' + name, ID=guid('Play_' + name))
            event.find('.//Action').set('ID', guid(name + '/action'))
            event.find('.//ObjectRef').attrib.update(sound.attrib)
            events.append(event)
            ref = copy.deepcopy(banks[0].find('.//ObjectRef'))
            ref.attrib.update(event.attrib)
            inclusions.append(ref)
        expected = {'Vocal','Bass','Drums','Guitar','Piano','Other','Instrumental'}
        if song not in ('FurElise', 'XiangShanSheGu'):
            expected.add('Harmony')
        if roles != expected:
            raise ValueError((song, roles, expected))
        banks.append(bank)
    configure_effects(BUILD, sounds, banks)
    configure_spatial_hall(BUILD, banks)
    bank_names.append('SpatialHallPreset')
    for tree, path in zip(trees, units):
        tree.write(path, encoding='utf-8', xml_declaration=True)
    result = subprocess.run([str(console), 'generate-soundbank', str(BUILD / source.name),
                             '--platform', 'Windows', 'Mac', '--bank', *bank_names])
    if result.returncode not in (0, 2):
        sys.exit(result.returncode)
    for platform in ('Windows', 'Mac'):
        shutil.copyfile(BUILD / 'GeneratedSoundBanks' / platform / 'Init.bnk', DEST / platform / 'Init.bnk')
        for bank in bank_names:
            file = BUILD / 'GeneratedSoundBanks' / platform / (bank + '.bnk')
            if file.stat().st_size < (100 if bank == 'SpatialHallPreset' else 1000000):
                raise ValueError(f'Missing embedded audio: {file}')
            shutil.copyfile(file, DEST / platform / file.name)
            meta = DEST / platform / (file.name + '.meta')
            if not meta.exists():
                lines = (meta.parent / 'UpmixStems.bnk.meta').read_text().splitlines()
                lines[1] = 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, platform + '/' + bank).hex
                meta.write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    build_song_banks(Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve())
