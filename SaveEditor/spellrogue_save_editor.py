#!/usr/bin/env python3
"""
SpellRogue Save Editor
Decrypts and re-encrypts .sav files for SpellRogue.

Usage:
  Decrypt:   python3 spellrogue_save_editor.py decrypt  <input.sav>  <output.json>
  Encrypt:   python3 spellrogue_save_editor.py encrypt  <input.json> <output.sav>
  
Example workflow:
  1. python3 spellrogue_save_editor.py decrypt Session.sav session_edit.json
  2. Edit session_edit.json in any text editor
  3. python3 spellrogue_save_editor.py encrypt session_edit.json Session.sav
  4. Replace the game's Session.sav (make sure game is closed!)

Requirements:
  pip install pycryptodome
"""

import sys, os, json, base64, secrets
from Crypto.Cipher import AES
from Crypto.Util.Padding import pad, unpad

# Hardcoded keys extracted from Assembly-CSharp.dll
AES_KEY = base64.b64decode('GS3cwXk+9PqYBwC/N0iwfpIthlL0tB0TJ5/aODtcnLo=')

# File format:
#   Bytes 0-3:  Magic header (0x20000000 = 32)
#   Bytes 4-7:  Secondary header bytes (preserved as-is)
#   Bytes 8-23: AES-CBC IV (16 bytes, unique per file)
#   Bytes 24+:  AES-256-CBC encrypted payload
#
# Encrypted payload:
#   Bytes 0-31: 32-byte integrity prefix (preserved as-is on re-encrypt)
#   Bytes 32+:  JSON content (without leading '{"version": ')


def decrypt(sav_path: str, json_path: str):
    with open(sav_path, 'rb') as f:
        data = f.read()

    header = data[:8]
    iv = data[8:24]
    ciphertext = data[24:]

    cipher = AES.new(AES_KEY, AES.MODE_CBC, iv)
    plaintext = unpad(cipher.decrypt(ciphertext), 16)

    integrity_prefix = plaintext[:32]
    json_fragment = plaintext[32:].decode('utf-8')

    # Reconstruct full JSON (game strips leading '{"version": ' on save)
    full_json = json.loads('{"version": ' + json_fragment)

    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(full_json, f, indent=2, ensure_ascii=False)

    # Save metadata needed for re-encryption
    meta = {
        'original_header': header.hex(),
        'original_integrity_prefix': integrity_prefix.hex(),
    }
    meta_path = json_path + '.meta'
    with open(meta_path, 'w') as f:
        json.dump(meta, f, indent=2)

    print(f"Decrypted: {sav_path} -> {json_path}")
    print(f"Metadata:  {meta_path}  (keep this alongside the JSON!)")
    print(f"JSON size: {os.path.getsize(json_path):,} bytes")


def encrypt(json_path: str, sav_path: str):
    # Load JSON
    with open(json_path, 'r', encoding='utf-8') as f:
        full_json = json.load(f)

    # Load metadata (header bytes and integrity prefix from original file)
    meta_path = json_path + '.meta'
    if os.path.exists(meta_path):
        with open(meta_path) as f:
            meta = json.load(f)
        original_header = bytes.fromhex(meta['original_header'])
        integrity_prefix = bytes.fromhex(meta['original_integrity_prefix'])
        print(f"Using original header and integrity prefix from {meta_path}")
    else:
        print(f"WARNING: No .meta file found. Using default header bytes.")
        original_header = bytes.fromhex('2000000000000000')  # fallback header
        integrity_prefix = bytes(32)  # zeroed prefix (game may reject this)

    # Reconstruct the stored JSON fragment (strip '{"version": ' prefix)
    full_json_str = json.dumps(full_json, separators=(',', ': '), ensure_ascii=False)
    # The stored fragment is everything after '{"version": '
    prefix_to_strip = '{"version": '
    if not full_json_str.startswith(prefix_to_strip.replace(' ', '')):
        # Try matching with spaces
        for sep in ['{"version": ', '{"version":'  ]:
            if full_json_str.startswith(sep):
                json_fragment = full_json_str[len(sep):]
                break
        else:
            # Fallback: serialize and strip manually
            json_fragment = json.dumps(full_json, indent=2, ensure_ascii=False)
            json_fragment = json_fragment[len('{\n  "version": '):]
    else:
        json_fragment = full_json_str[len(prefix_to_strip.replace(' ', '')):]

    # Re-serialize to match original format (with \r\n and 2-space indent)
    json_fragment = json.dumps(full_json, indent=2, ensure_ascii=False)
    # Strip outer braces and re-add version prefix stripping
    lines = json_fragment.split('\n')
    # Remove first line '{' and last line '}'
    inner = '\n'.join(lines[1:-1])
    # First line of inner is '  "version": "1.0.0",'
    # We need to strip '  "version": ' from start... actually easier approach:
    # store as: <value_of_version_key>,\r\n  <rest...>}
    version_val = json.dumps(full_json['version'])
    rest = {k: v for k, v in full_json.items() if k != 'version'}
    rest_str = json.dumps(rest, indent=2, ensure_ascii=False)
    # rest_str is {"key2": ...} - strip leading {
    rest_inner = rest_str[1:]  # remove '{'
    json_fragment_final = (version_val + ',\r\n' + rest_inner).encode('utf-8')

    # Build plaintext: [32 byte integrity prefix] [json fragment]
    plaintext = integrity_prefix + json_fragment_final

    # Encrypt with new random IV
    new_iv = secrets.token_bytes(16)
    cipher = AES.new(AES_KEY, AES.MODE_CBC, new_iv)
    ciphertext = cipher.encrypt(pad(plaintext, 16))

    # Write output: [8 byte header] [16 byte IV] [ciphertext]
    with open(sav_path, 'wb') as f:
        f.write(original_header)
        f.write(new_iv)
        f.write(ciphertext)

    print(f"Encrypted: {json_path} -> {sav_path}")
    print(f"Output size: {os.path.getsize(sav_path):,} bytes")
    print(f"IMPORTANT: Make sure the game is fully closed before replacing the save file!")


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)

    command = sys.argv[1].lower()
    src = sys.argv[2]
    dst = sys.argv[3]

    if command == 'decrypt':
        decrypt(src, dst)
    elif command == 'encrypt':
        encrypt(src, dst)
    else:
        print(f"Unknown command: {command}")
        print(__doc__)
        sys.exit(1)


if __name__ == '__main__':
    main()
