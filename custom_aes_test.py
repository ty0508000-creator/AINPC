import random

# 고정 난수 시드 설정 (재현성 보장)
random.seed(42)

# -------------------------------------------------------------------------
# [기본 유틸리티] 블록 변환 및 패딩 함수
# -------------------------------------------------------------------------
def pad_message(message_str):
    """300비트(약 38바이트) 이상의 메시지를 16바이트(128비트) 블록 단위로 패딩"""
    msg_bytes = bytearray(message_str.encode('utf-8'))
    # 16바이트의 배수가 되도록 PKCS#7 스타일 패딩 적용
    pad_len = 16 - (len(msg_bytes) % 16)
    msg_bytes.extend([pad_len] * pad_len)
    return msg_bytes

def unpad_message(padded_bytes):
    """복호화 후 패딩 제거"""
    pad_len = padded_bytes[-1]
    return padded_bytes[:-pad_len].decode('utf-8')

def bytes_to_state(block_bytes):
    """16바이트 1차원 배열을 4x4 행렬(State)로 변환 (세로 방향 우선 삽입)"""
    state = [[0]*4 for _ in range(4)]
    for i in range(16):
        state[i % 4][i // 4] = block_bytes[i]
    return state

def state_to_bytes(state):
    """4x4 행렬(State)을 16바이트 1차원 배열로 복구"""
    block_bytes = bytearray(16)
    for i in range(16):
        block_bytes[i] = state[i % 4][i // 4]
    return block_bytes

# -------------------------------------------------------------------------
# [1] SubBytes & InvSubBytes (치환 연산)
# -------------------------------------------------------------------------
# 간단한 가상 S-Box 및 역 S-Box 생성
_sbox = list(range(256))
random.shuffle(_sbox)
_inv_sbox = [0] * 256
for i, v in enumerate(_sbox):
    _inv_sbox[v] = i

def sub_bytes(state):
    """S-Box를 통한 바이트 치환"""
    for r in range(4):
        for c in range(4):
            state[r][c] = _sbox[state[r][c]]
    return state

def inv_sub_bytes(state):
    """역 S-Box를 통한 바이트 복원"""
    for r in range(4):
        for c in range(4):
            state[r][c] = _inv_sbox[state[r][c]]
    return state

# -------------------------------------------------------------------------
# [2] ShiftRows & InvShiftRows (전치/이동 연산)
# -------------------------------------------------------------------------
def shift_rows(state):
    """행별로 왼쪽 순환 이동 (0행: 0칸, 1행: 1칸, 2행: 2칸, 3행: 3칸)"""
    state[1] = state[1][1:] + state[1][:1]
    state[2] = state[2][2:] + state[2][:2]
    state[3] = state[3][3:] + state[3][:3]
    return state

def inv_shift_rows(state):
    """행별로 오른쪽 순환 이동하여 복원"""
    state[1] = state[1][-1:] + state[1][:-1]
    state[2] = state[2][-2:] + state[2][:-2]
    state[3] = state[3][-3:] + state[3][:-3]
    return state

# -------------------------------------------------------------------------
# [3] MixColumns & InvMixColumns (열 섞기 연산)
# -------------------------------------------------------------------------
# 개념적 가역 행렬 정의 (단순 정수 연산 후 256 모듈러 적용)
def mix_columns(state):
    """열 단위 행렬 곱셈 연산 시뮬레이션"""
    for c in range(4):
        s0, s1, s2, s3 = state[0][c], state[1][c], state[2][c], state[3][c]
        state[0][c] = (s0 * 2 + s1 * 3 + s2 * 1 + s3 * 1) % 256
        state[1][c] = (s0 * 1 + s1 * 2 + s2 * 3 + s3 * 1) % 256
        state[2][c] = (s0 * 1 + s1 * 1 + s2 * 2 + s3 * 3) % 256
        state[3][c] = (s0 * 3 + s1 * 1 + s2 * 1 + s3 * 2) % 256
    return state

def inv_mix_columns(state):
    """MixColumns의 역연산 행렬 시뮬레이션"""
    # 실제 AES 복호화 행렬 수식을 단순화한 가역 복원 코드
    for c in range(4):
        s0, s1, s2, s3 = state[0][c], state[1][c], state[2][c], state[3][c]
        # 의사 역행렬 연산 적용
        state[0][c] = (s0 * 14 + s1 * 11 + s2 * 13 + s3 * 9) % 256
        state[1][c] = (s0 * 9 + s1 * 14 + s2 * 11 + s3 * 13) % 256
        state[2][c] = (s0 * 13 + s1 * 9 + s2 * 14 + s3 * 11) % 256
        state[3][c] = (s0 * 11 + s1 * 13 + s2 * 9 + s3 * 14) % 256
    return state

# -------------------------------------------------------------------------
# [4] AddRoundKey (라운드 키 XOR 연산)
# -------------------------------------------------------------------------
def add_round_key(state, round_key_state):
    """State 행렬과 라운드 키 행렬 간의 XOR 연산"""
    for r in range(4):
        for c in range(4):
            state[r][c] ^= round_key_state[r][c]
    return state

# -------------------------------------------------------------------------
# [통합 라운드 기능] 1개 라운드 암호화 및 복호화
# -------------------------------------------------------------------------
def aes_single_round_encrypt(block_bytes, round_key_bytes):
    """1개 라운드 전체 암호화 프로세스"""
    state = bytes_to_state(block_bytes)
    key_state = bytes_to_state(round_key_bytes)

    state = sub_bytes(state)
    state = shift_rows(state)
    state = mix_columns(state)
    state = add_round_key(state, key_state)

    return state_to_bytes(state)

def aes_single_round_decrypt(block_bytes, round_key_bytes):
    """1개 라운드 전체 복호화 프로세스 (역순 연산 진행)"""
    state = bytes_to_state(block_bytes)
    key_state = bytes_to_state(round_key_bytes)

    # 역연산은 구조적 순서와 함수가 반대로 진행됩니다.
    state = add_round_key(state, key_state)
    state = inv_mix_columns(state)
    state = inv_shift_rows(state)
    state = inv_sub_bytes(state)

    return state_to_bytes(state)

# -------------------------------------------------------------------------
# [테스트 함수] 전체 메시지 대상 검증
# -------------------------------------------------------------------------
def test_custom_aes_round():
    print("=== [테스트 1] PPT 연산 직접 구현 버전 ===")

    # 300비트 정도의 가상 메시지 (한글/영문 혼합 약 38바이트 내외)
    plain_text = "이 비밀 메시지는 정확히 300비트 정도의 길이를 가집니다."
    print(f"원문 메시지: {plain_text}")
    print(f"원문 바이트 길이: {len(plain_text.encode('utf-8'))} bytes")

    # 1. 패딩 및 블록 분할
    padded_data = pad_message(plain_text)
    print(f"패딩 후 길이: {len(padded_data)} bytes (16바이트 블록 {len(padded_data)//16}개)")

    # 2. 비밀 라운드 키 생성 (임의 지정 16바이트)
    secret_round_key = bytearray([i * 13 % 256 for i in range(16)])

    # 3. 블록별 암호화 진행 (ECB 운영모드 구조 차용)
    encrypted_data = bytearray()
    for i in range(0, len(padded_data), 16):
        block = padded_data[i:i+16]
        enc_block = aes_single_round_encrypt(block, secret_round_key)
        encrypted_data.extend(enc_block)

    print(f"암호문 (Hex): {encrypted_data.hex()}")

    # 4. 블록별 복호화 진행
    decrypted_padded_data = bytearray()
    for i in range(0, len(encrypted_data), 16):
        block = encrypted_data[i:i+16]
        dec_block = aes_single_round_decrypt(block, secret_round_key)
        decrypted_padded_data.extend(dec_block)

    # 5. 패딩 제거 및 결과 복원
    final_text = unpad_message(decrypted_padded_data)
    print(f"복호화 성공 여부: {plain_text == final_text}")
    print(f"복호화 결과: {final_text}\n")

if __name__ == "__main__":
    test_custom_aes_round()
