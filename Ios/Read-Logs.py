"""Read only AtriaProbe diagnostics through the existing paired Apple Devices relay (WSL)."""
import ctypes as c
from datetime import datetime, timezone
from pathlib import Path
import plistlib
import sys

lib = c.CDLL('libimobiledevice-1.0.so.6')
plist = c.CDLL('libplist-2.0.so.4')
Pointer, String, UInt32, UInt64 = c.c_void_p, c.c_char_p, c.c_uint32, c.c_uint64
PointerOut = c.POINTER(Pointer)
for name, arguments in {
    'idevice_new': [PointerOut, String], 'idevice_free': [Pointer],
    'house_arrest_client_start_service': [Pointer, PointerOut, String],
    'house_arrest_client_free': [Pointer],
    'house_arrest_send_command': [Pointer, String, String],
    'house_arrest_get_result': [Pointer, PointerOut],
    'afc_client_new_from_house_arrest_client': [Pointer, PointerOut],
    'afc_client_free': [Pointer],
    'afc_read_directory': [Pointer, String, PointerOut],
    'afc_dictionary_free': [Pointer],
    'afc_file_open': [Pointer, String, UInt64, c.POINTER(UInt64)],
    'afc_file_read': [Pointer, UInt64, Pointer, UInt32, c.POINTER(UInt32)],
    'afc_file_close': [Pointer, UInt64],
}.items():
    function = getattr(lib, name)
    function.argtypes = arguments
    function.restype = c.c_int
plist.plist_to_xml.argtypes = [Pointer, PointerOut, c.POINTER(UInt32)]
plist.plist_mem_free.argtypes = [Pointer]
plist.plist_free.argtypes = [Pointer]

APP_ID = b'XTL-3CM7U5KLYR.com.maltyyev.atria.probe'
REMOTE_ROOT = 'Library/Application Support/Atria/'
READ_ONLY = 1
NOT_FOUND = 8
MAXIMUM_LOG_BYTES = 8 * 1024 * 1024

def check(code, operation):
    if code:
        raise RuntimeError(f'{operation}: device error {code}')

device, house, afc, result, xml = (Pointer() for _ in range(5))
timestamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
output = Path(__file__).resolve().parent / 'DeviceLogs' / timestamp
try:
    check(lib.idevice_new(c.byref(device), None), 'Connect to paired USB device')
    check(lib.house_arrest_client_start_service(device, c.byref(house), b'AtriaLogRead'), 'Open app service')
    check(lib.house_arrest_send_command(house, b'VendContainer', APP_ID), 'Request Atria container')
    check(lib.house_arrest_get_result(house, c.byref(result)), 'Read container result')
    length = UInt32()
    plist.plist_to_xml(result, c.byref(xml), c.byref(length))
    response = plistlib.loads(c.string_at(xml, length.value))
    if response.get('Status') != 'Complete':
        raise RuntimeError(f'Atria container unavailable: {response}')
    check(lib.afc_client_new_from_house_arrest_client(house, c.byref(afc)), 'Open read-only diagnostic session')

    names = ['ios-error.log', 'ios-session.log', 'ios-session.previous.log']
    entries = Pointer()
    code = lib.afc_read_directory(afc, (REMOTE_ROOT + 'logs').encode(), c.byref(entries))
    if code != NOT_FOUND:
        check(code, 'List host logs')
        try:
            values = c.cast(entries, c.POINTER(String))
            index = 0
            while values[index]:
                name = values[index].decode('utf-8')
                if name.startswith('host-') and name.endswith('.log') and '/' not in name and '\\' not in name:
                    names.append('logs/' + name)
                index += 1
        finally:
            lib.afc_dictionary_free(entries)

    copied = []
    for name in sorted(names):
        handle = UInt64()
        code = lib.afc_file_open(afc, (REMOTE_ROOT + name).encode(), READ_ONLY, c.byref(handle))
        if code == NOT_FOUND:
            continue
        check(code, f'Open {name}')
        data = bytearray()
        try:
            while True:
                buffer, count = c.create_string_buffer(8192), UInt32()
                check(lib.afc_file_read(afc, handle, buffer, len(buffer), c.byref(count)), f'Read {name}')
                if count.value == 0:
                    break
                data.extend(buffer.raw[:count.value])
                if len(data) > MAXIMUM_LOG_BYTES:
                    raise RuntimeError(f'{name} exceeds diagnostic size limit; no partial file saved')
        finally:
            lib.afc_file_close(afc, handle)
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        copied.append(target)
        print(f'{name}: {len(data)} bytes')

    if not copied:
        print('No Atria logs found. Launch the updated application once.', file=sys.stderr)
        sys.exit(2)
    print(f'Saved {len(copied)} logs to {output}')
finally:
    if xml: plist.plist_mem_free(xml)
    if result: plist.plist_free(result)
    if afc: lib.afc_client_free(afc)
    if house: lib.house_arrest_client_free(house)
    if device: lib.idevice_free(device)
