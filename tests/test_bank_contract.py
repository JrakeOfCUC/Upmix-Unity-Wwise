from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import build_song_banks as song
from spatial_hall_banks import configure_spatial_hall

def generate(build):
    inputs = {
        "Busses/Default Work Unit.wwu": '<Root ID="bus"><Bus Name="Main Audio Bus"/></Root>',
        "Game Parameters/Default Work Unit.wwu": '<Root><WorkUnit ID="param"/></Root>',
        "Effects/Factory Effects.wwu": '<Root><Effect PluginName="Wwise Delay"/><Effect PluginName="Wwise RoomVerb" Name="Hall_Medium"><PropertyList><Property Name="DryLevel" Value="0"/></PropertyList></Effect><Effect PluginName="Wwise RoomVerb" Name="Room_Small"><PropertyList/></Effect></Root>',
    }
    for name, xml in inputs.items():
        path = build / name
        path.parent.mkdir(parents=True)
        path.write_text(xml)
    sounds = ET.fromstring('<Sounds><Sound Name="Stem_Vocal"><PropertyList/><ActiveSourceList/></Sound></Sounds>')
    banks = ET.fromstring('<Banks><Bank Name="UpmixStems"><ObjectInclusionList/></Bank></Banks>')
    song.configure_effects(build, sounds, banks)
    configure_spatial_hall(build, banks)
    return sounds, banks, ET.parse(build / 'Busses/Default Work Unit.wwu').getroot(), ET.parse(build / 'Game Parameters/Default Work Unit.wwu').getroot()


class BankContract(unittest.TestCase):
    def test_effect_slots_and_bank_references(self):
        with tempfile.TemporaryDirectory() as folder:
            sounds, banks, buses, parameters = generate(Path(folder))
        self.assertEqual([p.get('Name') for p in parameters.iter('GameParameter')],
                         ['StemEQLow', 'StemEQMid', 'StemEQHigh'])
        sound = sounds.find('Sound')
        self.assertEqual([e.tag for e in sound], ['PropertyList', 'ObjectLists', 'ActiveSourceList'])
        hall = banks.find("Bank[@Name='SpatialHallPreset']")
        refs = hall.findall('ObjectInclusionList/ObjectRef')
        self.assertEqual([ref.get('Name') for ref in refs], [f'SpatialHall_{i}' for i in range(8)])
        for ref in refs:
            bus = buses.find(f".//AuxBus[@ID='{ref.get('ID')}']")
            effect = bus.find("ObjectLists/ObjectList[@Name='Effects']/EffectSlot/ReferenceList/Reference[@Name='Effect']/Custom/Effect")
            self.assertEqual(effect.get('Name'), ref.get('Name') + '_Effect')


class PublishContract(unittest.TestCase):
    def test_clean_checkout_retry_and_existing_meta(self):
        with tempfile.TemporaryDirectory() as root:
            generated = Path(root) / 'generated'
            destination = Path(root) / 'assets'
            for platform in song.PLATFORMS:
                folder = generated / platform
                folder.mkdir(parents=True)
                (folder / 'Init.bnk').write_bytes(b'init')
                (folder / 'SpatialHallPreset.bnk').write_bytes(bytes(100))
            last = generated / 'Mac/SpatialHallPreset.bnk'
            last.write_bytes(b'x')
            with self.assertRaises(ValueError):
                song.publish_banks(generated, destination, ['SpatialHallPreset'])
            self.assertFalse(destination.exists())
            last.write_bytes(bytes(100))
            song.publish_banks(generated, destination, ['SpatialHallPreset'])
            meta = destination / 'Mac/SpatialHallPreset.bnk.meta'
            self.assertIn('guid:', meta.read_text())
            meta.write_text('existing metadata')
            song.publish_banks(generated, destination, ['SpatialHallPreset'])
            self.assertEqual('existing metadata', meta.read_text())
