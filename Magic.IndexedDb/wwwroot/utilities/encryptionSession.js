// The owner creates one session per browser application and account-bound database.
// This class only manages in-memory state. The owner supplies acquisition and
// authenticates a durable key check before its callback returns a key.
export class EncryptionSession {
    #key;
    #pending;
    #disposed = false;

    get isUnlocked() {
        return this.#key !== undefined;
    }

    ensureUnlocked(acquireVerifiedKey) {
        if (this.#disposed) {
            return Promise.reject(new Error("Magic encryption session was disposed."));
        }
        if (this.#key) {
            return Promise.resolve();
        }
        if (this.#pending) {
            return this.#pending.result;
        }

        const pending = { controller: new AbortController() };
        this.#pending = pending;
        const signal = pending.controller.signal;
        let cancel;
        const cancelled = new Promise((_, reject) => {
            cancel = () => reject(signal.reason);
            signal.addEventListener("abort", cancel, { once: true });
        });
        const acquired = Promise.resolve().then(() => {
            signal.throwIfAborted();
            return acquireVerifiedKey(signal);
        });
        pending.result = Promise.race([acquired, cancelled]).then(key => {
            signal.throwIfAborted();
            if (!key) {
                throw new Error("The key provider did not supply a verified key.");
            }
            this.#key = { value: key };
        }).finally(() => {
            signal.removeEventListener("abort", cancel);
            if (this.#pending === pending) {
                this.#pending = undefined;
            }
        });
        return pending.result;
    }

    async useKey(operation) {
        const key = this.#key;
        if (!key) {
            throw new Error("Magic encryption session is locked.");
        }

        const assertCurrent = () => {
            if (this.#key !== key) {
                throw new Error("Magic encryption session was locked during the operation.");
            }
        };
        const result = await operation(key.value, assertCurrent);
        assertCurrent();
        return result;
    }

    lock() {
        const pending = this.#pending;
        this.#pending = undefined;
        this.#key = undefined;
        pending?.controller.abort();
    }

    dispose() {
        this.#disposed = true;
        this.lock();
    }
}
