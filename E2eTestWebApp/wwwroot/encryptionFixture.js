// Public test fixture. Production applications must supply their own data keys.
export function createFixtureBrowserKey() {
    return crypto.subtle.importKey("raw", new Uint8Array(32), "AES-GCM", false, ["encrypt", "decrypt"]);
}
