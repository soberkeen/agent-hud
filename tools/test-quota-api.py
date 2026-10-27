# -*- coding: utf-8 -*-
# 调研脚本：解出本机 zcode coding-plan 密钥，实测配额端点的真实响应结构
# 密钥只在本地解密使用，绝不打印
import json, base64, hashlib, urllib.request, sys, os

CREDS = os.path.expanduser(r"~\.zcode\v2\credentials.json")
SECRET = "zcode-credential-fallback:win32:" + os.path.expanduser("~") + ":" + os.getlogin()

def b64u(b):
    return base64.urlsafe_b64decode(b + "=" * (-len(b) % 4))

def decrypt(enc):
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
    assert enc.startswith("enc:v1:")
    iv_b64, tag_b64, ct_b64 = enc[len("enc:v1:"):].split(".")
    key = hashlib.sha256(SECRET.encode()).digest()
    aes = AESGCM(key)
    # AESGCM.expect ct||tag
    return aes.decrypt(b64u(iv_b64), b64u(ct_b64) + b64u(tag_b64), None).decode("utf-8")

def fetch(url, key):
    req = urllib.request.Request(url, headers={"Authorization": "Bearer " + key})
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:
        return -1, repr(e)

d = json.load(open(CREDS))
out = {}
for k, v in d.items():
    if isinstance(v, str) and v.startswith("enc:v1:"):
        try:
            out[k] = decrypt(v)
        except Exception as e:
            out[k] = None
            print("[dec fail]", k.split(":")[0], e)

# 找 coding-plan api key
key = None
for k, v in out.items():
    if "api-key" in k and v:
        key = v
        print("[have key]", k)
if not key:
    print("NO KEY"); sys.exit(1)

tests = [
    "https://open.bigmodel.cn/api/monitor/usage/quota/limit",
    "https://open.bigmodel.cn/api/monitor/usage/quota/limit?type=2",
    "https://api.z.ai/api/monitor/usage/quota/limit",
]
for u in tests:
    st, body = fetch(u, key)
    print("=" * 20, st, u)
    try:
        print(json.dumps(json.loads(body), indent=1, ensure_ascii=False)[:2500])
    except Exception:
        print(body[:800])
