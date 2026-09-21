import random

random.seed(42)

# GF(2^8) 곱셈 (기약다항식 0x11b)
def gmul(a, b):
    p = 0
    for _ in range(8):
        if b & 1:
            p ^= a
        hi = a & 0x80
        a = (a << 1) & 0xff
        if hi:
            a ^= 0x1b
        b >>= 1
    return p


def pad(msg):
    b = bytearray(msg.encode('utf-8'))
    n = 16 - (len(b) % 16)
    b.extend([n] * n)
    return b


def unpad(b):
    n = b[-1]
    return bytes(b[:-n]).decode('utf-8')


def to_state(block):
    st = [[0] * 4 for _ in range(4)]
    for i in range(16):
        st[i % 4][i // 4] = block[i]
    return st


def to_bytes(st):
    out = bytearray(16)
    for i in range(16):
        out[i] = st[i % 4][i // 4]
    return out


sbox = [
    0x63,0x7c,0x77,0x7b,0xf2,0x6b,0x6f,0xc5,0x30,0x01,0x67,0x2b,0xfe,0xd7,0xab,0x76,
    0xca,0x82,0xc9,0x7d,0xfa,0x59,0x47,0xf0,0xad,0xd4,0xa2,0xaf,0x9c,0xa4,0x72,0xc0,
    0xb7,0xfd,0x93,0x26,0x36,0x3f,0xf7,0xcc,0x34,0xa5,0xe5,0xf1,0x71,0xd8,0x31,0x15,
    0x04,0xc7,0x23,0xc3,0x18,0x96,0x05,0x9a,0x07,0x12,0x80,0xe2,0xeb,0x27,0xb2,0x75,
    0x09,0x83,0x2c,0x1a,0x1b,0x6e,0x5a,0xa0,0x52,0x3b,0xd6,0xb3,0x29,0xe3,0x2f,0x84,
    0x53,0xd1,0x00,0xed,0x20,0xfc,0xb1,0x5b,0x6a,0xcb,0xbe,0x39,0x4a,0x4c,0x58,0xcf,
    0xd0,0xef,0xaa,0xfb,0x43,0x4d,0x33,0x85,0x45,0xf9,0x02,0x7f,0x50,0x3c,0x9f,0xa8,
    0x51,0xa3,0x40,0x8f,0x92,0x9d,0x38,0xf5,0xbc,0xb6,0xda,0x21,0x10,0xff,0xf3,0xd2,
    0xcd,0x0c,0x13,0xec,0x5f,0x97,0x44,0x17,0xc4,0xa7,0x7e,0x3d,0x64,0x5d,0x19,0x73,
    0x60,0x81,0x4f,0xdc,0x22,0x2a,0x90,0x88,0x46,0xee,0xb8,0x14,0xde,0x5e,0x0b,0xdb,
    0xe0,0x32,0x3a,0x0a,0x49,0x06,0x24,0x5c,0xc2,0xd3,0xac,0x62,0x91,0x95,0xe4,0x79,
    0xe7,0xc8,0x37,0x6d,0x8d,0xd5,0x4e,0xa9,0x6c,0x56,0xf4,0xea,0x65,0x7a,0xae,0x08,
    0xba,0x78,0x25,0x2e,0x1c,0xa6,0xb4,0xc6,0xe8,0xdd,0x74,0x1f,0x4b,0xbd,0x8b,0x8a,
    0x70,0x3e,0xb5,0x66,0x48,0x03,0xf6,0x0e,0x61,0x35,0x57,0xb9,0x86,0xc1,0x1d,0x9e,
    0xe1,0xf8,0x98,0x11,0x69,0xd9,0x8e,0x94,0x9b,0x1e,0x87,0xe9,0xce,0x55,0x28,0xdf,
    0x8c,0xa1,0x89,0x0d,0xbf,0xe6,0x42,0x68,0x41,0x99,0x2d,0x0f,0xb0,0x54,0xbb,0x16,
]
inv_sbox = [0] * 256
for i in range(256):
    inv_sbox[sbox[i]] = i


def sub_bytes(st):
    for r in range(4):
        for c in range(4):
            st[r][c] = sbox[st[r][c]]
    return st


def inv_sub_bytes(st):
    for r in range(4):
        for c in range(4):
            st[r][c] = inv_sbox[st[r][c]]
    return st


def shift_rows(st):
    for r in range(1, 4):
        st[r] = st[r][r:] + st[r][:r]
    return st


def inv_shift_rows(st):
    for r in range(1, 4):
        st[r] = st[r][-r:] + st[r][:-r]
    return st


def mix_columns(st):
    for c in range(4):
        a0, a1, a2, a3 = st[0][c], st[1][c], st[2][c], st[3][c]
        st[0][c] = gmul(a0, 2) ^ gmul(a1, 3) ^ a2 ^ a3
        st[1][c] = a0 ^ gmul(a1, 2) ^ gmul(a2, 3) ^ a3
        st[2][c] = a0 ^ a1 ^ gmul(a2, 2) ^ gmul(a3, 3)
        st[3][c] = gmul(a0, 3) ^ a1 ^ a2 ^ gmul(a3, 2)
    return st


def inv_mix_columns(st):
    for c in range(4):
        a0, a1, a2, a3 = st[0][c], st[1][c], st[2][c], st[3][c]
        st[0][c] = gmul(a0, 14) ^ gmul(a1, 11) ^ gmul(a2, 13) ^ gmul(a3, 9)
        st[1][c] = gmul(a0, 9) ^ gmul(a1, 14) ^ gmul(a2, 11) ^ gmul(a3, 13)
        st[2][c] = gmul(a0, 13) ^ gmul(a1, 9) ^ gmul(a2, 14) ^ gmul(a3, 11)
        st[3][c] = gmul(a0, 11) ^ gmul(a1, 13) ^ gmul(a2, 9) ^ gmul(a3, 14)
    return st


def add_round_key(st, key):
    for r in range(4):
        for c in range(4):
            st[r][c] ^= key[r][c]
    return st


def enc_round(block, key):
    st = to_state(block)
    k = to_state(key)
    st = sub_bytes(st)
    st = shift_rows(st)
    st = mix_columns(st)
    st = add_round_key(st, k)
    return to_bytes(st)


def dec_round(block, key):
    st = to_state(block)
    k = to_state(key)
    st = add_round_key(st, k)
    st = inv_mix_columns(st)
    st = inv_shift_rows(st)
    st = inv_sub_bytes(st)
    return to_bytes(st)


# --- 연산별 테스트 ---
def sample():
    return to_state(bytearray((i * 7 + 3) % 256 for i in range(16)))


def test_sub_bytes():
    s = sample()
    orig = [row[:] for row in s]
    ok = inv_sub_bytes(sub_bytes(s)) == orig
    print("SubBytes/InvSubBytes:", ok)


def test_shift_rows():
    s = sample()
    orig = [row[:] for row in s]
    ok = inv_shift_rows(shift_rows(s)) == orig
    print("ShiftRows/InvShiftRows:", ok)


def test_mix_columns():
    s = sample()
    orig = [row[:] for row in s]
    ok = inv_mix_columns(mix_columns(s)) == orig
    print("MixColumns/InvMixColumns:", ok)


def test_add_round_key():
    s = sample()
    orig = [row[:] for row in s]
    k = to_state(bytearray((i * 13) % 256 for i in range(16)))
    ok = add_round_key(add_round_key(s, k), k) == orig
    print("AddRoundKey:", ok)


def test_message():
    text = "이 비밀 메시지는 대략 300비트 길이의 테스트 문장입니다."
    print("원문:", text, "(%d bytes)" % len(text.encode('utf-8')))

    data = pad(text)
    key = bytearray(random.randint(0, 255) for _ in range(16))
    print("키:", key.hex())

    ct = bytearray()
    for i in range(0, len(data), 16):
        ct += enc_round(data[i:i+16], key)
    print("암호문:", ct.hex())

    pt = bytearray()
    for i in range(0, len(ct), 16):
        pt += dec_round(ct[i:i+16], key)
    out = unpad(pt)
    print("복호문:", out)
    print("일치:", out == text)


if __name__ == "__main__":
    test_sub_bytes()
    test_shift_rows()
    test_mix_columns()
    test_add_round_key()
    print()
    test_message()
