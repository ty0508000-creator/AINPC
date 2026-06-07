# -*- coding: utf-8 -*-
import olefile, zlib, struct, sys, io

# HWP 5.x control char categories
CTRL_CHAR = {0,10,13,24,25,26,27,28,29,30,31}      # 1 wchar
CTRL_INLINE = {4,5,6,7,8,9,19,20}                    # 8 wchars
CTRL_EXTEND = {1,2,3,11,12,14,15,16,17,18,21,22,23}  # 8 wchars

def parse_para_text(data):
    """data: bytes of HWPTAG_PARA_TEXT record (UTF-16LE wchars with controls)"""
    out = []
    i = 0
    n = len(data)
    while i + 1 < n:
        wc = data[i] | (data[i+1] << 8)
        if wc in CTRL_CHAR:
            if wc in (10, 13):
                out.append('\n')
            i += 2
        elif wc in CTRL_INLINE or wc in CTRL_EXTEND:
            i += 16  # 8 wchars
        else:
            out.append(chr(wc))
            i += 2
    return ''.join(out)

def iter_records(buf):
    i = 0
    n = len(buf)
    while i + 4 <= n:
        header = struct.unpack('<I', buf[i:i+4])[0]
        tag_id = header & 0x3FF
        level = (header >> 10) & 0x3FF
        size = (header >> 20) & 0xFFF
        i += 4
        if size == 0xFFF:
            size = struct.unpack('<I', buf[i:i+4])[0]
            i += 4
        payload = buf[i:i+size]
        i += size
        yield tag_id, level, payload

def extract(path):
    ole = olefile.OleFileIO(path)
    # FileHeader: byte 36 has flags; bit0 = compressed
    fh = ole.openstream('FileHeader').read()
    compressed = bool(fh[36] & 0x01)
    # gather sections
    sections = []
    for entry in ole.listdir():
        if entry[0] == 'BodyText':
            sections.append(entry)
    # sort by section number
    def secnum(e):
        try:
            return int(e[-1].replace('Section',''))
        except:
            return 0
    sections.sort(key=secnum)
    texts = []
    for sec in sections:
        raw = ole.openstream(sec).read()
        if compressed:
            try:
                raw = zlib.decompress(raw, -15)
            except Exception as ex:
                pass
        for tag_id, level, payload in iter_records(raw):
            if tag_id == 67:  # HWPTAG_PARA_TEXT
                t = parse_para_text(payload)
                if t.strip():
                    texts.append(t)
    ole.close()
    return '\n'.join(texts)

if __name__ == '__main__':
    path = sys.argv[1]
    outpath = sys.argv[2]
    out = extract(path)
    with open(outpath, 'w', encoding='utf-8') as f:
        f.write(out)
    print('wrote', len(out), 'chars to', outpath)
