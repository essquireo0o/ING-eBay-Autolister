# Encrypted credentials backup

`credentials.json.enc` is an AES-256-CBC (PBKDF2, 200000 iterations) encrypted copy of
`credentials.json` (eBay API keys/tokens, Terapeak session reference, etc.) — committed here so a
fresh clone of this repo can restore working API access without the plaintext keys ever being
committed. The passphrase is **not** stored in this repo; it was generated once and given to the
account owner to keep in a password manager.

This is a one-time export, not kept in sync automatically — re-run the encrypt command below
whenever the real `credentials.json` changes and you want the backup updated.

## Decrypt

```
openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -in credentials.json.enc -out credentials.json
```
(enter the passphrase when prompted, or add `-pass pass:YOUR_PASSPHRASE` / `-pass file:path/to/passphrase.txt`)

## Re-encrypt (after updating credentials.json)

```
openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -in credentials.json -out credentials.json.enc
```
