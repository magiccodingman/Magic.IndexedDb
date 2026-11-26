export function repairProcessed(row) {
    if (row.Value !== 11) throw new Error('Original information is missing.');
    return { ...row, Value: 10, Repairs: row.Repairs + 1 };
}

export function repairUnprocessed(row) {
    if (row.Value !== 1) throw new Error('Original information is missing.');
    return { ...row, Value: 10, Repairs: row.Repairs + 1 };
}

export async function verify(row) {
    if (globalThis.pauseRecoveryVerifier) {
        globalThis.recoveryVerifierEntered = true;
        await new Promise(() => {});
    }
    return row.Value === 10 && row.Repairs === 1;
}
