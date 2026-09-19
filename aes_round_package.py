from Crypto.Cipher import AES
from Crypto.Util.Padding import pad, unpad
from Crypto.Random import get_random_bytes

text = "이 비밀 메시지는 대략 300비트 길이의 테스트 문장입니다."
data = text.encode('utf-8')
print("원문:", text, "(%d bytes)" % len(data))

key = get_random_bytes(16)
print("키:", key.hex())

# CBC
iv = get_random_bytes(16)
c = AES.new(key, AES.MODE_CBC, iv)
ct = c.encrypt(pad(data, 16))
print("\n[CBC]")
print("iv:", iv.hex())
print("암호문:", ct.hex())
out = unpad(AES.new(key, AES.MODE_CBC, iv).decrypt(ct), 16).decode('utf-8')
print("복호문:", out)
print("일치:", out == text)

# GCM
nonce = get_random_bytes(12)
c = AES.new(key, AES.MODE_GCM, nonce=nonce)
ct, tag = c.encrypt_and_digest(data)
print("\n[GCM]")
print("nonce:", nonce.hex())
print("암호문:", ct.hex())
print("tag:", tag.hex())
out = AES.new(key, AES.MODE_GCM, nonce=nonce).decrypt_and_verify(ct, tag).decode('utf-8')
print("복호문:", out)
print("일치:", out == text)
