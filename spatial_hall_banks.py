"""Create the media-free SpatialHallPreset bank in the generated Wwise project."""
import copy
import xml.etree.ElementTree as ET
from hall_design import HALL_BANK, RETURNS, bus_name
from wwise_work_unit import guid, effect_slot


def configure_spatial_hall(build, banks):
    def prop(parent, name, kind, value):
        existing = parent.find(f"Property[@Name='{name}']")
        if existing is not None:
            parent.remove(existing)
        ET.SubElement(parent, 'Property', Name=name, Type=kind, Value=str(value))

    path = build / 'Busses/Default Work Unit.wwu'
    tree = ET.parse(path)
    children = tree.find(".//Bus[@Name='Main Audio Bus']/ChildrenList")
    assert children is not None, 'Run configure_effects first'
    factory = ET.parse(build / 'Effects/Factory Effects.wwu')
    bank = copy.deepcopy(banks[0])
    bank.attrib.update(Name=HALL_BANK, ID=guid('spatial-hall/bank'))
    inclusions = bank.find('ObjectInclusionList')
    inclusions.clear()
    banks.append(bank)
    for i, node in enumerate(RETURNS):
        name = bus_name(i)
        early = node['layer'] == 'early'
        bus = ET.SubElement(children, 'AuxBus', Name=name, ID=guid('spatial-hall/' + name))
        properties = ET.SubElement(bus, 'PropertyList')
        prop(properties, 'ListenerRelativeRouting', 'bool', 'True')
        prop(properties, '3DSpatialization', 'int16', 1)
        prop(properties, 'BusChannelConfig', 'int32', 16641)  # mono, per WObjects.xml
        effect = copy.deepcopy(factory.find(".//Effect[@PluginName='Wwise Delay']") if early
                               else factory.find(".//Effect[@Name='Hall_Medium'][@PluginName='Wwise RoomVerb']"))
        assert effect is not None
        effect.attrib.update(Name=name + '_Effect', ID=guid('spatial-hall/' + name + '/effect'))
        props = effect.find('PropertyList')
        if props is None:
            props = ET.SubElement(effect, 'PropertyList')
        if early:
            prop(props, 'DelayTime', 'Real32', (.029, .037)[i])
            prop(props, 'FeedbackEnabled', 'bool', 'False')
            prop(props, 'WetDryMix', 'Real32', 100)
            prop(props, 'OutputLevel', 'Real32', 0)
        else:
            prop(props, 'DryLevel', 'Real32', -96.3)
            prop(props, 'EnableEarlyReflections', 'bool', 'False')
            prop(props, 'ERLevel', 'Real32', -96.3)
            prop(props, 'ReverbLevel', 'Real32', 0)
            prop(props, 'PreDelay', 'Real32', 80 + (i-2)*7)
            prop(props, 'DecayTime', 'Real32', 1.3 + (i-2)*.07)
            prop(props, 'RoomShape', 'Real32', 70 + (i-2)*5)
            prop(props, 'Diffusion', 'Real32', 90 + (i-2)*2)
        effect_slot(bus, effect, guid('spatial-hall/' + name + '/slot'))
        ET.SubElement(inclusions, 'ObjectRef', Name=name, ID=guid('spatial-hall/' + name),
                      WorkUnitID=tree.getroot().get('ID'), Origin='Manual', Filter='7')
    tree.write(path, encoding='utf-8', xml_declaration=True)
