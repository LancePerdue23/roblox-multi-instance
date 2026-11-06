import { generateKeyPairSync } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const [directory, keyId] = process.argv.slice(2);
if (!directory || !/^[A-Za-z0-9_-]{1,64}$/.test(keyId ?? '')) throw new Error('Usage: node generate-keys.mjs OUTPUT_DIRECTORY KEY_ID');
const target = resolve(directory);
const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
await mkdir(target, { recursive: true });
// No secrets are printed; create-new prevents overwriting a signing key accidentally.
await writeFile(join(target, 'receipt-private.jwk'), JSON.stringify(privateKey.export({ format: 'jwk' })), { flag: 'wx', mode: 0o600 });
await writeFile(join(target, 'support-config.json'), JSON.stringify({ endpoint: '', publicKeys: { [keyId]: publicKey.export({ format: 'der', type: 'spki' }).toString('base64') }, robloxGamePassId: null }, null, 2), { flag: 'wx' });
console.log('Wrote private signing key and public desktop configuration to the requested directory. Back up the private key securely; never commit or package it.');
