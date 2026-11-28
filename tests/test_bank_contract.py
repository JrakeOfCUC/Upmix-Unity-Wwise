from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
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


    def test_install_failure_restores_previous_files(self):
        with tempfile.TemporaryDirectory() as root:
            generated = Path(root) / 'generated'
            destination = Path(root) / 'assets'
            for platform in song.PLATFORMS:
                (generated / platform).mkdir(parents=True)
                (generated / platform / 'Init.bnk').write_bytes(b'new')
                (destination / platform).mkdir(parents=True)
                (destination / platform / 'Init.bnk').write_bytes(b'old')
            replace = Path.replace

            def fail_on_mac(path, target):
                if target == destination / 'Mac/Init.bnk':
                    raise PermissionError(target)
                return replace(path, target)

            with patch.object(Path, 'replace', fail_on_mac), self.assertRaises(PermissionError):
                song.publish_banks(generated, destination, [])
            for platform in song.PLATFORMS:
                self.assertEqual((destination / platform / 'Init.bnk').read_bytes(), b'old')
                self.assertFalse((destination / platform / 'Init.bnk.meta').exists())


class BuildIsolationContract(unittest.TestCase):
    def test_overlapping_builder_rejected_and_retry_allowed(self):
        with tempfile.TemporaryDirectory() as root:
            lock = Path(root) / 'build'
            with song.exclusive_build(lock):
                with self.assertRaises(RuntimeError):
                    with song.exclusive_build(lock):
                        self.fail("Both builders entered the same directory")
            with song.exclusive_build(lock):
                self.assertTrue(lock.is_dir())

    def test_warning_with_missing_output_cannot_publish_cached_banks(self):
        with tempfile.TemporaryDirectory() as root:
            build = Path(root)
            destination = build / 'assets'
            for platform in song.PLATFORMS:
                folder = build / 'GeneratedSoundBanks' / platform
                folder.mkdir(parents=True)
                (folder / 'Init.bnk').write_bytes(b'stale')
            with patch.object(song.subprocess, 'run', return_value=song.subprocess.CompletedProcess([], 2)):
                with self.assertRaises(FileNotFoundError):
                    song.generate_and_publish(build, 'project.wproj', 'console', [], destination)
            self.assertFalse(destination.exists())


class MixerBindingContract(unittest.TestCase):
    def test_unity_controls_match_authored_effects(self):
        from stem_effects import EQ_BANDS, UNITY_SOURCE, unity_source
        # The serialized-in-memory EQ state uses the three components of a Vector3.
        self.assertEqual(len(EQ_BANDS), 3)
        self.assertEqual(UNITY_SOURCE.read_text(encoding="utf-8"), unity_source())
