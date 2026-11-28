"""Stable Upmix object IDs and Wwise inline effect serialization."""
import uuid
import xml.etree.ElementTree as ET


def guid(key):
    return '{' + str(uuid.uuid5(uuid.NAMESPACE_URL, 'upmix/' + key)).upper() + '}'


def effect_slot(owner, effect, slot_id):
    lists = ET.Element('ObjectLists')
    children = owner.find('ActiveSourceList')
    owner.insert(list(owner).index(children) if children is not None else len(owner), lists)
    slots = ET.SubElement(lists, 'ObjectList', Name='Effects')
    slot = ET.SubElement(slots, 'EffectSlot', Name='', ID=slot_id)
    ref = ET.SubElement(ET.SubElement(slot, 'ReferenceList'), 'Reference', Name='Effect')
    ET.SubElement(ref, 'Custom').append(effect)
